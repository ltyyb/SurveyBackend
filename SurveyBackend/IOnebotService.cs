using Sisters.WudiLib;
using Sisters.WudiLib.Responses;

namespace SurveyBackend
{
    public interface IOnebotService
    {
        bool IsAvailable { get; }
        bool IsDisabled { get; }
        HttpApiClient? onebotApi { get; }
        DateTime LastMessageTime { get; }
        Task<SendGroupMessageResponseData?> SendGroupMessageAsync(long groupId, string message);
        Task<SendGroupMessageResponseData?> SendGroupMessageAsync(long groupId, Sisters.WudiLib.Message message);
        Task<SendMessageResponseData?> SendMessageAsync(Sisters.WudiLib.Posts.Endpoint endpoint, string message);

        Task<SendMessageResponseData?> SendMessageAsync(Sisters.WudiLib.Posts.Endpoint endpoint, Sisters.WudiLib.Message message);
        Task<SendPrivateMessageResponseData?> SendPrivateMessageAsync(long qqId, string message);
        Task<SendPrivateMessageResponseData?> SendPrivateMessageAsync(long qqId, Sisters.WudiLib.Message message);
        Task<SendMessageResponseData?> SendMessageWithAtAsync(Sisters.WudiLib.Posts.Endpoint endpoint, long userId, string message);

        Task<SendMessageResponseData?> SendMessageWithAtAsync(Sisters.WudiLib.Posts.Endpoint endpoint, long userId, SendingMessage message);
        Task<SendMessageResponseData?> ReplyMessageWithAtAsync(Sisters.WudiLib.Posts.Message fatherMessage, SendingMessage message);
    }
}
