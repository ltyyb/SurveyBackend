using MessageContext = Sisters.WudiLib.Posts.Message;

namespace SurveyBackend.Bot.Commands.Infrastructure;

// 异步命令处理

public interface IAsyncCommandHandler
{
    string CommandName { get; }
    string[] Aliases { get; }
    string Description { get; }
    /// <summary>
    /// 控制在 disable-system 状态下是否仍可被路由。
    /// </summary>
    bool IsSuperCommand => false;
    Task<CommandResponse?> ExecuteAsync(MessageContext context, string[] args, CancellationToken cancellationToken = default);
}
