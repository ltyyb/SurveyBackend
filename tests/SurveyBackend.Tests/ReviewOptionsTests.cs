using System.Globalization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SurveyBackend.Configuration;
using Xunit;

namespace SurveyBackend.Tests;

public class ReviewOptionsTests
{
    [Theory]
    [InlineData(null, null, 4, 0.6)]
    [InlineData("7", "0.8", 7, 0.8)]
    [InlineData("1", "0", 1, 0.0)]
    [InlineData("4", "1", 4, 1.0)]
    [InlineData("8", null, 8, 0.6)]
    [InlineData(null, "0.75", 4, 0.75)]
    [InlineData("2147483647", "0.6", int.MaxValue, 0.6)]
    public async Task StartAsync_ValidOrMissingReviewConfiguration_UsesExpectedOptions(
        string? minimumVotes, string? rate, int expectedMinimumVotes, double expectedRate)
    {
        using var host = CreateHost(minimumVotes, rate);
        await host.StartAsync();
        var options = host.Services.GetRequiredService<IOptions<ReviewOptions>>().Value;
        Assert.Equal(expectedMinimumVotes, options.MinimumVotes);
        Assert.Equal(expectedRate, options.AgreeRateThreshold);
        await host.StopAsync();
    }

    [Theory]
    [InlineData("0", "0.6", "Review:MinimumVotes")]
    [InlineData("-1", "0.6", "Review:MinimumVotes")]
    [InlineData("4", "-0.1", "Review:AgreeRateThreshold")]
    [InlineData("4", "1.1", "Review:AgreeRateThreshold")]
    [InlineData("4", "NaN", "Review:AgreeRateThreshold")]
    [InlineData("4", "Infinity", "Review:AgreeRateThreshold")]
    [InlineData("4", "-Infinity", "Review:AgreeRateThreshold")]
    public async Task StartAsync_InvalidReviewConfiguration_ThrowsValidationError(
        string minimumVotes, string rate, string expectedKey)
    {
        using var host = CreateHost(minimumVotes, rate);
        var exception = await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync());
        Assert.Contains(expectedKey, exception.Message);
    }

    [Theory]
    [InlineData("text", "0.6")]
    [InlineData("2147483648", "0.6")]
    [InlineData("3.5", "0.6")]
    [InlineData("4", "text")]
    [InlineData("", "0.6")]
    [InlineData("4", "")]
    public async Task StartAsync_UnparseableReviewConfiguration_ThrowsBindingError(string minimumVotes, string rate)
    {
        using var host = CreateHost(minimumVotes, rate);
        await Assert.ThrowsAsync<InvalidOperationException>(() => host.StartAsync());
    }

    [Fact]
    public async Task StartAsync_ExampleJson_BindsAndValidatesDocumentedDefaults()
    {
        var examplePath = Path.Combine(AppContext.BaseDirectory, "appsettings.example.json");
        using var host = CreateHost(configure: configuration => configuration.AddJsonFile(examplePath));

        await host.StartAsync();

        var options = host.Services.GetRequiredService<IOptions<ReviewOptions>>().Value;
        Assert.Equal(4, options.MinimumVotes);
        Assert.Equal(0.6, options.AgreeRateThreshold);
        await host.StopAsync();
    }

    [Fact]
    public async Task StartAsync_JsonOverride_BindsConfiguredReviewThresholds()
    {
        using var json = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(
            """{"Review":{"MinimumVotes":7,"AgreeRateThreshold":0.8}}"""));
        using var host = CreateHost(configure: configuration => configuration.AddJsonStream(json));

        await host.StartAsync();

        var options = host.Services.GetRequiredService<IOptions<ReviewOptions>>().Value;
        Assert.Equal(7, options.MinimumVotes);
        Assert.Equal(0.8, options.AgreeRateThreshold);
        await host.StopAsync();
    }

    [Fact]
    public async Task StartAsync_MultipleInvalidFields_ReportsBothConfigurationKeys()
    {
        using var host = CreateHost("0", "1.1");

        var exception = await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync());

        var failures = exception.Failures.ToArray();
        Assert.Equal(2, failures.Length);
        Assert.Contains(failures, failure => failure.Contains("Review:MinimumVotes"));
        Assert.Contains(failures, failure => failure.Contains("Review:AgreeRateThreshold"));
    }

    [Theory]
    [InlineData("fr-FR")]
    [InlineData("zh-CN")]
    public async Task StartAsync_DifferentCurrentCulture_BindsRateUsingInvariantFormat(string cultureName)
    {
        var previousCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(cultureName);
            using var host = CreateHost("4", "0.75");

            await host.StartAsync();

            Assert.Equal(0.75, host.Services.GetRequiredService<IOptions<ReviewOptions>>().Value.AgreeRateThreshold);
            await host.StopAsync();
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
        }
    }

    private static IHost CreateHost(string? minimumVotes = null, string? rate = null,
        Action<IConfigurationBuilder>? configure = null)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Configuration.Sources.Clear();
        var values = new Dictionary<string, string?>();
        if (minimumVotes is not null)
        {
            values["Review:MinimumVotes"] = minimumVotes;
        }
        if (rate is not null)
        {
            values["Review:AgreeRateThreshold"] = rate;
        }
        builder.Configuration.AddInMemoryCollection(values);
        configure?.Invoke(builder.Configuration);
        builder.Services.AddSingleton<IValidateOptions<ReviewOptions>, ReviewOptionsValidator>();
        builder.Services.AddOptions<ReviewOptions>()
            .BindConfiguration(ReviewOptions.SectionName)
            .ValidateOnStart();
        return builder.Build();
    }
}
