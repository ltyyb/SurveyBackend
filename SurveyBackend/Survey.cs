using Newtonsoft.Json;
using SurveyBackend.Controllers;

namespace SurveyBackend
{
    public class Survey
    {
        public string Version { get; private set; } = "V0.0.0";
        /// <summary>
        /// 原始 Survey Json，不应直接被获取，应使用 <seealso cref="GetSpecificSurveyJson(string)"/> 方法获得客制化问卷 Json.
        /// </summary>
        public string SurveyJson { get; private set; } = string.Empty;
        private string filePath = string.Empty;

        // 单例实例
        private static readonly Lazy<Survey> _instance = new(() => new Survey());

        // 公共静态属性用于访问单例
        public static Survey Instance => _instance.Value;

        // 私有构造函数，防止外部实例化
        private Survey() { }

        public static Survey? LoadFromFile(string packedJsonPath, ILogger<SurveyController>? logger = null)
        {
            try
            {
                if (File.Exists(packedJsonPath) == false)
                {
                    logger?.LogWarning($"{packedJsonPath} does not exist, returning null.");
                    return null;
                }
                var packedJsonStr = System.IO.File.ReadAllText(packedJsonPath);
                var packedJson = Newtonsoft.Json.Linq.JObject.Parse(packedJsonStr);
                if (packedJson["version"] is null || packedJson["surveyJson"] is null)
                {
                    logger?.LogWarning($"Invalid packed JSON format in {packedJsonPath}. 'version' or 'surveyJson' is missing.");
                    return null;
                }
                var version = packedJson["version"]!.ToString();
                var surveyJson = packedJson["surveyJson"]!.ToString();

                // 更新单例实例的属性
                var instance = Instance;
                instance.Version = version;
                instance.SurveyJson = surveyJson;
                instance.filePath = packedJsonPath;


                logger?.LogInformation($"Successfully loaded survey from file: {packedJsonPath}");

                return instance;
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, $"Failed to load survey from file: {packedJsonPath}");
                return null;
            }
        }

        public void Reload()
        {
            if (string.IsNullOrEmpty(filePath))
            {
                throw new InvalidOperationException("Cannot reload survey: file path is not set.");
            }
            var loadedSurvey = LoadFromFile(filePath);
            if (loadedSurvey == null)
            {
                throw new InvalidOperationException($"Failed to reload survey from {filePath}");
            }
            // 更新当前实例的属性
            Version = loadedSurvey.Version;
            SurveyJson = loadedSurvey.SurveyJson;
        }
        /// <summary>
        /// 替换 QQ号 占位符获得指定用户的客制化问卷 Json。
        /// </summary>
        /// <param name="qqId"></param>
        /// <returns></returns>
        public string GetSpecificSurveyJson(string qqId)
        {
            // 替换 SurveyJson 中的 {Specific_QQId} 占位符
            string originalJson = SurveyJson;
            string specificJson = originalJson.Replace("{Specific_QQId}", qqId);
            return specificJson;
        }
    }
}
