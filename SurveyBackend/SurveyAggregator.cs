using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Text;

namespace SurveyBackend
{
    // 问题元数据
    internal class QuestionMeta
    {
        public string? Name { get; set; }
        public string? Title { get; set; }
        public string? Type { get; set; }
        // value -> localized label
        public Dictionary<string, string> ChoiceMap { get; set; } = new Dictionary<string, string>();
        // 标识是否允许多选（checkbox 等）
        public bool IsMultiple { get; set; }
        // 标识是否为 matrix (行/列)
        public bool IsMatrix { get; set; }
        // 如果 matrix，保存行与列元数据（value->label）
        public Dictionary<string, string> MatrixRows { get; set; } = new Dictionary<string, string>();
        public Dictionary<string, string> MatrixColumns { get; set; } = new Dictionary<string, string>();
        // 是否为文本输入
        public bool IsText { get; set; }
    }

    internal static class SurveySchemaParser
    {
        // 解析 SurveyJS 的 schema，提取每个题目的 name,type,choices -> localized text
        public static List<QuestionMeta> ParseQuestions(JObject schemaRoot, string locale = "zh-cn")
        {
            var metas = new List<QuestionMeta>();
            // SurveyJS 通常把题目放在 pages[].elements 或 elements
            var rootElements = new List<JToken>();
            if (schemaRoot["pages"] is JArray pages)
            {
                foreach (var p in pages)
                {
                    if (p["elements"] is JArray elems)
                    {
                        rootElements.AddRange(elems.Children());
                    }
                }
            }
            if (schemaRoot["elements"] is JArray elemsRoot)
            {
                rootElements.AddRange(elemsRoot.Children());
            }

            foreach (var el in rootElements)
            {
                CollectElement(el, metas, locale);
            }

            return metas;
        }
        public static List<QuestionMeta> ParseQuestionsFiltered(JObject schemaRoot, string locale = "zh-cn", string? pageFilter = null, string[]? questionFilter = null)
        {
            var metas = new List<QuestionMeta>();
            var rootElements = new List<JToken>();
            if (schemaRoot["pages"] is JArray pages)
            {
                foreach (var p in pages)
                {
                    bool pageMatches = string.IsNullOrEmpty(pageFilter);
                    if (!pageMatches)
                    {
                        var pname = p["name"]?.Value<string>() ?? string.Empty;
                        string ptitle = string.Empty;
                        var titleToken = p["title"];
                        if (titleToken != null)
                        {
                            if (titleToken.Type == JTokenType.String) ptitle = titleToken.Value<string>()!;
                            else if (titleToken.Type == JTokenType.Object) ptitle = titleToken.Value<string>(locale) ?? titleToken.Value<string>("default") ?? titleToken.Children<JProperty>().FirstOrDefault()?.Value.ToString()!;
                        }
                        if (!string.IsNullOrEmpty(pname) && pname.Equals(pageFilter, StringComparison.OrdinalIgnoreCase)) pageMatches = true;
                        else if (!string.IsNullOrEmpty(ptitle) && pageFilter is not null && ptitle.IndexOf(pageFilter, StringComparison.OrdinalIgnoreCase) >= 0) pageMatches = true;
                    }


                    if (pageMatches && p["elements"] is JArray elems)
                    {
                        rootElements.AddRange(elems.Children());
                    }
                }
            }


            // 只有在没有设置 pageFilter 时才把根 elements 一并加入（通常问卷直接在根 elements）
            if (schemaRoot["elements"] is JArray elemsRoot && string.IsNullOrEmpty(pageFilter))
            {
                rootElements.AddRange(elemsRoot.Children());
            }


            foreach (var el in rootElements)
            {
                CollectElement(el, metas, locale);
            }


            // 如果设置了 questionFilter，则按题目 name 做最终筛选
            if (questionFilter != null && questionFilter.Length > 0)
            {
                var set = new HashSet<string>(questionFilter);
                metas = metas.Where(m => set.Contains(m.Name!)).ToList();
            }


            return metas;
        }
        private static void CollectElement(JToken el, List<QuestionMeta> metas, string locale)
        {
            if (el == null) return;
            var etype = el.Value<string>("type") ?? "panel";
            if (etype == "panel")
            {
                // panel 中可能嵌套 elements
                if (el["elements"] is JArray sub)
                {
                    foreach (var s in sub) CollectElement(s, metas, locale);
                }
                return;
            }

            var name = el.Value<string>("name");
            if (string.IsNullOrEmpty(name)) return;

            string? title = null;
            var titleToken = el["title"];
            if (titleToken != null)
            {
                if (titleToken.Type == JTokenType.String)
                    title = titleToken.Value<string>();
                else if (titleToken.Type == JTokenType.Object)
                    title = titleToken.Value<string>(locale) ?? titleToken.Value<string>("default") ?? titleToken.Children<JProperty>().FirstOrDefault()?.Value.ToString();
            }
            var meta = new QuestionMeta { Name = name, Type = etype, Title = title };

            switch (etype)
            {
                case "checkbox":
                    meta.IsMultiple = true;
                    ParseChoices(el, meta, locale);
                    break;
                case "radiogroup":
                case "dropdown":
                    meta.IsMultiple = false;
                    ParseChoices(el, meta, locale);
                    break;
                case "matrix":
                case "matrixdropdown":
                    meta.IsMatrix = true;
                    ParseMatrix(el, meta, locale);
                    break;
                case "text":
                case "comment":
                    meta.IsText = true;
                    break;
                default:
                    // 其他类型，当作文本或选择处理
                    if (el["choices"] != null) ParseChoices(el, meta, locale);
                    break;
            }

            metas.Add(meta);
        }

