using System.Text.Json;
using SurveyBackend.Controllers;

namespace SurveyBackend
{
    public class SurveyPkgInstance
    {
        private string filePath = string.Empty;
        public string LatestVersion { get; private set; } = string.Empty;
        private string pkgName = string.Empty;
        public Dictionary<string, Survey> SurveyVerPairs { get; private set; } = new();

        // 单例实例
        private static readonly Lazy<SurveyPkgInstance> _instance = new(() => new SurveyPkgInstance());

        // 公共静态属性用于访问单例
        public static SurveyPkgInstance Instance => _instance.Value;

        // 私有构造函数，防止外部实例化
        private SurveyPkgInstance() { }

        /// <summary>
        /// 从指定的 JSON 文件加载问卷包。返回问卷包单例。
        /// </summary>
        /// <param name="packedJsonPath"></param>
        /// <param name="logger"></param>
        /// <returns></returns>
        public static SurveyPkgInstance? LoadFromFile(string packedJsonPath, ILogger<SurveyController>? logger = null)
        {
            try
            {
                if (File.Exists(packedJsonPath) == false)
                {
                    logger?.LogWarning("{packedJsonPath} does not exist, returning null.", packedJsonPath);
                    return null;
                }
                var packedJsonStr = File.ReadAllText(packedJsonPath);

                var surveyPkg = JsonSerializer.Deserialize<SurveyPackage>(packedJsonStr);
                if (surveyPkg is null
                    || string.IsNullOrWhiteSpace(surveyPkg.Name)
                    || string.IsNullOrWhiteSpace(surveyPkg.LatestVer)
                    || surveyPkg.Surveys.Count < 1)
                {
                    logger?.LogWarning("Failed to deserialize JSON from {packedJsonPath}. Please check the format.", packedJsonPath);
                    return null;
                }

                logger?.LogInformation("Try to loading survey package {Name} ..", surveyPkg.Name);
                Dictionary<string, Survey> surveyVerPairs = new();
                foreach (var kvp in surveyPkg.Surveys)
                {
                    logger?.LogInformation($"""
                                    - {kvp.Key}
                                      | Description: {kvp.Value.Description}
                                      | Release Date: {ParseReleaseDate(kvp.Value.ReleaseDate).ToString("yyyy-MM-dd")}
                                    """);
                    Survey survey = new(kvp.Key, kvp.Value.Description,
                        kvp.Value.SurveyJson, ParseReleaseDate(kvp.Value.ReleaseDate));
                    surveyVerPairs.Add(kvp.Key, survey);
                    logger?.LogInformation("Added into surveyVerPairs: {Version}", kvp.Key);
                }

                // 更新单例实例的属性
                var instance = Instance;
                instance.filePath = packedJsonPath;
                instance.LatestVersion = surveyPkg.LatestVer;
                instance.pkgName = surveyPkg.Name;
                instance.SurveyVerPairs = surveyVerPairs;

                logger?.LogInformation("Load survey {name} OK.", instance.pkgName);
                return instance;

            }
            catch (Exception ex)
            {
                logger?.LogError(ex, $"Failed to load survey from file: {packedJsonPath}");
                return null;
            }
        }

        /// <summary>
        /// 重新加载问卷包。适用于问卷包实例在原有位置发生修改替换的情形。
        /// </summary>
        /// <exception cref="InvalidOperationException"></exception>
        public void Reload()
        {
            if (string.IsNullOrEmpty(filePath))
            {
                throw new InvalidOperationException("Cannot reload survey: file path is not set.");
            }
            var loadedSurvey = LoadFromFile(filePath)
                               ?? throw new InvalidOperationException($"Failed to reload survey from {filePath}");
            // 更新当前实例的属性
            this.filePath = loadedSurvey.filePath;
            this.LatestVersion = loadedSurvey.LatestVersion;
            this.pkgName = loadedSurvey.pkgName;
            this.SurveyVerPairs = loadedSurvey.SurveyVerPairs;
        }
        /// <summary>
        /// 获取最新版本的 Survey，找不到最新版本的 Survey 时将抛出 <see cref="KeyNotFoundException"/>.
        /// </summary>
        /// <returns></returns>
        /// <exception cref="KeyNotFoundException"></exception>
        public Survey GetSurvey()
        {
            return GetSurvey(LatestVersion);
        }
        /// <summary>
        /// 尝试获取最新版本的 Survey，如果找不到则返回 false，并将 <paramref name="survey"/> 设置为 null。
        /// </summary>
        /// <param name="survey"></param>
        /// <returns></returns>
        public bool TryGetSurvey(out Survey? survey)
        {
            return TryGetSurvey(LatestVersion, out survey);
        }
        /// <summary>
        /// 尝试获取指定版本的 Survey，如果找不到则返回 false，并将 <paramref name="survey"/> 设置为 null。
        /// </summary>
        /// <param name="version"></param>
        /// <param name="survey"></param>
        /// <returns></returns>
        public bool TryGetSurvey(string version, out Survey? survey)
        {
            return SurveyVerPairs.TryGetValue(version, out survey);
        }
        /// <summary>
        /// 获取指定版本的 Survey，找不到指定版本的 Survey 时将抛出 <see cref="KeyNotFoundException"/>.
        /// </summary>
        /// <param name="version"></param>
        /// <returns></returns>
        /// <exception cref="KeyNotFoundException"></exception>
        public Survey GetSurvey(string version)
        {
            if (SurveyVerPairs.TryGetValue(version, out var survey))
            {
                return survey;
            }
            else
            {
                throw new KeyNotFoundException($"Survey version '{version}' not found.");
            }
        }

        public bool IsVersionValid(string version)
        {
            if (string.IsNullOrWhiteSpace(version))
            {
                return false;
            }
            return SurveyVerPairs.ContainsKey(version);
        }
        private static DateTime ParseReleaseDate(string releaseDate)
        {
            if (long.TryParse(releaseDate, out long unixTime))
            {
                var time = DateTimeOffset.FromUnixTimeSeconds(unixTime).UtcDateTime;
                return time;
            }
            else
            {
                throw new FormatException("Invalid release date format.");
            }
        }
    }
}
