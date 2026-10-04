namespace SurveyBackend.Models;

public class ForceEditGrant
{
    public string RequestId { get; set; } = string.Empty;
    public required Request Request { get; set; }
    public string TargetUserId { get; set; } = string.Empty;
    public required User TargetUser { get; set; }
    public string QuestionnaireId { get; set; } = string.Empty;
    public required Questionnaire Questionnaire { get; set; }
    public string? SubmissionId { get; set; }
    public Submission? Submission { get; set; }
}
