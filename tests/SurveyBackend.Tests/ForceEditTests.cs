using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using SurveyBackend.Controllers;
using SurveyBackend.Bot.Commands.Infrastructure;
using SurveyBackend.Data;
using SurveyBackend.Models;
using Xunit;

namespace SurveyBackend.Tests;

public sealed class ForceEditTests
{
    [Theory]
    [InlineData(ForceEditTestContext.SubmissionId)]
    [InlineData(ForceEditTestContext.ReviewId)]
    [InlineData(ForceEditTestContext.UserId)]
    [InlineData("222222")]
    public async Task Command_AllIdentifierTypes_BindsAdminAndOriginalSubmission(string identifier)
    {
        using var context = new ForceEditTestContext();
        var requestId = await context.IssueAsync(identifier);
        using var db = context.OpenDb();
        var grant = await db.ForceEditGrants.Include(g => g.Request).SingleAsync();
        Assert.Equal(requestId, grant.RequestId);
        Assert.Equal(ForceEditTestContext.AdminId, grant.Request.UserId);
        Assert.Equal(RequestType.ForceEdit, grant.Request.RequestType);
        Assert.Equal(ForceEditTestContext.UserId, grant.TargetUserId);
        Assert.Equal(ForceEditTestContext.SubmissionId, grant.SubmissionId);
        var result = Assert.IsType<OkObjectResult>(await context.Controller(db).GetForceEditAsync(requestId));
        var json = JsonSerializer.SerializeToElement(result.Value);
        Assert.Equal("edit", json.GetProperty("mode").GetString());
        Assert.Contains("222222 2026-10-03", json.GetProperty("surveyJson").GetString());
        Assert.Contains("before", json.GetProperty("surveyData").GetString());
        Assert.Empty(context.Logger.Errors);
    }

    [Theory]
    [InlineData(UserGroup.NewComer, true)]
    [InlineData(UserGroup.PendingUser, true)]
    [InlineData(UserGroup.VerifiedUser, true)]
    [InlineData(UserGroup.Admin, false)]
    [InlineData(UserGroup.SuperAdmin, false)]
    public async Task Command_ForbiddenRoleOrGroup_DoesNotIssueLink(UserGroup group, bool isPrivate)
    {
        using var context = new ForceEditTestContext();
        using var db = context.OpenDb();
        (await db.Users.SingleAsync(u => u.UserId == ForceEditTestContext.AdminId)).UserGroup = group;
        await db.SaveChangesAsync();
        var result = await context.Command.ExecuteAsync(ForceEditTestContext.Message(isPrivate: isPrivate), ["222222"]);
        Assert.False(result!.Success);
        Assert.Empty(await db.ForceEditGrants.ToListAsync());
        Assert.Empty(await db.Requests.ToListAsync());
    }

    [Fact]
    public async Task Command_SuperAdminPrivate_CanIssueLink()
    {
        using var context = new ForceEditTestContext();
        using var db = context.OpenDb();
        (await db.Users.SingleAsync(u => u.UserId == ForceEditTestContext.AdminId)).UserGroup = UserGroup.SuperAdmin;
        await db.SaveChangesAsync();
        await context.IssueAsync("222222");
    }

    [Theory]
    [InlineData(ForceEditTestContext.UserId)]
    [InlineData("222222")]
    [InlineData("333333")]
    public async Task Save_UserWithoutSubmission_CreatesReviewForTargetAndConsumesRequest(string identifier)
    {
        using var context = new ForceEditTestContext(hasSubmission: false, targetGroup: UserGroup.NewComer);
        var requestId = await context.IssueAsync(identifier);
        using var db = context.OpenDb();
        var grant = await db.ForceEditGrants.SingleAsync();
        Assert.Null(grant.SubmissionId);
        var result = await context.Controller(db).SaveForceEditAsync(requestId, new() { Answers = "{\"answer\":\"代填\"}" });
        Assert.IsType<OkObjectResult>(result);
        db.ChangeTracker.Clear();
        var submission = await db.Submissions.Include(s => s.User).SingleAsync();
        Assert.Equal(grant.TargetUserId, submission.UserId);
        Assert.NotEqual(ForceEditTestContext.AdminId, submission.UserId);
        Assert.Equal(UserGroup.PendingUser, submission.User.UserGroup);
        Assert.Equal(context.Clock.GetUtcNow().UtcDateTime, submission.CreatedAt);
        Assert.Equal(ReviewStatus.Pending, (await db.ReviewSubmissions.SingleAsync()).Status);
        Assert.Equal(submission.SubmissionId, (await db.ReviewSubmissions.SingleAsync()).SubmissionId);
        Assert.True((await db.Requests.SingleAsync()).IsDisabled);
        Assert.Equal(3, context.Onebot.GroupMessages.Count);
        Assert.Contains(submission.User.QQId, context.Onebot.GroupMessages[1].Content);
        Assert.Contains(submission.SubmissionId, context.Onebot.GroupMessages[1].Content);
        Assert.Empty(context.Logger.Errors);
    }

