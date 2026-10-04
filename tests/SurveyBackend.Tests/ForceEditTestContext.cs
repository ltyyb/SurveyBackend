using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SurveyBackend.Bot;
using SurveyBackend.Bot.Commands;
using SurveyBackend.Configuration;
using SurveyBackend.Controllers;
using SurveyBackend.Data;
using SurveyBackend.Models;
using Xunit;

namespace SurveyBackend.Tests;

internal sealed class ForceEditTestContext : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "SurveyBackend.Tests", Guid.NewGuid().ToString("N"));
    private readonly ServiceProvider _provider;
    public string DatabasePath => Path.Combine(_directory, "data.db");
    public FixedTimeProvider Clock { get; } = new(ReviewTestContext.Now);
    public RecordingOnebotService Onebot { get; } = new();
    public RecordingLogger<SurveyController> Logger { get; } = new();
    public ForceEditCommand Command { get; }
    public IServiceScopeFactory ScopeFactory => _provider.GetRequiredService<IServiceScopeFactory>();
    public const string AdminId = "AdminUser0000001";
    public const string UserId = "TargetUser000001";
    public const string SubmissionId = "Submission000001";
    public const string ReviewId = "ReviewData000001";

    public ForceEditTestContext(bool hasSubmission = true, UserGroup targetGroup = UserGroup.PendingUser)
    {
        Directory.CreateDirectory(_directory);
        var services = new ServiceCollection();
        ConfigureServices(services);
        _provider = services.BuildServiceProvider();
        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MainDbContext>();
        db.Database.Migrate();
        var admin = new User { UserId = AdminId, QQId = "111111", UserGroup = UserGroup.Admin };
        var target = new User { UserId = UserId, QQId = "222222", UserGroup = targetGroup };
        var questionnaire = new Questionnaire
        {
            QuestionnaireId = "Form0001",
            Survey = new Survey { SurveyId = "Survey01", Title = "入群问卷", IsVerifySurvey = true, NeedReview = true, UniquePerUser = true },
            SurveyJson = "{\"title\":\"{Specific_QQId} {Survey_Release_Date}\"}",
            ReleaseDate = Clock.GetUtcNow().UtcDateTime.AddDays(-1)
        };
        db.Users.AddRange(admin, target);
        db.Questionnaires.Add(questionnaire);
        if (hasSubmission)
        {
            var review = new ReviewSubmissionData
            {
                ReviewSubmissionDataId = ReviewId,
                Status = ReviewStatus.Approved,
                AIInsights = "旧见解",
                Submission = new Submission
                {
                    SubmissionId = SubmissionId, User = target, Questionnaire = questionnaire,
                    SurveyData = "{\"answer\":\"before\"}", CreatedAt = Clock.GetUtcNow().UtcDateTime.AddDays(-2)
                }
            };
            db.ReviewSubmissions.Add(review);
            db.ReviewVotes.Add(new ReviewVote { ReviewSubmissionData = review, User = admin, VoteType = VoteType.Upvote });
        }
        db.SaveChanges();
        Command = new ForceEditCommand(_provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new ApiOptions { SurveyLinkEndpoint = "https://example.test/survey" }), Clock);
    }

    private void ConfigureServices(IServiceCollection services)
    {
        services.AddDbContext<MainDbContext>(builder => builder.UseSqlite(SqliteDatabase.ConnectionString(DatabasePath, pooling: false)));
        services.AddSingleton<TimeProvider>(Clock);
        services.AddSingleton<IOnebotService>(Onebot);
        services.AddSingleton<ILogger<SurveyController>>(Logger);
        services.AddSingleton(Options.Create(new BotOptions { MainGroupId = 100, VerifyGroupId = 200 }));
        services.AddSingleton(Options.Create(new ApiOptions { SurveyLinkEndpoint = "https://example.test/survey/" }));
        services.AddSingleton(Options.Create(new LlmOptions()));
    }

    public MainDbContext OpenDb() => SqliteDatabase.CreateContext(DatabasePath);

    public SurveyController Controller(MainDbContext db) => new(Logger, NullLoggerFactory.Instance,
        Options.Create(new BotOptions { MainGroupId = 100, VerifyGroupId = 200 }),
        Options.Create(new ApiOptions { SurveyLinkEndpoint = "https://example.test/survey/" }),
        Options.Create(new LlmOptions()), Onebot, db, Clock);

    public async Task<string> IssueAsync(string identifier, string? surveyId = null)
    {
        var response = await Command.ExecuteAsync(Message(), surveyId is null ? [identifier] : [identifier, surveyId]);
        Assert.True(response!.Success, response.Message?.Raw);
        using var db = OpenDb();
        var requestId = System.Text.RegularExpressions.Regex.Match(response.Message!.Raw, "requestId=([A-Za-z0-9_-]+)").Groups[1].Value;
        var grant = await db.ForceEditGrants.SingleAsync(g => g.RequestId == requestId);
        Assert.Contains($"actions/forceEdit?requestId={grant.RequestId}", response.Message!.Raw);
        return grant.RequestId;
    }

    public async Task<WebApplication> StartApiAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.Configuration.Sources.Clear();
        builder.Configuration.AddInMemoryCollection();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        ConfigureServices(builder.Services);
        builder.Services.AddControllers().AddApplicationPart(typeof(SurveyController).Assembly);
        builder.Services.AddCors(options => options.AddPolicy("AllowAll", policy => policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));
        var app = builder.Build();
        app.UseCors("AllowAll");
        app.MapControllers();
        await app.StartAsync();
        return app;
    }

    public static Sisters.WudiLib.Posts.Message Message(long qqId = 111111, bool isPrivate = true,
        string commandText = "/survey force-edit")
    {
        var json = $$$"""{"time":1790812800,"self_id":999999,"post_type":"message","message_type":"{{{(isPrivate ? "private" : "group")}}}","sub_type":"{{{(isPrivate ? "friend" : "normal")}}}","message_id":1,"user_id":{{{qqId}}},"group_id":200,"message":"/survey force-edit","raw_message":"/survey force-edit","font":0,"sender":{"user_id":{{{qqId}}},"nickname":"test"}}""";
        var message = Newtonsoft.Json.Linq.JObject.Parse(json);
        message["message"] = commandText;
        message["raw_message"] = commandText;
        json = message.ToString();
        return isPrivate
            ? Newtonsoft.Json.JsonConvert.DeserializeObject<Sisters.WudiLib.Posts.PrivateMessage>(json)!
            : Newtonsoft.Json.JsonConvert.DeserializeObject<Sisters.WudiLib.Posts.GroupMessage>(json)!;
    }

    public void Dispose()
    {
        _provider.Dispose();
        Directory.Delete(_directory, recursive: true);
    }
}
