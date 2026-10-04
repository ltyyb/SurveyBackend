using NanoidDotNet;

namespace SurveyBackend.Models;

public class Request
{
    public string RequestId { get; set; } = Nanoid.Generate(size: 16);
    public RequestType RequestType { get; set; }
    public required User User { get; set; }
    public string? UserId { get; set; }
    public bool IsDisabled { get; set; } = false;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public Request() { }
    public Request(User user, RequestType requestType)
    {
        User = user;
        RequestType = requestType;
    }
}