    [Fact]
    public async Task Save_ExistingDisabledSubmission_ChangesAnswersAndPreservesReviewState()
    {
        using var context = new ForceEditTestContext(targetGroup: UserGroup.VerifiedUser);
        using var seed = context.OpenDb();
        var original = await seed.Submissions.SingleAsync();
        original.IsDisabled = true;
        await seed.SaveChangesAsync();
        var requestId = await context.IssueAsync(ForceEditTestContext.ReviewId);
        using var db = context.OpenDb();
        var controller = context.Controller(db);
        Assert.IsType<OkObjectResult>(await controller.GetForceEditAsync(requestId));
        Assert.IsType<OkObjectResult>(await controller.SaveForceEditAsync(requestId, new() { Answers = "{\"answer\":\"after\"}" }));
        db.ChangeTracker.Clear();
        var saved = await db.Submissions.SingleAsync();
        Assert.Equal("{\"answer\":\"after\"}", saved.SurveyData);
        Assert.Equal(original.CreatedAt, saved.CreatedAt);
        Assert.Equal(original.UserId, saved.UserId);
        Assert.Equal(original.QuestionnaireId, saved.QuestionnaireId);
        Assert.True(saved.IsDisabled);
        Assert.Equal(ReviewStatus.Approved, (await db.ReviewSubmissions.SingleAsync()).Status);
        Assert.Contains("reinsight", (await db.ReviewSubmissions.SingleAsync()).AIInsights);
        Assert.Equal(UserGroup.VerifiedUser, (await db.Users.SingleAsync(u => u.UserId == original.UserId)).UserGroup);
        Assert.Single(await db.ReviewVotes.ToListAsync());
        Assert.Empty(context.Onebot.GroupMessages);
        Assert.Equal(403, Assert.IsType<ObjectResult>(await controller.SaveForceEditAsync(requestId, new() { Answers = "{}" })).StatusCode);
        Assert.Equal(403, Assert.IsType<ObjectResult>(await controller.GetForceEditAsync(requestId)).StatusCode);
        Assert.Empty(context.Logger.Errors);
    }

    [Theory]
    [InlineData("expired")]
    [InlineData("disabled")]
    [InlineData("wrong-type")]
    [InlineData("demoted")]
    [InlineData("future")]
    [InlineData("unknown")]
    public async Task Api_InvalidAuthorization_RejectsReadAndWrite(string condition)
    {
        using var context = new ForceEditTestContext();
        var requestId = await context.IssueAsync("222222");
        using (var seed = context.OpenDb())
        {
            var request = await seed.Requests.Include(r => r.User).SingleAsync();
            switch (condition)
            {
                case "expired": context.Clock.Advance(TimeSpan.FromHours(2)); break;
                case "disabled": request.IsDisabled = true; break;
                case "wrong-type": request.RequestType = RequestType.SurveyAccess; break;
                case "demoted": request.User.UserGroup = UserGroup.VerifiedUser; break;
                case "future": request.CreatedAt = context.Clock.GetUtcNow().UtcDateTime.AddMinutes(1); break;
                case "unknown": requestId = "unknown"; break;
            }
            await seed.SaveChangesAsync();
        }
        using var db = context.OpenDb();
        var controller = context.Controller(db);
        Assert.Equal(403, Assert.IsType<ObjectResult>(await controller.GetForceEditAsync(requestId)).StatusCode);
        Assert.Equal(403, Assert.IsType<ObjectResult>(await controller.SaveForceEditAsync(requestId, new() { Answers = "{}" })).StatusCode);
        Assert.Contains("before", (await db.Submissions.SingleAsync()).SurveyData);
        Assert.Empty(context.Logger.Errors);
    }

