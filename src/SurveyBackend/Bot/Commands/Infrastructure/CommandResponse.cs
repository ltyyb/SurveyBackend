using Message = Sisters.WudiLib.SendingMessage;

namespace SurveyBackend.Bot.Commands.Infrastructure;

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
