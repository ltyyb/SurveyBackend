using Microsoft.Extensions.Configuration;
using SurveyBackend.Configuration;
using Xunit;

namespace SurveyBackend.Tests;

public sealed class BackupOptionsTests
{
    [Fact]
    public void Bind_ExampleConfiguration_PreservesSafeDefaults()
    {
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.example.json")).Build();
        var options = configuration.GetSection("Backup").Get<BackupOptions>()!;
        Assert.False(options.Enabled);
        Assert.Equal(TimeSpan.FromHours(1), options.Interval);
        Assert.Equal(7, options.LocalRetentionDays);
        Assert.Equal(TimeSpan.FromMilliseconds(10), options.StepDelay);
        Assert.True(new BackupOptionsValidator().Validate(null, options).Succeeded);
    }

    [Fact]
    public void Bind_AllOssSettings_AreUnderBackupSection()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Backup:Enabled"] = "true", ["Backup:Interval"] = "06:00:00",
            ["Backup:Oss:Enabled"] = "true", ["Backup:Oss:Region"] = "cn-hangzhou",
            ["Backup:Oss:Bucket"] = "test-bucket", ["Backup:Oss:AccessKeyId"] = "synthetic-id",
            ["Backup:Oss:AccessKeySecret"] = "synthetic-secret", ["Backup:Oss:SecurityToken"] = "synthetic-token"
        }).Build();
        var options = configuration.GetSection("Backup").Get<BackupOptions>()!;
        Assert.Equal(TimeSpan.FromHours(6), options.Interval);
        Assert.Equal("synthetic-token", options.Oss.SecurityToken);
        Assert.True(new BackupOptionsValidator().Validate(null, options).Succeeded);
    }

    [Theory]
    [InlineData("00:00:00")]
    [InlineData("00:00:59")]
    [InlineData("31.00:00:00")]
    public void Validate_InvalidInterval_RejectsConfiguration(string interval)
    {
        var options = new BackupOptions { Enabled = true, Interval = TimeSpan.Parse(interval) };
        var result = new BackupOptionsValidator().Validate(null, options);
        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, failure => failure.Contains("Backup:Interval"));
    }

    [Theory]
    [InlineData("http://oss-cn-hangzhou.aliyuncs.com")]
    [InlineData("https://user:secret@example.test")]
    [InlineData("https://example.test/?token=secret")]
    public void Validate_UnsafeOssEndpoint_RejectsWithoutPrintingSecrets(string endpoint)
    {
        var options = new BackupOptions { Enabled = true };
        options.Oss.Enabled = true;
        options.Oss.Endpoint = endpoint;
        var result = new BackupOptionsValidator().Validate(null, options);
        Assert.True(result.Failed);
        Assert.DoesNotContain("secret", string.Join(" ", result.Failures!));
    }

    [Fact]
    public void GetDirectory_RelativeDirectory_UsesDatabaseDirectory()
    {
        var options = new BackupOptions();
        var root = Path.Combine(Path.GetTempPath(), "survey-backup-options");
        Assert.Equal(Path.Combine(root, "backups"), options.GetDirectory(Path.Combine(root, "data.db")));
    }
}
