using System.Globalization;
using System.Text;
using Sisters.WudiLib.Posts;
using MessageContext = Sisters.WudiLib.Posts.Message;
using Request = SurveyBackend.Models.Request;

namespace SurveyBackend.Bot.Commands;

public class ForceEditCommand : AuthorizedAsyncCommand
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ApiOptions _apiOptions;
    private readonly TimeProvider _timeProvider;

    public ForceEditCommand(IServiceScopeFactory scopeFactory, IOptions<ApiOptions> apiOptions,
        TimeProvider? timeProvider = null) : base(scopeFactory)
    {
        _scopeFactory = scopeFactory;
        _apiOptions = apiOptions.Value;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public override string CommandName => "force-edit";
    public override string Description => "编辑指定提交或代填入群问卷，仅管理员私聊可用。";

    protected override async Task<CommandResponse?> ExecuteAuthorizedAsync(MessageContext context, string[] args,
        CancellationToken cancellationToken = default)
    {
        if (context is not PrivateMessage)
        {
            return CommandResponse.FailureResponse("❌ 出于安全考虑，本命令仅可在私聊中使用。");
        }
        if (args.Length is < 1 or > 2 || string.IsNullOrWhiteSpace(args[0]))
        {
            return CommandResponse.FailureResponse("使用方法: /survey force-edit <SubmissionId / ReviewSubmissionDataId / UserId / QQ号> [SurveyId]\n请使用完整 ID；SurveyId 用于选择代填的入群问卷。");
        }

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MainDbContext>();
        var identifier = args[0];
        var submissionIds = await db.Submissions.Where(s => s.SubmissionId == identifier)
            .Select(s => s.SubmissionId).ToListAsync(cancellationToken);
        submissionIds.AddRange(await db.ReviewSubmissions.Where(r => r.ReviewSubmissionDataId == identifier)
            .Select(r => r.SubmissionId!).ToListAsync(cancellationToken));
        submissionIds = submissionIds.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var users = await db.Users.Where(u => u.UserId == identifier || u.QQId == identifier)
            .ToListAsync(cancellationToken);
        if (submissionIds.Count + users.Count > 1)
        {
            return CommandResponse.FailureResponse("❌ 参数匹配到多个对象，请使用目标提交的完整 SubmissionId。");
        }

        Submission? submission = null;
        User targetUser;
        if (submissionIds.Count == 1)
        {
            submission = await db.Submissions.Include(s => s.User).Include(s => s.Questionnaire)
                .SingleAsync(s => s.SubmissionId == submissionIds[0], cancellationToken);
            targetUser = submission.User;
        }
        else
        {
            if (users.Count == 1)
            {
                targetUser = users[0];
            }
            else if (identifier.Length <= 16 && long.TryParse(identifier, NumberStyles.None,
                CultureInfo.InvariantCulture, out var qqId) && qqId > 0)
            {
                var normalizedQqId = qqId.ToString(CultureInfo.InvariantCulture);
                targetUser = await db.Users.SingleOrDefaultAsync(u => u.QQId == normalizedQqId, cancellationToken)
                    ?? new User { QQId = normalizedQqId };
            }
            else
            {
                return CommandResponse.FailureResponse("❌ 无法找到该 ID 对应的对象；注册新用户时请提供有效的 QQ 号。");
            }

            submission = await db.Submissions.Include(s => s.Questionnaire)
                .Where(s => s.UserId == targetUser.UserId && s.Questionnaire.Survey.IsVerifySurvey)
                .OrderByDescending(s => s.CreatedAt).ThenBy(s => s.SubmissionId)
                .FirstOrDefaultAsync(cancellationToken);
        }

        Questionnaire questionnaire;
        if (submission is not null)
        {
            if (args.Length == 2)
            {
                return CommandResponse.FailureResponse("❌ 该用户已有提交，无需选择 SurveyId。请只提供目标 ID 或 QQ 号。");
            }
            questionnaire = submission.Questionnaire;
        }
        else
        {
            var surveys = await db.Surveys.Where(s => s.IsVerifySurvey).OrderBy(s => s.SurveyId)
                .ToListAsync(cancellationToken);
            if (surveys.Count == 0)
            {
                return CommandResponse.FailureResponse("❌ 系统未配置入群审核问卷。");
            }
            var selectedSurvey = args.Length == 2
                ? surveys.SingleOrDefault(s => string.Equals(s.SurveyId, args[1], StringComparison.OrdinalIgnoreCase))
                : surveys.Count == 1 ? surveys[0] : null;
            if (selectedSurvey is null)
            {
                var help = new StringBuilder("请选择入群问卷，使用 /survey force-edit <目标 ID 或 QQ 号> <SurveyId>:\n");
                foreach (var survey in surveys)
                {
                    help.AppendLine($"{survey.SurveyId}: {survey.Title} — {survey.Description}");
                }
                return CommandResponse.FailureResponse(help.ToString());
            }
            var latest = await db.Questionnaires.Where(q => q.SurveyId == selectedSurvey.SurveyId)
                .OrderByDescending(q => q.ReleaseDate).ThenBy(q => q.QuestionnaireId)
                .FirstOrDefaultAsync(cancellationToken);
            if (latest is null)
            {
                return CommandResponse.FailureResponse("❌ 所选入群问卷尚未发布 Questionnaire，无法生成链接。");
            }
            questionnaire = latest;
        }

        var admin = await db.Users.SingleAsync(u => u.QQId == context.UserId.ToString(), cancellationToken);
        var request = new Request
        {
            User = admin,
            RequestType = RequestType.ForceEdit,
            CreatedAt = _timeProvider.GetUtcNow().UtcDateTime
        };
        db.ForceEditGrants.Add(new ForceEditGrant
        {
            Request = request,
            TargetUser = targetUser,
            Questionnaire = questionnaire,
            Submission = submission
        });
        await db.SaveChangesAsync(cancellationToken);
        var link = $"{_apiOptions.SurveyLinkBase}actions/forceEdit?requestId={Uri.EscapeDataString(request.RequestId)}";
        return CommandResponse.SuccessResponse($"""
            ✅ {(submission is null ? "入群问卷代填" : "提交编辑")}链接已生成。
            {link}

            提交者 QQ: {targetUser.QQId}
            UserId: {targetUser.UserId}
            QuestionnaireId: {questionnaire.QuestionnaireId}
            SubmissionId: {submission?.SubmissionId ?? "保存时创建"}
            请勿泄露链接，链接2小时内有效，保存成功后失效。
            """);
    }
}