        private static void ParseChoices(JToken el, QuestionMeta meta, string locale)
        {
            var choices = el["choices"] as JArray;
            if (choices == null) return;
            foreach (var c in choices)
            {
                // choice 可能为 string，也可能为 object { value:..., text: ... }
                if (c.Type == JTokenType.String)
                {
                    var v = c.Value<string>();
                    if (v is not null) meta.ChoiceMap[v] = v;
                }
                else if (c.Type == JTokenType.Object)
                {
                    var val = c.Value<string>("value") ?? c.Value<string>("name") ?? c.Value<string>("id");
                    string? label = null;
                    var textToken = c["text"] ?? c["title"] ?? c["label"];
                    if (textToken != null)
                    {
                        if (textToken.Type == JTokenType.String) label = textToken.Value<string>();
                        else if (textToken.Type == JTokenType.Object)
                        {
                            // 支持多语言对象
                            label = textToken.Value<string>(locale) ?? textToken.Value<string>("default") ?? ((JObject)textToken).Properties().FirstOrDefault()?.Value.ToString();
                        }
                    }
                    if (string.IsNullOrEmpty(label)) label = val ?? c.ToString(Formatting.None);
                    if (val == null) val = label;
                    meta.ChoiceMap[val] = label;
                }
            }
        }

        private static void ParseMatrix(JToken el, QuestionMeta meta, string locale)
        {
            // matrix 有 rows 和 columns
            var rows = el["rows"] as JArray;
            var cols = el["columns"] as JArray;
            if (rows != null)
            {
                foreach (var r in rows)
                {
                    if (r.Type == JTokenType.String)
                    {
                        var v = r.Value<string>();
                        if (v is not null) meta.MatrixRows[v] = v;
                    }
                    else if (r.Type == JTokenType.Object)
                    {
                        var val = r.Value<string>("value") ?? r.Value<string>("name");
                        var textToken = r["text"] ?? r["title"] ?? r["label"];
                        if (textToken != null)
                        {
                            string? label = GetLocalizedText(textToken, locale) ?? val;
                            val ??= label;
                            if (val is not null && label is not null) meta.MatrixRows[val] = label;
                        }
                        else
                        {
                            Console.WriteLine("无法匹配 textToken");
                        }
                    }
                }
            }
            if (cols != null)
            {
                foreach (var c in cols)
                {
                    if (c.Type == JTokenType.String)
                    {
                        var v = c.Value<string>();
                        if (v is not null) meta.MatrixColumns[v] = v;
                    }
                    else if (c.Type == JTokenType.Object)
                    {
                        var val = c.Value<string>("value") ?? c.Value<string>("name");
                        var textToken = c["text"] ?? c["title"] ?? c["label"];
                        string? label = GetLocalizedText(textToken!, locale) ?? val;
                        val ??= label;
                        if (val is not null && label is not null) meta.MatrixColumns[val] = label;
                    }
                }
            }
        }

