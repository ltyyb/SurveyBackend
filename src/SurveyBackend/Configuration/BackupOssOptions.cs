namespace SurveyBackend.Configuration;

public sealed class BackupOssOptions
{
    public bool Enabled { get; set; }
    public string Region { get; set; } = "";
    public string Endpoint { get; set; } = "";
    public string Bucket { get; set; } = "";
    public string Prefix { get; set; } = "survey-backend/production";
    public string AccessKeyId { get; set; } = "";
    public string AccessKeySecret { get; set; } = "";
    public string SecurityToken { get; set; } = "";
}
