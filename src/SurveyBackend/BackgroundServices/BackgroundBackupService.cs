using SurveyBackend.Services.Backups;

namespace SurveyBackend.BackgroundServices;

public sealed class BackgroundBackupService(BackupService backups, IOnebotService onebot,
    IOptions<BotOptions> botOptions, TimeProvider clock, ILogger<BackgroundBackupService> logger) : BackgroundService
{
    private DateTimeOffset? _nextAlertAt;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!backups.Options.Enabled) return;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CheckAsync(stoppingToken);
            }
            catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError("备份后台服务失败：{ErrorType}", exception.GetType().Name);
                await NotifyAsync("备份后台服务异常，请检查日志和目录权限。", stoppingToken);
            }
            await Task.Delay(TimeSpan.FromSeconds(5), clock, stoppingToken);
        }
    }

    internal async Task CheckAsync(CancellationToken cancellationToken)
    {
        await backups.TickAsync(cancellationToken);
        var records = backups.Records;
        var latest = backups.Latest;
        var now = clock.GetUtcNow();
        var lastSuccess = records.FirstOrDefault(record => record.State == "Completed");
        var overdue = lastSuccess is not null && now - lastSuccess.CompletedAt > backups.Options.Interval * 2;
        var uploadProblem = records.Any(record => record.State == "Completed" &&
            (record.UploadState == "Failed" || record.UploadState == "Retry" && record.UploadAttempts >= 3 ||
             record.UploadState == "Pending" && now - record.StartedAt > backups.Options.Interval));
        if (latest?.State is "Failed" or "Interrupted" || overdue || uploadProblem || backups.MaintenanceError is not null)
            await NotifyAsync("数据库备份异常：本地快照失败、备份过期或 OSS 上传积压。请使用 /survey backup 和 /survey backup list 查看。", cancellationToken);
    }

    private async Task NotifyAsync(string message, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        if (_nextAlertAt > now || !onebot.IsAvailable)
            return;
        _nextAlertAt = now.AddMinutes(5);
        try
        {
            var response = await onebot.SendPrivateMessageAsync(botOptions.Value.AdminId, message).WaitAsync(cancellationToken);
            if (response is not null) _nextAlertAt = now.AddHours(1);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogError("备份告警发送失败：{ErrorType}", exception.GetType().Name);
        }
    }
}
