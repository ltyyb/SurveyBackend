using System.Text;
using MessageContext = Sisters.WudiLib.Posts.Message;

namespace SurveyBackend.Bot.Commands;
// disable 指令
public class DisableSubmissionCommand : AuthorizedAsyncCommand
{
    public override string CommandName => "disable";

    public override string Description => "禁用一个自己的提交，使其无法再被审核。使用方法: /survey disable [SubmissionId] \n" +
                                          "其中 SubmissionId 可以简写为前8位，本命令仅已审核用户可用。\n" +
                                          "留空SubmissionId将关闭入群问卷的审核权限。";
    public override UserGroup[] RequiredPermission => [UserGroup.VerifiedUser, UserGroup.Admin, UserGroup.SuperAdmin];

    private readonly IServiceScopeFactory _serviceScopeFactory;
    public DisableSubmissionCommand(IServiceScopeFactory serviceScopeFactory) : base(serviceScopeFactory)
    {
        _serviceScopeFactory = serviceScopeFactory;
    }

    protected async override Task<CommandResponse?> ExecuteAuthorizedAsync(MessageContext context, string[] args, CancellationToken cancellationToken = default)
    {
        if (args.Length == 1)
        {
            var submissionIdInput = args[0];
            using var scope = _serviceScopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<MainDbContext>();
            var user = await db.Users.Where(u => u.QQId == context.UserId.ToString())
                                     .SingleOrDefaultAsync(cancellationToken);
            if (user is null)
            {
                return CommandResponse.FailureResponse("❌ 无法找到您的用户信息，请确保您已正确注册。这是一个wtf错误，如有疑问请联系管理员。");
            }

            Submission? submission;
            if (user.UserGroup == UserGroup.SuperAdmin || user.UserGroup == UserGroup.Admin)
            {
                submission = await db.Submissions
                                                .Include(s => s.User)
                                                .Where(s => EF.Functions.Like(
                                                    s.SubmissionId,
                                                    submissionIdInput + "%"))
                                                .SingleOrDefaultAsync(cancellationToken);
            }
            else
            {
                submission = await db.Submissions
                                                .Include(s => s.User)
                                                .Where(s => EF.Functions.Like(
                                                        s.SubmissionId,
                                                        submissionIdInput + "%")
                                                        && s.UserId == user.UserId)
                                                .SingleOrDefaultAsync(cancellationToken);
            }
            if (submission is null)
            {
                return CommandResponse.FailureResponse("❌ 无法找到对应的提交。请检查输入的 SubmissionId 是否正确。");
            }
            if (submission.IsDisabled)
            {
                return CommandResponse.FailureResponse("❌ 该提交已被禁用，无需重复操作。");
            }
            submission.IsDisabled = true;
            db.Submissions.Update(submission);
            await db.SaveChangesAsync(cancellationToken);
            return CommandResponse.SuccessResponse($"✅ 已成功禁用该提交: {submission.SubmissionId} by {submission.User.QQId}");
        }
        else if (args.Length == 0)
        {
            using var scope = _serviceScopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<MainDbContext>();
            var submissions = await db.Submissions.Include(s => s.Questionnaire)
                                                    .ThenInclude(q => q.Survey)
                                                  .Include(s => s.User)
                                                  .Where(s => s.IsDisabled == false
                                                    && s.Questionnaire.Survey.IsVerifySurvey == true
                                                    && s.User.QQId == context.UserId.ToString())
                                                  .ToListAsync(cancellationToken);
            var sb = new StringBuilder();
            sb.AppendLine($"发现 {submissions.Count} 个您的入群问卷提交");
            foreach (var submission in submissions)
            {
                submission.IsDisabled = true;
                db.Submissions.Update(submission);
                sb.AppendLine($"✅ 已禁用提交 ID: {submission.SubmissionId}");
            }
            await db.SaveChangesAsync(cancellationToken);
            sb.AppendLine("已提交的更改。");
            return CommandResponse.SuccessResponse(sb.ToString());
        }
        else
        {
            var msg = """
            参数不正确。
            本命令用于禁用一个提交，使其无法再被公开获取回答或审核。
            使用方法:
            /survey disable [SubmissionId]

            其中 SubmissionId 是提交的 ID，可以简写为前8位，具体可参考审核推送消息。
            留空SubmissionId将关闭入群问卷的审核权限。

            例如:
            /survey disable abcdef12
            /survey disable
            """;
            return CommandResponse.SuccessResponse(msg);
        }
    }
}
