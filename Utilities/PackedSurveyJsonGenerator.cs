using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public class PackedSurveyJsonGenerator
{
    /// <summary>
    /// ���� packedSurveyJson ��ʽ�� JSON �ַ���
    /// </summary>
    /// <param name="version">�汾��</param>
    /// <param name="surveyJson">ԭʼ�ʾ� JSON �ַ���</param>
    /// <returns>packedSurveyJson ��ʽ�� JSON �ַ���</returns>
    public static string GeneratePackedSurveyJson(string version, string surveyJson)
    {
        // ȷ�� surveyJson ����Ч�� JSON
        var surveyJObject = JObject.Parse(surveyJson);

        var packed = new JObject
        {
            ["version"] = version,
            ["surveyJson"] = surveyJObject.ToString(Formatting.None)
        };

        return packed.ToString(Formatting.Indented);
    }
}
