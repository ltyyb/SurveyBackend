using SurveyBackend.Configuration;
using SurveyBackend.Models;
using Xunit;

namespace SurveyBackend.Tests;

public class BackgroundVerifyServiceTests
{
    [Theory]
    [InlineData(1, 2, 1, 4, 0.6, ReviewStatus.Approved)]
    [InlineData(0, 2, 1, 4, 0.6, ReviewStatus.Pending)]
    [InlineData(-1, 2, 1, 4, 0.6, ReviewStatus.Pending)]
    [InlineData(1, 2, 0, 4, 0.6, ReviewStatus.Pending)]
    [InlineData(1, 0, 0, 4, 0.6, ReviewStatus.Pending)]
    [InlineData(1, 1, 2, 4, 0.6, ReviewStatus.Pending)]
    [InlineData(1, 4, 2, 10, 0.9, ReviewStatus.Approved)]
    [InlineData(1, 3, 2, 10, 0.9, ReviewStatus.Pending)]
    [InlineData(1, 2, 1, 3, 0.9, ReviewStatus.Approved)]
    [InlineData(1, 2, 1, 4, 1.0, ReviewStatus.Approved)]
    [InlineData(0, 3, 1, 4, 0.6, ReviewStatus.Approved)]
    [InlineData(0, 2, 2, 4, 0.6, ReviewStatus.Rejected)]
    [InlineData(0, 3, 2, 4, 0.6, ReviewStatus.Rejected)]
    [InlineData(0, 2, 1, 3, 0.6, ReviewStatus.Approved)]
    [InlineData(0, 3, 1, 4, 0.75, ReviewStatus.Rejected)]
    [InlineData(0, 3, 1, 5, 0.6, ReviewStatus.Pending)]
    [InlineData(1, 2, 2, 4, 0.6, ReviewStatus.Rejected)]
    [InlineData(0, 1, 0, 1, 0.0, ReviewStatus.Approved)]
    [InlineData(0, 4, 0, 4, 1.0, ReviewStatus.Rejected)]
    [InlineData(-TimeSpan.TicksPerDay, 2, 1, 4, 0.6, ReviewStatus.Pending)]
    [InlineData(-2 * TimeSpan.TicksPerDay, 2, 1, 4, 0.6, ReviewStatus.Pending)]
    [InlineData(1, 3, 0, 4, 0.6, ReviewStatus.Approved)]
    [InlineData(0, 0, 4, 4, 0.6, ReviewStatus.Rejected)]
    [InlineData(0, 3, 0, 4, 0.6, ReviewStatus.Pending)]
    [InlineData(0, 3, 1, int.MaxValue, 0.6, ReviewStatus.Pending)]
    public async Task VerifyResponse_VoteAndTimeBoundaries_PersistsExpectedDecision(
        long ticksBeyond24Hours, int agreeCount, int denyCount, int minimumVotes,
        double agreeRateThreshold, ReviewStatus expectedStatus)
    {
        using var context = new ReviewTestContext(new ReviewOptions
        {
            MinimumVotes = minimumVotes,
            AgreeRateThreshold = agreeRateThreshold
        });
        var createdAt = ReviewTestContext.Now.UtcDateTime.AddHours(-24).AddTicks(-ticksBeyond24Hours);
        var reviewId = await context.SeedReviewAsync(createdAt, agreeCount, denyCount);

        await context.VerifyAsync();
        await context.VerifyAsync();

        var review = await context.ReadReviewAsync(reviewId);
        Assert.Equal(expectedStatus, review.Status);
        var expectedGroup = expectedStatus switch
        {
            ReviewStatus.Approved => UserGroup.VerifiedUser,
            ReviewStatus.Rejected => UserGroup.NewComer,
            _ => UserGroup.PendingUser
        };
        Assert.Equal(expectedGroup, review.Submission.User.UserGroup);
        if (expectedStatus == ReviewStatus.Pending)
        {
            Assert.Empty(context.Onebot.GroupMessages);
        }
        else
        {
            AssertNotification(context, review, expectedStatus);
        }
    }

