using MessageContext = Sisters.WudiLib.Posts.Message;

namespace SurveyBackend.Bot.Commands;
// vote 指令
public class VoteCommand : AuthorizedAsyncCommand
{
    private readonly IServiceScopeFactory _serviceScopeFactory;
    public VoteCommand(IServiceScopeFactory serviceScopeFactory) : base(serviceScopeFactory)
    {
        _serviceScopeFactory = serviceScopeFactory;
    }
    public override string CommandName => "vote";

    public override string Description => "投票一个需要审核的问卷。使用方法: /survey vote [SubmissionId] [a/d]\n " +
    "其中 a 代表通过，d 代表拒绝。SubmissionId 可以简写为前8位，具体可参考审核推送消息。";

    public override UserGroup[] RequiredPermission => [UserGroup.VerifiedUser, UserGroup.Admin, UserGroup.SuperAdmin];


    protected async override Task<CommandResponse?> ExecuteAuthorizedAsync(MessageContext context, string[] args, CancellationToken cancellationToken = default)
    {
        if (args.Length == 2)
        {
            var submissionIdInput = args[0];
            var voteInput = args[1];
            if (!voteInput.Equals("a", StringComparison.OrdinalIgnoreCase)
                && !voteInput.Equals("d", StringComparison.OrdinalIgnoreCase))
            {
                return CommandResponse.FailureResponse("❌ 无效的投票选项。请使用 'a' 代表通过，'d' 代表拒绝。");
            }
            var isApprove = voteInput.Equals("a", StringComparison.OrdinalIgnoreCase);

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
            var submission = reviewSubmissionData.Submission;
            if (submission.IsDisabled)
            {
                return CommandResponse.FailureResponse("❌ 该提交已被禁用，无法投票。");
            }
            if (reviewSubmissionData.Status != ReviewStatus.Pending)
            {
                return CommandResponse.FailureResponse("❌ 该提交已审核完毕，无法投票。");
            }
            var existingVote = await db.ReviewVotes
                .Where(v => v.ReviewSubmissionDataId == reviewSubmissionData.ReviewSubmissionDataId
                    && v.User.QQId == context.UserId.ToString())
                .SingleOrDefaultAsync(cancellationToken);
            if (existingVote is not null)
            {
                existingVote.VoteType = isApprove ? VoteType.Upvote : VoteType.Downvote;
                db.ReviewVotes.Update(existingVote);
                await db.SaveChangesAsync(cancellationToken);
                return CommandResponse.SuccessResponse("✅ 已更新您的投票。");
            }
            else
            {
                var user = await db.Users.Where(u => u.QQId == context.UserId.ToString())
                                         .SingleOrDefaultAsync(cancellationToken);
                if (user is null)
                {
                    return CommandResponse.FailureResponse("❌ 无法找到您的用户信息，请确保您已加入正确用户组。如有疑问请联系管理员。");
                }
                if (user.UserGroup != UserGroup.VerifiedUser && user.UserGroup != UserGroup.Admin && user.UserGroup != UserGroup.SuperAdmin)
                {
                    return CommandResponse.FailureResponse("❌ 您没有权限投票。请确保您已加入正确用户组。如有疑问请联系管理员。");
                }
                var vote = new ReviewVote
                {
                    ReviewSubmissionData = reviewSubmissionData,
                    User = user,
                    VoteType = isApprove ? VoteType.Upvote : VoteType.Downvote
                };
                db.ReviewVotes.Add(vote);
                await db.SaveChangesAsync(cancellationToken);
                return CommandResponse.SuccessResponse("✅ 已记录您的投票。");
            }
        }
        else
        {
            var msg = """
            参数不正确。
            本命令用于投票一个需要审核的问卷提交。你只能为审核群中待审核的提交投票。
            使用方法:
            /survey vote [SubmissionId] [a/d]

            其中 SubmissionId 是提交的 ID，可以简写为前8位，具体可参考审核推送消息。
            [a/d] 代表你的投票选项，a 代表通过，d 代表拒绝。

            例如:
            /survey vote abcdef12 a
            """;
            return CommandResponse.SuccessResponse(msg);
        }
    }
}
