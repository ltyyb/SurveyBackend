namespace SurveyBackend.Configuration;

public sealed class BotOptions
{
    public const string SectionName = "Bot";

    public string AccessToken { get; init; } = string.Empty;
    public int WsPort { get; init; }
    public long MainGroupId { get; init; }
    public long VerifyGroupId { get; init; }
    public long AdminId { get; init; }
}
