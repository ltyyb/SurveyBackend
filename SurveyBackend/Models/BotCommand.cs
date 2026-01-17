using Microsoft.EntityFrameworkCore;
using System.Text;
using Message = Sisters.WudiLib.SendingMessage;
using MessageContext = Sisters.WudiLib.Posts.Message;

namespace SurveyBackend.Models
{
    // 命令接口
    public interface ICommandHandler
    {
        string CommandName { get; }
        string[] Aliases { get; }
        string Description { get; }
        bool Execute(MessageContext context, string[] args, out Message response);
    }

    // 命令处理器基类
    public abstract class CommandHandlerBase : ICommandHandler
    {
        public abstract string CommandName { get; }
        public virtual string[] Aliases => Array.Empty<string>();
        public abstract string Description { get; }

        public abstract bool Execute(MessageContext context, string[] args, out Message response);
    }

    // 命令注册器
    public class SurveyCommandRegistry
    {
        private readonly Dictionary<string, ICommandHandler> _handlers = new(StringComparer.OrdinalIgnoreCase);
        public const string CMD_PREFIX = "/survey";

        public void RegisterCommand(ICommandHandler handler)
        {
            _handlers[handler.CommandName] = handler;
            foreach (var alias in handler.Aliases)
            {
                _handlers[alias] = handler;
            }
        }

        public bool TryExecuteSurveyCommand(string message, MessageContext context, out Message? response)
        {
            var trimmedMessage = message.Trim();

            // 检查是否以 /survey 开头
            if (!trimmedMessage.StartsWith(CMD_PREFIX, StringComparison.OrdinalIgnoreCase))
            {
                response = null;
                return false;
            }

            // 去掉前缀，获取实际命令
            var commandContent = trimmedMessage[CMD_PREFIX.Length..].Trim();

            // 如果只有前缀没有命令，显示帮助
            if (string.IsNullOrWhiteSpace(commandContent))
            {
                response = GetHelpMessage();
                return true;
            }

            // 拆分命令和参数
            var parts = commandContent.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var cmdName = parts[0].ToLower();
            var args = parts.Skip(1).ToArray();

            if (_handlers.TryGetValue(cmdName, out var handler))
            {
                return handler.Execute(context, args, out response);
            }

            // 如果命令不存在，显示帮助
            response = GetHelpMessage($"未知命令: {cmdName}");
            return false;
        }

        private string GetHelpMessage(string? customMessage = null)
        {
            var helpBuilder = new StringBuilder();

            if (!string.IsNullOrEmpty(customMessage))
            {
                helpBuilder.AppendLine(customMessage);
                helpBuilder.AppendLine();
            }

            helpBuilder.AppendLine($"使用 {CMD_PREFIX} + 命令 来操作 SurveyBot");
            helpBuilder.AppendLine("可用命令:");

            foreach (var handler in _handlers.Values.Distinct())
            {
                helpBuilder.AppendLine($"• {CMD_PREFIX} {handler.CommandName} - {handler.Description}");
                if (handler.Aliases.Length > 0)
                {
                    helpBuilder.AppendLine($"  别名: {string.Join(", ", handler.Aliases.Select(a => $"{CMD_PREFIX} {a}"))}");
                }
            }

            helpBuilder.AppendLine($"\n示例: {CMD_PREFIX} help");

            return helpBuilder.ToString();
        }

        public IEnumerable<ICommandHandler> GetRegisteredCommands()
        {
            return _handlers.Values.Distinct();
        }
    }
    // 带权限控制的命令基类

    public abstract class AuthorizedCommand(MainDbContext _db) : CommandHandlerBase
    {
        public virtual UserGroup[] RequiredPermission => [UserGroup.SuperAdmin, UserGroup.Admin];
        public bool HasPermission(MessageContext context)
        {
            UserGroup userGroup;
            // 实现权限检查逻辑
            var user = _db.Users.Where(u => u.QQId == context.UserId.ToString())
                                .SingleOrDefault();
            userGroup = user is null ? UserGroup.NewComer : user.UserGroup;

            return RequiredPermission.Contains(userGroup);
        }



        public override bool Execute(MessageContext context, string[] args, out Message response)
        {
            if (!HasPermission(context))
            {
                response = new Message("你没有使用这一指令的权限。");
                return false;
            }
            return ExecuteAuthorized(context, args, out response);
        }

        protected abstract bool ExecuteAuthorized(MessageContext context, string[] args, out Message response);
    }

    // 异步命令处理
    public interface IAsyncCommandHandler
    {
        Task<bool> ExecuteAsync(MessageContext context, string[] args, out Message response);
    }

}
