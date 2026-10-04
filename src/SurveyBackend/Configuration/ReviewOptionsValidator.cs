namespace SurveyBackend.Configuration;

public sealed class ReviewOptionsValidator : IValidateOptions<ReviewOptions>
{
    public ValidateOptionsResult Validate(string? name, ReviewOptions options)
    {
        var failures = new List<string>();
        if (options.MinimumVotes < 1)
        {
            failures.Add("Review:MinimumVotes 必须是正整数。");
        }
        if (!double.IsFinite(options.AgreeRateThreshold) || options.AgreeRateThreshold is < 0 or > 1)
        {
            failures.Add("Review:AgreeRateThreshold 必须是 0 到 1 之间的有限数值。");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
