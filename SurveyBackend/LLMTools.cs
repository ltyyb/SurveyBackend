using Microsoft.Extensions.AI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OpenAI;
using System.Text;

namespace SurveyBackend
{
    public class LLMTools
    {
        //public const string SysPrompt = """
        //        你是一个AI问卷审阅者。你正在审阅厦门六中同安校区音游部的新生入群问卷。
        //        本音游部是一个包容性较强的社群，不必过多考虑问卷填写者的音游相关实力。
        //        但我们希望创造一个和谐的讨论氛围并尽量隔离成绩造假行为的出现。所以我们使用了这一问卷审核制度。

        //        你现在需要根据以下要点，给出问卷评分及相关见解：
        //        1. 评分范围为0-100分，如无明显问题的问卷不应评定低于75分。
        //        2. 提供的问卷可能为自然语言形式，我将提供给你 Part 2 - Part 3 的问题以及用户的填写。Part 2 与 3 的作答可能为混合提供。
        //        3. Part 2 的填写内容均为音游素养相关的问题，请注意该部分的审核，不必过多考虑问卷填写者的音游相关实力，
        //            但如果发现其填写内容中有明显的前后题目选择不一致或可能存在作假或虚填行为，请酌情扣分并在见解中指出。
        //            例如，某个用户填写了擅长/喜爱的音乐游戏玩法种类，但在勾选其曾经/现在接触过的音乐游戏却没有该种玩法，且该情况多次出现，应考虑扣分。
        //            请注意，用户不一定填写自己的音游水平量化值，若某个音游PTT/RKS/Level 为 0 则表示用户不愿意透露此数据。 请勿以此为评分依据。
        //            此部分评分重点在于用户的作答是否合理、前后是否一致、是否有明显的作假嫌疑，不建议以参与度低作为扣分理由。
        //            请注意，除了"请在下方填写您其它音游的潜力值或其他能代表您该游戏水平的指标"题目(如果有)以外，该部分表述均为预设的CheckBox题，即回答表述均为预设，请勿以规范表达为由扣分。
        //        4. Part 3 的填写内容为成员素质保证测试，如在其中可能有违反社群规定和NSFW内容倾向的选择，请酌情扣分并在见解中指出。
        //        5. 你需要在回答的开头直接点明你的分数，并在见解中简单总结该用户的填写情况，在这之后指出扣分的原因。
        //        6. 你的回答不应该为 Markdown 格式，善用换行符。

        //        稍后 user 将提供问卷内容，请你根据上述要求进行评分和见解分析。
        //        """;
        private readonly string? sysPrompt;
        private List<ChatMessage>? _chatHistory = [];
        private readonly IChatClient? chatClient;
        private readonly IConfiguration _configuration;
        private readonly ILogger<LLMTools> _logger;
        public bool IsAvailable { get; private set; } = false;
        public LLMTools(IConfiguration configuration, ILogger<LLMTools> logger)
        {
            _configuration = configuration;
            _logger = logger;
            string? model = _configuration["LLM:ModelName"];
            string? key = _configuration["LLM:OpenAIKey"];
            string? endpoint = _configuration["LLM:OpenAIEndpoint"];
            string? sysPromptPath = _configuration["LLM:SysPromptPath"];

            if (string.IsNullOrWhiteSpace(model))
            {
                _logger.LogError("OpenAI 模型名称未配置。AI 见解将不可用。");
            }
            else if (string.IsNullOrWhiteSpace(key))
            {
                _logger.LogError("OpenAI Key 未配置。AI 见解将不可用。");
            }
            else if (string.IsNullOrWhiteSpace(endpoint))
            {
                _logger.LogError("OpenAI Endpoint 未配置。AI 见解将不可用。");
            }
            else if (string.IsNullOrWhiteSpace(sysPromptPath) || !File.Exists(sysPromptPath))
            {
                _logger.LogError("系统提示词文件路径未配置或文件不存在。AI 见解将不可用。");
            }
            else
            {
                try
                {
                    // Create the IChatClient
                    chatClient =
                        new OpenAIClient(new System.ClientModel.ApiKeyCredential(key),
                                         new OpenAIClientOptions { Endpoint = new Uri(endpoint) }).GetChatClient(model).AsIChatClient();
                    sysPrompt = File.ReadAllText(sysPromptPath);
                    IsAvailable = true;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "初始化 OpenAI Client 出现异常。AI 见解将不可用。");
                }

            }

        }

