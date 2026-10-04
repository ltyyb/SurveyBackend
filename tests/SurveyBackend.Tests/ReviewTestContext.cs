using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SurveyBackend.BackgroundServices;
using SurveyBackend.Configuration;
using SurveyBackend.Data;
using SurveyBackend.Models;
using Xunit;

namespace SurveyBackend.Tests;

internal sealed class ReviewTestContext : IDisposable
{
    public static readonly DateTimeOffset Now = new(2026, 10, 4, 0, 0, 0, TimeSpan.Zero);
    public const long MainGroupId = 100;
    public const long VerifyGroupId = 200;
    private readonly ServiceProvider _provider;
    private readonly SqliteConnection _connection;
    private long _nextQqId = 123456;

    public FixedTimeProvider Clock { get; }
    public RecordingOnebotService Onebot { get; } = new();
    public RecordingLogger<BackgroundVerifyService> Logger { get; } = new();
    public BackgroundVerifyService Service { get; }

    public ReviewTestContext(ReviewOptions? options = null, DateTimeOffset? now = null,
        SaveChangesInterceptor? saveChangesInterceptor = null)
    {
        var services = new ServiceCollection();
        _connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=True");
        _connection.Open();
        services.AddDbContext<MainDbContext>(builder =>
        {
            builder.UseSqlite(_connection);
            if (saveChangesInterceptor is not null)
            {
                builder.AddInterceptors(saveChangesInterceptor);
            }
        });
        _provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true
        });
        using (var scope = _provider.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<MainDbContext>().Database.Migrate();
        }
        Clock = new FixedTimeProvider(now ?? Now);
        Service = new BackgroundVerifyService(Logger, Onebot,
            Options.Create(new BotOptions { MainGroupId = MainGroupId, VerifyGroupId = VerifyGroupId }),
            Options.Create(options ?? new ReviewOptions()),
            _provider.GetRequiredService<IServiceScopeFactory>(), Clock);
    }

    public async Task VerifyAsync()
    {
        await Service.VerifyResponse(CancellationToken.None);
        Assert.Empty(Logger.Errors);
    }

    public async Task<string> SeedReviewAsync(DateTime createdAt, int agreeCount, int denyCount,
        ReviewStatus status = ReviewStatus.Pending, UserGroup userGroup = UserGroup.PendingUser)
    {
        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MainDbContext>();
        var review = new ReviewSubmissionData
        {
            Status = status,
            Submission = new Submission
            {
                Questionnaire = new Questionnaire
                {
                    Survey = new Survey { NeedReview = true, IsVerifySurvey = true },
                    SurveyJson = "{}"
                },
                User = new User { QQId = (_nextQqId++).ToString(), UserGroup = userGroup },
                SurveyData = "{}",
                CreatedAt = createdAt
            }
        };
        db.ReviewSubmissions.Add(review);
        AddVotes(db, review, agreeCount, denyCount);
        await db.SaveChangesAsync();
        return review.ReviewSubmissionDataId;
    }

    public async Task AddVotesAsync(string reviewId, int agreeCount, int denyCount)
    {
        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MainDbContext>();
        var review = await db.ReviewSubmissions.SingleAsync(r => r.ReviewSubmissionDataId == reviewId);
        AddVotes(db, review, agreeCount, denyCount);
        await db.SaveChangesAsync();
    }

    public async Task ChangeVoteAsync(string reviewId, VoteType from, VoteType to)
    {
        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MainDbContext>();
        var vote = await db.ReviewVotes.FirstAsync(v => v.ReviewSubmissionDataId == reviewId && v.VoteType == from);
        vote.VoteType = to;
        await db.SaveChangesAsync();
    }

    public async Task<ReviewSubmissionData> ReadReviewAsync(string reviewId)
    {
        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MainDbContext>();
        return await db.ReviewSubmissions.AsNoTracking()
            .Include(r => r.Submission).ThenInclude(s => s.User)
            .SingleAsync(r => r.ReviewSubmissionDataId == reviewId);
    }

    private void AddVotes(MainDbContext db, ReviewSubmissionData review, int agreeCount, int denyCount)
    {
        for (var index = 0; index < agreeCount + denyCount; index++)
        {
            db.ReviewVotes.Add(new ReviewVote
            {
                ReviewSubmissionData = review,
                User = new User { QQId = (_nextQqId++).ToString(), UserGroup = UserGroup.VerifiedUser },
                VoteType = index < agreeCount ? VoteType.Upvote : VoteType.Downvote
            });
        }
    }

    public void Dispose()
    {
        Service.Dispose();
        _provider.Dispose();
        _connection.Dispose();
    }
}
