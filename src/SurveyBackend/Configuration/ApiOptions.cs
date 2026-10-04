namespace SurveyBackend.Configuration;

public sealed class ApiOptions
{
    public const string SectionName = "API";

    public string Endpoint { get; init; } = string.Empty;
    public string SurveyLinkEndpoint { get; init; } = string.Empty;

    public string SurveyLinkBase => SurveyLinkEndpoint.EndsWith('/')
        ? SurveyLinkEndpoint
        : SurveyLinkEndpoint + "/";
}