    [Theory]
    [InlineData("")]
    [InlineData("invalid")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("123")]
    public async Task Save_InvalidAnswers_DoesNotConsumeRequest(string answers)
    {
        using var context = new ForceEditTestContext();
        var requestId = await context.IssueAsync("222222");
        using var db = context.OpenDb();
        Assert.IsType<BadRequestObjectResult>(await context.Controller(db).SaveForceEditAsync(requestId, new() { Answers = answers }));
        Assert.False((await db.Requests.SingleAsync()).IsDisabled);
        Assert.Contains("before", (await db.Submissions.SingleAsync()).SurveyData);
    }

    [Fact]
    public async Task Command_MultipleSurveys_RequiresSelectionAndUsesLatestVersion()
    {
        using var context = new ForceEditTestContext(hasSubmission: false);
        using var db = context.OpenDb();
        var survey = new Survey { SurveyId = "Survey02", IsVerifySurvey = true, NeedReview = true };
        db.Questionnaires.AddRange(
            new Questionnaire { QuestionnaireId = "OldForm2", Survey = survey, SurveyJson = "{}", ReleaseDate = ReviewTestContext.Now.UtcDateTime.AddDays(-1) },
            new Questionnaire { QuestionnaireId = "NewForm2", Survey = survey, SurveyJson = "{}", ReleaseDate = ReviewTestContext.Now.UtcDateTime });
        await db.SaveChangesAsync();
        var response = await context.Command.ExecuteAsync(ForceEditTestContext.Message(), ["333333"]);
        Assert.False(response!.Success);
        Assert.Contains("Survey02", response.Message!.Raw);
        Assert.False(await db.Users.AnyAsync(u => u.QQId == "333333"));
        Assert.Empty(await db.Requests.ToListAsync());
        var requestId = await context.IssueAsync("333333", "Survey02");
        Assert.Equal("NewForm2", (await db.ForceEditGrants.SingleAsync(g => g.RequestId == requestId)).QuestionnaireId);
    }

    [Fact]
    public async Task Command_NoPublishedQuestionnaire_ReturnsErrorWithoutRegisteringUser()
    {
        using var context = new ForceEditTestContext(hasSubmission: false);
        using var db = context.OpenDb();
        db.Questionnaires.RemoveRange(db.Questionnaires);
        await db.SaveChangesAsync();
        var result = await context.Command.ExecuteAsync(ForceEditTestContext.Message(), ["333333"]);
        Assert.False(result!.Success);
        Assert.Contains("尚未发布", result.Message!.Raw);
        Assert.False(await db.Users.AnyAsync(u => u.QQId == "333333"));
        Assert.Empty(await db.Requests.ToListAsync());
    }

    [Fact]
    public async Task Save_UserSubmittedSinceLinkIssued_ReturnsConflictAndRollsBackClaim()
    {
        using var context = new ForceEditTestContext(hasSubmission: false);
        var requestId = await context.IssueAsync("222222");
        using (var seed = context.OpenDb())
        {
            seed.Submissions.Add(new Submission { User = await seed.Users.SingleAsync(u => u.QQId == "222222"),
                Questionnaire = await seed.Questionnaires.SingleAsync(), SurveyData = "{}" });
            await seed.SaveChangesAsync();
        }
        using var db = context.OpenDb();
        Assert.IsType<ConflictObjectResult>(await context.Controller(db).SaveForceEditAsync(requestId, new() { Answers = "{}" }));
        db.ChangeTracker.Clear();
        Assert.Single(await db.Submissions.ToListAsync());
        Assert.False((await db.Requests.SingleAsync()).IsDisabled);
        Assert.Empty(await db.ReviewSubmissions.ToListAsync());
    }

