using System.Reflection;
using System.Text;
using Message = Sisters.WudiLib.SendingMessage;
using MessageContext = Sisters.WudiLib.Posts.Message;

namespace SurveyBackend.Bot.Commands.Infrastructure;

// 命令注册器
public class SurveyCommandRegistry
{
    private readonly Dictionary<string, ICommandHandler> _handlers = new(StringComparer.OrdinalIgnoreCase);
    public const string CMD_PREFIX = "/survey";
    public bool IsSystemDisabled { get; set; } = false;

    private readonly IServiceScopeFactory _serviceScopeFactory;
    public SurveyCommandRegistry(IServiceScopeFactory serviceScopeFactory)
    {
        _serviceScopeFactory = serviceScopeFactory;
    }

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
        try
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
                if (IsSystemDisabled)
                    return CreateSystemDisabledResponse();
                else
                    return CommandResponse.SuccessResponse(new Message(
                        GetHelpMessage(await GetUserGroupAsync(context.UserId, cancellationToken))));
            }

            // 拆分命令和参数（支持引号包裹的参数）
            var parts = ParseCommandParts(commandContent);
            var cmdName = parts[0];
            var args = parts[1..];

            if (_handlers.TryGetValue(cmdName, out var handler))
            {
                if (IsSystemDisabled && !handler.IsSuperCommand)
                {
                    return CreateSystemDisabledResponse();
                }

                return handler is IAsyncCommandHandler asyncHandler
                    ? await asyncHandler.ExecuteAsync(context, args, cancellationToken)
                    : handler.Execute(context, args);
            }

            // 如果命令不存在，显示帮助
            var userGroup = await GetUserGroupAsync(context.UserId, cancellationToken);
            return CommandResponse.FailureResponse(new Message(
                GetHelpMessage(userGroup, $"未知命令: {cmdName}")));
        }
        catch (InvalidOperationException ex)
            when (ex.Message.Contains("构造消息段失败", StringComparison.Ordinal))
        {
            throw;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"发生命令执行异常: {ex}");
            return CommandResponse.FailureResponse($"执行命令时发生异常:\n{ex.GetType().FullName}: {ex.Message}\n\n稍后再试或联系管理员。");
        }


    }

    private async Task<UserGroup> GetUserGroupAsync(long userId, CancellationToken cancellationToken)
    {
        using var scope = _serviceScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MainDbContext>();
        var userGroup = await db.Users
            .Where(u => u.QQId == userId.ToString())
            .Select(u => (UserGroup?)u.UserGroup)
            .SingleOrDefaultAsync(cancellationToken);

        return userGroup ?? UserGroup.NewComer;
    }

    private static CommandResponse CreateSystemDisabledResponse()
    {
        return CommandResponse.FailureResponse(
            "当前 Survey 服务已临时关闭，可能正在进行检修或其他事宜。请联系管理员获得帮助。");
    }
    private string GetHelpMessage(UserGroup userGroup, string? customMessage = null)
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
            if (handler is AuthorizedAsyncCommand asyncAuthHandler)
            {
                if (asyncAuthHandler.RequiredPermission.Contains(userGroup))
                {
                    helpBuilder.AppendLine($"↣ {CMD_PREFIX} {handler.CommandName} - {handler.Description}");
                    if (handler.Aliases.Length > 0)
                    {
                        helpBuilder.AppendLine($"  别名: {string.Join(", ", handler.Aliases.Select(a => $"{CMD_PREFIX} {a}"))}");
                    }
                }
            }
            else if (handler is AuthorizedCommand authHandler)
            {
                if (authHandler.RequiredPermission.Contains(userGroup))
                {
                    helpBuilder.AppendLine($"↠ {CMD_PREFIX} {handler.CommandName} - {handler.Description}");
                    if (handler.Aliases.Length > 0)
                    {
                        helpBuilder.AppendLine($"  别名: {string.Join(", ", handler.Aliases.Select(a => $"{CMD_PREFIX} {a}"))}");
                    }
                }
            }
            else
            {
                helpBuilder.AppendLine($"→ {CMD_PREFIX} {handler.CommandName} - {handler.Description}");
                if (handler.Aliases.Length > 0)
                {
                    helpBuilder.AppendLine($"  别名: {string.Join(", ", handler.Aliases.Select(a => $"{CMD_PREFIX} {a}"))}");
                }
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

            if (ch == '"')
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
