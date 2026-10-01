namespace SurveyBackend.Models;

public class ReviewVote
{
    public int Id { get; set; }
    /// <summary>
    /// 投票目标问卷提交导航属性
    /// </summary>
    public required ReviewSubmissionData ReviewSubmissionData { get; set; }
    /// <summary>
    /// 投票目标问卷提交外键
    /// </summary>
    public string? ReviewSubmissionDataId { get; set; }
    /// <summary>
    /// 投票用户导航属性
    /// </summary>
    public required User User { get; set; }
    /// <summary>
    /// 投票用户外键
    /// </summary>
    public string? UserId { get; set; }
    /// <summary>
    /// 投票类型，Upvote 或 Downvote
    /// </summary>
    public VoteType VoteType { get; set; }
    /// <summary>
    /// 投票最后更新时间
    /// </summary>
    public DateTime VoteTime { get; set; } = DateTime.UtcNow;
    public ReviewVote() { }
    public ReviewVote(ReviewSubmissionData reviewSubmissionData, User user, VoteType voteType)
    {
        ReviewSubmissionData = reviewSubmissionData;
        User = user;
        VoteType = voteType;
    }
}
