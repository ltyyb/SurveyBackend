using MessageContext = Sisters.WudiLib.Posts.Message;

namespace SurveyBackend.Bot.Commands.Infrastructure;

// 命令处理器基类
public abstract class CommandHandlerBase : ICommandHandler
{
    public abstract string CommandName { get; }
    public virtual string[] Aliases => Array.Empty<string>();
    public abstract string Description { get; }
    /// <summary>
    /// 控制在 disable-system 状态下是否仍可被路由。
    /// </summary>
    public virtual bool IsSuperCommand => false;

    public abstract CommandResponse? Execute(MessageContext context, string[] args);
}
