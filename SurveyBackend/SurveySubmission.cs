namespace SurveyBackend
{
    public class SurveySubmission
    {
        public string userId { get; set; } = string.Empty;
        public Dictionary<string, object>? Answers { get; set; }
    }
}
