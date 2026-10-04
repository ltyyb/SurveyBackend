using SurveyBackend.Configuration;
using Xunit;

namespace SurveyBackend.Tests;

public class ReviewOptionsValidatorTests
{
    private readonly ReviewOptionsValidator _validator = new();

    [Theory]
    [InlineData(1, 0.0)]
    [InlineData(4, 0.6)]
    [InlineData(int.MaxValue, 1.0)]
    public void Validate_ValidThresholds_Succeeds(int minimumVotes, double rate)
    {
        var result = _validator.Validate(null, new ReviewOptions
        {
            MinimumVotes = minimumVotes,
            AgreeRateThreshold = rate
        });

        Assert.True(result.Succeeded);
        Assert.False(result.Failed);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void Validate_NonPositiveMinimumVotes_ReportsMinimumVotes(int minimumVotes)
    {
        var result = _validator.Validate(null, new ReviewOptions { MinimumVotes = minimumVotes });

        Assert.True(result.Failed);
        Assert.Contains("Review:MinimumVotes", Assert.Single(result.Failures!));
    }

    [Theory]
    [InlineData(-0.001)]
    [InlineData(1.001)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void Validate_InvalidRate_ReportsAgreeRateThreshold(double rate)
    {
        var result = _validator.Validate(null, new ReviewOptions { AgreeRateThreshold = rate });

        Assert.True(result.Failed);
        Assert.Contains("Review:AgreeRateThreshold", Assert.Single(result.Failures!));
    }

    [Fact]
    public void Validate_BothThresholdsInvalid_ReturnsBothFailures()
    {
        var result = _validator.Validate(null, new ReviewOptions
        {
            MinimumVotes = 0,
            AgreeRateThreshold = double.NaN
        });

        Assert.True(result.Failed);
        var failures = result.Failures!.ToArray();
        Assert.Equal(2, failures.Length);
        Assert.Contains(failures, failure => failure.Contains("Review:MinimumVotes"));
        Assert.Contains(failures, failure => failure.Contains("Review:AgreeRateThreshold"));
    }
}
