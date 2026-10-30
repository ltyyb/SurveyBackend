using System.Reflection;

namespace SurveyBackend.Services.Backups;

public sealed class BackupService : IAsyncDisposable
{
    private readonly BackupOptions _options;
    private readonly string _databasePath;
    private readonly BackupStore _store;
    private readonly SqliteBackupSnapshot _snapshot;
    private readonly IBackupUploader _uploader;
    private readonly TimeProvider _clock;
    private readonly ILogger<BackupService> _logger;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _sync = new();
    private Task _activeTask = Task.CompletedTask;
    private BackupRecord? _activeRecord;
    private bool _initialized;
    private DateTimeOffset? _nextRunAt;
    private bool _scheduleDirty;
    private string? _maintenanceError;

    public BackupService(IOptions<BackupOptions> options, string databasePath, BackupStore store,
        SqliteBackupSnapshot snapshot, IBackupUploader uploader, TimeProvider clock, ILogger<BackupService> logger)
    {
        _options = options.Value;
        _databasePath = databasePath;
        _store = store;
        _snapshot = snapshot;
        _uploader = uploader;
        _clock = clock;
        _logger = logger;
    }

    public BackupOptions Options => _options;
    public string DirectoryPath => _store.DirectoryPath;
    public DateTimeOffset? NextRunAt { get { lock (_sync) return _nextRunAt; } }
    public bool IsRunning { get { lock (_sync) return !_activeTask.IsCompleted; } }
    public string? MaintenanceError => _maintenanceError;
    public IReadOnlyList<BackupRecord> Records => _store.ReadAll();
    public IReadOnlyList<BackupRecord> LocalFiles => Records.Where(record => File.Exists(_store.GetPath(record.Id, ".db"))).ToArray();
    public BackupRecord? Latest
    {
        get
        {
            lock (_sync)
            {
                var records = Records;
                if (_activeRecord?.State == "Failed" &&
                    (records.Count == 0 || _activeRecord.StartedAt >= records[0].StartedAt))
                    return _activeRecord;
                return !_activeTask.IsCompleted && _activeRecord is not null
                    ? records.FirstOrDefault(record => record.Id == _activeRecord.Id) ?? _activeRecord
                    : records.FirstOrDefault();
            }
        }
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled) return;
        lock (_sync) { if (_initialized) return; }
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_initialized) return;
            using var lease = _store.AcquireLease();
            var now = _clock.GetUtcNow();
            _store.Recover(now);
            var schedule = _store.ReadSchedule();
            var lastSuccess = Records.FirstOrDefault(record => record.State == "Completed");
            lock (_sync)
            {
                _nextRunAt = schedule?.Interval == _options.Interval ? schedule.NextRunAt : now;
                if (lastSuccess is null || now - lastSuccess.CompletedAt >= _options.Interval)
                    _nextRunAt = now;
                _store.SaveSchedule(new BackupSchedule(_nextRunAt.Value, _options.Interval));
                _initialized = true;
            }
        }
        finally { _gate.Release(); }
    }

    public async Task<BackupRun> RunAsync(CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled) throw new InvalidOperationException("BackupDisabled");
        await InitializeAsync(cancellationToken);
        return StartRun("manual");
    }

    private BackupRun StartRun(string trigger)
    {
        lock (_sync)
        {
            if (!_activeTask.IsCompleted)
                return new BackupRun(_activeRecord!.Id, true);
            var now = _clock.GetUtcNow();
            var oss = _options.Oss;
            var record = new BackupRecord
            {
                Id = $"{now:yyyyMMddTHHmmssZ}-{Guid.NewGuid():N}", StartedAt = now, Trigger = trigger,
                UploadState = oss.Enabled ? "Pending" : "Disabled",
                OssRegion = oss.Region, OssEndpoint = oss.Endpoint, OssBucket = oss.Bucket,
                ApplicationVersion = typeof(BackupService).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown"
            };
            record = record with { OssKey = $"{oss.Prefix}/{now:yyyy/MM/dd}/{record.Id}.db" };
            _activeRecord = record;
            _activeTask = Task.Run(() => ExecuteRunAsync(record, _lifetime.Token));
            return new BackupRun(record.Id, false);
        }
    }

    private async Task ExecuteRunAsync(BackupRecord record, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            using var lease = _store.AcquireLease();
            try
            {
                var now = _clock.GetUtcNow();
                var fullCheck = Records.FirstOrDefault(item => item.State == "Completed" && item.FullCheck)?.CompletedAt;
                record = record with { FullCheck = fullCheck is null || now - fullCheck >= _options.FullCheckInterval };
                _store.Save(record);
                PersistSchedule();
                _store.Cleanup(_options, now);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(_options.Timeout);
                record = await _snapshot.CreateAsync(_databasePath, record, _store, _options, timeout.Token);
                record = record with { State = "Completed", CompletedAt = _clock.GetUtcNow() };
                _store.Save(record);
                lock (_sync) _activeRecord = record;
                _logger.LogInformation("备份 {BackupId} 本地快照完成，{Size} 字节", record.Id, record.Size);
            }
            catch (Exception exception)
            {
                record = record with
                {
                    State = cancellationToken.IsCancellationRequested ? "Interrupted" : "Failed",
                    CompletedAt = _clock.GetUtcNow(), Error = SafeError(exception)
                };
                _store.Save(record);
                _store.DeleteTemporary(record.Id);
                lock (_sync) _activeRecord = record;
                _logger.LogError("备份 {BackupId} 失败：{Error}", record.Id, record.Error);
                return;
            }
            if (_options.Oss.Enabled)
                await UploadAsync(record, cancellationToken);
            _store.Cleanup(_options, _clock.GetUtcNow());
        }
        catch (Exception exception)
        {
            _maintenanceError = SafeError(exception);
            _logger.LogError("备份任务维护失败：{Error}", _maintenanceError);
            lock (_sync) _activeRecord = record with { State = "Failed", Error = _maintenanceError };
        }
        finally { _gate.Release(); }
    }

    public async Task TickAsync(CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled) return;
        await InitializeAsync(cancellationToken);
        var now = _clock.GetUtcNow();
        lock (_sync)
        {
            if (now >= _nextRunAt)
            {
                var skipped = (now - _nextRunAt.Value).Ticks / _options.Interval.Ticks + 1;
                _nextRunAt = _nextRunAt.Value.AddTicks(checked(skipped * _options.Interval.Ticks));
                _scheduleDirty = true;
                StartRun("scheduled");
            }
        }
        if (!await _gate.WaitAsync(0, cancellationToken)) return;
        try
        {
            using var lease = _store.AcquireLease();
            lock (_sync)
            {
                PersistSchedule();
            }
            if (_options.Oss.Enabled)
            {
                var pending = Records.Where(record => record.State == "Completed" &&
                    record.UploadState is "Pending" or "Retry" && (record.NextUploadAt is null || record.NextUploadAt <= now))
                    .OrderBy(record => record.StartedAt).FirstOrDefault();
                if (pending is not null) await UploadAsync(pending, cancellationToken);
            }
            _store.Cleanup(_options, now);
            _maintenanceError = null;
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            _maintenanceError = SafeError(exception);
            _logger.LogError("备份队列维护失败：{Error}", _maintenanceError);
        }
        finally { _gate.Release(); }
    }

    private async Task UploadAsync(BackupRecord record, CancellationToken cancellationToken)
    {
        record = record with { UploadState = "Uploading", UploadAttempts = record.UploadAttempts + 1 };
        _store.Save(record);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_options.UploadTimeout);
            await _uploader.UploadAsync(record, _store.GetPath(record.Id, ".db"), timeout.Token);
            record = record with { UploadState = "Uploaded", UploadedAt = _clock.GetUtcNow(), UploadError = null, NextUploadAt = null };
            _store.Save(record);
            _logger.LogInformation("备份 {BackupId} 已上传 OSS", record.Id);
        }
        catch (Exception exception)
        {
            var terminal = exception is InvalidDataException or FileNotFoundException ||
                exception.Message == "OssDestinationChanged";
            var seconds = Math.Min(_options.MaxUploadRetryInterval.TotalSeconds,
                _options.UploadRetryInterval.TotalSeconds * Math.Pow(2, Math.Min(record.UploadAttempts - 1, 20)));
            record = record with
            {
                UploadState = terminal ? "Failed" : "Retry", UploadError = SafeError(exception),
                NextUploadAt = terminal ? null : _clock.GetUtcNow().AddSeconds(seconds)
            };
            _store.Save(record);
            _logger.LogError("备份 {BackupId} 上传失败：{Error}，状态 {UploadState}", record.Id, record.UploadError, record.UploadState);
        }
    }

    public Task WaitForIdleAsync()
    {
        lock (_sync) return _activeTask;
    }

    private void PersistSchedule()
    {
        lock (_sync)
        {
            if (!_scheduleDirty) return;
            _store.SaveSchedule(new BackupSchedule(_nextRunAt!.Value, _options.Interval));
            _scheduleDirty = false;
        }
    }

    private static string SafeError(Exception exception) => exception.Message switch
    {
        "BackupCapacityExceeded" or "BackupForeignKeyCheckFailed" or "BackupIntegrityCheckFailed" or
        "BackupHashMismatch" or "OssDestinationChanged" => exception.Message,
        _ => exception is OperationCanceledException ? "CancelledOrTimedOut" : exception.GetType().Name
    };

    public async ValueTask DisposeAsync()
    {
        await _lifetime.CancelAsync();
        try { await WaitForIdleAsync(); } catch (OperationCanceledException) { }
        _lifetime.Dispose();
        _gate.Dispose();
    }
}
