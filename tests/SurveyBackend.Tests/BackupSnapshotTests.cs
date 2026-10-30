using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SurveyBackend.Configuration;
using SurveyBackend.Data;
using SurveyBackend.Models;
using SurveyBackend.Services.Backups;
using Xunit;

namespace SurveyBackend.Tests;

public sealed class BackupSnapshotTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "SurveyBackend.Tests", Guid.NewGuid().ToString("N"));
    private readonly BackupStore _store;
    private readonly BackupOptions _options = new() { Enabled = true, MinFreeDiskBytes = 0, PagesPerStep = 1 };
    private string SourcePath => Path.Combine(_directory, "data.db");

    public BackupSnapshotTests()
    {
        Directory.CreateDirectory(_directory);
        _store = new BackupStore(Path.Combine(_directory, "backups"), NullLogger<BackupStore>.Instance);
    }

    [Fact]
    public async Task CreateAsync_UncheckpointedWal_PublishesIndependentVerifiedDatabase()
    {
        using var source = SqliteDatabase.CreateContext(SourcePath);
        SqliteDatabase.Initialize(source);
        source.Database.OpenConnection();
        source.Database.ExecuteSqlRaw("PRAGMA wal_autocheckpoint=0;");
        source.Users.Add(new User { QQId = "123456", UserGroup = UserGroup.Admin });
        source.SaveChanges();
        Assert.True(new FileInfo(SourcePath + "-wal").Length > 0);
        using var lease = _store.AcquireLease();
        var result = await new SqliteBackupSnapshot().CreateAsync(SourcePath, Record(), _store, _options, default);
        Assert.NotNull(result.Sha256);
        Assert.NotEmpty(result.Migrations);
        using var restored = SqliteDatabase.CreateContext(_store.GetPath(result.Id, ".db"));
        Assert.Equal("123456", restored.Users.Single().QQId);
        Assert.False(File.Exists(_store.GetPath(result.Id, ".db") + "-wal"));
    }

    [Fact]
    public async Task CreateAsync_ConcurrentWrites_PreservesCommittedTransactionBoundaries()
    {
        using var source = SqliteDatabase.CreateContext(SourcePath);
        SqliteDatabase.Initialize(source);
        source.Database.ExecuteSqlRaw("CREATE TABLE backup_test (id INTEGER PRIMARY KEY, value TEXT);");
        source.Database.ExecuteSqlRaw("INSERT INTO backup_test VALUES (1, hex(zeroblob(65536)));");
        using var lease = _store.AcquireLease();
        var backupTask = new SqliteBackupSnapshot().CreateAsync(SourcePath, Record(), _store, _options, default);
        using var writer = new SqliteConnection(SqliteDatabase.ConnectionString(SourcePath, pooling: false));
        writer.Open();
        using var command = writer.CreateCommand();
        command.CommandText = "BEGIN; INSERT INTO backup_test VALUES (2, 'a'); INSERT INTO backup_test VALUES (3, 'b'); COMMIT;";
        command.ExecuteNonQuery();
        var result = await backupTask.WaitAsync(TimeSpan.FromSeconds(10));
        using var restored = new SqliteConnection(SqliteDatabase.ConnectionString(_store.GetPath(result.Id, ".db"), pooling: false));
        restored.Open();
        using var check = restored.CreateCommand();
        check.CommandText = "SELECT COUNT(*) FROM backup_test WHERE id IN (2, 3);";
        Assert.Contains(Convert.ToInt32(check.ExecuteScalar()), new[] { 0, 2 });
    }

    [Fact]
    public async Task CreateAsync_CapacityExceeded_DoesNotPublishOrChangeSource()
    {
        using var source = SqliteDatabase.CreateContext(SourcePath);
        SqliteDatabase.Initialize(source);
        using var lease = _store.AcquireLease();
        _options.MaxLocalBytes = 1;
        var record = Record();
        await Assert.ThrowsAsync<IOException>(() => new SqliteBackupSnapshot().CreateAsync(SourcePath, record, _store, _options, default));
        Assert.False(File.Exists(_store.GetPath(record.Id, ".db")));
        Assert.Empty(source.Database.GetPendingMigrations());
    }

    [Fact]
    public void RecoverAndCleanup_InterruptedAndPendingFiles_KeepLastSuccessAndUploadQueue()
    {
        using var lease = _store.AcquireLease();
        var now = DateTimeOffset.UtcNow;
        var success = Record() with { State = "Completed", UploadState = "Uploaded", StartedAt = now.AddDays(-10) };
        var pending = Record() with { State = "Completed", UploadState = "Uploading", StartedAt = now.AddDays(-9) };
        var interrupted = Record() with { StartedAt = now.AddDays(-8) };
        foreach (var record in new[] { success, pending, interrupted }) _store.Save(record);
        File.WriteAllText(_store.GetPath(success.Id, ".db"), "keep");
        File.WriteAllText(_store.GetPath(pending.Id, ".db"), "keep");
        File.WriteAllText(_store.GetPath(interrupted.Id, ".db.part"), "partial");
        _store.Recover(now);
        Assert.Equal("Interrupted", _store.ReadAll().Single(record => record.Id == interrupted.Id).State);
        Assert.Equal("Retry", _store.ReadAll().Single(record => record.Id == pending.Id).UploadState);
        _store.Cleanup(_options, now);
        Assert.True(File.Exists(_store.GetPath(pending.Id, ".db")));
        Assert.False(File.Exists(_store.GetPath(interrupted.Id, ".db.part")));
        Assert.True(File.Exists(_store.GetPath(success.Id, ".db")));
        Assert.Throws<ArgumentException>(() => _store.GetPath("../data", ".db"));
        Assert.Throws<IOException>(() => _store.AcquireLease());
    }

    private static BackupRecord Record() => new()
    {
        Id = $"{DateTimeOffset.UtcNow:yyyyMMddTHHmmssZ}-{Guid.NewGuid():N}", StartedAt = DateTimeOffset.UtcNow, FullCheck = true
    };

    public void Dispose() => Directory.Delete(_directory, recursive: true);
}
