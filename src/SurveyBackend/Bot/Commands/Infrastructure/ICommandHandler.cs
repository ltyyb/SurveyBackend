using MessageContext = Sisters.WudiLib.Posts.Message;

namespace SurveyBackend.Bot.Commands.Infrastructure;

// 命令接口
public interface ICommandHandler
{
    string CommandName { get; }
    string[] Aliases { get; }
    string Description { get; }
    /// <summary>
    /// 控制在 disable-system 状态下是否仍可被路由。
    /// </summary>
    bool IsSuperCommand => false;
    CommandResponse? Execute(MessageContext context, string[] args);
}
