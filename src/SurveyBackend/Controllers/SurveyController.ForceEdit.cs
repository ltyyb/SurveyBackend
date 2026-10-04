using System.Text.Json;
using Microsoft.AspNetCore.Mvc;

namespace SurveyBackend.Controllers;

public partial class SurveyController
{
    [HttpGet("force-edit/{requestId}")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<ActionResult> GetForceEditAsync(string requestId, CancellationToken cancellationToken = default)
    {
        var grant = await LoadForceEditGrantAsync(requestId, cancellationToken, tracking: false);
        if (!IsForceEditGrantValid(grant))
        {
            return StatusCode(403, new { status = -2, error = "Force-edit request is invalid, expired or no longer authorized." });
        }
        return Ok(new
        {
            status = 0,
            requestId = grant!.RequestId,
            mode = grant.SubmissionId is null ? "create" : "edit",
            userId = grant.TargetUserId,
            qqId = grant.TargetUser.QQId,
            questionnaireId = grant.QuestionnaireId,
            submissionId = grant.SubmissionId,
            surveyJson = GetSpecificSurveyJson(grant.Questionnaire, grant.TargetUser),
            surveyData = grant.Submission?.SurveyData,
            expiresAt = grant.Request.CreatedAt.AddHours(2)
        });
    }

    [HttpPost("force-edit/{requestId}/submission")]
    public async Task<ActionResult> SaveForceEditAsync(string requestId, [FromBody] ForceEditSubmission data,
        CancellationToken cancellationToken = default)
    {
        if (data is null || string.IsNullOrWhiteSpace(data.Answers) || !IsAnswerObject(data.Answers))
        {
            return BadRequest(new { status = -1, error = "Answers must be a JSON object." });
        }

        Submission submission;
        ReviewSubmissionData? newReview = null;
        using (var transaction = await _db.Database.BeginTransactionAsync(cancellationToken))
        {
            // 先领取请求；SQLite 写锁使重复保存和创建检查在同一事务中执行。
            var now = _timeProvider.GetUtcNow().UtcDateTime;
            var cutoff = now.AddHours(-2);
            var claimed = await _db.Requests.Where(r => r.RequestId == requestId
                    && r.RequestType == RequestType.ForceEdit && !r.IsDisabled
                    && r.CreatedAt > cutoff && r.CreatedAt <= now
                    && (r.User.UserGroup == UserGroup.Admin || r.User.UserGroup == UserGroup.SuperAdmin))
                .ExecuteUpdateAsync(setters => setters.SetProperty(r => r.IsDisabled, true), cancellationToken);
            if (claimed != 1)
            {
                return StatusCode(403, new { status = -2, error = "Force-edit request is invalid, expired or no longer authorized." });
            }

            var grant = await LoadForceEditGrantAsync(requestId, cancellationToken);
            if (grant is null)
            {
                return NotFound(new { status = -3, error = "Force-edit target no longer exists." });
            }
            if (grant.Submission is not null)
            {
                submission = grant.Submission;
                submission.SurveyData = data.Answers;
                var reviews = await _db.ReviewSubmissions.Where(r => r.SubmissionId == submission.SubmissionId)
                    .ToListAsync(cancellationToken);
                foreach (var review in reviews)
                {
                    review.AIInsights = "问卷已由管理员修改，请使用 /survey reinsight 重新生成见解。";
                }
            }
            else
            {
                if (!grant.Questionnaire.Survey.IsVerifySurvey)
                {
                    return Conflict(new { status = -4, error = "The selected survey is no longer an entrance survey." });
                }
                if (await _db.Submissions.AnyAsync(s => s.UserId == grant.TargetUserId
                    && s.Questionnaire.Survey.IsVerifySurvey, cancellationToken))
                {
                    return Conflict(new { status = -4, error = "The user already has an entrance submission. Generate a new force-edit link." });
                }
                submission = new Submission
                {
                    User = grant.TargetUser,
                    Questionnaire = grant.Questionnaire,
                    SurveyData = data.Answers,
                    CreatedAt = now
                };
                _db.Submissions.Add(submission);
                newReview = new ReviewSubmissionData { Submission = submission };
                _db.ReviewSubmissions.Add(newReview);
                if (grant.TargetUser.UserGroup == UserGroup.NewComer)
                {
                    grant.TargetUser.UserGroup = UserGroup.PendingUser;
                }
            }
            await _db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            _logger.LogInformation("Administrator {AdminUserId} saved submission {SubmissionId} for user {TargetUserId} via force-edit.",
                grant.Request.UserId, submission.SubmissionId, grant.TargetUserId);
        }

        if (newReview is not null)
        {
            await GenerateInsight(newReview);
            await PushResponse(newReview);
        }
        return Ok(new { status = 0, submissionId = submission.SubmissionId });
    }

    private Task<ForceEditGrant?> LoadForceEditGrantAsync(string requestId, CancellationToken cancellationToken,
        bool tracking = true)
    {
        var query = tracking ? _db.ForceEditGrants.AsTracking() : _db.ForceEditGrants.AsNoTracking();
        return query.Include(g => g.Request).ThenInclude(r => r.User)
            .Include(g => g.TargetUser).Include(g => g.Questionnaire).ThenInclude(q => q.Survey)
            .Include(g => g.Submission)
            .SingleOrDefaultAsync(g => g.RequestId == requestId, cancellationToken);
    }

    private bool IsForceEditGrantValid(ForceEditGrant? grant)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        return grant is not null && grant.Request.RequestType == RequestType.ForceEdit
            && !grant.Request.IsDisabled && grant.Request.CreatedAt <= now
            && grant.Request.CreatedAt > now.AddHours(-2)
            && grant.Request.User.UserGroup is UserGroup.Admin or UserGroup.SuperAdmin;
    }

    private static bool IsAnswerObject(string answers)
    {
        try
        {
            using var document = JsonDocument.Parse(answers);
            return document.RootElement.ValueKind == JsonValueKind.Object;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