        public async Task<string?> GetInsight(string surveyContentPrompt)
        {
            if (!IsAvailable) return null;
            _chatHistory =
            [
                new ChatMessage(ChatRole.System, sysPrompt)
            ];
            string response = string.Empty;
            _chatHistory.Add(new ChatMessage(ChatRole.User, surveyContentPrompt));
            await foreach (ChatResponseUpdate item in
                chatClient!.GetStreamingResponseAsync(_chatHistory))
            {
                response += item.Text;
            }
            _chatHistory.Add(new ChatMessage(ChatRole.Assistant, response));
            return response;
        }

                /// <summary>
        /// 将问卷原始响应数据解析为自然语言格式的字符串。
        /// </summary>
        /// <param name="surveyRawJson">问卷结构的原始JSON字符串。</param>
        /// <param name="responseRawJson">用户填写的问卷响应原始JSON字符串。</param>
        /// <param name="pageNames">需要解析的问卷页面名称数组。</param>
        /// <returns>解析后的自然语言格式字符串，如果解析失败则返回null。</returns>
        public string? ParseSurveyResponseToNL(string surveyRawJson, string responseRawJson, string[] pageNames)
        {
            if (string.IsNullOrWhiteSpace(surveyRawJson) || string.IsNullOrWhiteSpace(responseRawJson))
            {
                return null;
            }

            JObject surveyJson;
            JObject answerJson;
            try
            {
                // 解析问卷结构JSON
                surveyJson = JObject.Parse(surveyRawJson);
                // 解析用户响应JSON
                answerJson = JObject.Parse(responseRawJson);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "问卷结构或用户响应 JSON 解析失败。");
                return null;
            }

            var elements = CollectElements(surveyJson, pageNames);
            if (elements is null || elements.Count == 0)
            {
                return null;
            }

            // 构建元素字典：筛选指定页面中的问题元素，并以问题名称为键存储
            var elementDict = elements
                .Where(e => e["name"] != null)
                .GroupBy(e => e["name"]!.ToString(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            var sb = new StringBuilder();

            // 遍历用户响应中的每个属性（即每个问题的回答）
            foreach (var prop in answerJson.Properties())
            {
                string name = prop.Name;
                var value = prop.Value;

                // 如果当前问题不在指定页面中，则跳过
                if (!elementDict.TryGetValue(name, out JObject? element))
                {
                    continue;
                }

                // 获取问题标题，优先使用中文标题，否则使用问题名称
                string title = GetElementTitle(element, name, DefaultLocale);
                string resultText = FormatAnswerForElement(element, value, surveyJson, answerJson, name, DefaultLocale);

                // 将问题标题和回答拼接到最终结果中
                sb.AppendLine($"{title}: {resultText}");
            }

            return sb.ToString();
        }
        #region 元素格式化处理

        private const string DefaultLocale = "zh-cn";
        private const string DefaultOtherLabel = "填写其他答案";

        private static List<JObject> CollectElements(JObject surveyJson, string[] pageNames)
        {
            var elements = new List<JObject>();
            if (surveyJson["pages"] is JArray pages)
            {
                HashSet<string> pageSet = pageNames != null && pageNames.Length > 0
                    ? new HashSet<string>(pageNames, StringComparer.OrdinalIgnoreCase)
                    : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var p in pages)
                {
                    var pname = p?["name"]?.ToString();
                    if (pageSet.Count > 0 && (string.IsNullOrWhiteSpace(pname) || !pageSet.Contains(pname)))
                    {
                        continue;
                    }

                    if (p?["elements"] is JArray elems)
                    {
                        foreach (var e in elems)
                        {
                            elements.AddRange(FlattenElements(e));
                        }
                    }
                }
            }
            else if (surveyJson["elements"] is JArray rootElements)
            {
                foreach (var e in rootElements)
                {
                    elements.AddRange(FlattenElements(e));
                }
            }

            return elements;
        }

