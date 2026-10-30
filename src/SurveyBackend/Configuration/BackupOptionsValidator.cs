namespace SurveyBackend.Configuration;

public sealed class BackupOptionsValidator : IValidateOptions<BackupOptions>
{
    public ValidateOptionsResult Validate(string? name, BackupOptions options)
    {
        if (!options.Enabled)
            return ValidateOptionsResult.Success;

        var failures = new List<string>();
        if (options.Interval < TimeSpan.FromMinutes(1) || options.Interval > TimeSpan.FromDays(30))
            failures.Add("Backup:Interval 必须在 1 分钟到 30 天之间。");
        if (string.IsNullOrWhiteSpace(options.Directory))
            failures.Add("Backup:Directory 不能为空。");
        if (options.LocalRetentionDays is < 1 or > 3650)
            failures.Add("Backup:LocalRetentionDays 必须在 1 到 3650 之间。");
        if (options.MaxLocalBytes < 1024 * 1024 || options.MinFreeDiskBytes < 0)
            failures.Add("Backup:MaxLocalBytes 至少为 1 MiB，MinFreeDiskBytes 不能为负数。");
        CheckDuration(options.Timeout, "Timeout");
        CheckDuration(options.UploadTimeout, "UploadTimeout");
        if (options.PagesPerStep is < 1 or > 4096)
            failures.Add("Backup:PagesPerStep 必须在 1 到 4096 之间。");
        if (options.StepDelay < TimeSpan.Zero || options.StepDelay > TimeSpan.FromSeconds(1))
            failures.Add("Backup:StepDelay 必须在 0 到 1 秒之间。");
        if (options.FullCheckInterval < TimeSpan.FromMinutes(1) || options.FullCheckInterval > TimeSpan.FromDays(30))
            failures.Add("Backup:FullCheckInterval 必须在 1 分钟到 30 天之间。");
        CheckDuration(options.UploadRetryInterval, "UploadRetryInterval");
        CheckDuration(options.MaxUploadRetryInterval, "MaxUploadRetryInterval");
        if (options.MaxUploadRetryInterval < options.UploadRetryInterval)
            failures.Add("Backup:MaxUploadRetryInterval 不能小于 UploadRetryInterval。");

        if (options.Oss.Enabled)
        {
            var oss = options.Oss;
            if (string.IsNullOrWhiteSpace(oss.Region) || string.IsNullOrWhiteSpace(oss.Bucket))
                failures.Add("Backup:Oss:Region 和 Bucket 不能为空。");
            if (string.IsNullOrWhiteSpace(oss.AccessKeyId) || string.IsNullOrWhiteSpace(oss.AccessKeySecret))
                failures.Add("Backup:Oss:AccessKeyId 和 AccessKeySecret 不能为空。");
            if (!string.IsNullOrEmpty(oss.Endpoint) &&
                (!Uri.TryCreate(oss.Endpoint, UriKind.Absolute, out var uri) || uri.Scheme != "https" ||
                 uri.AbsolutePath != "/" || uri.Query.Length != 0 || uri.Fragment.Length != 0 || uri.UserInfo.Length != 0))
                failures.Add("Backup:Oss:Endpoint 必须是无路径、查询参数和凭据的 HTTPS 地址，或留空。");
            if (string.IsNullOrWhiteSpace(oss.Prefix) || oss.Prefix.StartsWith('/') ||
                oss.Prefix.Contains('\\') || oss.Prefix.Any(char.IsControl) ||
                oss.Prefix.Split('/').Any(part => part is "" or "." or ".."))
                failures.Add("Backup:Oss:Prefix 必须是非空对象前缀，不能包含空段或相对路径段。");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);

        void CheckDuration(TimeSpan value, string key)
        {
            if (value < TimeSpan.FromSeconds(1) || value > TimeSpan.FromHours(1))
                failures.Add($"Backup:{key} 必须在 1 秒到 1 小时之间。");
        }
    }
}
