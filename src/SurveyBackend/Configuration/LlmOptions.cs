namespace SurveyBackend.Configuration;

public sealed class LlmOptions
{
    public const string SectionName = "LLM";

    public string ModelName { get; init; } = string.Empty;
    public string OpenAIKey { get; init; } = string.Empty;
    public string OpenAIEndpoint { get; init; } = string.Empty;
    public string SysPromptPath { get; init; } = string.Empty;
    public LlmReasoningEffort ReasoningEffort { get; init; } = LlmReasoningEffort.Medium;
    public bool UseWebSearch { get; init; }
}
