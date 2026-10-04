using MessageContext = Sisters.WudiLib.Posts.Message;

namespace SurveyBackend.Bot.Commands;
// insight 指令
public class InsightCommand : AuthorizedAsyncCommand
{
    public override string CommandName => "insight";
    public override string Description => "使用方法: /survey insight [SubmissionId] \n 获取指定问卷提交的AI分析。SubmissionId 可以简写。";
    public override UserGroup[] RequiredPermission => [UserGroup.VerifiedUser, UserGroup.Admin, UserGroup.SuperAdmin];
    private readonly IServiceScopeFactory _serviceScopeFactory;
    public InsightCommand(IServiceScopeFactory serviceScopeFactory) : base(serviceScopeFactory)
    {
        _serviceScopeFactory = serviceScopeFactory;
    }
    protected async override Task<CommandResponse?> ExecuteAuthorizedAsync(MessageContext context, string[] args, CancellationToken cancellationToken = default)
    {
        if (args.Length == 1)
        {
            var submissionId = args[0];
            using var scope = _serviceScopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<MainDbContext>();
            var submissions = await db.Submissions.Include(s => s.User)
                                                 .Include(s => s.Questionnaire)
                                                    .ThenInclude(q => q.Survey)
                                                 .Where(s => EF.Functions.Like(
                                                    s.SubmissionId,
                                                    submissionId + "%"))
                                                 .ToListAsync(cancellationToken);
            if (submissions.Count == 0)
            {
                return CommandResponse.FailureResponse("❌ 无法找到对应的问卷提交数据，请检查 Submission ID 是否正确。");
            }
            if (submissions.Count > 1)
            {
                return CommandResponse.FailureResponse("❌ 找到多个匹配的问卷提交数据，请提供更完整的 Submission ID 以获得准确匹配。");
            }
            var submission = submissions[0];
            var reviewData = await db.ReviewSubmissions.Where(r => r.SubmissionId == submission.SubmissionId)
                                           .SingleOrDefaultAsync(cancellationToken);
            if (reviewData is null)
            {
                return CommandResponse.FailureResponse("❌ 无法找到对应的审核数据，可能该问卷非审核问卷。");
            }
            var insight = reviewData.AIInsights;
            if (string.IsNullOrEmpty(insight) || insight == "不可用")
            {
                return CommandResponse.FailureResponse("❌ AI分析尚未完成或不可用。");
            }
            return CommandResponse.SuccessResponse(insight);
        }
        else
        {
            var msg = """
                参数不正确。
                本命令用于获取指定问卷提交已有的AI分析。

                使用方法:
                /survey insight [SubmissionId]
                SubmissionId 可以简写。

                更多信息请查阅文档或联系管理员。
                """;
            return CommandResponse.SuccessResponse(msg);
        }
    }
}
