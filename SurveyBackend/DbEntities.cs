using NanoidDotNet;

namespace SurveyBackend
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
        public string QuestionnaireId { get; set; } = Nanoid.Generate(size: 8);
        public string FriendlyName { get; set; }
        /// <summary>
        /// 问卷的题面，遵循 Survey.js 相关规范
        /// </summary>
        public string SurveyJson { get; set; }
        public Questionnaire(string friendlyName, string surveyJson)
        {
            FriendlyName = friendlyName;
            SurveyJson = surveyJson;
        }
    }
    /// <summary>
    /// 用户提交表
    /// </summary>
    public class Submission
    {
        public string SubmissionId { get; set; } = Nanoid.Generate(size: 16);
        public string QuestionnaireId { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// 用户的提交回答，遵循 Survey.js 相关规范
        /// </summary>
        public string SurveyData { get; set; }

        public Submission(string questionnaireId, string surveyData)
        {
            QuestionnaireId = questionnaireId;
            SurveyData = surveyData;
        }
    }



}
