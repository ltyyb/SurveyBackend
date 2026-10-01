using NanoidDotNet;

namespace SurveyBackend.Models;
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
    /// <summary>
    /// 提交给AI的页面名称，为null或空集合将不提交给AI
    /// </summary>
    public string[]? LLMPageNames { get; set; }
    public DateTime ReleaseDate { get; set; } = DateTime.UtcNow;
    public required string SurveyJson { get; set; }

    public Questionnaire() { }
    public Questionnaire(Survey survey, string surveyJson)
    {
        Survey = survey;
        SurveyJson = surveyJson;
    }
}
