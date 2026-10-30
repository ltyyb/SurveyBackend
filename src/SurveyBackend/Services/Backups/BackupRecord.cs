namespace SurveyBackend.Services.Backups;

public sealed record BackupRecord
{
    public required string Id { get; init; }
    public required DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }
    public string Trigger { get; init; } = "manual";
    public string State { get; init; } = "Running";
    public string UploadState { get; init; } = "Disabled";
    public string? Error { get; init; }
    public string? UploadError { get; init; }
    public long Size { get; init; }
    public string? Sha256 { get; init; }
    public bool FullCheck { get; init; }
    public string ApplicationVersion { get; init; } = "";
    public string[] Migrations { get; init; } = [];
    public string OssRegion { get; init; } = "";
    public string OssEndpoint { get; init; } = "";
    public string OssBucket { get; init; } = "";
    public string OssKey { get; init; } = "";
    public int UploadAttempts { get; init; }
    public DateTimeOffset? NextUploadAt { get; init; }
    public DateTimeOffset? UploadedAt { get; init; }
}
