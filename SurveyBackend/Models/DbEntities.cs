using NanoidDotNet;

namespace SurveyBackend.Models
{
    /// <summary>
    /// 用户实体类
    /// </summary>
    public class User
    {
        public string UserId { get; set; } = Nanoid.Generate(size: 16);

        public string QQId { get; set; }

        public User(string qqId)
        {
            QQId = qqId;
        }
    }
    /// <summary>
    /// 问卷实体类
    /// </summary>
    public class Questionnaire
    {
        /// <summary>
        /// 问卷唯一标识符
        /// </summary>
        public string QuestionnaireId { get; set; } = Nanoid.Generate(size: 8);
        public string FriendlyName { get; set; }
        /// <summary>
        /// 控制是否每个用户只能提交一次
        /// </summary>
        public bool UniquePerUser { get; set; }
        public bool NeedReview { get; set; }
        public DateTime ReleaseDate { get; set; } = DateTime.UtcNow;
        /// <summary>
        /// 问卷的题面，遵循 Survey.js 相关规范
        /// </summary>
        public string SurveyJson { get; set; }
        public Questionnaire(string friendlyName, string surveyJson, bool uniquePerUser = true, bool needReview = false)
        {
            FriendlyName = friendlyName;
            SurveyJson = surveyJson;
            UniquePerUser = uniquePerUser;
            NeedReview = needReview;
        }
    }
    /// <summary>
    /// 用户提交表
    /// </summary>
    public class Submission
    {
        /// <summary>
        /// 提交唯一标识符
        /// </summary>
        public string SubmissionId { get; set; } = Nanoid.Generate(size: 16);
        /// <summary>
        /// 8位简短提交 ID，便于展示
        /// </summary>
        public string ShortSubmissionId => SubmissionId[..8];
        /// <summary>
        /// 提交所属的问卷 ID
        /// </summary>
        public Questionnaire Questionnaire { get; set; }
        /// <summary>
        /// 提交所属的用户
        /// </summary>
        public User User { get; set; }
        /// <summary>
        /// 提交时间
        /// </summary>
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public bool IsDisabled { get; set; } = false;

        /// <summary>
        /// 用户的提交回答，遵循 Survey.js 相关规范
        /// </summary>
        public string SurveyData { get; set; }



        public Submission(Questionnaire questionnaire, string surveyData, User user)
        {
            Questionnaire = questionnaire;
            SurveyData = surveyData;
            User = user;
        }

        public override string ToString() => $"SubmissionId: {SubmissionId}, QuestionnaireId: {Questionnaire.QuestionnaireId}, UserId: {User.UserId}, CreatedAt: {CreatedAt}, IsDisabled: {IsDisabled}";
    }
    public enum ReviewStatus
    {
        Pending = 0,
        Approved = 1,
        Rejected = 2
    }
    public class ReviewSubmissionData
    {
        public string ReviewSubmissionDataId { get; set; } = Nanoid.Generate(size: 16);
        public Submission Submission { get; set; }

        public string AIInsights { get; set; } = "不可用";

        public ReviewStatus Status { get; set; } = ReviewStatus.Pending;

        public ReviewSubmissionData(Submission submission)
        {
            Submission = submission;
        }

    }

    public enum VoteType
    {
        Upvote = 1,
        Downvote = -1
    }
    public class ReviewVote
    {
        public int Id { get; set; }
        public ReviewSubmissionData ReviewSubmissionData { get; set; }
        public User User { get; set; }
        public VoteType VoteType { get; set; }
        public DateTime VoteTime { get; set; } = DateTime.UtcNow;

        public ReviewVote(ReviewSubmissionData reviewSubmissionData, User user, VoteType voteType)
        {
            ReviewSubmissionData = reviewSubmissionData;
            User = user;
            VoteType = voteType;
        }
    }

}
