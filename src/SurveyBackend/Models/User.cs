using NanoidDotNet;

namespace SurveyBackend.Models;
/// <summary>
/// 用户实体类
/// </summary>
public class User
{
    public string UserId { get; set; } = Nanoid.Generate(size: 16);

    public required string QQId { get; set; }
    public UserGroup UserGroup { get; set; } = UserGroup.NewComer;
    public User()
    {

    }

    public User(string qqId)
    {
        QQId = qqId;
    }
}