    [Theory]
    [InlineData(ReviewStatus.Approved)]
    [InlineData(ReviewStatus.Rejected)]
    public async Task VerifyResponse_AlreadyReviewed_DoesNotChangeStatusOrNotify(ReviewStatus status)
    {
        using var context = new ReviewTestContext();
        var reviewId = await context.SeedReviewAsync(ReviewTestContext.Now.UtcDateTime.AddDays(-2), 2, 1,
            status, UserGroup.Admin);

        await context.VerifyAsync();

        var review = await context.ReadReviewAsync(reviewId);
        Assert.Equal(status, review.Status);
        Assert.Equal(UserGroup.Admin, review.Submission.User.UserGroup);
        Assert.Empty(context.Onebot.GroupMessages);
    }

    [Fact]
    public async Task VerifyResponse_NoReviews_DoesNotNotify()
    {
        using var context = new ReviewTestContext();
        await context.VerifyAsync();
        Assert.Empty(context.Onebot.GroupMessages);
    }

    [Fact]
    public async Task VerifyResponse_MixedReviews_CountsVotesAndNotifiesEachUserIndependently()
    {
        using var context = new ReviewTestContext();
        var old = ReviewTestContext.Now.UtcDateTime.AddDays(-2);
        var recent = ReviewTestContext.Now.UtcDateTime.AddHours(-1);
        var expected = new Dictionary<string, (ReviewStatus Status, UserGroup Group)>
        {
            [await context.SeedReviewAsync(old, 2, 1)] = (ReviewStatus.Approved, UserGroup.VerifiedUser),
            [await context.SeedReviewAsync(recent, 3, 1)] = (ReviewStatus.Approved, UserGroup.VerifiedUser),
            [await context.SeedReviewAsync(recent, 2, 2)] = (ReviewStatus.Rejected, UserGroup.NewComer),
            [await context.SeedReviewAsync(old, 0, 0)] = (ReviewStatus.Pending, UserGroup.PendingUser),
            [await context.SeedReviewAsync(recent, 2, 0)] = (ReviewStatus.Pending, UserGroup.PendingUser),
            [await context.SeedReviewAsync(old, 10, 0, ReviewStatus.Rejected, UserGroup.Admin)] = (ReviewStatus.Rejected, UserGroup.Admin)
        };

        await context.VerifyAsync();
        await context.VerifyAsync();

        Assert.Equal(3, context.Onebot.GroupMessages.Count);
        foreach (var (reviewId, (status, group)) in expected)
        {
            var review = await context.ReadReviewAsync(reviewId);
            Assert.Equal(status, review.Status);
            Assert.Equal(group, review.Submission.User.UserGroup);
            var messages = context.Onebot.GroupMessages
                .Where(m => m.Content.Contains($"[CQ:at,qq={review.Submission.User.QQId}]"));
            if (group is UserGroup.PendingUser or UserGroup.Admin)
            {
                Assert.Empty(messages);
            }
            else
            {
                var message = Assert.Single(messages);
                Assert.Equal(ReviewTestContext.VerifyGroupId, message.GroupId);
                Assert.Contains(status == ReviewStatus.Approved ? "已通过审核" : "未通过审核", message.Content);
            }
        }
    }

    [Fact]
    public async Task VerifyResponse_NewVotesAfterPending_ApprovesOnNextCheck()
    {
        using var context = new ReviewTestContext();
        var reviewId = await context.SeedReviewAsync(ReviewTestContext.Now.UtcDateTime.AddDays(-2), 2, 0);
        await context.VerifyAsync();
        Assert.Equal(ReviewStatus.Pending, (await context.ReadReviewAsync(reviewId)).Status);
        Assert.Empty(context.Onebot.GroupMessages);

        await context.AddVotesAsync(reviewId, 0, 1);
        await context.VerifyAsync();

        var review = await context.ReadReviewAsync(reviewId);
        Assert.Equal(ReviewStatus.Approved, review.Status);
        Assert.Equal(UserGroup.VerifiedUser, review.Submission.User.UserGroup);
        AssertNotification(context, review, ReviewStatus.Approved);
    }