    [Fact]
    public async Task HttpApi_TamperedTargetAndConcurrentSave_CannotRedirectOrDuplicateSubmission()
    {
        using var context = new ForceEditTestContext(hasSubmission: false, targetGroup: UserGroup.NewComer);
        var requestId = await context.IssueAsync("222222");
        await using var app = await context.StartApiAsync();
        using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
        var response = await client.GetAsync($"/api/Survey/force-edit/{requestId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var document = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("create", document.GetProperty("mode").GetString());
        Assert.Equal(ForceEditTestContext.UserId, document.GetProperty("userId").GetString());
        var payload = new { answers = "{\"answer\":\"HTTP\"}", userId = ForceEditTestContext.AdminId,
            submissionId = "other-submission", questionnaireId = "other-form" };
        var saves = await Task.WhenAll(Enumerable.Range(0, 2)
            .Select(_ => client.PostAsJsonAsync($"/api/Survey/force-edit/{requestId}/submission", payload)));
        Assert.Equal(1, saves.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.Equal(1, saves.Count(r => r.StatusCode == HttpStatusCode.Forbidden));
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/api/Survey/force-edit/{requestId}")).StatusCode);
        using var db = context.OpenDb();
        Assert.Equal(ForceEditTestContext.UserId, (await db.Submissions.SingleAsync()).UserId);
        Assert.Equal("Form0001", (await db.Submissions.SingleAsync()).QuestionnaireId);
        Assert.Single(await db.ReviewSubmissions.ToListAsync());
        Assert.Empty(context.Logger.Errors);
    }

    [Fact]
    public async Task Migration_ExistingDatabase_UpgradesAndRollsBackWithoutChangingSubmission()
    {
        using var context = new ForceEditTestContext();
        using var db = context.OpenDb();
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("20261004175807_EnforceStringLengths");
        Assert.Throws<InvalidOperationException>(() => SqliteDatabase.Initialize(db));
        await migrator.MigrateAsync();
        await migrator.MigrateAsync();
        Assert.False(db.Database.HasPendingModelChanges());
        var requestId = await context.IssueAsync("222222");
        Assert.Equal(requestId, (await db.ForceEditGrants.SingleAsync()).RequestId);
        await migrator.MigrateAsync("20261004175807_EnforceStringLengths");
        Assert.Contains("before", (await db.Submissions.SingleAsync()).SurveyData);
        await migrator.MigrateAsync();
        Assert.Empty(await db.ForceEditGrants.ToListAsync());
        Assert.Single(await db.ReviewVotes.ToListAsync());
    }

    [Fact]
    public async Task Save_DatabaseFailure_RollsBackAnswersAndRequestClaim()
    {
        using var context = new ForceEditTestContext();
        var requestId = await context.IssueAsync("222222");
        using (var db = context.OpenDb())
        {
            await db.Database.ExecuteSqlRawAsync("CREATE TRIGGER fail_force_edit BEFORE UPDATE OF SurveyData ON submissions BEGIN SELECT RAISE(ABORT, 'test failure'); END;");
            await Assert.ThrowsAsync<DbUpdateException>(() => context.Controller(db).SaveForceEditAsync(requestId, new() { Answers = "{}" }));
        }
        using var verify = context.OpenDb();
        Assert.False((await verify.Requests.SingleAsync()).IsDisabled);
        Assert.Contains("before", (await verify.Submissions.SingleAsync()).SurveyData);
        Assert.Equal("旧见解", (await verify.ReviewSubmissions.SingleAsync()).AIInsights);
    }

    [Fact]
    public async Task HttpApi_TwoCreationLinksForSameUser_OnlyCreatesOneSubmission()
    {
        using var context = new ForceEditTestContext(hasSubmission: false, targetGroup: UserGroup.NewComer);
        var first = await context.IssueAsync("222222");
        var second = await context.IssueAsync("222222");
        await using var app = await context.StartApiAsync();
        using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
        var responses = await Task.WhenAll(new[] { first, second }.Select(id =>
            client.PostAsJsonAsync($"/api/Survey/force-edit/{id}/submission", new { answers = "{}" })));
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Conflict);
        using var db = context.OpenDb();
        Assert.Single(await db.Submissions.ToListAsync());
        Assert.Equal(1, await db.Requests.CountAsync(r => r.IsDisabled));
        Assert.Empty(context.Logger.Errors);
    }

    [Fact]
    public async Task Save_NotificationFailure_DataRemainsSavedAndErrorIsRecorded()
    {
        using var context = new ForceEditTestContext(hasSubmission: false, targetGroup: UserGroup.NewComer);
        var requestId = await context.IssueAsync("222222");
        context.Onebot.OnSend = (_, _) => throw new InvalidOperationException("test delivery failure");
        using var db = context.OpenDb();
        Assert.IsType<OkObjectResult>(await context.Controller(db).SaveForceEditAsync(requestId, new() { Answers = "{}" }));
        Assert.Single(await db.Submissions.ToListAsync());
        Assert.True((await db.Requests.SingleAsync()).IsDisabled);
        var error = Assert.Single(context.Logger.Errors);
        Assert.Contains("pushing survey response", error.Message);
        Assert.Equal("test delivery failure", error.Exception!.Message);
    }

    [Theory]
    [InlineData(UserGroup.Admin)]
    [InlineData(UserGroup.SuperAdmin)]
    [InlineData(UserGroup.VerifiedUser)]
    public async Task Save_CreateForExistingMember_PreservesUserGroup(UserGroup group)
    {
        using var context = new ForceEditTestContext(hasSubmission: false, targetGroup: group);
        var requestId = await context.IssueAsync("222222");
        using var db = context.OpenDb();
        Assert.IsType<OkObjectResult>(await context.Controller(db).SaveForceEditAsync(requestId, new() { Answers = "{}" }));
        Assert.Equal(group, (await db.Users.SingleAsync(u => u.UserId == ForceEditTestContext.UserId)).UserGroup);
        Assert.Empty(context.Logger.Errors);
    }

    [Theory]
    [InlineData("missing-id")]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("12345678901234567")]
    public async Task Command_InvalidIdentifier_DoesNotRegisterOrIssueRequest(string identifier)
    {
        using var context = new ForceEditTestContext();
        var result = await context.Command.ExecuteAsync(ForceEditTestContext.Message(), [identifier]);
        Assert.False(result!.Success);
        using var db = context.OpenDb();
        Assert.Equal(2, await db.Users.CountAsync());
        Assert.Empty(await db.Requests.ToListAsync());
    }

