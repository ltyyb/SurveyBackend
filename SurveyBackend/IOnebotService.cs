using Sisters.WudiLib.Responses;

namespace SurveyBackend
{
    public interface IOnebotService
    {
        bool IsAvailable { get; }
        Task<SendGroupMessageResponseData?> SendGroupMessageAsync(long groupId, string message);
        Task<SendGroupMessageResponseData?> SendGroupMessageAsync(long groupId, Sisters.WudiLib.Message message);
        Task<SendMessageResponseData?> SendMessage(Sisters.WudiLib.Posts.Endpoint endpoint, string message);

        Task<SendMessageResponseData?> SendMessage(Sisters.WudiLib.Posts.Endpoint endpoint, Sisters.WudiLib.Message message);

        Task<SendMessageResponseData?> SendMessageWithAt(Sisters.WudiLib.Posts.Endpoint endpoint, long userId, string message);

        Task<SendMessageResponseData?> SendMessageWithAt(Sisters.WudiLib.Posts.Endpoint endpoint, long userId, Sisters.WudiLib.Message message);
    }
}