    [Fact]
    public async Task VerifyResponse_ClockCrosses24Hours_ApprovesWithoutNewVotes()
    {
        using var context = new ReviewTestContext();
        var reviewId = await context.SeedReviewAsync(ReviewTestContext.Now.UtcDateTime.AddHours(-24), 2, 1);
        await context.VerifyAsync();
        Assert.Equal(ReviewStatus.Pending, (await context.ReadReviewAsync(reviewId)).Status);
        Assert.Empty(context.Onebot.GroupMessages);

        context.Clock.Advance(TimeSpan.FromTicks(1));
        await context.VerifyAsync();

        var review = await context.ReadReviewAsync(reviewId);
        Assert.Equal(ReviewStatus.Approved, review.Status);
        AssertNotification(context, review, ReviewStatus.Approved);
    }

    [Fact]
    public async Task VerifyResponse_ExistingVoteChanged_UsesUpdatedVoteType()
    {
        using var context = new ReviewTestContext();
        var reviewId = await context.SeedReviewAsync(ReviewTestContext.Now.UtcDateTime.AddHours(-1), 2, 1);
        await context.VerifyAsync();
        Assert.Equal(ReviewStatus.Pending, (await context.ReadReviewAsync(reviewId)).Status);

        await context.ChangeVoteAsync(reviewId, VoteType.Downvote, VoteType.Upvote);
        await context.AddVotesAsync(reviewId, 0, 1);
        await context.VerifyAsync();

        var review = await context.ReadReviewAsync(reviewId);
        Assert.Equal(ReviewStatus.Approved, review.Status);
        AssertNotification(context, review, ReviewStatus.Approved);
    }

    [Theory]
    [InlineData(8, DateTimeKind.Utc)]
    [InlineData(-7, DateTimeKind.Utc)]
    [InlineData(8, DateTimeKind.Unspecified)]
    [InlineData(-7, DateTimeKind.Unspecified)]
    public async Task VerifyResponse_ClockHasOffset_ComparesSubmissionAgainstUtc(int offsetHours, DateTimeKind kind)
    {
        var now = ReviewTestContext.Now.ToOffset(TimeSpan.FromHours(offsetHours));
        using var context = new ReviewTestContext(now: now);
        var before = DateTime.SpecifyKind(ReviewTestContext.Now.UtcDateTime.AddHours(-24).AddTicks(-1), kind);
        var at = DateTime.SpecifyKind(ReviewTestContext.Now.UtcDateTime.AddHours(-24), kind);
        var expiredId = await context.SeedReviewAsync(before, 2, 1);
        var pendingId = await context.SeedReviewAsync(at, 2, 1);

        await context.VerifyAsync();

        var expired = await context.ReadReviewAsync(expiredId);
        Assert.Equal(ReviewStatus.Approved, expired.Status);
        Assert.Equal(ReviewStatus.Pending, (await context.ReadReviewAsync(pendingId)).Status);
        AssertNotification(context, expired, ReviewStatus.Approved);
    }

    [Theory]
    [InlineData(3, 1, ReviewStatus.Approved, UserGroup.VerifiedUser)]
    [InlineData(1, 3, ReviewStatus.Rejected, UserGroup.NewComer)]
    public async Task VerifyResponse_DecisionMade_PersistsStatusAndGroupBeforeNotification(
        int agreeCount, int denyCount, ReviewStatus expectedStatus, UserGroup expectedGroup)
    {
        using var context = new ReviewTestContext();
        var reviewId = await context.SeedReviewAsync(ReviewTestContext.Now.UtcDateTime, agreeCount, denyCount);
        context.Onebot.OnSend = async (_, _) =>
        {
            var persisted = await context.ReadReviewAsync(reviewId);
            Assert.Equal(expectedStatus, persisted.Status);
            Assert.Equal(expectedGroup, persisted.Submission.User.UserGroup);
        };

        await context.VerifyAsync();

        AssertNotification(context, await context.ReadReviewAsync(reviewId), expectedStatus);
    }

