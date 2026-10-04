using NanoidDotNet;

namespace SurveyBackend.Models;

public class Survey
{
    public string SurveyId { get; set; } = Nanoid.Generate(size: 8);
    public string Title { get; set; } = "未命名问卷";
    public string Description { get; set; } = "";
    /// <summary>
    /// 控制是否每个用户只能提交一次
    /// </summary>
    public bool UniquePerUser { get; set; }
    public bool NeedReview { get; set; }
    public bool IsVerifySurvey { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Survey() { }
    public Survey(string title, string description, bool uniquePerUser, bool needReview, bool isVerifyQuestionnaire, DateTime releaseDate)
    {
        Title = title;
        Description = description;
        IsVerifySurvey = isVerifyQuestionnaire;
        CreatedAt = releaseDate;
        if (IsVerifySurvey)
        {
            UniquePerUser = true;
            NeedReview = true;
        }
        else
        {
            UniquePerUser = uniquePerUser;
            NeedReview = needReview;
        }
    }
}
