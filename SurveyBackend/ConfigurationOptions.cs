namespace SurveyBackend;

public sealed class BotOptions
{
    public const string SectionName = "Bot";

    public string AccessToken { get; init; } = string.Empty;
    public int WsPort { get; init; }
    public long MainGroupId { get; init; }
    public long VerifyGroupId { get; init; }
    public long AdminId { get; init; }
}

public sealed class ApiOptions
{
    public const string SectionName = "API";

    public string Endpoint { get; init; } = string.Empty;
    public string SurveyLinkEndpoint { get; init; } = string.Empty;

    public string SurveyLinkBase => SurveyLinkEndpoint.EndsWith('/')
        ? SurveyLinkEndpoint
        : SurveyLinkEndpoint + "/";
}

public sealed class LlmOptions
{
    public const string SectionName = "LLM";

    public string ModelName { get; init; } = string.Empty;
    public string OpenAIKey { get; init; } = string.Empty;
    public string OpenAIEndpoint { get; init; } = string.Empty;
    public string SysPromptPath { get; init; } = string.Empty;
    public bool UseWebSearch { get; init; }
}

public sealed class ApplicationOptions
{
    public bool IsDisabled { get; init; }
}