        private static string? GetLocalizedText(JToken token, string locale)
        {
            if (token == null) return null;
            if (token.Type == JTokenType.String) return token.Value<string>();
            if (token.Type == JTokenType.Object)
            {
                var t = token[locale]?.Value<string>();
                if (!string.IsNullOrEmpty(t)) return t;
                var first = token.Children<JProperty>().FirstOrDefault();
                return first?.Value?.ToString();
            }
            return token.ToString();
        }
    }

    internal class Aggregator
    {
        public List<QuestionMeta> Metas { get; }

        // questionName -> (label -> count)
        public Dictionary<string, Dictionary<string, long>> Counts = new Dictionary<string, Dictionary<string, long>>();
        // For matrix: questionName -> row -> column -> count
        public Dictionary<string, Dictionary<string, Dictionary<string, long>>> MatrixCounts = new Dictionary<string, Dictionary<string, Dictionary<string, long>>>();
        // For text inputs: questionName -> value -> count
        public Dictionary<string, Dictionary<string, long>> TextCounts = new Dictionary<string, Dictionary<string, long>>();
        // Null/missing counts
        public Dictionary<string, long> MissingCounts = new Dictionary<string, long>();

        public Aggregator(List<QuestionMeta> metas)
        {
            Metas = metas;
            foreach (var m in metas)
            {
                if (m.IsMatrix && m.Name is not null)
                {
                    MatrixCounts[m.Name] = new Dictionary<string, Dictionary<string, long>>();
                    // initialize rows/cols with zero
                    foreach (var r in m.MatrixRows.Keys)
                    {
                        MatrixCounts[m.Name][r] = new Dictionary<string, long>();
                        foreach (var c in m.MatrixColumns.Keys) MatrixCounts[m.Name][r][c] = 0;
                    }
                }
                else if (m.IsText && m.Name is not null)
                {
                    TextCounts[m.Name] = new Dictionary<string, long>();
                }
                else
                {
                    if (m.Name is not null) Counts[m.Name] = new Dictionary<string, long>();
                    // init choice labels
                    foreach (var kv in m.ChoiceMap)
                    {
                        var label = kv.Value ?? kv.Key;
                        if (m.Name is not null && !Counts[m.Name].ContainsKey(label)) Counts[m.Name][label] = 0;
                    }
                }
                MissingCounts[m.Name!] = 0;
            }
        }

        public void AddResponse(JObject resp)
        {
            foreach (var m in Metas)
            {
                if (m.Name is null)
                {
                    continue;
                }
                var token = resp[m.Name!];
                if (token == null || token.Type == JTokenType.Null)
                {
                    MissingCounts[m.Name!]++;
                    continue;
                }

                try
                {
                    if (m.IsMatrix && token.Type == JTokenType.Object)
                    {
                        var obj = (JObject)token;
                        foreach (var rowProp in obj.Properties())
                        {
                            var rowKey = rowProp.Name; // usually row value
                            var colVal = rowProp.Value?.ToString();
                            if (string.IsNullOrEmpty(colVal)) continue;
                            var rowLabel = m.MatrixRows.ContainsKey(rowKey) ? m.MatrixRows[rowKey] : rowKey;
                            var colLabel = m.MatrixColumns.ContainsKey(colVal) ? m.MatrixColumns[colVal] : colVal;
                            if (!MatrixCounts.ContainsKey(m.Name!)) MatrixCounts[m.Name!] = new Dictionary<string, Dictionary<string, long>>();
                            if (!MatrixCounts[m.Name].ContainsKey(rowLabel)) MatrixCounts[m.Name][rowLabel] = new Dictionary<string, long>();
                            if (!MatrixCounts[m.Name][rowLabel].ContainsKey(colLabel)) MatrixCounts[m.Name][rowLabel][colLabel] = 0;
                            MatrixCounts[m.Name][rowLabel][colLabel]++;
                        }
                    }
                    else if (m.IsMultiple || token.Type == JTokenType.Array)
                    {
                        var arr = token as JArray ?? new JArray(token);
                        foreach (var item in arr)
                        {
                            var val = item.Type == JTokenType.String || item.Type == JTokenType.Integer ? item.ToString() : item.ToString(Formatting.None);
                            string label = m.ChoiceMap.ContainsKey(val) ? m.ChoiceMap[val] : val;
                            if (!Counts.ContainsKey(m.Name)) Counts[m.Name] = new Dictionary<string, long>();
                            if (!Counts[m.Name].ContainsKey(label)) Counts[m.Name][label] = 0;
                            Counts[m.Name][label]++;
                        }
                    }
                    else if (m.IsText)
                    {
                        var s = token.Type == JTokenType.String ? token.Value<string>() : token.ToString(Formatting.None);
                        if (string.IsNullOrWhiteSpace(s)) { MissingCounts[m.Name]++; continue; }
                        if (!TextCounts[m.Name].ContainsKey(s)) TextCounts[m.Name][s] = 0;
                        TextCounts[m.Name][s]++;
                    }
                    else
                    {
                        var val = token.Type == JTokenType.String || token.Type == JTokenType.Integer || token.Type == JTokenType.Boolean ? token.ToString() : token.ToString(Formatting.None);
                        string label = m.ChoiceMap.ContainsKey(val) ? m.ChoiceMap[val] : val;
                        if (!Counts.ContainsKey(m.Name)) Counts[m.Name] = new Dictionary<string, long>();
                        if (!Counts[m.Name].ContainsKey(label)) Counts[m.Name][label] = 0;
                        Counts[m.Name][label]++;
                    }
                }
                catch (Exception ex)
                {
                    // 忽略单个题目的解析错误
                    Console.WriteLine($"题目 {m.Name} 解析错误: {ex.Message}");
                }
            }
        }
    }