    [Fact]
    public async Task Api_JustBeforeExpiry_AllowsAccess()
    {
        using var context = new ForceEditTestContext();
        var requestId = await context.IssueAsync("222222");
        context.Clock.Advance(TimeSpan.FromHours(2) - TimeSpan.FromTicks(1));
        using var db = context.OpenDb();
        Assert.IsType<OkObjectResult>(await context.Controller(db).GetForceEditAsync(requestId));
        Assert.IsType<OkObjectResult>(await context.Controller(db).SaveForceEditAsync(requestId, new() { Answers = "{}" }));
        Assert.Empty(context.Logger.Errors);
    }

    [Fact]
    public async Task Command_RegistryRoutesForceEdit_ReturnsBoundLink()
    {
        using var context = new ForceEditTestContext();
        var registry = new SurveyCommandRegistry(context.ScopeFactory);
        registry.RegisterCommand(context.Command);
        var response = await registry.TryExecuteSurveyCommandAsync(
            ForceEditTestContext.Message(commandText: "/survey force-edit 222222"));
        Assert.True(response!.Success);
        using var db = context.OpenDb();
        Assert.Equal(ForceEditTestContext.SubmissionId, (await db.ForceEditGrants.SingleAsync()).SubmissionId);
    }

    [Fact]
    public async Task Api_DeletedTarget_DoesNotTurnEditIntoCreation()
    {
        using var context = new ForceEditTestContext();
        var requestId = await context.IssueAsync("222222");
        using var db = context.OpenDb();
        db.Submissions.Remove(await db.Submissions.SingleAsync());
        await db.SaveChangesAsync();
        var controller = context.Controller(db);
        Assert.Equal(403, Assert.IsType<ObjectResult>(await controller.GetForceEditAsync(requestId)).StatusCode);
        Assert.IsType<NotFoundObjectResult>(await controller.SaveForceEditAsync(requestId, new() { Answers = "{}" }));
        db.ChangeTracker.Clear();
        Assert.Empty(await db.Submissions.ToListAsync());
        Assert.False((await db.Requests.SingleAsync()).IsDisabled);
    }

    [Fact]
    public async Task Command_OrdinarySubmissionId_AllowsEditing()
    {
        using var context = new ForceEditTestContext();
        using var db = context.OpenDb();
        var survey = await db.Surveys.SingleAsync();
        survey.IsVerifySurvey = false;
        survey.NeedReview = false;
        await db.SaveChangesAsync();
        var requestId = await context.IssueAsync(ForceEditTestContext.SubmissionId);
        Assert.IsType<OkObjectResult>(await context.Controller(db).SaveForceEditAsync(requestId, new() { Answers = "{}" }));
        Assert.Equal("{}", (await db.Submissions.SingleAsync()).SurveyData);
        Assert.Empty(context.Logger.Errors);
    }
}
