namespace SurveyBackend.Configuration;

public sealed class BackupOptions
{
    public const string SectionName = "Backup";

    public bool Enabled { get; set; }
    public TimeSpan Interval { get; set; } = TimeSpan.FromHours(1);
    public string Directory { get; set; } = "backups";
    public int LocalRetentionDays { get; set; } = 7;
    public long MaxLocalBytes { get; set; } = 10L * 1024 * 1024 * 1024;
    public long MinFreeDiskBytes { get; set; } = 512L * 1024 * 1024;
    public TimeSpan Timeout { get; set; } = TimeSpan.FromMinutes(5);
    public int PagesPerStep { get; set; } = 128;
    public TimeSpan StepDelay { get; set; } = TimeSpan.FromMilliseconds(10);
    public TimeSpan FullCheckInterval { get; set; } = TimeSpan.FromDays(1);
    public TimeSpan UploadTimeout { get; set; } = TimeSpan.FromMinutes(5);
    public TimeSpan UploadRetryInterval { get; set; } = TimeSpan.FromMinutes(1);
    public TimeSpan MaxUploadRetryInterval { get; set; } = TimeSpan.FromMinutes(30);
    public BackupOssOptions Oss { get; set; } = new();

    public string GetDirectory(string databasePath) =>
        Path.GetFullPath(Directory, Path.GetDirectoryName(Path.GetFullPath(databasePath))!);
}
