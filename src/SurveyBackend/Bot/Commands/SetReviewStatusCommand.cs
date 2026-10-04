using MessageContext = Sisters.WudiLib.Posts.Message;

namespace SurveyBackend.Bot.Commands;
// setreviewstatus 指令
public class SetReviewStatusCommand : AuthorizedAsyncCommand
{
    public override string CommandName => "setreviewstatus";
    public override string[] Aliases => ["setrs"];
    public override string Description => "设置一个提交的众审状态。使用方法: /survey setrs [SubmissionId] [pending/approved/rejected] \n" +
                                          "其中 SubmissionId 可以简写为前8位，本命令仅审核管理员可用。\n" +
                                          "该命令将直接设置众审状态，不受投票结果影响。";
    public override UserGroup[] RequiredPermission => [UserGroup.Admin, UserGroup.SuperAdmin];
    private readonly IServiceScopeFactory _serviceScopeFactory;
    public SetReviewStatusCommand(IServiceScopeFactory serviceScopeFactory) : base(serviceScopeFactory)
    {
        _serviceScopeFactory = serviceScopeFactory;
    }
    protected async override Task<CommandResponse?> ExecuteAuthorizedAsync(MessageContext context, string[] args, CancellationToken cancellationToken = default)
    {
        if (args.Length == 2)
        {
            var submissionIdInput = args[0];
            var statusInput = args[1];
            ReviewStatus newStatus;
            if (statusInput.Equals("pending", StringComparison.OrdinalIgnoreCase))
            {
                newStatus = ReviewStatus.Pending;
            }
            else if (statusInput.Equals("approved", StringComparison.OrdinalIgnoreCase))
            {
                newStatus = ReviewStatus.Approved;
            }
            else if (statusInput.Equals("rejected", StringComparison.OrdinalIgnoreCase))
            {
                newStatus = ReviewStatus.Rejected;
            }
            else
            {
                return CommandResponse.FailureResponse("❌ 无效的状态选项。请使用 'pending' 代表待审，'approved' 代表通过，'rejected' 代表拒绝。");
            }

            using var scope = _serviceScopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<MainDbContext>();
            var reviewSubmissionData = await db.ReviewSubmissions.Include(r => r.Submission)
                                               .Where(s => EF.Functions.Like(
                                                   s.Submission.SubmissionId,
                                                   submissionIdInput + "%"))
                                               .SingleOrDefaultAsync(cancellationToken);
            if (reviewSubmissionData is null)
            {
                return CommandResponse.FailureResponse("❌ 无法找到对应的提交。请检查输入的 SubmissionId 是否正确。");
            }
            reviewSubmissionData.Status = newStatus;
            db.ReviewSubmissions.Update(reviewSubmissionData);
            await db.SaveChangesAsync(cancellationToken);
            return CommandResponse.SuccessResponse($"✅ 已将 Submission {reviewSubmissionData.Submission.SubmissionId} 的众审状态设置为 {newStatus}。");
        }
        else
        {
            var msg = """
            参数不正确。
            本命令用于设置一个提交的众审状态。
            使用方法:
            /survey setrs [SubmissionId] [pending/approved/rejected]

            其中 SubmissionId 是提交的 ID，可以简写为前8位。
            """;
            return CommandResponse.SuccessResponse(msg);
        }
    }
}