        private static IEnumerable<JObject> FlattenElements(JToken? token)
        {
            if (token is not JObject obj) yield break;

            if (obj["elements"] is JArray nestedElements)
            {
                foreach (var n in nestedElements)
                {
                    foreach (var child in FlattenElements(n))
                    {
                        yield return child;
                    }
                }
            }

            var type = obj["type"]?.ToString();
            if (string.Equals(type, "panel", StringComparison.OrdinalIgnoreCase))
            {
                yield break;
            }

            if (string.Equals(type, "paneldynamic", StringComparison.OrdinalIgnoreCase))
            {
                yield return obj;
                yield break;
            }

            yield return obj;
        }

        private static string GetElementTitle(JObject element, string fallbackName, string locale)
        {
            var title = GetLocalizedText(element["title"], locale);
            return string.IsNullOrWhiteSpace(title) ? fallbackName : title;
        }

        private static string? GetLocalizedText(JToken? token, string locale)
        {
            if (token == null || token.Type == JTokenType.Null) return null;
            if (token.Type == JTokenType.String) return token.Value<string>();
            if (token.Type == JTokenType.Object)
            {
                var t = token[locale]?.Value<string>();
                if (!string.IsNullOrWhiteSpace(t)) return t;
                var def = token["default"]?.Value<string>();
                if (!string.IsNullOrWhiteSpace(def)) return def;
                var first = token.Children<JProperty>().FirstOrDefault();
                return first?.Value?.ToString();
            }
            return token.ToString(Formatting.None);
        }

        private static string? GetTokenString(JToken? token)
        {
            if (token == null || token.Type == JTokenType.Null) return null;
            if (token.Type == JTokenType.String) return token.Value<string>();
            return token.ToString(Formatting.None);
        }

        private static string FormatAnswerForElement(JObject element, JToken value, JObject surveyJson, JObject answersScope, string name, string locale)
        {
            if (value == null || value.Type == JTokenType.Null) return string.Empty;
            string type = element["type"]?.ToString() ?? string.Empty;

            switch (type)
            {
                case "radiogroup":
                case "checkbox":
                case "dropdown":
                case "tagbox":
                case "imagepicker":
                case "ranking":
                case "rating":
                    return FormatSelectionAnswer(element, value, surveyJson, answersScope, name, locale);

                case "boolean":
                    return FormatBooleanAnswer(element, value, locale);

                case "multipletext":
                    return FormatMultipleTextAnswer(element, value, locale);

                case "matrix":
                    return FormatMatrixAnswer(element, value, locale);

                case "matrixdropdown":
                    return FormatMatrixDropdownAnswer(element, value, locale);

                case "matrixdynamic":
                    return FormatMatrixDynamicAnswer(element, value, locale);

                case "paneldynamic":
                    return FormatPanelDynamicAnswer(element, value, surveyJson, locale);

                case "file":
                    return FormatFileAnswer(value);

                case "signaturepad":
                    return FormatSignatureAnswer(value);

                case "text":
                case "comment":
                default:
                    return GetTokenString(value) ?? string.Empty;
            }
        }

