using Microsoft.EntityFrameworkCore;
using System.Reflection;
using System.Text;
using Message = Sisters.WudiLib.SendingMessage;
using MessageContext = Sisters.WudiLib.Posts.Message;

namespace SurveyBackend.Models
{
    // 命令响应封装类
    public class CommandResponse
    {
        public Message? Message { get; set; }
        public bool Success { get; set; }

        public CommandResponse(Message? message, bool success)
        {
            Message = message;
            Success = success;
        }

        public static CommandResponse SuccessResponse() => new(null, true);
        public static CommandResponse FailureResponse() => new(null, false);
        public static CommandResponse SuccessResponse(Message message) => new(message, true);
        public static CommandResponse FailureResponse(Message message) => new(message, false);
        public static CommandResponse FailureResponse(string messageText) => new(new Message(messageText), false);
        public static CommandResponse SuccessResponse(string messageText) => new(new Message(messageText), true);
    }

    // 命令接口
    public interface ICommandHandler
    {
        string CommandName { get; }
        string[] Aliases { get; }
        string Description { get; }
        CommandResponse? Execute(MessageContext context, string[] args);
    }

    // 命令处理器基类
    public abstract class CommandHandlerBase : ICommandHandler
    {
        public abstract string CommandName { get; }
        public virtual string[] Aliases => Array.Empty<string>();
        public abstract string Description { get; }

        public abstract CommandResponse? Execute(MessageContext context, string[] args);
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

        public CommandResponse? TryExecuteSurveyCommand(MessageContext context)
        {
            return TryExecuteSurveyCommandAsync(context).GetAwaiter().GetResult();
        }

        public async Task<CommandResponse?> TryExecuteSurveyCommandAsync(MessageContext context, CancellationToken cancellationToken = default)
        {
            var message = context.Content.Text ?? string.Empty;
            var trimmedMessage = message.Trim();

            // 检查是否以 /survey 开头
            if (!trimmedMessage.StartsWith(CMD_PREFIX, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            // 去掉前缀，获取实际命令
            var commandContent = trimmedMessage[CMD_PREFIX.Length..].Trim();

            // 如果只有前缀没有命令，显示帮助
            if (string.IsNullOrWhiteSpace(commandContent))
            {
                return CommandResponse.SuccessResponse(new Message(GetHelpMessage()));
            }

            // 拆分命令和参数（支持引号包裹的参数）
            var parts = ParseCommandParts(commandContent);
            var cmdName = parts[0].ToLower();
            var args = parts.Skip(1).ToArray();

            if (_handlers.TryGetValue(cmdName, out var handler))
            {
                if (handler is IAsyncCommandHandler asyncHandler)
                {
                    return await asyncHandler.ExecuteAsync(context, args, cancellationToken);
                }

                return handler.Execute(context, args);
            }

            // 如果命令不存在，显示帮助
            return CommandResponse.FailureResponse(new Message(GetHelpMessage($"未知命令: {cmdName}")));
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
            helpBuilder.AppendLine($"""
                                    =================================
                                    Developed by Aunt_nuozhen with ❤
                                    Powered by Aunt Studio & .NET 10
                                    后端版本: {Assembly
                                            .GetExecutingAssembly()
                                            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                                            .InformationalVersion ?? "未知"}
                                    """);

            return helpBuilder.ToString();
        }

        public IEnumerable<ICommandHandler> GetRegisteredCommands()
        {
            return _handlers.Values.Distinct();
        }

        private static string[] ParseCommandParts(string commandContent)
        {
            var parts = new List<string>();
            var current = new StringBuilder();
            var inQuotes = false;

            for (var i = 0; i < commandContent.Length; i++)
            {
                var ch = commandContent[i];

                if (ch == '"' )
                {
                    if (inQuotes && i + 1 < commandContent.Length && commandContent[i + 1] == '"')
                    {
                        // 允许在引号内用 "" 表示一个字面双引号
                        current.Append('"');
                        i++;
                        continue;
                    }

                    inQuotes = !inQuotes;
                    continue;
                }

                if (char.IsWhiteSpace(ch) && !inQuotes)
                {
                    if (current.Length > 0)
                    {
                        parts.Add(current.ToString());
                        current.Clear();
                    }

                    continue;
                }

                current.Append(ch);
            }

            if (current.Length > 0)
            {
                parts.Add(current.ToString());
            }

            return parts.ToArray();
        }
    }
    // 带权限控制的命令基类

    public abstract class AuthorizedCommand(IServiceScopeFactory _dbScopeFactory) : CommandHandlerBase
    {
        public virtual UserGroup[] RequiredPermission => [UserGroup.SuperAdmin, UserGroup.Admin];
        public bool HasPermission(MessageContext context)
        {
            UserGroup userGroup;
            using var scope = _dbScopeFactory.CreateScope();
            var _db = scope.ServiceProvider.GetRequiredService<MainDbContext>();
            var user = _db.Users.Where(u => u.QQId == context.UserId.ToString())
                                .SingleOrDefault();
            userGroup = user is null ? UserGroup.NewComer : user.UserGroup;

            return RequiredPermission.Contains(userGroup);
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

    // 异步命令处理

    public interface IAsyncCommandHandler
    {
        string CommandName { get; }
        string[] Aliases { get; }
        string Description { get; }
        Task<CommandResponse?> ExecuteAsync(MessageContext context, string[] args, CancellationToken cancellationToken = default);
    }
    // 异步命令处理器基类
    public abstract class AsyncCommandHandlerBase : IAsyncCommandHandler, ICommandHandler
    {
        public abstract string CommandName { get; }
        public virtual string[] Aliases => Array.Empty<string>();
        public abstract string Description { get; }

        public abstract Task<CommandResponse?> ExecuteAsync(MessageContext context, string[] args, CancellationToken cancellationToken = default);

        // 同步版本的兼容方法
        public CommandResponse? Execute(MessageContext context, string[] args)
        {
            var task = ExecuteAsync(context, args);
            task.Wait();
            return task.Result;
        }
    }
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
            UserGroup userGroup;
            using var scope = _dbScopeFactory.CreateScope();
            var _db = scope.ServiceProvider.GetRequiredService<MainDbContext>();
            var user = await _db.Users.Where(u => u.QQId == context.UserId.ToString())
                                .SingleOrDefaultAsync(cancellationToken);
            userGroup = user is null ? UserGroup.NewComer : user.UserGroup;

            return RequiredPermission.Contains(userGroup);
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

}
