using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SurveyBackend.Bot.Commands;
using SurveyBackend.Configuration;
using SurveyBackend.Controllers;
using SurveyBackend.Data;
using SurveyBackend.Models;
using SurveyBackend.Services.Statistics;
using Xunit;

namespace SurveyBackend.Tests;

public sealed class SqliteMigrationTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "SurveyBackend.Tests", Guid.NewGuid().ToString("N"));
    private string DumpPath => Path.Combine(_directory, "dump.sql");
    private string DatabasePath => Path.Combine(_directory, "data.db");

    public SqliteMigrationTests()
    {
        Directory.CreateDirectory(_directory);
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "mysql-dump.sql"), DumpPath);
    }

    [Fact]
    public async Task Import_MySqlData_PreservesValuesQueriesAndAutoIncrement()
    {
        var result = MySqlDumpImporter.Import(DumpPath, DatabasePath);
        Assert.Equal(11, result.RowCounts.Values.Sum());
        using var db = SqliteDatabase.CreateContext(DatabasePath);
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        Assert.False(db.Database.HasPendingModelChanges());
        SqliteDatabase.Initialize(db);
        var submission = await db.Submissions.Include(s => s.User).Include(s => s.Questionnaire)
            .SingleAsync(s => s.SubmissionId == "submitmixedcase1");
        Assert.Equal(DateTimeKind.Utc, submission.CreatedAt.Kind);
        Assert.Equal(UserGroup.PendingUser, submission.User.UserGroup);
        Assert.Equal(new[] { "页面一" }, submission.Questionnaire.LLMPageNames);
        Assert.Null((await db.Questionnaires.SingleAsync(q => q.QuestionnaireId == "FormAb02")).LLMPageNames);
        Assert.Contains("中文😀", submission.SurveyData);
        var review = await db.ReviewSubmissions.SingleAsync();
        Assert.Equal("中文😀;逗号,单引号'和双引号\"及反斜杠\\\n第二行", review.AIInsights);
        Assert.Equal(ReviewStatus.Pending, review.Status);
        var cutoff = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
        Assert.Equal(1, await db.Submissions.CountAsync(s => s.CreatedAt <= cutoff));
        Assert.Equal(0, await db.Submissions.CountAsync(s => s.CreatedAt < cutoff));
        Assert.Equal(new[] { "FormAb02", "FormAb01" }, await db.Questionnaires.OrderBy(q => q.ReleaseDate).Select(q => q.QuestionnaireId).ToArrayAsync());
        Assert.Single(await db.Submissions.Where(s => EF.Functions.Like(s.SubmissionId, "submitmi%")).ToListAsync());
        var vote = new ReviewVote { ReviewSubmissionData = review, User = submission.User, VoteType = VoteType.Downvote };
        db.ReviewVotes.Add(vote);
        await db.SaveChangesAsync();
        Assert.Equal(100, vote.Id);
        db.Questionnaires.Single(q => q.QuestionnaireId == "FormAb01").LLMPageNames![0] = "修改页面";
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        Assert.Equal(new[] { "修改页面" }, (await db.Questionnaires.SingleAsync(q => q.QuestionnaireId == "FormAb01")).LLMPageNames);
    }

    [Fact]
    public async Task Import_MySqlData_ControllersPermissionsAndStatisticsUseImportedRelationships()
    {
        MySqlDumpImporter.Import(DumpPath, DatabasePath);
        using var db = SqliteDatabase.CreateContext(DatabasePath);
        var requestController = new RequestController(db) { ControllerContext = new() { HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext() } };
        Assert.IsType<Microsoft.AspNetCore.Mvc.OkObjectResult>(await requestController.GetRequestInfo("requestmixed001"));
        Assert.IsType<Microsoft.AspNetCore.Mvc.OkObjectResult>(await requestController.GetUserOfRequest("requestmixed001"));
        var controller = new SurveyController(NullLogger<SurveyController>.Instance, NullLoggerFactory.Instance,
            Options.Create(new BotOptions()), Options.Create(new ApiOptions()), Options.Create(new LlmOptions()),
            new RecordingOnebotService(), db)
        { ControllerContext = new() { HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext() } };
        var duplicate = await controller.GetSurveyAsync("formab01", "usermixedcase01");
        Assert.Equal(403, Assert.IsType<Microsoft.AspNetCore.Mvc.ObjectResult>(duplicate.Result).StatusCode);
        Assert.IsType<Microsoft.AspNetCore.Mvc.OkObjectResult>((await controller.GetSurveyAsync("formab01", "newusercase001")).Result);
        Assert.IsType<Microsoft.AspNetCore.Mvc.OkObjectResult>(await controller.GetSurveyDataAsync("formab01", "submitmixedcase1"));
        var report = await new SurveyStatisticsTool(db).BuildReportAsync(await db.Surveys.SingleAsync());
        Assert.Contains("answer", report);

        var services = new ServiceCollection();
        services.AddDbContext<MainDbContext>(builder => builder.UseSqlite(SqliteDatabase.ConnectionString(DatabasePath, pooling: false)));
        using var provider = services.BuildServiceProvider();
        var vote = new VoteCommand(provider.GetRequiredService<IServiceScopeFactory>());
        var context = CreateMessage(111111);
        Assert.False(await vote.HasPermissionAsync(context));
        context = CreateMessage(654321);
        Assert.True(await vote.HasPermissionAsync(context));
        var response = await vote.ExecuteAsync(context, ["submitmi", "d"]);
        Assert.True(response!.Success);
        db.ChangeTracker.Clear();
        Assert.Equal(VoteType.Downvote, (await db.ReviewVotes.SingleAsync()).VoteType);
        Assert.Equal(37, (await db.ReviewVotes.SingleAsync()).Id);
    }

    [Fact]
    public async Task Import_MySqlData_ForeignKeysCascadeAndSaveTransactionAreEnforced()
    {
        MySqlDumpImporter.Import(DumpPath, DatabasePath);
        using var db = SqliteDatabase.CreateContext(DatabasePath);
        var user = await db.Users.SingleAsync(u => u.QQId == "123456");
        user.UserGroup = UserGroup.VerifiedUser;
        db.Users.Add(new User { UserId = "RollbackUser01", QQId = "654321" });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();
        Assert.Equal(UserGroup.PendingUser, (await db.Users.SingleAsync(u => u.QQId == "123456")).UserGroup);
        Assert.False(await db.Users.AnyAsync(u => u.UserId == "RollbackUser01"));
        await Assert.ThrowsAsync<SqliteException>(() => db.Database.ExecuteSqlRawAsync("UPDATE submissions SET UserId = 'missing';"));
        await Assert.ThrowsAsync<SqliteException>(() => db.Database.ExecuteSqlRawAsync("UPDATE surveys SET Title = {0};", new string('x', 201)));
        db.Surveys.Remove(await db.Surveys.SingleAsync());
        await db.SaveChangesAsync();
        Assert.Empty(await db.Questionnaires.ToListAsync());
        Assert.Empty(await db.Submissions.ToListAsync());
        Assert.Empty(await db.ReviewSubmissions.ToListAsync());
        Assert.Empty(await db.ReviewVotes.ToListAsync());
        Assert.Equal(3, await db.Users.CountAsync());
        Assert.Single(await db.Requests.ToListAsync());
    }

    [Fact]
    public void Import_ExistingTarget_IsNeverReplaced()
    {
        MySqlDumpImporter.Import(DumpPath, DatabasePath);
        var original = File.ReadAllBytes(DatabasePath);
        Assert.Throws<IOException>(() => MySqlDumpImporter.Import(DumpPath, DatabasePath));
        Assert.Equal(original, File.ReadAllBytes(DatabasePath));
    }

    [Theory]
    [InlineData("'UserMixedCase01','2026-10-01", "'missing','2026-10-01")]
    [InlineData("'ReviewMixedCase1','SubmitMixedCase1'", "'ReviewMixedCase1','MissingSub01'")]
    [InlineData("unsupported", "unsupported")]
    [InlineData("truncated", "truncated")]
    [InlineData("duplicate", "duplicate")]
    public void Import_InvalidDump_DoesNotPublishPartialDatabase(string from, string to)
    {
        var sql = File.ReadAllText(DumpPath);
        sql = from switch
        {
            "unsupported" => sql + "DROP TABLE users;",
            "truncated" => sql.TrimEnd()[..^1],
            "duplicate" => sql + "INSERT INTO `users` (`UserId`,`QQId`,`UserGroup`) VALUES ('UserMixedCase01','999999','0');",
            _ => sql.Replace(from, to, StringComparison.Ordinal)
        };
        File.WriteAllText(DumpPath, sql);
        Assert.ThrowsAny<Exception>(() => MySqlDumpImporter.Import(DumpPath, DatabasePath));
        Assert.False(File.Exists(DatabasePath));
        Assert.Empty(Directory.GetFiles(_directory, "*.import-*"));
    }

    [Fact]
    public void Initialize_NewAndExistingDatabase_IsRepeatableAndUsesWal()
    {
        using var db = SqliteDatabase.CreateContext(DatabasePath);
        SqliteDatabase.Initialize(db);
        SqliteDatabase.Initialize(db);
        Assert.Empty(db.Database.GetPendingMigrations());
        db.Database.OpenConnection();
        using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = "PRAGMA journal_mode;";
        Assert.Equal("wal", command.ExecuteScalar());
        command.CommandText = "PRAGMA foreign_keys;";
        Assert.Equal(1L, command.ExecuteScalar());
    }

    [Fact]
    public void DatabaseOptions_RelativePath_IsResolvedAgainstProgramDirectory()
    {
        Assert.Equal(Path.Combine(_directory, "data.db"), new DatabaseOptions().GetFullPath(_directory));
        Assert.Equal(DatabasePath, new DatabaseOptions { Path = DatabasePath }.GetFullPath(AppContext.BaseDirectory));
        Assert.Throws<InvalidOperationException>(() => new DatabaseOptions { Path = " " }.GetFullPath(_directory));
    }

    [Fact]
    public void Initialize_ExistingDatabaseWithPendingMigration_RefusesStartup()
    {
        using var db = SqliteDatabase.CreateContext(DatabasePath);
        db.Database.Migrate();
        db.Database.ExecuteSqlRaw("DELETE FROM __EFMigrationsHistory WHERE MigrationId LIKE '%EnforceStringLengths';");
        var exception = Assert.Throws<InvalidOperationException>(() => SqliteDatabase.Initialize(db));
        Assert.Contains("--migrate-database", exception.Message);
    }

    public void Dispose()
    {
        Directory.Delete(_directory, recursive: true);
    }

    private static Sisters.WudiLib.Posts.GroupMessage CreateMessage(long userId)
    {
        return Newtonsoft.Json.JsonConvert.DeserializeObject<Sisters.WudiLib.Posts.GroupMessage>(
            $$$"""{"time":1790812800,"self_id":999999,"post_type":"message","message_type":"group","sub_type":"normal","message_id":1,"user_id":{{{userId}}},"group_id":200,"message":"test","raw_message":"test","font":0,"sender":{"user_id":{{{userId}}},"nickname":"test"}}""")!;
    }
}
