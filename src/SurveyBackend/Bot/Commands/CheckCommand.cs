using Message = Sisters.WudiLib.SendingMessage;
using MessageContext = Sisters.WudiLib.Posts.Message;

namespace SurveyBackend.Bot.Commands;

// check 指令
public class CheckCommand : AuthorizedAsyncCommand
{
    public override string CommandName => "check";
    public override string Description => "使用方法: /survey check \n检查自己问卷的信息，包括投票数据等。";
    public override UserGroup[] RequiredPermission => [UserGroup.PendingUser];
    private readonly IServiceScopeFactory _serviceScopeFactory;
    public CheckCommand(IServiceScopeFactory serviceScopeFactory) : base(serviceScopeFactory)
    {
        _serviceScopeFactory = serviceScopeFactory;
    }
    protected async override Task<CommandResponse?> ExecuteAuthorizedAsync(MessageContext context, string[] args, CancellationToken cancellationToken = default)
    {
        if (args.Length == 0)
        {
            using var scope = _serviceScopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<MainDbContext>();
            var user = await db.Users.Where(u => u.QQId == context.UserId.ToString())
                                     .SingleOrDefaultAsync(cancellationToken);
            if (user is null)
            {
                return CommandResponse.FailureResponse("❌ 无法找到您的用户数据，请确保您已提交过问卷并等待审核。");
            }
            var reviewSubmissionData = await db.ReviewSubmissions.Include(r => r.Submission)
                                                                   .ThenInclude(s => s.Questionnaire)
                                                                       .ThenInclude(q => q.Survey)
                                                                   .Where(r => r.Submission.UserId == user.UserId
                                                                      && r.Submission.Questionnaire.Survey.IsVerifySurvey == true)
                                                                   .SingleOrDefaultAsync(cancellationToken);
            if (reviewSubmissionData is null)
            {
                return CommandResponse.FailureResponse("❌ 无法找到您的审核问卷提交数据，请确保您提交的问卷是入群审核问卷，并且已进入审核流程。");
            }
            string reviewStatusMsg = reviewSubmissionData.Status switch
            {
                ReviewStatus.Pending => "🟡 审核进行中",
                ReviewStatus.Approved => "🟢 已通过",
                ReviewStatus.Rejected => "🔴 已拒绝",
                _ => "⚪ 未知",
            };
            var votes = await db.ReviewVotes.Where(v => v.ReviewSubmissionDataId == reviewSubmissionData.ReviewSubmissionDataId)
                                            .ToListAsync(cancellationToken);
            string msg = $"""
                    您的审核问卷提交信息:

                    Submission ID: {reviewSubmissionData.Submission.SubmissionId}
                    作答的 Questionnaire ID: {reviewSubmissionData.Submission.QuestionnaireId} ({reviewSubmissionData.Submission.Questionnaire.ReleaseDate})
                    关联问卷: {reviewSubmissionData.Submission.Questionnaire.Survey.Title} ({reviewSubmissionData.Submission.Questionnaire.Survey.SurveyId})
                    提交时间: {reviewSubmissionData.Submission.CreatedAt}
                    众审状态: {reviewStatusMsg}
                    赞成票: {votes.Count(v => v.VoteType == VoteType.Upvote)}
                    反对票: {votes.Count(v => v.VoteType == VoteType.Downvote)}
                    赞成率: {(votes.Count == 0 ? "N/A" : $"{(double)votes.Count(v => v.VoteType == VoteType.Upvote) / votes.Count * 100:F2}%")}
                    """;
            return CommandResponse.SuccessResponse(new Message(msg));
        }
        else
        {
            var msg = """
            参数不正确。
            本命令用于检查自己的审核问卷提交信息，包括当前审核状态和众审投票数据。

            使用方法:
            /survey check

            仅当您处于待审核用户组时有效，且会自动匹配您提交的审核问卷提交数据。
            """;
            return CommandResponse.SuccessResponse(msg);
        }
    }
}
