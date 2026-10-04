using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SurveyBackend.Bot.Commands;
using SurveyBackend.Bot.Commands.Infrastructure;
using SurveyBackend.Configuration;
using SurveyBackend.Data;
using SurveyBackend.Models;
using Xunit;

namespace SurveyBackend.Tests;

[Collection("Time zone")]
public sealed class CommandTimeZoneTests : IDisposable
{
    private readonly string? _originalTimeZone = Environment.GetEnvironmentVariable("TZ");

    [Theory]
    [InlineData("Asia/Shanghai", 8)]
    [InlineData("UTC", 0)]
    [InlineData("America/New_York", -5)]
    public async Task Commands_DatabaseUtcTimes_DisplayConfiguredTimeZoneWithoutChangingStoredValues(string timeZone, int offsetHours)
    {
        Environment.SetEnvironmentVariable("TZ", timeZone);
        using var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=True");
        await connection.OpenAsync();
        var services = new ServiceCollection();
        services.AddDbContext<MainDbContext>(options => options.UseSqlite(connection));
        using var provider = services.BuildServiceProvider();
        var createdAt = new DateTime(2026, 1, 1, 20, 30, 45, DateTimeKind.Utc);
        var releaseDate = new DateTime(2026, 1, 1, 21, 45, 15, DateTimeKind.Utc);
        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MainDbContext>();
            await db.Database.MigrateAsync();
            var pendingUser = new User { QQId = "111111", UserGroup = UserGroup.PendingUser };
            var survey = new Survey
            {
                SurveyId = "surv0001", NeedReview = true, IsVerifySurvey = true, CreatedAt = createdAt
            };
            var questionnaire = new Questionnaire
            {
                QuestionnaireId = "form0001", Survey = survey, SurveyJson = "{}", ReleaseDate = releaseDate
            };
            var submission = new Submission
            {
                SubmissionId = "submit0001", Questionnaire = questionnaire, User = pendingUser,
                SurveyData = "{}", CreatedAt = createdAt
            };
            db.ReviewSubmissions.Add(new ReviewSubmissionData
            {
                ReviewSubmissionDataId = "review0001", Submission = submission, Status = ReviewStatus.Pending
            });
            db.Submissions.Add(new Submission
            {
                SubmissionId = "submit0002", User = pendingUser, SurveyData = "{}", CreatedAt = createdAt,
                Questionnaire = new Questionnaire
                {
                    QuestionnaireId = "form0002", SurveyJson = "{}", ReleaseDate = releaseDate,
                    Survey = new Survey { SurveyId = "surv0002", CreatedAt = createdAt }
                }
            });
            db.Requests.Add(new Request { RequestId = "request0001", User = pendingUser, CreatedAt = createdAt });
            db.Users.Add(new User { QQId = "222222", UserGroup = UserGroup.VerifiedUser });
            await db.SaveChangesAsync();
        }

        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();
        var registry = new SurveyCommandRegistry(scopeFactory);
        registry.RegisterCommand(new InfoCommand(scopeFactory));
        registry.RegisterCommand(new CheckCommand(scopeFactory));
        registry.RegisterCommand(new ReviewCommand(scopeFactory,
            Options.Create(new ApiOptions { SurveyLinkEndpoint = "https://example.com/survey" })));
        var expectedCreatedAt = createdAt.AddHours(offsetHours).ToString();
        var expectedReleaseDate = releaseDate.AddHours(offsetHours).ToString();

        Assert.Contains($"创建时间: {expectedCreatedAt}", await ExecuteAsync("info surv0001"));
        Assert.Contains($"发布日期: {expectedReleaseDate}", await ExecuteAsync("info form0001"));
        Assert.Contains($"提交时间: {expectedCreatedAt}", await ExecuteAsync("info request0001"));
        foreach (var command in new[] { "info submit0001", "info submit0002", "info review0001", "review submit0001", "rv submit0001", "check" })
        {
            var message = await ExecuteAsync(command, command == "check" ? 111111 : 222222);
            Assert.Contains($"提交时间: {expectedCreatedAt}", message);
            Assert.Contains($"({expectedReleaseDate})", message);
        }

        foreach (var (command, userId) in new[] { ("info surv0001", 111111L), ("review submit0001", 111111L), ("check", 222222L) })
        {
            var response = await registry.TryExecuteSurveyCommandAsync(CreateMessage(command, userId));
            Assert.NotNull(response);
            Assert.False(response.Success);
            Assert.Contains("权限不足", response.Message!.Raw);
        }

        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MainDbContext>();
            var storedTimes = (await db.Surveys.Select(s => s.CreatedAt).ToListAsync())
                .Concat(await db.Submissions.Select(s => s.CreatedAt).ToListAsync())
                .Concat(await db.Requests.Select(r => r.CreatedAt).ToListAsync());
            Assert.All(storedTimes, value =>
            {
                Assert.Equal(createdAt, value);
                Assert.Equal(DateTimeKind.Utc, value.Kind);
            });
            Assert.All(await db.Questionnaires.Select(q => q.ReleaseDate).ToListAsync(), value =>
            {
                Assert.Equal(releaseDate, value);
                Assert.Equal(DateTimeKind.Utc, value.Kind);
            });
        }

        async Task<string> ExecuteAsync(string command, long userId = 222222)
        {
            var response = await registry.TryExecuteSurveyCommandAsync(CreateMessage(command, userId));
            Assert.NotNull(response);
            Assert.True(response.Success, response.Message?.Raw);
            return Assert.IsType<Sisters.WudiLib.SendingMessage>(response.Message).Raw;
        }
    }

    [Theory]
    [InlineData(6, 59, 59, 1, 59, 59)]
    [InlineData(7, 0, 0, 3, 0, 0)]
    public void ToDisplayTime_DaylightSavingTransition_UsesOffsetAtStoredInstant(
        int utcHour, int utcMinute, int utcSecond, int localHour, int localMinute, int localSecond)
    {
        Environment.SetEnvironmentVariable("TZ", "America/New_York");
        var utcTime = new DateTime(2026, 3, 8, utcHour, utcMinute, utcSecond, DateTimeKind.Utc);

        Assert.Equal(new DateTime(2026, 3, 8, localHour, localMinute, localSecond), utcTime.ToDisplayTime());
    }

    [Fact]
    public void ToDisplayTime_NoTimeZoneEnvironmentVariable_UsesSystemTimeZone()
    {
        Environment.SetEnvironmentVariable("TZ", null);
        var utcTime = new DateTime(2026, 1, 1, 20, 30, 45, DateTimeKind.Utc);

        Assert.Equal(utcTime.ToLocalTime(), utcTime.ToDisplayTime());
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("TZ", _originalTimeZone);
    }

    private static Sisters.WudiLib.Posts.GroupMessage CreateMessage(string command, long userId)
    {
        return Newtonsoft.Json.JsonConvert.DeserializeObject<Sisters.WudiLib.Posts.GroupMessage>(
            $$$"""{"time":1790812800,"self_id":999999,"post_type":"message","message_type":"group","sub_type":"normal","message_id":1,"user_id":{{{userId}}},"group_id":200,"message":"/survey {{{command}}}","raw_message":"/survey {{{command}}}","font":0,"sender":{"user_id":{{{userId}}},"nickname":"test"}}""")!;
    }
}
