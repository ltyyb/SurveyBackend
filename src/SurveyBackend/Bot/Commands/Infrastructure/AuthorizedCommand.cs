using MessageContext = Sisters.WudiLib.Posts.Message;

namespace SurveyBackend.Bot.Commands.Infrastructure;
// 带权限控制的命令基类

public abstract class AuthorizedCommand(IServiceScopeFactory _dbScopeFactory) : CommandHandlerBase
{
    public virtual UserGroup[] RequiredPermission => [UserGroup.SuperAdmin, UserGroup.Admin];

    public bool HasPermission(MessageContext context)
    {
        using var scope = _dbScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MainDbContext>();
        var requiredPermissions = RequiredPermission;
        return db.Users.Any(u =>
            u.QQId == context.UserId.ToString()
            && requiredPermissions.Contains(u.UserGroup));
    }



    public override CommandResponse? Execute(MessageContext context, string[] args)
    {
        if (!HasPermission(context))
        {
            return CommandResponse.FailureResponse("你没有使用这一指令的权限。");
        }
        return ExecuteAuthorized(context, args);
    }

    protected abstract CommandResponse? ExecuteAuthorized(MessageContext context, string[] args);
}
