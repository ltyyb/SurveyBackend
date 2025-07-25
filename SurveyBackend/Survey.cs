namespace SurveyBackend
{
    public class Survey
    {
        public string Version { get; private set; } = string.Empty;
        public string VersionDescription { get; private set; } = string.Empty;
        public DateTime ReleaseDate { get; private set; }
        /// <summary>
        /// 原始 Survey Json，不应直接被获取，应使用 <seealso cref="GetSpecificSurveyJsonByQQId(string)"/> 方法获得客制化问卷 Json.
        /// </summary>
        public string SurveyJson { get; private set; } = string.Empty;

        public Survey(string version, string versionDescription, string surveyJson, DateTime releaseDate)
        {
            Version = version;
            VersionDescription = versionDescription;
            SurveyJson = surveyJson;
            this.ReleaseDate = releaseDate;
        }

        /// <summary>
        /// 替换 QQ号 占位符获得指定用户的客制化问卷 Json。
        /// </summary>
        /// <param name="qqId"></param>
        /// <returns></returns>
        public string GetSpecificSurveyJsonByQQId(string qqId)
        {
            // 替换 SurveyJson 中的 {Specific_QQId} 占位符
            string originalJson = SurveyJson;
            string specificJson = originalJson.Replace("{Specific_QQId}", qqId);
            specificJson = specificJson.Replace("{Survey_Version}", Version);
            specificJson = specificJson.Replace("{Survey_Version_Description}", VersionDescription);
            specificJson = specificJson.Replace("{Survey_Release_Date}", ReleaseDate.ToString("yyyy-MM-dd"));
            return specificJson;
        }
        /// <summary>
        /// 使用 UserId 查询 QQ号, 替换占位符获得指定用户的客制化问卷 Json。
        /// </summary>
        /// <seealso cref="GetSpecificSurveyJsonByQQId(string)"/>
        /// <param name="userId"></param>
        /// <param name="logger"></param>
        /// <param name="connStr"></param>
        /// <returns></returns>
        public async Task<string?> GetSpecificSurveyJsonByUserId(string userId, ILogger logger, string connStr)
        {
            var surveyUser = await SurveyUser.GetUserByIdAsync(userId, logger, connStr);
            if (surveyUser is null || string.IsNullOrWhiteSpace(surveyUser.QQId))
            {
                logger.LogError("Failed to retrieve SurveyUser for UserId: {UserId}", userId);
                return null;
            }
            string specificJson = GetSpecificSurveyJsonByQQId(surveyUser.QQId);
            return specificJson;
        }
    }
}
