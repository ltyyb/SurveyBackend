namespace SurveyBackend.Configuration;

public sealed class ReviewOptions
{
    public const string SectionName = "Review";

    public int MinimumVotes { get; init; } = 4;
    public double AgreeRateThreshold { get; init; } = 0.6;
}