    [Theory]
    [InlineData(3, 1, ReviewStatus.Approved, UserGroup.VerifiedUser)]
    [InlineData(1, 3, ReviewStatus.Rejected, UserGroup.NewComer)]
    public async Task VerifyResponse_NotificationThrows_KeepsSavedDecisionAndLogsException(
        int agreeCount, int denyCount, ReviewStatus expectedStatus, UserGroup expectedGroup)
    {
        using var context = new ReviewTestContext();
        var reviewId = await context.SeedReviewAsync(ReviewTestContext.Now.UtcDateTime, agreeCount, denyCount);
        var failure = new InvalidOperationException("模拟通知失败");
        context.Onebot.OnSend = (_, _) => Task.FromException(failure);

        await context.Service.VerifyResponse(CancellationToken.None);

        var review = await context.ReadReviewAsync(reviewId);
        Assert.Equal(expectedStatus, review.Status);
        Assert.Equal(expectedGroup, review.Submission.User.UserGroup);
        Assert.Same(failure, Assert.Single(context.Logger.Errors).Exception);
        await context.Service.VerifyResponse(CancellationToken.None);
        Assert.Single(context.Logger.Errors);
        AssertNotification(context, review, expectedStatus);
    }

    [Theory]
    [InlineData(3, 1, ReviewStatus.Approved, UserGroup.VerifiedUser)]
    [InlineData(1, 3, ReviewStatus.Rejected, UserGroup.NewComer)]
    public async Task VerifyResponse_SaveFails_DoesNotNotifyOrPersistPartialDecisionAndRetriesNextCheck(
        int agreeCount, int denyCount, ReviewStatus expectedStatus, UserGroup expectedGroup)
    {
        var interceptor = new FailingSaveChangesInterceptor();
        using var context = new ReviewTestContext(saveChangesInterceptor: interceptor);
        var reviewId = await context.SeedReviewAsync(ReviewTestContext.Now.UtcDateTime, agreeCount, denyCount);
        var failure = new InvalidOperationException("模拟数据库保存失败");
        interceptor.Failure = failure;

        await context.Service.VerifyResponse(CancellationToken.None);

        var review = await context.ReadReviewAsync(reviewId);
        Assert.Equal(ReviewStatus.Pending, review.Status);
        Assert.Equal(UserGroup.PendingUser, review.Submission.User.UserGroup);
        Assert.Empty(context.Onebot.GroupMessages);
        Assert.Same(failure, Assert.Single(context.Logger.Errors).Exception);

        interceptor.Failure = null;
        await context.Service.VerifyResponse(CancellationToken.None);

        review = await context.ReadReviewAsync(reviewId);
        Assert.Equal(expectedStatus, review.Status);
        Assert.Equal(expectedGroup, review.Submission.User.UserGroup);
        Assert.Single(context.Logger.Errors);
        AssertNotification(context, review, expectedStatus);
    }

    private static void AssertNotification(ReviewTestContext context, ReviewSubmissionData review, ReviewStatus status)
    {
        var message = Assert.Single(context.Onebot.GroupMessages);
        Assert.Equal(ReviewTestContext.VerifyGroupId, message.GroupId);
        Assert.Contains($"[CQ:at,qq={review.Submission.User.QQId}]", message.Content);
        if (status == ReviewStatus.Approved)
        {
            Assert.Contains("已通过审核", message.Content);
            Assert.Contains($"主群 {ReviewTestContext.MainGroupId}", message.Content);
        }
        else
        {
            Assert.Contains("未通过审核", message.Content);
        }
    }
}
