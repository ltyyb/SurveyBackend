using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public class PackedSurveyJsonGenerator
{
    /// <summary>
    /// 生成 packedSurveyJson 格式的 JSON 字符串
    /// </summary>
    /// <param name="version">版本号</param>
    /// <param name="surveyJson">原始问卷 JSON 字符串</param>
    /// <returns>packedSurveyJson 格式的 JSON 字符串</returns>
    public static string GeneratePackedSurveyJson(string version, string surveyJson)
    {
        // 确保 surveyJson 是有效的 JSON
        var surveyJObject = JObject.Parse(surveyJson);

        var packed = new JObject
        {
            ["version"] = version,
            ["surveyJson"] = surveyJObject.ToString(Formatting.None)
        };

        return packed.ToString(Formatting.Indented);
    }
}