        private static string FormatSelectionAnswer(JObject element, JToken value, JObject surveyJson, JObject answersScope, string name, string locale)
        {
            var choiceInfo = BuildChoiceInfo(element, locale);
            var otherComment = GetOtherComment(surveyJson, element, answersScope, name);
            bool hasOther = choiceInfo.HasOther || HasOtherFlag(element);

            if (value.Type == JTokenType.Array)
            {
                var texts = new List<string>();
                bool usedOther = false;
                foreach (var v in value)
                {
                    var vStr = GetTokenString(v);
                    if (string.IsNullOrWhiteSpace(vStr)) continue;
                    var formatted = FormatChoiceValue(vStr, choiceInfo, hasOther, otherComment, ref usedOther);
                    if (!string.IsNullOrWhiteSpace(formatted)) texts.Add(formatted);
                }

                if (hasOther && !usedOther && !string.IsNullOrWhiteSpace(otherComment))
                {
                    texts.Add($"{choiceInfo.OtherLabel ?? DefaultOtherLabel}: {otherComment}");
                }

                return string.Join("，", texts);
            }

            var single = GetTokenString(value);
            if (!string.IsNullOrWhiteSpace(single))
            {
                bool usedOther = false;
                var formatted = FormatChoiceValue(single, choiceInfo, hasOther, otherComment, ref usedOther);
                if (string.IsNullOrWhiteSpace(formatted) && hasOther && !string.IsNullOrWhiteSpace(otherComment))
                {
                    formatted = $"{choiceInfo.OtherLabel ?? DefaultOtherLabel}: {otherComment}";
                }
                return formatted ?? string.Empty;
            }

            return GetTokenString(value) ?? string.Empty;
        }

        private static string FormatBooleanAnswer(JObject element, JToken value, string locale)
        {
            var labelTrue = GetLocalizedText(element["labelTrue"], locale) ?? "是";
            var labelFalse = GetLocalizedText(element["labelFalse"], locale) ?? "否";
            if (value.Type == JTokenType.Boolean)
            {
                return value.Value<bool>() ? labelTrue : labelFalse;
            }
            if (bool.TryParse(value.ToString(), out bool boolVal))
            {
                return boolVal ? labelTrue : labelFalse;
            }
            return GetTokenString(value) ?? string.Empty;
        }

        private static string FormatMultipleTextAnswer(JObject element, JToken value, string locale)
        {
            if (value is not JObject obj)
            {
                return GetTokenString(value) ?? string.Empty;
            }

            var items = BuildValueLabelMap(element["items"] as JArray, locale, ["name", "value", "id"], ["title", "text", "label"]);
            var texts = new List<string>();
            foreach (var prop in obj.Properties())
            {
                var label = items.TryGetValue(prop.Name, out var l) ? l : prop.Name;
                var val = GetTokenString(prop.Value) ?? string.Empty;
                texts.Add($"{label}: {val}");
            }
            return string.Join("；", texts);
        }

        private static string FormatMatrixAnswer(JObject element, JToken value, string locale)
        {
            if (value is not JObject obj)
            {
                return GetTokenString(value) ?? string.Empty;
            }

            var rowMap = BuildValueLabelMap(element["rows"] as JArray, locale, ["value", "name", "id"], ["text", "title", "label"]);
            var colMap = BuildValueLabelMap(element["columns"] as JArray, locale, ["value", "name", "id"], ["text", "title", "label"]);

            var parts = new List<string>();
            foreach (var row in obj.Properties())
            {
                var rowLabel = rowMap.TryGetValue(row.Name, out var rl) ? rl : row.Name;
                if (row.Value is JArray arr)
                {
                    var colLabels = new List<string>();
                    foreach (var item in arr)
                    {
                        var colValue = GetTokenString(item) ?? string.Empty;
                        var colLabel = colMap.TryGetValue(colValue, out var cl) ? cl : colValue;
                        if (!string.IsNullOrWhiteSpace(colLabel)) colLabels.Add(colLabel);
                    }
                    parts.Add($"{rowLabel}: {string.Join("，", colLabels)}");
                }
                else
                {
                    var colValue = GetTokenString(row.Value) ?? string.Empty;
                    var colLabel = colMap.TryGetValue(colValue, out var cl) ? cl : colValue;
                    parts.Add($"{rowLabel}: {colLabel}");
                }
            }
            return string.Join("；", parts);
        }

