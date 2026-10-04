using MessageContext = Sisters.WudiLib.Posts.Message;

namespace SurveyBackend.Bot.Commands.Infrastructure;
// 带权限控制的异步命令基类
public abstract class AuthorizedAsyncCommand : AsyncCommandHandlerBase
{
    private readonly IServiceScopeFactory _dbScopeFactory;
    public AuthorizedAsyncCommand(IServiceScopeFactory dbScopeFactory)
    {
        _dbScopeFactory = dbScopeFactory;
    }
    public virtual UserGroup[] RequiredPermission => [UserGroup.SuperAdmin, UserGroup.Admin];

    public async Task<bool> HasPermissionAsync(MessageContext context, CancellationToken cancellationToken = default)
    {
        using var scope = _dbScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MainDbContext>();
        var requiredPermissions = RequiredPermission;
        return await db.Users.AnyAsync(
            u => u.QQId == context.UserId.ToString()
                && requiredPermissions.Contains(u.UserGroup),
            cancellationToken);
    }
    public override sealed async Task<CommandResponse?> ExecuteAsync(MessageContext context, string[] args, CancellationToken cancellationToken = default)
    {
        if (!await HasPermissionAsync(context, cancellationToken))
        {
            return CommandResponse.FailureResponse("❌ 权限不足，无法执行此命令");
        }

        return await ExecuteAuthorizedAsync(context, args, cancellationToken);
    }

    protected abstract Task<CommandResponse?> ExecuteAuthorizedAsync(MessageContext context, string[] args, CancellationToken cancellationToken = default);
}
