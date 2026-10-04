using MessageContext = Sisters.WudiLib.Posts.Message;

namespace SurveyBackend.Bot.Commands.Infrastructure;
// 异步命令处理器基类
public abstract class AsyncCommandHandlerBase : IAsyncCommandHandler, ICommandHandler
{
    public abstract string CommandName { get; }
    public virtual string[] Aliases => Array.Empty<string>();
    public abstract string Description { get; }
    /// <summary>
    /// 控制在 disable-system 状态下是否仍可被路由。
    /// </summary>
    public virtual bool IsSuperCommand => false;

    public abstract Task<CommandResponse?> ExecuteAsync(MessageContext context, string[] args, CancellationToken cancellationToken = default);

    // 同步版本的兼容方法
    public CommandResponse? Execute(MessageContext context, string[] args)
    {
        return ExecuteAsync(context, args).GetAwaiter().GetResult();
    }
}