        private static string FormatMatrixDropdownAnswer(JObject element, JToken value, string locale)
        {
            if (value is not JObject obj)
            {
                return GetTokenString(value) ?? string.Empty;
            }

            var rowMap = BuildValueLabelMap(element["rows"] as JArray, locale, ["value", "name", "id"], ["text", "title", "label"]);
            var columnMetas = BuildMatrixColumnMetas(element["columns"] as JArray, locale);

            var parts = new List<string>();
            foreach (var row in obj.Properties())
            {
                var rowLabel = rowMap.TryGetValue(row.Name, out var rl) ? rl : row.Name;
                if (row.Value is not JObject rowObj)
                {
                    parts.Add($"{rowLabel}: {GetTokenString(row.Value)}");
                    continue;
                }

                var cellTexts = new List<string>();
                if (columnMetas.Count > 0)
                {
                    foreach (var meta in columnMetas)
                    {
                        if (!rowObj.TryGetValue(meta.Name, StringComparison.OrdinalIgnoreCase, out var cellValue)) continue;
                        var formatted = FormatMatrixCellValue(meta, cellValue, locale);
                        if (!string.IsNullOrWhiteSpace(formatted))
                        {
                            cellTexts.Add($"{meta.Label}: {formatted}");
                        }
                    }
                }
                else
                {
                    foreach (var prop in rowObj.Properties())
                    {
                        var formatted = GetTokenString(prop.Value) ?? string.Empty;
                        cellTexts.Add($"{prop.Name}: {formatted}");
                    }
                }

                parts.Add($"{rowLabel}: {string.Join("，", cellTexts)}");
            }

            return string.Join("；", parts);
        }

        private static string FormatMatrixDynamicAnswer(JObject element, JToken value, string locale)
        {
            if (value is not JArray arr)
            {
                return GetTokenString(value) ?? string.Empty;
            }

            var columnMetas = BuildMatrixColumnMetas(element["columns"] as JArray, locale);
            var parts = new List<string>();
            int index = 1;
            foreach (var rowToken in arr)
            {
                if (rowToken is not JObject rowObj)
                {
                    parts.Add($"第{index}行: {GetTokenString(rowToken)}");
                    index++;
                    continue;
                }

                var cellTexts = new List<string>();
                if (columnMetas.Count > 0)
                {
                    foreach (var meta in columnMetas)
                    {
                        if (!rowObj.TryGetValue(meta.Name, StringComparison.OrdinalIgnoreCase, out var cellValue)) continue;
                        var formatted = FormatMatrixCellValue(meta, cellValue, locale);
                        if (!string.IsNullOrWhiteSpace(formatted))
                        {
                            cellTexts.Add($"{meta.Label}: {formatted}");
                        }
                    }
                }
                else
                {
                    foreach (var prop in rowObj.Properties())
                    {
                        var formatted = GetTokenString(prop.Value) ?? string.Empty;
                        cellTexts.Add($"{prop.Name}: {formatted}");
                    }
                }

                parts.Add($"第{index}行: {string.Join("，", cellTexts)}");
                index++;
            }

            return string.Join("；", parts);
        }

