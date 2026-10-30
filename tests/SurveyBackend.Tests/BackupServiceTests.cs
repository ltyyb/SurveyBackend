using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SurveyBackend.BackgroundServices;
using SurveyBackend.Bot.Commands;
using SurveyBackend.Bot.Commands.Infrastructure;
using SurveyBackend.Configuration;
using SurveyBackend.Data;
using SurveyBackend.Models;
using SurveyBackend.Services.Backups;
using Xunit;

namespace SurveyBackend.Tests;

public sealed class BackupServiceTests : IAsyncLifetime
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "SurveyBackend.Tests", Guid.NewGuid().ToString("N"));
    private readonly BackupOptions _options = new() { Enabled = true, MinFreeDiskBytes = 0 };
    private readonly FixedTimeProvider _clock = new(new DateTimeOffset(2026, 10, 30, 0, 0, 0, TimeSpan.Zero));
    private readonly RecordingLogger<BackupService> _logger = new();
    private readonly TestUploader _uploader = new();
    private BackupStore _store = null!;
    private BackupService _service = null!;
    private string SourcePath => Path.Combine(_directory, "data.db");

    public Task InitializeAsync()
    {
        Directory.CreateDirectory(_directory);
        using var source = SqliteDatabase.CreateContext(SourcePath);
        SqliteDatabase.Initialize(source);
        source.Users.Add(new User { QQId = "123456", UserGroup = UserGroup.Admin });
        source.SaveChanges();
        _store = new BackupStore(Path.Combine(_directory, "backups"), NullLogger<BackupStore>.Instance);
        _service = CreateService();
        return Task.CompletedTask;
    }

    private BackupService CreateService() => new(Options.Create(_options), SourcePath, _store,
        new SqliteBackupSnapshot(), _uploader, _clock, _logger);

    [Fact]
    public async Task TickAsync_ManualBackup_DoesNotResetAutomaticSchedule()
    {
        await _service.TickAsync();
        await _service.WaitForIdleAsync();
        var next = _service.NextRunAt;
        _clock.Advance(TimeSpan.FromMinutes(30));
        await _service.RunAsync();
        await _service.WaitForIdleAsync();
        Assert.Equal(next, _service.NextRunAt);
        _clock.Advance(TimeSpan.FromMinutes(30));
        await _service.TickAsync();
        await _service.WaitForIdleAsync();
        Assert.Equal(3, _service.LocalFiles.Count);
        Assert.Equal(next + _options.Interval, _service.NextRunAt);
        Assert.Empty(_logger.Errors);
    }

    [Fact]
    public async Task RunAsync_OverlappingManualAndScheduledRuns_MergeWithoutWaitingForUpload()
    {
        _options.Oss.Enabled = true;
        _uploader.Block = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = await _service.RunAsync();
        await _uploader.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var second = await _service.RunAsync().WaitAsync(TimeSpan.FromSeconds(1));
        Assert.True(second.Merged);
        Assert.Equal(first.Id, second.Id);
        _clock.Advance(TimeSpan.FromHours(1));
        await _service.TickAsync().WaitAsync(TimeSpan.FromSeconds(1));
        _uploader.Block.SetResult();
        await _service.WaitForIdleAsync();
        Assert.Single(_service.LocalFiles);
        Assert.Equal("Uploaded", _service.Latest!.UploadState);
        Assert.Empty(_logger.Errors);
    }

    [Fact]
    public async Task TickAsync_UploadFailureAndRestart_RetriesExistingSnapshotWithoutLosingResult()
    {
        _options.Oss.Enabled = true;
        _uploader.Fail = true;
        await _service.TickAsync();
        await _service.WaitForIdleAsync();
        var failed = _service.Latest!;
        Assert.Equal("Completed", failed.State);
        Assert.Equal("Retry", failed.UploadState);
        Assert.Single(_logger.Errors);
        Assert.DoesNotContain("synthetic-secret", _logger.Errors[0].Message);
        await _service.DisposeAsync();
        _service = CreateService();
        await _service.InitializeAsync();
        Assert.Equal(failed.Id, _service.Latest!.Id);
        _clock.Advance(_options.UploadRetryInterval);
        _uploader.Fail = false;
        await _service.TickAsync();
        Assert.Single(_service.LocalFiles);
        Assert.Equal("Uploaded", _service.Latest!.UploadState);
        Assert.Equal(failed.Sha256, _service.Latest.Sha256);
        Assert.Single(_logger.Errors);
    }

    [Fact]
    public async Task DisposeAsync_UploadInProgress_PreservesLocalSnapshotAndDurableRetry()
    {
        _options.Oss.Enabled = true;
        _uploader.Block = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await _service.RunAsync();
        await _uploader.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await _service.DisposeAsync();
        _uploader.Block.SetResult();
        _service = CreateService();
        await _service.InitializeAsync();
        Assert.Equal("Completed", _service.Latest!.State);
        Assert.Equal("Retry", _service.Latest.UploadState);
        Assert.Single(_service.LocalFiles);
        Assert.Single(_logger.Errors);
    }

    [Fact]
    public async Task InitializeAsync_OverdueSchedule_PerformsOnlyOneCatchUpRun()
    {
        await _service.TickAsync();
        await _service.WaitForIdleAsync();
        await _service.DisposeAsync();
        _clock.Advance(TimeSpan.FromDays(3));
        _service = CreateService();
        await _service.TickAsync();
        await _service.WaitForIdleAsync();
        await _service.TickAsync();
        await _service.WaitForIdleAsync();
        Assert.Equal(2, _service.LocalFiles.Count);
        Assert.True(_service.NextRunAt > _clock.GetUtcNow());
        Assert.Empty(_logger.Errors);
    }

    [Fact]
    public async Task RunAsync_CapacityFailure_PersistsFailureAndLeavesDatabaseUsable()
    {
        _options.MaxLocalBytes = 1;
        await _service.RunAsync();
        await _service.WaitForIdleAsync();
        Assert.Equal("Failed", _service.Latest!.State);
        Assert.Equal("BackupCapacityExceeded", _service.Latest.Error);
        Assert.Empty(_service.LocalFiles);
        using var source = SqliteDatabase.CreateContext(SourcePath);
        Assert.Equal("123456", source.Users.Single().QQId);
        Assert.Single(_logger.Errors);
    }

    [Fact]
    public async Task Command_AllSubcommands_EnforcesPermissionsAndHidesCredentials()
    {
        using var users = new ForceEditTestContext();
        _options.Oss.AccessKeyId = "synthetic-id";
        _options.Oss.AccessKeySecret = "synthetic-secret";
        _options.Oss.SecurityToken = "synthetic-token";
        var logger = new RecordingLogger<BackupCommand>();
        var registry = new SurveyCommandRegistry(users.ScopeFactory);
        registry.RegisterCommand(new BackupCommand(users.ScopeFactory, _service, logger));
        foreach (var suffix in new[] { "", "run", "list", "info" })
        {
            var response = await registry.TryExecuteSurveyCommandAsync(ForceEditTestContext.Message(
                222222, commandText: $"/survey backup {suffix}"));
            Assert.False(response!.Success);
            Assert.Contains("权限不足", response.Message!.Raw);
        }
        var before = await Execute("");
        Assert.Contains("暂无", before);
        Assert.Contains("任务已开始", await Execute("run"));
        await _service.WaitForIdleAsync();
        Assert.Contains("本地结果：成功", await Execute(""));
        Assert.Contains("本地备份：1 份", await Execute("list"));
        Assert.Contains("页码超出范围", await Execute("list 2"));
        var info = await Execute("info");
        foreach (var secret in new[] { "synthetic-id", "synthetic-secret", "synthetic-token" }) Assert.DoesNotContain(secret, info);
        Assert.Contains("01:00:00", info);
        var invalid = await registry.TryExecuteSurveyCommandAsync(ForceEditTestContext.Message(commandText: "/survey backup run extra"));
        Assert.False(invalid!.Success);
        Assert.Single(_service.LocalFiles);
        Assert.Empty(logger.Errors);
        Assert.Empty(_logger.Errors);

        async Task<string> Execute(string suffix)
        {
            var response = await registry.TryExecuteSurveyCommandAsync(ForceEditTestContext.Message(commandText: $"/survey backup {suffix}"));
            Assert.True(response!.Success, response.Message!.Raw);
            return response.Message!.Raw;
        }
    }

    [Fact]
    public async Task Command_BackupDisabled_QueriesWorkAndRunDoesNotCreateDirectory()
    {
        _options.Enabled = false;
        using var users = new ForceEditTestContext();
        var command = new BackupCommand(users.ScopeFactory, _service, NullLogger<BackupCommand>.Instance);
        foreach (var args in new[] { Array.Empty<string>(), new[] { "list" }, new[] { "info" } })
            Assert.True((await command.ExecuteAsync(ForceEditTestContext.Message(), args))!.Success);
        Assert.False((await command.ExecuteAsync(ForceEditTestContext.Message(), ["run"]))!.Success);
        await _service.TickAsync();
        Assert.False(Directory.Exists(_store.DirectoryPath));
        Assert.Empty(_logger.Errors);
    }

    [Fact]
    public async Task CheckAsync_BackupFailure_AlertsAdministratorWithBoundedRetries()
    {
        _options.MaxLocalBytes = 1;
        var onebot = new RecordingOnebotService();
        var logger = new RecordingLogger<BackgroundBackupService>();
        var background = new BackgroundBackupService(_service, onebot,
            Options.Create(new BotOptions { AdminId = 123456 }), _clock, logger);
        await background.CheckAsync(default);
        await _service.WaitForIdleAsync();
        await background.CheckAsync(default);
        await background.CheckAsync(default);
        Assert.Single(onebot.PrivateMessages);
        Assert.Equal(123456, onebot.PrivateMessages[0].UserId);
        Assert.Contains("/survey backup", onebot.PrivateMessages[0].Content);
        Assert.Empty(logger.Errors);
        Assert.Single(_logger.Errors);
    }

    public async Task DisposeAsync()
    {
        await _service.DisposeAsync();
        Directory.Delete(_directory, recursive: true);
    }

    private sealed class TestUploader : IBackupUploader
    {
        public bool Fail { get; set; }
        public TaskCompletionSource? Block { get; set; }
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task UploadAsync(BackupRecord record, string path, CancellationToken cancellationToken)
        {
            Entered.TrySetResult();
            if (Block is not null) await Block.Task.WaitAsync(cancellationToken);
            if (Fail) throw new HttpRequestException("synthetic-secret");
            Assert.True(File.Exists(path));
        }
    }
}
