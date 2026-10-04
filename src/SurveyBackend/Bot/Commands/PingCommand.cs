using MessageContext = Sisters.WudiLib.Posts.Message;

namespace SurveyBackend.Bot.Commands;

// ping 指令
public class PingCommand : AsyncCommandHandlerBase
{
    public override string CommandName => "ping";
    public override string Description => "使用方法: /survey ping \n 测试问卷系统是否可用。";

    public PingCommand() : base()
    {
    }

    public override async Task<CommandResponse?> ExecuteAsync(MessageContext context, string[] args, CancellationToken cancellationToken = default)
    {
        if (args.Length == 0)
        {
            return CommandResponse.SuccessResponse("Pong~ 问卷系统可用。");
        }
        else
        {
            var msg = """
                参数不正确。
                本命令用于测试问卷系统是否可用。

                使用方法:
                /survey ping
                """;
            return CommandResponse.SuccessResponse(msg);
        }
    }
}
