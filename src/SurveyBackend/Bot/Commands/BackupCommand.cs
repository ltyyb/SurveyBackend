using System.Globalization;
using SurveyBackend.Services.Backups;
using MessageContext = Sisters.WudiLib.Posts.Message;

namespace SurveyBackend.Bot.Commands;

public sealed class BackupCommand(IServiceScopeFactory scopeFactory, BackupService backups,
    ILogger<BackupCommand> logger) : AuthorizedAsyncCommand(scopeFactory)
{
    public override string CommandName => "backup";
    public override string Description => "查看备份结果；run 立即备份，list [页码] 查看本地文件，info 查看配置。";
    public override bool IsSuperCommand => true;

    protected override async Task<CommandResponse?> ExecuteAuthorizedAsync(MessageContext context, string[] args,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (args.Length == 0) return CommandResponse.SuccessResponse(Status());
            if (args is ["info"]) return CommandResponse.SuccessResponse(Info());
            if (args is ["run"])
            {
                if (!backups.Options.Enabled) return CommandResponse.FailureResponse("备份未启用，请先配置 Backup 并重启。");
                var run = await backups.RunAsync(cancellationToken);
                return CommandResponse.SuccessResponse($"{(run.Merged ? "已合并到正在执行的备份任务" : "备份任务已开始")}：{run.Id}\n使用 /survey backup 查看执行结果。");
            }
            if (args is ["list"])
                return CommandResponse.SuccessResponse(List(1));
            if (args is ["list", var pageText] && int.TryParse(pageText, NumberStyles.None,
                    CultureInfo.InvariantCulture, out var page) && page > 0)
                return CommandResponse.SuccessResponse(List(page));
            return CommandResponse.FailureResponse("用法：/survey backup | backup run | backup list [页码] | backup info");
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogError("备份命令失败：{ErrorType}", exception.GetType().Name);
            return CommandResponse.FailureResponse("备份操作失败，请检查服务日志及目录权限。已有备份不会因此被替换。");
        }
    }

    private string Status()
    {
        var latest = backups.Latest;
        var lastSuccess = backups.Records.FirstOrDefault(record => record.State == "Completed");
        return $"""
            备份：{(backups.Options.Enabled ? "已启用" : "未启用")}，{(backups.IsRunning ? "任务执行中" : "空闲")}
            最近任务：{latest?.Id ?? "暂无"}
            本地结果：{LocalState(latest?.State)}
            开始：{DisplayTime(latest?.StartedAt)}
            完成：{DisplayTime(latest?.CompletedAt)}
            文件大小：{latest?.Size ?? 0} 字节
            上传状态：{UploadState(latest?.UploadState)}
            上传完成：{DisplayTime(latest?.UploadedAt)}
            下次重试：{DisplayTime(latest?.NextUploadAt)}
            失败摘要：{latest?.Error ?? latest?.UploadError ?? backups.MaintenanceError ?? "无"}
            最近成功快照：{lastSuccess?.Id ?? "暂无"}
            下次自动备份：{DisplayTime(backups.NextRunAt)}
            """;
    }

    private string List(int page)
    {
        var files = backups.LocalFiles;
        const int pageSize = 10;
        var pages = Math.Max(1, (files.Count + pageSize - 1) / pageSize);
        if (page > pages) return $"页码超出范围，共 {pages} 页。";
        var lines = files.Skip((page - 1) * pageSize).Take(pageSize).Select(record =>
            $"{record.Id}.db\n{DisplayTime(record.StartedAt)}，{record.Size} 字节\n本地：{LocalState(record.State)}；上传：{UploadState(record.UploadState)}{(record.UploadError is null ? "" : $"（{record.UploadError}）")}");
        return $"本地备份：{files.Count} 份，第 {page}/{pages} 页\n" +
            (files.Count == 0 ? "暂无备份文件。" : string.Join("\n\n", lines));
    }

    private string Info()
    {
        var options = backups.Options;
        var oss = options.Oss;
        return $"""
            备份：{(options.Enabled ? "已启用" : "未启用")}
            自动周期：{options.Interval}
            本地目录：{backups.DirectoryPath}
            保留天数：{options.LocalRetentionDays}
            容量上限：{options.MaxLocalBytes} 字节
            最低剩余空间：{options.MinFreeDiskBytes} 字节
            快照时限：{options.Timeout}；上传时限：{options.UploadTimeout}
            复制批次：{options.PagesPerStep} 页；批次间隔：{options.StepDelay}
            完整校验周期：{options.FullCheckInterval}
            上传重试间隔：{options.UploadRetryInterval}，最大 {options.MaxUploadRetryInterval}
            OSS：{(oss.Enabled ? "已启用" : "未启用")}
            Region：{oss.Region}
            Endpoint：{DisplayEndpoint(oss.Endpoint)}
            Bucket：{oss.Bucket}
            Prefix：{oss.Prefix}
            凭据：{(!string.IsNullOrWhiteSpace(oss.AccessKeyId) && !string.IsNullOrWhiteSpace(oss.AccessKeySecret) ? "已配置（隐藏）" : "未配置")}
            加密及远端保留：由 OSS Bucket 的 SSE-KMS 和生命周期管理
            下次自动备份：{DisplayTime(backups.NextRunAt)}
            配置修改后重启生效。
            """;
    }

    private static string DisplayTime(DateTimeOffset? value) => value?.UtcDateTime.ToDisplayTime().ToString("yyyy-MM-dd HH:mm:ss") ?? "暂无";
    private static string DisplayEndpoint(string endpoint) => string.IsNullOrWhiteSpace(endpoint)
        ? "按 Region 自动选择 HTTPS 地址"
        : Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) && uri.Scheme == "https" &&
          uri.UserInfo.Length == 0 && uri.Query.Length == 0 && uri.Fragment.Length == 0 && uri.AbsolutePath == "/"
            ? endpoint : "无效地址（已隐藏）";
    private static string LocalState(string? state) => state switch
    {
        "Running" => "执行中", "Completed" => "成功", "Failed" => "失败", "Interrupted" => "中断", _ => "暂无"
    };
    private static string UploadState(string? state) => state switch
    {
        "Disabled" => "未启用", "Pending" => "待上传", "Uploading" => "上传中", "Uploaded" => "已上传",
        "Retry" => "等待重试", "Failed" => "上传失败（需处理）", _ => "暂无"
    };
}