    internal class Reporter
    {
        private readonly Aggregator Agg;
        private readonly string? OutDir;
        public Reporter(Aggregator agg, string outDir)
        {
            Agg = agg; OutDir = outDir;
        }
        public Reporter(Aggregator agg)
        {
            Agg = agg;
        }
        public void DumpConsole()
        {
            foreach (var m in Agg.Metas)
            {
                if (m.Name is null)
                {
                    continue;
                }
                Console.WriteLine("========================================");
                Console.WriteLine($"题目: {m.Name}  ({m.Title})  类型: {m.Type}");
                Console.WriteLine($"缺失/空: {Agg.MissingCounts.GetValueOrDefault(m.Name, 0)}");

                if (m.IsMatrix)
                {
                    if (Agg.MatrixCounts.TryGetValue(m.Name, out var mat))
                    {
                        foreach (var r in mat)
                        {
                            Console.WriteLine($" 行: {r.Key}");
                            foreach (var c in r.Value)
                            {
                                Console.WriteLine($"   {c.Key}: {c.Value}");
                            }
                        }
                    }
                }
                else if (m.IsText)
                {
                    Console.WriteLine(" 文本答案（出现频次前20）:");
                    if (Agg.TextCounts.TryGetValue(m.Name, out var txt))
                    {
                        foreach (var kv in txt.OrderByDescending(k => k.Value).Take(20))
                        {
                            Console.WriteLine($"   {kv.Value}	{kv.Key}");
                        }
                    }
                }
                else
                {
                    if (Agg.Counts.TryGetValue(m.Name, out var dict))
                    {
                        foreach (var kv in dict.OrderByDescending(k => k.Value))
                        {
                            Console.WriteLine($"   {kv.Key}: {kv.Value}");
                        }
                    }
                }
            }
        }

        public string ReportString()
        {
            var stringBuilder = new StringBuilder();
            foreach (var m in Agg.Metas)
            {
                if (m.Name is null)
                {
                    continue;
                }
                stringBuilder.AppendLine("========================================");
                stringBuilder.AppendLine($"题目: {m.Name}  ({m.Title})  类型: {m.Type}");
                stringBuilder.AppendLine($"缺失/空: {Agg.MissingCounts.GetValueOrDefault(m.Name, 0)}");

                if (m.IsMatrix)
                {
                    if (Agg.MatrixCounts.TryGetValue(m.Name, out var mat))
                    {
                        foreach (var r in mat)
                        {
                            stringBuilder.AppendLine($" 行: {r.Key}");
                            foreach (var c in r.Value)
                            {
                                stringBuilder.AppendLine($"   {c.Key}: {c.Value}");
                            }
                        }
                    }
                }
                else if (m.IsText)
                {
                    stringBuilder.AppendLine(" 文本答案（出现频次前20）:");
                    if (Agg.TextCounts.TryGetValue(m.Name, out var txt))
                    {
                        foreach (var kv in txt.OrderByDescending(k => k.Value).Take(20))
                        {
                            stringBuilder.AppendLine($"   {kv.Value}	{kv.Key}");
                        }
                    }
                }
                else
                {
                    if (Agg.Counts.TryGetValue(m.Name, out var dict))
                    {
                        foreach (var kv in dict.OrderByDescending(k => k.Value))
                        {
                            stringBuilder.AppendLine($"   {kv.Key}: {kv.Value}");
                        }
                    }
                }
            }
            return stringBuilder.ToString();

        }

