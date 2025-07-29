using System.Text.Json.Serialization;

namespace Utilities
{

    public class SurveyInfo
    {
        [JsonPropertyName("description")]
        public string Description { get; set; } = string.Empty;

        [JsonPropertyName("releaseDate")]
        public string ReleaseDate { get; set; } = string.Empty;

        [JsonPropertyName("json")]
        public string SurveyJson { get; set; } = string.Empty;
    }

    public class SurveyPackage
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("latestVer")]
        public string LatestVer { get; set; } = string.Empty;


        [JsonPropertyName("surveys")]
        public Dictionary<string, SurveyInfo> Surveys { get; set; } = new();
    }
}