        private static string FormatPanelDynamicAnswer(JObject element, JToken value, JObject surveyJson, string locale)
        {
            if (value is not JArray arr)
            {
                return GetTokenString(value) ?? string.Empty;
            }

            var templateElements = CollectTemplateElements(element);
            var templateMap = templateElements
                .Where(e => e["name"] != null)
                .GroupBy(e => e["name"]!.ToString(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            var parts = new List<string>();
            int index = 1;
            foreach (var rowToken in arr)
            {
                if (rowToken is not JObject rowObj)
                {
                    parts.Add($"第{index}项: {GetTokenString(rowToken)}");
                    index++;
                    continue;
                }

                var subParts = new List<string>();
                foreach (var prop in rowObj.Properties())
                {
                    if (!templateMap.TryGetValue(prop.Name, out var subElement))
                    {
                        subParts.Add($"{prop.Name}: {GetTokenString(prop.Value)}");
                        continue;
                    }

                    var subTitle = GetElementTitle(subElement, prop.Name, locale);
                    var formatted = FormatAnswerForElement(subElement, prop.Value, surveyJson, rowObj, prop.Name, locale);
                    if (!string.IsNullOrWhiteSpace(formatted))
                    {
                        subParts.Add($"{subTitle}: {formatted}");
                    }
                }

                parts.Add($"第{index}项: {string.Join("，", subParts)}");
                index++;
            }

            return string.Join("；", parts);
        }

        private static string FormatFileAnswer(JToken value)
        {
            if (value is not JArray arr)
            {
                return GetTokenString(value) ?? string.Empty;
            }

            var names = new List<string>();
            foreach (var item in arr)
            {
                if (item is JObject obj)
                {
                    var name = GetTokenString(obj["name"]) ?? GetTokenString(obj["fileName"]);
                    if (!string.IsNullOrWhiteSpace(name)) names.Add(name);
                }
            }

            if (names.Count > 0)
            {
                return string.Join("，", names);
            }

            return $"共 {arr.Count} 个文件";
        }

        private static string FormatSignatureAnswer(JToken value)
        {
            var str = GetTokenString(value);
            if (string.IsNullOrWhiteSpace(str)) return string.Empty;
            return "签名已提交";
        }

        private sealed class MatrixColumnMeta
        {
            public string Name { get; init; } = string.Empty;
            public string Label { get; init; } = string.Empty;
            public string CellType { get; init; } = string.Empty;
            public ChoiceInfo ChoiceInfo { get; init; } = new ChoiceInfo();
        }

        private static List<MatrixColumnMeta> BuildMatrixColumnMetas(JArray? columns, string locale)
        {
            var metas = new List<MatrixColumnMeta>();
            if (columns == null) return metas;

            foreach (var c in columns)
            {
                if (c is not JObject obj) continue;
                var name = GetTokenString(obj["name"]) ?? GetTokenString(obj["value"]) ?? GetTokenString(obj["id"]);
                if (string.IsNullOrWhiteSpace(name)) continue;
                var label = GetLocalizedText(obj["title"] ?? obj["text"] ?? obj["label"], locale) ?? name;
                var cellType = GetTokenString(obj["cellType"]) ?? string.Empty;
                var choices = BuildChoiceInfo(obj, locale);
                metas.Add(new MatrixColumnMeta { Name = name, Label = label, CellType = cellType, ChoiceInfo = choices });
            }

            return metas;
        }

        private static string FormatMatrixCellValue(MatrixColumnMeta meta, JToken cellValue, string locale)
        {
            if (cellValue == null || cellValue.Type == JTokenType.Null) return string.Empty;

            switch (meta.CellType)
            {
                case "checkbox":
                case "tagbox":
                    if (cellValue.Type == JTokenType.Array)
                    {
                        var texts = new List<string>();
                        foreach (var v in cellValue)
                        {
                            var vStr = GetTokenString(v);
                            if (string.IsNullOrWhiteSpace(vStr)) continue;
                            if (meta.ChoiceInfo.Map.TryGetValue(vStr, out var label)) texts.Add(label);
                            else texts.Add(vStr);
                        }
                        return string.Join("，", texts);
                    }
                    break;
                case "radiogroup":
                case "dropdown":
                    var single = GetTokenString(cellValue);
                    if (!string.IsNullOrWhiteSpace(single) && meta.ChoiceInfo.Map.TryGetValue(single, out var mapped))
                    {
                        return mapped;
                    }
                    return single ?? string.Empty;
                case "boolean":
                    if (cellValue.Type == JTokenType.Boolean) return cellValue.Value<bool>() ? "是" : "否";
                    break;
            }

            var fallback = GetTokenString(cellValue);
            if (!string.IsNullOrWhiteSpace(fallback) && meta.ChoiceInfo.Map.TryGetValue(fallback, out var label2))
            {
                return label2;
            }
            return fallback ?? string.Empty;
        }

        private static List<JObject> CollectTemplateElements(JObject panelDynamic)
        {
            var result = new List<JObject>();
            if (panelDynamic["templateElements"] is not JArray templateElements) return result;
            foreach (var el in templateElements)
            {
                result.AddRange(FlattenTemplateElements(el));
            }
            return result;
        }

        private static IEnumerable<JObject> FlattenTemplateElements(JToken? token)
        {
            if (token is not JObject obj) yield break;
            var type = obj["type"]?.ToString();
            if (obj["elements"] is JArray nestedElements)
            {
                foreach (var n in nestedElements)
                {
                    foreach (var child in FlattenTemplateElements(n))
                    {
                        yield return child;
                    }
                }
            }
            if (string.Equals(type, "panel", StringComparison.OrdinalIgnoreCase)
                || string.Equals(type, "paneldynamic", StringComparison.OrdinalIgnoreCase))
            {
                yield break;
            }

            yield return obj;
        }

        private sealed class ChoiceInfo
        {
            public Dictionary<string, string> Map { get; } = new(StringComparer.OrdinalIgnoreCase);
            public string? OtherValue { get; set; }
            public string? OtherLabel { get; set; }
            public bool HasOther => !string.IsNullOrWhiteSpace(OtherValue);
        }

        private static ChoiceInfo BuildChoiceInfo(JObject element, string locale)
        {
            var info = new ChoiceInfo();
            AddChoiceItems(info, element["choices"] as JArray, locale);
            AddRateValues(info, element["rateValues"] as JArray, locale);

            if (HasOtherFlag(element))
            {
                info.OtherValue ??= element["otherValue"]?.ToString() ?? "other";
                info.OtherLabel ??= GetLocalizedText(element["otherText"] ?? element["otherItemText"], locale);
            }

            if (string.IsNullOrWhiteSpace(info.OtherLabel) && !string.IsNullOrWhiteSpace(info.OtherValue))
            {
                info.OtherLabel = DefaultOtherLabel;
            }

            return info;
        }

        private static void AddChoiceItems(ChoiceInfo info, JArray? choices, string locale)
        {
            if (choices == null) return;
            foreach (var c in choices)
            {
                if (c.Type == JTokenType.String)
                {
                    var v = c.Value<string>();
                    if (!string.IsNullOrWhiteSpace(v)) info.Map[v] = v;
                    continue;
                }
                if (c is JObject obj)
                {
                    bool isOther = obj.Value<bool?>("isOther") == true || obj.Value<bool?>("other") == true;
                    var val = GetTokenString(obj["value"]) ?? GetTokenString(obj["name"]) ?? GetTokenString(obj["id"]);
                    var label = GetLocalizedText(obj["text"] ?? obj["title"] ?? obj["label"], locale);
                    if (string.IsNullOrWhiteSpace(label)) label = val ?? obj.ToString(Formatting.None);
                    if (string.IsNullOrWhiteSpace(val)) val = label;
                    if (!string.IsNullOrWhiteSpace(val)) info.Map[val] = label ?? val;

                    if (isOther)
                    {
                        info.OtherValue ??= val;
                        info.OtherLabel ??= label ?? val;
                    }
                }
            }
        }

        private static void AddRateValues(ChoiceInfo info, JArray? rateValues, string locale)
        {
            if (rateValues == null) return;
            foreach (var c in rateValues)
            {
                if (c.Type == JTokenType.String)
                {
                    var v = c.Value<string>();
                    if (!string.IsNullOrWhiteSpace(v)) info.Map[v] = v;
                    continue;
                }
                if (c is JObject obj)
                {
                    var val = GetTokenString(obj["value"]) ?? GetTokenString(obj["name"]) ?? GetTokenString(obj["id"]);
                    var label = GetLocalizedText(obj["text"] ?? obj["title"] ?? obj["label"], locale);
                    if (string.IsNullOrWhiteSpace(label)) label = val ?? obj.ToString(Formatting.None);
                    if (string.IsNullOrWhiteSpace(val)) val = label;
                    if (!string.IsNullOrWhiteSpace(val)) info.Map[val] = label ?? val;
                }
            }
        }

        private static bool HasOtherFlag(JObject element)
        {
            return element["showOtherItem"]?.Value<bool>() == true
                || element["hasOther"]?.Value<bool>() == true
                || element["otherValue"] != null;
        }

        private static string? GetOtherComment(JObject surveyJson, JObject element, JObject answerScope, string name)
        {
            var suffix = element["commentSuffix"]?.ToString()
                         ?? surveyJson["commentSuffix"]?.ToString()
                         ?? "-Comment";
            var token = answerScope[name + suffix];
            var comment = GetTokenString(token);
            return string.IsNullOrWhiteSpace(comment) ? null : comment;
        }

        private static Dictionary<string, string> BuildValueLabelMap(JArray? items, string locale, string[] valueKeys, string[] textKeys)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (items == null) return map;

            foreach (var item in items)
            {
                if (item.Type == JTokenType.String)
                {
                    var v = item.Value<string>();
                    if (!string.IsNullOrWhiteSpace(v)) map[v] = v;
                    continue;
                }
                if (item is not JObject obj) continue;

                string? val = null;
                foreach (var key in valueKeys)
                {
                    val = GetTokenString(obj[key]);
                    if (!string.IsNullOrWhiteSpace(val)) break;
                }

                JToken? labelToken = null;
                foreach (var key in textKeys)
                {
                    if (obj[key] != null)
                    {
                        labelToken = obj[key];
                        break;
                    }
                }

                var label = GetLocalizedText(labelToken, locale);
                if (string.IsNullOrWhiteSpace(label)) label = val ?? obj.ToString(Formatting.None);
                if (string.IsNullOrWhiteSpace(val)) val = label;
                if (!string.IsNullOrWhiteSpace(val)) map[val] = label ?? val;
            }

            return map;
        }

        private static string FormatChoiceValue(string value, ChoiceInfo choiceInfo, bool hasOther, string? otherComment, ref bool usedOther)
        {
            if (IsOtherValue(value, choiceInfo, hasOther))
            {
                usedOther = true;
                if (!string.IsNullOrWhiteSpace(otherComment))
                {
                    return $"{choiceInfo.OtherLabel ?? DefaultOtherLabel}: {otherComment}";
                }
                return choiceInfo.OtherLabel ?? DefaultOtherLabel;
            }

            if (choiceInfo.Map.TryGetValue(value, out var label))
            {
                return label;
            }

            if (hasOther && !string.IsNullOrWhiteSpace(otherComment))
            {
                usedOther = true;
                return $"{choiceInfo.OtherLabel ?? DefaultOtherLabel}: {otherComment}";
            }

            return value;
        }

        private static bool IsOtherValue(string value, ChoiceInfo choiceInfo, bool hasOther)
        {
            if (!hasOther) return false;
            if (!string.IsNullOrWhiteSpace(choiceInfo.OtherValue)
                && string.Equals(value, choiceInfo.OtherValue, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            return string.Equals(value, "other", StringComparison.OrdinalIgnoreCase);
        }
        #endregion
    }
}