        public void SaveJson(string filename)
        {

            if (OutDir is null) { return; }
            var outPath = Path.Combine(OutDir, filename);
            var root = new JObject();
            foreach (var m in Agg.Metas)
            {
                if (m.Name is null)
                {
                    continue;
                }
                var q = new JObject();
                q["name"] = m.Name;
                q["title"] = m.Title;
                q["type"] = m.Type;
                q["missing"] = Agg.MissingCounts.GetValueOrDefault(m.Name, 0);

                if (m.IsMatrix)
                {
                    var mat = new JObject();
                    if (Agg.MatrixCounts.TryGetValue(m.Name, out var matrix))
                    {
                        foreach (var r in matrix)
                        {
                            var jcols = new JObject();
                            foreach (var c in r.Value) jcols[c.Key] = c.Value;
                            mat[r.Key] = jcols;
                        }
                    }
                    q["matrix"] = mat;
                }
                else if (m.IsText)
                {
                    var arr = new JArray();
                    if (Agg.TextCounts.TryGetValue(m.Name, out var txt))
                    {
                        foreach (var kv in txt.OrderByDescending(k => k.Value))
                        {
                            var item = new JObject { ["value"] = kv.Key, ["count"] = kv.Value };
                            arr.Add(item);
                        }
                    }
                    q["text_counts"] = arr;
                }
                else
                {
                    var obj = new JObject();
                    if (Agg.Counts.TryGetValue(m.Name, out var dict))
                    {
                        foreach (var kv in dict) obj[kv.Key] = kv.Value;
                    }
                    q["counts"] = obj;
                }

                root[m.Name] = q;
            }

            File.WriteAllText(outPath, root.ToString(Formatting.Indented));
        }

        public void SaveCsv(string filename)
        {
            if (OutDir is null) { return; }
            // 生成一个简易 CSV，按题目输出，每个题目作为一个 block
            var outPath = Path.Combine(OutDir, filename);
            using (var sw = new StreamWriter(outPath))
            {
                foreach (var m in Agg.Metas)
                {
                    if (m.Name is null || m.Title is null)
                    {
                        continue;
                    }
                    sw.WriteLine($"Question:,{m.Name}");
                    sw.WriteLine($"Title:,{EscapeCsv(m.Title)}");
                    sw.WriteLine($"Type:,{m.Type}");
                    sw.WriteLine($"Missing:,{Agg.MissingCounts.GetValueOrDefault(m.Name, 0)}");

                    if (m.IsMatrix)
                    {
                        // 输出 header 行：Row,Col,Count
                        sw.WriteLine("Row,Column,Count");
                        if (Agg.MatrixCounts.TryGetValue(m.Name, out var mat))
                        {
                            foreach (var r in mat)
                            {
                                foreach (var c in r.Value)
                                {
                                    sw.WriteLine($"{EscapeCsv(r.Key)},{EscapeCsv(c.Key)},{c.Value}");
                                }
                            }
                        }
                    }
                    else if (m.IsText)
                    {
                        sw.WriteLine("Count,Value");
                        if (Agg.TextCounts.TryGetValue(m.Name, out var txt))
                        {
                            foreach (var kv in txt.OrderByDescending(k => k.Value))
                            {
                                sw.WriteLine($"{kv.Value},{EscapeCsv(kv.Key)}");
                            }
                        }
                    }
                    else
                    {
                        sw.WriteLine("Count,Label");
                        if (Agg.Counts.TryGetValue(m.Name, out var dict))
                        {
                            foreach (var kv in dict.OrderByDescending(k => k.Value))
                            {
                                sw.WriteLine($"{kv.Value},{EscapeCsv(kv.Key)}");
                            }
                        }
                    }

                    sw.WriteLine();
                }
            }
        }

        private static string EscapeCsv(string s)
        {
            if (s == null) return "";
            if (s.Contains(",") || s.Contains("\n") || s.Contains("\r") || s.Contains("\""))
                return "\"" + s.Replace("\"", "\"\"") + "\"";
            return s;
        }
    }
}
