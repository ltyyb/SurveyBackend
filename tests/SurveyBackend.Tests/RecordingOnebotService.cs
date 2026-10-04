using Sisters.WudiLib;
using Sisters.WudiLib.Responses;
using SurveyBackend.Bot;

namespace SurveyBackend.Tests;

internal sealed class RecordingOnebotService : IOnebotService
{
    public bool IsAvailable => true;
    public bool IsDisabled { get; set; }
    public HttpApiClient? onebotApi => null;
    public DateTime LastMessageTime => DateTime.UtcNow;
    public List<(long GroupId, string Content)> GroupMessages { get; } = [];
    public Func<long, Message, Task>? OnSend { get; set; }

    public async Task<SendGroupMessageResponseData?> SendGroupMessageAsync(long groupId, Message message)
    {
        GroupMessages.Add((groupId, message.Raw));
        if (OnSend is not null)
        {
            await OnSend(groupId, message);
        }
        return null;
    }

    public Task<SendGroupMessageResponseData?> SendGroupMessageAsync(long groupId, string message) => throw new NotSupportedException();
    public Task<SendMessageResponseData?> SendMessageAsync(Sisters.WudiLib.Posts.Endpoint endpoint, string message) => throw new NotSupportedException();
    public Task<SendMessageResponseData?> SendMessageAsync(Sisters.WudiLib.Posts.Endpoint endpoint, Message message) => throw new NotSupportedException();
    public Task<SendPrivateMessageResponseData?> SendPrivateMessageAsync(long qqId, string message) => throw new NotSupportedException();
    public Task<SendPrivateMessageResponseData?> SendPrivateMessageAsync(long qqId, Message message) => throw new NotSupportedException();
    public Task<SendMessageResponseData?> SendMessageWithAtAsync(Sisters.WudiLib.Posts.Endpoint endpoint, long userId, string message) => throw new NotSupportedException();
    public Task<SendMessageResponseData?> SendMessageWithAtAsync(Sisters.WudiLib.Posts.Endpoint endpoint, long userId, SendingMessage message) => throw new NotSupportedException();
    public Task<SendMessageResponseData?> ReplyMessageWithAtAsync(Sisters.WudiLib.Posts.Message fatherMessage, SendingMessage message) => throw new NotSupportedException();
}
