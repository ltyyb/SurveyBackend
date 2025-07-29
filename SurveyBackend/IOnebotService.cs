using Sisters.WudiLib;
using Sisters.WudiLib.Responses;

namespace SurveyBackend
{
    public interface IOnebotService
    {
        bool IsAvailable { get; }
        DateTime LastMessageTime { get; }
        Task<SendGroupMessageResponseData?> SendGroupMessageAsync(long groupId, string message);
        Task<SendGroupMessageResponseData?> SendGroupMessageAsync(long groupId, Sisters.WudiLib.Message message);
        Task<SendMessageResponseData?> SendMessage(Sisters.WudiLib.Posts.Endpoint endpoint, string message);

        Task<SendMessageResponseData?> SendMessage(Sisters.WudiLib.Posts.Endpoint endpoint, Sisters.WudiLib.Message message);

        Task<SendMessageResponseData?> SendMessageWithAt(Sisters.WudiLib.Posts.Endpoint endpoint, long userId, string message);

        Task<SendMessageResponseData?> SendMessageWithAt(Sisters.WudiLib.Posts.Endpoint endpoint, long userId, SendingMessage message);
    }
}
