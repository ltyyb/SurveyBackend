using NanoidDotNet;

namespace SurveyBackend.Models;

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
