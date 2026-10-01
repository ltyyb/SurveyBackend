using MessageContext = Sisters.WudiLib.Posts.Message;

namespace SurveyBackend.Bot.Commands;
// SetUser 指令
public class SetUserCommand : AuthorizedAsyncCommand
{
    public override string CommandName => "setuser";

    public override string Description => """
                                          设置指定用户的权限组。
                                          用法:
                                          /survey setuser [QQ号] [权限组]
                                          不带参使用以查看详细用法。
                                          """;

    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly ILogger<SetUserCommand> _logger;
    public SetUserCommand(IServiceScopeFactory serviceScopeFactory, ILogger<SetUserCommand> logger) : base(serviceScopeFactory)
    {
        _serviceScopeFactory = serviceScopeFactory;
        _logger = logger;
    }

    protected override async Task<CommandResponse?> ExecuteAuthorizedAsync(MessageContext context, string[] args, CancellationToken cancellationToken = default)
    {
        if (args.Length == 2)
        {
            var qqIdInput = args[0];
            var userGroupInput = args[1];
            if (!long.TryParse(qqIdInput, out long qqId))
            {
                return CommandResponse.FailureResponse("❌ 无效的QQ号。请确保输入的QQ号为数字。");
            }
            if (!Enum.TryParse(userGroupInput, true, out UserGroup userGroup) || !Enum.IsDefined(userGroup))
            {
                return CommandResponse.FailureResponse("❌ 无效的用户组。请使用 VerifiedUser, NewComer, PendingUser, Admin 或 SuperAdmin。");
            }
            using var scope = _serviceScopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<MainDbContext>();
            var user = await db.Users.Where(u => u.QQId == qqIdInput)
                                     .SingleOrDefaultAsync(cancellationToken);
            if (user is null)
            {
                return CommandResponse.FailureResponse("❌ 无法找到对应的用户。请检查输入的QQ号是否正确。");
            }
            user.UserGroup = userGroup;
            db.Users.Update(user);
            await db.SaveChangesAsync(cancellationToken);
            return CommandResponse.SuccessResponse($"✅ 已将用户 {qqIdInput} 的用户组设置为 {userGroup}。");
        }
        else
        {
            var msg = """
            参数不正确。
            本命令用于设置指定用户的权限组。
            使用方法:
            /survey setuser [QQ号] [权限组]

            其中 [权限组] 可选值为 NewComer, PendingUser, VerifiedUser, Admin 或 SuperAdmin, 或他们的值:
            NewComer = 0,
            PendingUser = 1,
            VerifiedUser = 2,
            Admin = 99,
            SuperAdmin = 100

            例如:
            /survey setuser 123456789 VerifiedUser
            /survey setuser 123456789 2
            """;
            return CommandResponse.SuccessResponse(msg);
        }
    }
}
