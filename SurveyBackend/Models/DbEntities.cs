using NanoidDotNet;

namespace SurveyBackend.Models
{
    using System.ComponentModel.DataAnnotations.Schema;
    /// <summary>
    /// 用户身份组
    /// </summary>
    public enum UserGroup
    {
        NewComer = 0,
        PendingUser = 1,
        VerifiedUser = 2,
        Admin = 99,
        SuperAdmin = 100
    }
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
    /// <summary>
    /// 问卷实体类
    /// </summary>
    public class Questionnaire
    {
        /// <summary>
        /// 问卷唯一标识符
        /// </summary>
        public string QuestionnaireId { get; set; } = Nanoid.Generate(size: 8);
        public required Survey Survey { get; set; }
        public string? SurveyId { get; set; }
        public DateTime ReleaseDate { get; set; } = DateTime.UtcNow;
        public required string SurveyJson { get; set; }

        public Questionnaire() { }
        public Questionnaire(Survey survey, string surveyJson)
        {
            Survey = survey;
            SurveyJson = surveyJson;
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
        [NotMapped]
        public string ShortSubmissionId => SubmissionId[..8];
        /// <summary>
        /// 提交所属的问卷
        /// </summary>
        public required Questionnaire Questionnaire { get; set; }
        public string? QuestionnaireId { get; set; }
        /// <summary>
        /// 提交所属的用户
        /// </summary>
        public required User User { get; set; }
        public string? UserId { get; set; }
        /// <summary>
        /// 提交时间
        /// </summary>
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public bool IsDisabled { get; set; } = false;

        /// <summary>
        /// 用户的提交回答，遵循 Survey.js 相关规范
        /// </summary>
        public required string SurveyData { get; set; }

        public Submission() { }

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
        public required Submission Submission { get; set; }
        public string? SubmissionId { get; set; }

        public string AIInsights { get; set; } = "不可用";

        public ReviewStatus Status { get; set; } = ReviewStatus.Pending;
        public ReviewSubmissionData() { }
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
    
    public enum RequestType
    {
        SurveyAccess = 0,
        QuestionnaireCreate = 1,
    }
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

}
