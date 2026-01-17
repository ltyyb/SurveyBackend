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
    public class CommandRegistry
    {
        private readonly Dictionary<string, ICommandHandler> _handlers = new(StringComparer.OrdinalIgnoreCase);

        public void RegisterCommand(ICommandHandler handler)
        {
            _handlers[handler.CommandName] = handler;
            foreach (var alias in handler.Aliases)
            {
                _handlers[alias] = handler;
            }
        }

        public bool TryExecuteCommand(string command, MessageContext context, out Message? response)
        {
            var parts = command.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
            {
                response = null;
                return false;
            }

            var cmdName = parts[0].TrimStart('/');
            var args = parts.Skip(1).ToArray();

            if (_handlers.TryGetValue(cmdName, out var handler))
            {
                return handler.Execute(context, args, out response);
            }

            response = new Message($"未知命令: {cmdName}，输入 /help 查看帮助");
            return false;
        }
        // 带权限控制的命令基类
        public abstract class AdminOnlyCommand(string adminId) : CommandHandlerBase
        {
            public virtual string RequiredPermission => "user";

            public bool HasPermission(MessageContext context)
            {
                // 实现权限检查逻辑
                if (RequiredPermission == "admin")
                {
                    // 检查是否是管理员
                    return context.UserId.ToString() == adminId;
                }
                return true;
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
}
