using System.ComponentModel.DataAnnotations.Schema;
using NanoidDotNet;

namespace SurveyBackend.Models;
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
