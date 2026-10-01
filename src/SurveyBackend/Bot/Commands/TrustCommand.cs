using Sisters.WudiLib.Posts;
using MessageContext = Sisters.WudiLib.Posts.Message;

namespace SurveyBackend.Bot.Commands;

// trust 指令
public class TrustCommand : AuthorizedAsyncCommand
{
    public override string CommandName => "trust";

    public override string Description => "无参时将本群所有成员设置为 VerifiedUser。仅应在初始化数据库时使用且仅允许在 MainGroupId 群内使用。\n" +
    "带参时将指定用户设置为 VerifiedUser。参数为用户QQ号。";

    private readonly BotOptions _botOptions;
    private readonly IOnebotService _onebot;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<TrustCommand> _logger;
    public TrustCommand(IOptions<BotOptions> botOptions, IOnebotService onebot, IServiceScopeFactory scopeFactory, ILogger<TrustCommand> logger)
        : base(scopeFactory)
    {
        _botOptions = botOptions.Value;
        _onebot = onebot;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }


    protected override async Task<CommandResponse?> ExecuteAuthorizedAsync(MessageContext context, string[] args, CancellationToken cancellationToken = default)
    {
        if (context is GroupMessage groupMsg)
        {
            if (args.Length == 1)
            {
                if (long.TryParse(args[0], out long userQQId))
                {
                    var result = await TrustUser(userQQId, cancellationToken);
                    if (result)
                    {
                        return CommandResponse.SuccessResponse($"成功注册并配置用户 {userQQId}。");
                    }
                    else
                    {
                        return CommandResponse.FailureResponse($"尝试注册并配置用户 {userQQId} 时发生错误。请查看日志获取详细信息。");
                    }
                }
                else
                {
                    return CommandResponse.FailureResponse("参数解析失败。请确保输入的参数为有效的QQ号。");
                }
            }
            else if (args.Length == 0 && groupMsg.GroupId == _botOptions.MainGroupId)
            {
                try
                {
                    if (_onebot.onebotApi is null || !_onebot.IsAvailable)
                    {
                        _logger.LogError("Onebot API 客户端未初始化");
                        return CommandResponse.FailureResponse("Onebot API 客户端未初始化，无法执行命令");
                    }
                    var members = await _onebot.onebotApi.GetGroupMemberListAsync(groupMsg.GroupId);
                    int successCount = 0;
                    int failCount = 0;
                    foreach (var member in members)
                    {
                        if (member.UserId == context.UserId)
                        {
                            // 跳过命令执行者，避免权限问题
                            continue;
                        }
                        var result = await TrustUser(member.UserId, cancellationToken);
                        if (result)
                        {
                            successCount++;
                        }
                        else
                        {
                            failCount++;
                        }
                    }
                    return CommandResponse.SuccessResponse($"成功注册并配置 {successCount} 个用户，失败 {failCount} 个用户。"
                    + "\n 查看日志获取详细信息。");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "尝试将群 {groupId} 中的用户注册到数据库中时发生错误", groupMsg.GroupId);
                    return CommandResponse.FailureResponse("尝试将群中用户注册到数据库中时发生错误:\n" + ex.Message);
                }
            }
            else
            {
                return CommandResponse.FailureResponse("❌ 不正确的用法。");
            }
        }
        else if (context is PrivateMessage)
        {
            if (args.Length == 1)
            {
                if (long.TryParse(args[0], out long userQQId))
                {
                    var result = await TrustUser(userQQId, cancellationToken);
                    if (result)
                    {
                        return CommandResponse.SuccessResponse($"成功注册并配置用户 {userQQId}。");
                    }
                    else
                    {
                        return CommandResponse.FailureResponse($"尝试注册并配置用户 {userQQId} 时发生错误。请查看日志获取详细信息。");
                    }
                }
                else
                {
                    return CommandResponse.FailureResponse("参数解析失败。请确保输入的参数为有效的QQ号。");
                }
            }
            else
            {
                return CommandResponse.FailureResponse("❌ 不正确的用法。");
            }
        }
        return null;
    }
    private async Task<bool> TrustUser(long userQQId, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("正在将用户 {user} 注册到数据库中。", userQQId.ToString());
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<MainDbContext>();
            User? user = await db.Users.Where(u => u.QQId == userQQId.ToString())
                                        .SingleOrDefaultAsync(cancellationToken);

            if (user is null)
            {
                // 用户不存在，创建新用户
                user = new User
                {
                    QQId = userQQId.ToString(),
                    UserGroup = UserGroup.VerifiedUser
                };
                db.Users.Add(user);
                await db.SaveChangesAsync(cancellationToken);
                _logger.LogInformation("用户 {qqId} 注册并设置为 VerifiedUser。", userQQId.ToString());
            }
            else if (user.UserGroup != UserGroup.VerifiedUser)
            {
                // 用户已存在，更新用户组
                user.UserGroup = UserGroup.VerifiedUser;
                db.Users.Update(user);
                await db.SaveChangesAsync(cancellationToken);
                _logger.LogInformation("用户 {qqId} 已存在，更新为 VerifiedUser。", userQQId.ToString());
            }
            return true;

        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "尝试添加用户 {qqId} 时发生错误", userQQId.ToString());
            return false;
        }
    }
}
