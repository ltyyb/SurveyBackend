using Message = Sisters.WudiLib.SendingMessage;
using MessageContext = Sisters.WudiLib.Posts.Message;

namespace SurveyBackend.Bot.Commands;
// review 指令
public class ReviewCommand : AuthorizedAsyncCommand
{
    public override string CommandName => "review";
    public override string[] Aliases => ["rv"];
    public override string Description => "使用方法: /survey review [SubmissionId] \n获取一个需审核问卷的审核链接。SubmissionId 可以简写。";
    public override UserGroup[] RequiredPermission => [UserGroup.VerifiedUser, UserGroup.Admin, UserGroup.SuperAdmin];
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly ApiOptions _apiOptions;
    public ReviewCommand(IServiceScopeFactory serviceScopeFactory, IOptions<ApiOptions> apiOptions) : base(serviceScopeFactory)
    {
        _serviceScopeFactory = serviceScopeFactory;
        _apiOptions = apiOptions.Value;
    }
    protected async override Task<CommandResponse?> ExecuteAuthorizedAsync(MessageContext context, string[] args, CancellationToken cancellationToken = default)
    {
        if (args.Length == 1)
        {
            var submissionId = args[0];
            using var scope = _serviceScopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<MainDbContext>();
            var reviewSubmissions = await db.ReviewSubmissions.Include(r => r.Submission)
                                                               .ThenInclude(s => s.Questionnaire)
                                                                   .ThenInclude(q => q.Survey)
                                                                .Include(r => r.Submission)
                                                                    .ThenInclude(s => s.User)
                                                               .Where(r => EF.Functions.Like(r.SubmissionId, submissionId + "%")
                                                                  && r.Submission.Questionnaire.Survey.NeedReview == true)
                                                               .ToListAsync(cancellationToken);
            if (reviewSubmissions.Count == 0)
            {
                return CommandResponse.FailureResponse("❌ 无法找到对应的审核问卷提交数据，请检查 Submission ID 是否正确，且该提交所属问卷为 NeedReview Survey。");
            }
            if (reviewSubmissions.Count > 1)
            {
                return CommandResponse.FailureResponse("❌ 找到多个匹配的审核问卷提交数据，请提供更完整的 Submission ID 以获得准确匹配。");
            }
            var reviewSubmission = reviewSubmissions[0];
            var surveyLinkEndpoint = _apiOptions.SurveyLinkBase;
            string reviewLink = $"{surveyLinkEndpoint}?review=true&questionnaireId={reviewSubmission.Submission.QuestionnaireId}&submissionId={reviewSubmission.SubmissionId}";
            string msg = $"""
                    审核链接: {reviewLink}

                    Submission ID: {reviewSubmission.SubmissionId}
                    用户 QQ: {reviewSubmission.Submission.User.QQId}
                    作答的 Questionnaire ID: {reviewSubmission.Submission.QuestionnaireId} ({reviewSubmission.Submission.Questionnaire.ReleaseDate.ToDisplayTime()})
                    关联问卷: {reviewSubmission.Submission.Questionnaire.Survey.Title} ({reviewSubmission.Submission.Questionnaire.Survey.SurveyId})
                    提交时间: {reviewSubmission.Submission.CreatedAt.ToDisplayTime()}
                    """;
            return CommandResponse.SuccessResponse(new Message(msg));
        }
        else
        {
            var msg = """
            参数不正确。
            本命令用于获取一个需审核问卷的审核链接。

            使用方法:
            /survey review [SubmissionId]

            其中 SubmissionId 为需要审核的问卷提交的 SubmissionId，支持前缀匹配但应尽可能使用完整 SubmissionId 以获得准确匹配。该提交所属的问卷必须为 NeedReview Survey。
            该命令会返回一个审核链接，点击后可以进入审核界面进行审核操作。
            """;
            return CommandResponse.SuccessResponse(msg);
        }
    }

}
