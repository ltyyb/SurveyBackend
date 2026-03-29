using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Globalization;
using System.Text;

namespace SurveyBackend.Models
{
    public sealed class SurveyStatisticsTool
    {
        private readonly MainDbContext _db;

        public SurveyStatisticsTool(MainDbContext db)
        {
            _db = db;
        }

        public async Task<string> BuildReportAsync(
            Survey survey,
            IEnumerable<string>? questionNameFilter = null,
            string locale = "zh-cn",
            CancellationToken cancellationToken = default)
        {
            var questionnaires = await _db.Questionnaires
                .AsNoTracking()
                .Where(q => q.SurveyId == survey.SurveyId)
                .OrderBy(q => q.ReleaseDate)
                .ToListAsync(cancellationToken);

            if (questionnaires.Count == 0)
            {
                return $"统计失败: Survey {survey.SurveyId} 下没有 Questionnaire。";
            }

            var submissions = await _db.Submissions
                .AsNoTracking()
                .Where(s => s.Questionnaire != null && s.Questionnaire.SurveyId == survey.SurveyId)
                .ToListAsync(cancellationToken);

            return BuildReportCore(
                $"Survey: {survey.Title}",
                survey.SurveyId,
                questionnaires,
                submissions,
                questionNameFilter,
                locale);
        }

        public async Task<string> BuildReportAsync(
            Questionnaire questionnaire,
            IEnumerable<string>? questionNameFilter = null,
            string locale = "zh-cn",
            CancellationToken cancellationToken = default)
        {
            var canonical = await _db.Questionnaires
                .AsNoTracking()
                .SingleOrDefaultAsync(q => q.QuestionnaireId == questionnaire.QuestionnaireId, cancellationToken);

            if (canonical is null)
            {
                return $"统计失败: 未找到 Questionnaire {questionnaire.QuestionnaireId}。";
            }

            var submissions = await _db.Submissions
                .AsNoTracking()
                .Where(s => s.QuestionnaireId == canonical.QuestionnaireId)
                .ToListAsync(cancellationToken);

            return BuildReportCore(
                $"Questionnaire: {canonical.QuestionnaireId}",
                canonical.QuestionnaireId,
                [canonical],
                submissions,
                questionNameFilter,
                locale);
        }

        public async Task<string> BuildReportByPageNamesAsync(
            Survey survey,
            IEnumerable<string> pageNames,
            string locale = "zh-cn",
            CancellationToken cancellationToken = default)
        {
            var pageFilter = BuildPageFilter(pageNames);
            if (pageFilter is null)
            {
                return "统计失败: pageNames 为空。";
            }

            var questionnaires = await _db.Questionnaires
                .AsNoTracking()
                .Where(q => q.SurveyId == survey.SurveyId)
                .OrderBy(q => q.ReleaseDate)
                .ToListAsync(cancellationToken);

            if (questionnaires.Count == 0)
            {
                return $"统计失败: Survey {survey.SurveyId} 下没有 Questionnaire。";
            }

            var submissions = await _db.Submissions
                .AsNoTracking()
                .Where(s => s.Questionnaire != null && s.Questionnaire.SurveyId == survey.SurveyId)
                .ToListAsync(cancellationToken);

            var pageParseWarnings = new List<string>();
            var questionNames = ExtractQuestionNamesFromPages(questionnaires, pageFilter, pageParseWarnings);
            if (questionNames.Count == 0)
            {
                if (pageParseWarnings.Count > 0)
                {
                    return $"未在指定页面中找到可统计题目。页面: {string.Join(", ", pageFilter)}。另外有 {pageParseWarnings.Count} 条页面解析告警。";
                }
                return $"未在指定页面中找到可统计题目。页面: {string.Join(", ", pageFilter)}。";
            }

            return BuildReportCore(
                $"Survey: {survey.Title}",
                survey.SurveyId,
                questionnaires,
                submissions,
                questionNames,
                locale);
        }

        public async Task<string> BuildReportByPageNamesAsync(
            Questionnaire questionnaire,
            IEnumerable<string> pageNames,
            string locale = "zh-cn",
            CancellationToken cancellationToken = default)
        {
            var pageFilter = BuildPageFilter(pageNames);
            if (pageFilter is null)
            {
                return "统计失败: pageNames 为空。";
            }

            var canonical = await _db.Questionnaires
                .AsNoTracking()
                .SingleOrDefaultAsync(q => q.QuestionnaireId == questionnaire.QuestionnaireId, cancellationToken);

            if (canonical is null)
            {
                return $"统计失败: 未找到 Questionnaire {questionnaire.QuestionnaireId}。";
            }

            var submissions = await _db.Submissions
                .AsNoTracking()
                .Where(s => s.QuestionnaireId == canonical.QuestionnaireId)
                .ToListAsync(cancellationToken);

            var pageParseWarnings = new List<string>();
            var questionNames = ExtractQuestionNamesFromPages([canonical], pageFilter, pageParseWarnings);
            if (questionNames.Count == 0)
            {
                if (pageParseWarnings.Count > 0)
                {
                    return $"未在指定页面中找到可统计题目。页面: {string.Join(", ", pageFilter)}。另外有 {pageParseWarnings.Count} 条页面解析告警。";
                }
                return $"未在指定页面中找到可统计题目。页面: {string.Join(", ", pageFilter)}。";
            }

            return BuildReportCore(
                $"Questionnaire: {canonical.QuestionnaireId}",
                canonical.QuestionnaireId,
                [canonical],
                submissions,
                questionNames,
                locale);
        }

        private static string BuildReportCore(
            string title,
            string id,
            IReadOnlyList<Questionnaire> questionnaires,
            IReadOnlyList<Submission> submissions,
            IEnumerable<string>? questionNameFilter,
            string locale)
        {
            var parseWarnings = new List<string>();
            var defs = BuildQuestionDefinitions(questionnaires, locale, parseWarnings);
            var filterSet = BuildFilter(questionNameFilter);

            if (filterSet is not null)
            {
                foreach (var missing in filterSet.Where(n => !defs.ContainsKey(n)))
                {
                    defs[missing] = QuestionDef.CreateSynthetic(missing);
                }
            }

            var activeQuestions = defs.Values
                .Where(d => filterSet is null || filterSet.Contains(d.Name))
                .OrderBy(d => d.Order)
                .ThenBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (activeQuestions.Count == 0)
            {
                return "没有可统计的问题，请检查筛选器。";
            }

            var accMap = activeQuestions.ToDictionary(d => d.Name, d => new StatAccumulator(d), StringComparer.OrdinalIgnoreCase);

            var totalCount = submissions.Count;
            var disabledCount = submissions.Count(s => s.IsDisabled);
            var validSubmissions = submissions.Where(s => !s.IsDisabled).ToList();

            foreach (var submission in validSubmissions)
            {
                JObject? answerObj;
                try
                {
                    answerObj = string.IsNullOrWhiteSpace(submission.SurveyData) ? null : JObject.Parse(submission.SurveyData);
                }
                catch
                {
                    answerObj = null;
                }

                foreach (var def in activeQuestions)
                {
                    accMap[def.Name].Add(answerObj?[def.Name]);
                }
            }

            var sb = new StringBuilder();
            sb.AppendLine("问卷统计结果");
            sb.AppendLine($"目标: {title} ({id})");
            sb.AppendLine($"Questionnaire 数: {questionnaires.Count}");
            sb.AppendLine($"提交总数: {totalCount}  有效: {validSubmissions.Count}  已禁用: {disabledCount}");
            sb.AppendLine(filterSet is null
                ? "筛选题目: 全部"
                : $"筛选题目: {string.Join(", ", filterSet)}");
            if (parseWarnings.Count > 0)
            {
                sb.AppendLine($"定义解析告警: {parseWarnings.Count} 条");
            }
            sb.AppendLine(new string('=', 48));

            var index = 1;
            foreach (var def in activeQuestions)
            {
                var acc = accMap[def.Name];
                sb.AppendLine($"[{index}] {def.Name}");
                sb.AppendLine($"标题: {def.Title}");
                sb.AppendLine($"题型: {string.Join("/", def.Types.OrderBy(t => t))}");
                sb.AppendLine($"作答: {acc.AnsweredCount}  缺失: {acc.MissingCount}");

                RenderDetail(sb, acc);

                sb.AppendLine(new string('-', 48));
                index++;
            }

            return sb.ToString().TrimEnd();
        }

        private static void RenderDetail(StringBuilder sb, StatAccumulator acc)
        {
            var kind = acc.Def.GetKindHint();
            if (kind == StatKind.Unknown)
            {
                kind = acc.InferKind();
            }

            if (kind == StatKind.MultiChoice)
            {
                sb.AppendLine($"多选总勾选次数: {acc.TotalSelections}  人均: {FormatNumber(acc.AnsweredCount == 0 ? 0 : (double)acc.TotalSelections / acc.AnsweredCount)}");
            }
            if (kind == StatKind.File)
            {
                sb.AppendLine($"累计文件项: {acc.FileItems}  人均上传数: {FormatNumber(acc.AnsweredCount == 0 ? 0 : (double)acc.FileItems / acc.AnsweredCount)}");
            }

            if (acc.OptionCounts.Count > 0)
            {
                sb.AppendLine("选项分布:");
                foreach (var p in acc.OptionCounts.OrderByDescending(x => x.Value).ThenBy(x => x.Key, StringComparer.Ordinal))
                {
                    var pct = acc.AnsweredCount == 0 ? 0 : (double)p.Value * 100 / acc.AnsweredCount;
                    sb.AppendLine($"  {Pad(p.Key, 22)} {p.Value,6} ({pct,6:0.00}%) {Bar(pct)}");
                }
            }

            if (acc.TextCounts.Count > 0)
            {
                sb.AppendLine($"文本答案(去重后 {acc.TextCounts.Count} 条，展示前 12):");
                foreach (var p in acc.TextCounts.OrderByDescending(x => x.Value).Take(12))
                {
                    var pct = acc.AnsweredCount == 0 ? 0 : (double)p.Value * 100 / acc.AnsweredCount;
                    sb.AppendLine($"  {p.Value,6} ({pct,6:0.00}%)  {Trim(p.Key, 80)}");
                }
            }

            if (acc.MatrixCounts.Count > 0)
            {
                sb.AppendLine("矩阵分布:");
                foreach (var row in acc.MatrixCounts.OrderBy(x => x.Key, StringComparer.Ordinal))
                {
                    var rowTotal = row.Value.Values.Sum();
                    sb.AppendLine($"  行 {row.Key} (样本 {rowTotal}):");
                    foreach (var col in row.Value.OrderByDescending(x => x.Value).ThenBy(x => x.Key, StringComparer.Ordinal))
                    {
                        var pct = rowTotal == 0 ? 0 : (double)col.Value * 100 / rowTotal;
                        sb.AppendLine($"    {Pad(col.Key, 18)} {col.Value,6} ({pct,6:0.00}%) {Bar(pct)}");
                    }
                }
            }
            if (acc.ObjectCounts.Count > 0)
            {
                sb.AppendLine("结构化字段分布:");
                foreach (var field in acc.ObjectCounts.OrderBy(x => x.Key, StringComparer.Ordinal))
                {
                    var fieldTotal = field.Value.Values.Sum();
                    sb.AppendLine($"  字段 {field.Key} (样本 {fieldTotal}):");
                    foreach (var p in field.Value.OrderByDescending(x => x.Value).ThenBy(x => x.Key, StringComparer.Ordinal).Take(8))
                    {
                        var pct = fieldTotal == 0 ? 0 : (double)p.Value * 100 / fieldTotal;
                        sb.AppendLine($"    {Pad(Trim(p.Key, 26), 26)} {p.Value,6} ({pct,6:0.00}%) {Bar(pct)}");
                    }
                }
            }

            if (acc.OptionCounts.Count == 0 &&
                acc.TextCounts.Count == 0 &&
                acc.MatrixCounts.Count == 0 &&
                acc.ObjectCounts.Count == 0)
            {
                sb.AppendLine("暂无可展示统计信息。");
            }
        }

        private static Dictionary<string, QuestionDef> BuildQuestionDefinitions(
            IReadOnlyList<Questionnaire> questionnaires,
            string locale,
            List<string> parseWarnings)
        {
            var defs = new Dictionary<string, QuestionDef>(StringComparer.OrdinalIgnoreCase);
            var order = 0;

            foreach (var qn in questionnaires)
            {
                if (string.IsNullOrWhiteSpace(qn.SurveyJson))
                {
                    parseWarnings.Add($"Questionnaire {qn.QuestionnaireId} 的 SurveyJson 为空");
                    continue;
                }

                JObject root;
                try
                {
                    root = JObject.Parse(qn.SurveyJson);
                }
                catch (Exception ex)
                {
                    parseWarnings.Add($"Questionnaire {qn.QuestionnaireId} 解析失败: {ex.Message}");
                    continue;
                }

                foreach (var element in EnumerateElements(root))
                {
                    var name = element.Value<string>("name");
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        continue;
                    }

                    var type = (element.Value<string>("type") ?? "unknown").Trim().ToLowerInvariant();
                    var title = GetLocalizedText(element["title"], locale) ?? name;

                    if (!defs.TryGetValue(name, out var def))
                    {
                        def = new QuestionDef
                        {
                            Name = name,
                            Title = title,
                            Order = order++
                        };
                        defs[name] = def;
                    }
                    else if (def.Title == def.Name && !string.IsNullOrWhiteSpace(title))
                    {
                        def.Title = title;
                    }

                    def.Types.Add(type);

                    ParseChoices(element["choices"], def.ChoiceLabels, locale);
                    ParseChoices(element["rateValues"], def.ChoiceLabels, locale);
                    ParseRowsOrColumns(element["rows"], def.RowLabels, locale);
                    ParseRowsOrColumns(element["columns"], def.ColumnLabels, locale);

                    ParseBooleanLabels(element, def, locale);
                    ParseMatrixDropdownColumns(element, def, locale);
                    ParseMultipleTextItems(element, def, locale);
                    ParsePanelDynamicItems(element, def, locale);
                }
            }

            return defs;
        }

        private static IEnumerable<JObject> EnumerateElements(JObject root)
        {
            var list = new List<JObject>();
            CollectElements(root["elements"], list);
            if (root["pages"] is JArray pages)
            {
                foreach (var page in pages.OfType<JObject>())
                {
                    CollectElements(page["elements"], list);
                }
            }
            return list;
        }

        private static void CollectElements(JToken? token, List<JObject> list)
        {
            if (token is null)
            {
                return;
            }
            if (token is JArray arr)
            {
                foreach (var item in arr)
                {
                    CollectElements(item, list);
                }
                return;
            }
            if (token is not JObject obj)
            {
                return;
            }

            var name = obj.Value<string>("name");
            if (!string.IsNullOrWhiteSpace(name))
            {
                list.Add(obj);
            }

            CollectElements(obj["elements"], list);
            CollectElements(obj["templateElements"], list);
        }

        private static void ParseChoices(JToken? token, Dictionary<string, string> target, string locale)
        {
            if (token is not JArray arr)
            {
                return;
            }

            foreach (var item in arr)
            {
                if (item is null)
                {
                    continue;
                }
                if (item.Type is JTokenType.String or JTokenType.Integer or JTokenType.Float or JTokenType.Boolean)
                {
                    var scalarValue = NormalizeScalar(item);
                    target[scalarValue] = scalarValue;
                    continue;
                }
                if (item is not JObject obj)
                {
                    continue;
                }

                var value = obj.Value<string>("value")
                            ?? obj.Value<string>("name")
                            ?? GetLocalizedText(obj["text"], locale)
                            ?? GetLocalizedText(obj["title"], locale);
                if (string.IsNullOrWhiteSpace(value))
                {
                    continue;
                }
                var label = GetLocalizedText(obj["text"], locale)
                            ?? GetLocalizedText(obj["title"], locale)
                            ?? GetLocalizedText(obj["label"], locale)
                            ?? value;
                target[value] = label;
            }
        }

        private static void ParseRowsOrColumns(JToken? token, Dictionary<string, string> target, string locale)
        {
            if (token is not JArray arr)
            {
                return;
            }
            foreach (var item in arr)
            {
                if (item is null)
                {
                    continue;
                }
                if (item.Type is JTokenType.String or JTokenType.Integer or JTokenType.Float or JTokenType.Boolean)
                {
                    var scalarValue = NormalizeScalar(item);
                    target[scalarValue] = scalarValue;
                    continue;
                }
                if (item is not JObject obj)
                {
                    continue;
                }
                var value = obj.Value<string>("value") ?? obj.Value<string>("name");
                if (string.IsNullOrWhiteSpace(value))
                {
                    continue;
                }
                var label = GetLocalizedText(obj["text"], locale)
                            ?? GetLocalizedText(obj["title"], locale)
                            ?? GetLocalizedText(obj["label"], locale)
                            ?? value;
                target[value] = label;
            }
        }
        private static void ParseBooleanLabels(JObject element, QuestionDef def, string locale)
        {
            var type = (element.Value<string>("type") ?? string.Empty).Trim().ToLowerInvariant();
            if (!string.Equals(type, "boolean", StringComparison.Ordinal))
            {
                return;
            }
            def.ChoiceLabels["true"] = GetLocalizedText(element["labelTrue"], locale) ?? "是";
            def.ChoiceLabels["false"] = GetLocalizedText(element["labelFalse"], locale) ?? "否";
        }

        private static void ParseMatrixDropdownColumns(JObject element, QuestionDef def, string locale)
        {
            var type = (element.Value<string>("type") ?? string.Empty).Trim().ToLowerInvariant();
            if (type is not ("matrixdropdown" or "matrixdynamic"))
            {
                return;
            }
            if (element["columns"] is not JArray columns)
            {
                return;
            }
            foreach (var col in columns.OfType<JObject>())
            {
                var colName = col.Value<string>("name") ?? col.Value<string>("value");
                if (string.IsNullOrWhiteSpace(colName))
                {
                    continue;
                }
                var label = GetLocalizedText(col["title"], locale) ?? colName;
                def.FieldLabels[colName] = label;

                if (!def.FieldChoiceLabels.TryGetValue(colName, out var map))
                {
                    map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    def.FieldChoiceLabels[colName] = map;
                }
                ParseChoices(col["choices"], map, locale);
                ParseChoices(col["rateValues"], map, locale);
            }
        }

        private static void ParseMultipleTextItems(JObject element, QuestionDef def, string locale)
        {
            var type = (element.Value<string>("type") ?? string.Empty).Trim().ToLowerInvariant();
            if (!string.Equals(type, "multipletext", StringComparison.Ordinal))
            {
                return;
            }
            if (element["items"] is not JArray items)
            {
                return;
            }
            foreach (var item in items.OfType<JObject>())
            {
                var name = item.Value<string>("name");
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }
                var label = GetLocalizedText(item["title"], locale) ?? name;
                def.FieldLabels[name] = label;
            }
        }

        private static void ParsePanelDynamicItems(JObject element, QuestionDef def, string locale)
        {
            var type = (element.Value<string>("type") ?? string.Empty).Trim().ToLowerInvariant();
            if (!string.Equals(type, "paneldynamic", StringComparison.Ordinal))
            {
                return;
            }
            if (element["templateElements"] is not JArray items)
            {
                return;
            }
            foreach (var item in items.OfType<JObject>())
            {
                var name = item.Value<string>("name");
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }
                var label = GetLocalizedText(item["title"], locale) ?? name;
                def.FieldLabels[name] = label;
            }
        }

        private static HashSet<string>? BuildFilter(IEnumerable<string>? names)
        {
            if (names is null)
            {
                return null;
            }
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var n in names)
            {
                var s = (n ?? string.Empty).Trim();
                if (!string.IsNullOrWhiteSpace(s))
                {
                    set.Add(s);
                }
            }
            return set.Count == 0 ? null : set;
        }

        private static HashSet<string>? BuildPageFilter(IEnumerable<string>? pageNames)
        {
            return BuildFilter(pageNames);
        }

        private static HashSet<string> ExtractQuestionNamesFromPages(
            IReadOnlyList<Questionnaire> questionnaires,
            HashSet<string> pageFilter,
            List<string> parseWarnings)
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var questionnaire in questionnaires)
            {
                if (string.IsNullOrWhiteSpace(questionnaire.SurveyJson))
                {
                    parseWarnings.Add($"Questionnaire {questionnaire.QuestionnaireId} 的 SurveyJson 为空");
                    continue;
                }

                JObject root;
                try
                {
                    root = JObject.Parse(questionnaire.SurveyJson);
                }
                catch (Exception ex)
                {
                    parseWarnings.Add($"Questionnaire {questionnaire.QuestionnaireId} 解析失败: {ex.Message}");
                    continue;
                }

                if (root["pages"] is not JArray pages)
                {
                    continue;
                }

                foreach (var page in pages.OfType<JObject>())
                {
                    var pageName = page.Value<string>("name");
                    if (string.IsNullOrWhiteSpace(pageName) || !pageFilter.Contains(pageName))
                    {
                        continue;
                    }

                    var elements = new List<JObject>();
                    CollectElements(page["elements"], elements);

                    foreach (var element in elements)
                    {
                        var questionName = element.Value<string>("name");
                        if (!string.IsNullOrWhiteSpace(questionName))
                        {
                            result.Add(questionName);
                        }
                    }
                }
            }

            return result;
        }

        private static string? GetLocalizedText(JToken? token, string locale)
        {
            if (token is null || token.Type == JTokenType.Null)
            {
                return null;
            }
            if (token.Type == JTokenType.String)
            {
                return token.Value<string>();
            }
            if (token is not JObject obj)
            {
                return token.ToString(Formatting.None);
            }

            var exact = obj.Properties().FirstOrDefault(p => string.Equals(p.Name, locale, StringComparison.OrdinalIgnoreCase));
            if (exact is not null)
            {
                var v = exact.Value.Type == JTokenType.String ? exact.Value.Value<string>() : exact.Value.ToString(Formatting.None);
                if (!string.IsNullOrWhiteSpace(v))
                {
                    return v;
                }
            }

            var def = obj.Properties().FirstOrDefault(p => string.Equals(p.Name, "default", StringComparison.OrdinalIgnoreCase));
            if (def is not null)
            {
                var v = def.Value.Type == JTokenType.String ? def.Value.Value<string>() : def.Value.ToString(Formatting.None);
                if (!string.IsNullOrWhiteSpace(v))
                {
                    return v;
                }
            }

            var first = obj.Properties().FirstOrDefault();
            if (first is null)
            {
                return null;
            }
            return first.Value.Type == JTokenType.String ? first.Value.Value<string>() : first.Value.ToString(Formatting.None);
        }

        private static string NormalizeScalar(JToken token)
        {
            return token.Type switch
            {
                JTokenType.String => token.Value<string>() ?? string.Empty,
                JTokenType.Boolean => token.Value<bool>() ? "true" : "false",
                JTokenType.Integer => token.Value<long>().ToString(CultureInfo.InvariantCulture),
                JTokenType.Float => token.Value<double>().ToString("0.###############", CultureInfo.InvariantCulture),
                JTokenType.Date => token.Value<DateTime>().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                _ => token.ToString(Formatting.None)
            };
        }

        private static string Bar(double pct)
        {
            const int width = 16;
            var fill = (int)Math.Round(Math.Clamp(pct, 0, 100) / 100d * width, MidpointRounding.AwayFromZero);
            return new string('#', fill).PadRight(width, '.');
        }

        private static string Pad(string value, int width)
        {
            if (value.Length >= width)
            {
                return value[..width];
            }
            return value.PadRight(width);
        }

        private static string Trim(string value, int maxLength)
        {
            if (value.Length <= maxLength)
            {
                return value;
            }
            return value[..Math.Max(0, maxLength - 3)] + "...";
        }

        private static string FormatNumber(double value)
        {
            return value.ToString("0.00", CultureInfo.InvariantCulture);
        }
        private enum StatKind
        {
            Unknown = 0,
            SingleChoice = 1,
            MultiChoice = 2,
            Text = 3,
            Matrix = 4,
            Object = 5,
            File = 6
        }

        private sealed class QuestionDef
        {
            public string Name { get; set; } = string.Empty;
            public string Title { get; set; } = string.Empty;
            public int Order { get; set; }
            public HashSet<string> Types { get; } = new(StringComparer.OrdinalIgnoreCase);
            public Dictionary<string, string> ChoiceLabels { get; } = new(StringComparer.OrdinalIgnoreCase);
            public Dictionary<string, string> RowLabels { get; } = new(StringComparer.OrdinalIgnoreCase);
            public Dictionary<string, string> ColumnLabels { get; } = new(StringComparer.OrdinalIgnoreCase);
            public Dictionary<string, string> FieldLabels { get; } = new(StringComparer.OrdinalIgnoreCase);
            public Dictionary<string, Dictionary<string, string>> FieldChoiceLabels { get; } = new(StringComparer.OrdinalIgnoreCase);

            public static QuestionDef CreateSynthetic(string name)
            {
                return new QuestionDef
                {
                    Name = name,
                    Title = $"{name} (未在问卷定义中找到)",
                    Order = int.MaxValue
                };
            }

            public StatKind GetKindHint()
            {
                if (Types.Count == 0)
                {
                    return StatKind.Unknown;
                }
                if (Types.Any(t => t is "file" or "signaturepad")) return StatKind.File;
                if (Types.Any(t => t == "matrix")) return StatKind.Matrix;
                if (Types.Any(t => t is "matrixdropdown" or "matrixdynamic" or "multipletext" or "paneldynamic")) return StatKind.Object;
                if (Types.Any(t => t is "text" or "comment")) return StatKind.Text;
                if (Types.Any(t => t is "checkbox" or "tagbox" or "ranking" or "imagepicker")) return StatKind.MultiChoice;
                if (Types.Any(t => t is "radiogroup" or "dropdown" or "rating" or "boolean")) return StatKind.SingleChoice;
                return StatKind.Unknown;
            }

            public string MapChoice(string raw) => ChoiceLabels.TryGetValue(raw, out var label) ? label : raw;

            public string MapFieldChoice(string field, string raw)
            {
                if (FieldChoiceLabels.TryGetValue(field, out var map) && map.TryGetValue(raw, out var label))
                {
                    return label;
                }
                return MapChoice(raw);
            }
        }

        private sealed class StatAccumulator
        {
            public QuestionDef Def { get; }
            public int AnsweredCount { get; private set; }
            public int MissingCount { get; private set; }
            public long TotalSelections { get; private set; }
            public long FileItems { get; private set; }
            public Dictionary<string, long> OptionCounts { get; } = new(StringComparer.Ordinal);
            public Dictionary<string, long> TextCounts { get; } = new(StringComparer.Ordinal);
            public Dictionary<string, Dictionary<string, long>> MatrixCounts { get; } = new(StringComparer.Ordinal);
            public Dictionary<string, Dictionary<string, long>> ObjectCounts { get; } = new(StringComparer.Ordinal);

            public StatAccumulator(QuestionDef def)
            {
                Def = def;
            }

            public void Add(JToken? token)
            {
                if (IsMissing(token))
                {
                    MissingCount++;
                    return;
                }
                AnsweredCount++;

                switch (Def.GetKindHint())
                {
                    case StatKind.File:
                        AddFile(token!);
                        break;
                    case StatKind.Matrix:
                        AddMatrix(token!);
                        break;
                    case StatKind.Object:
                        AddObject(token!);
                        break;
                    case StatKind.Text:
                        AddText(token!);
                        break;
                    case StatKind.MultiChoice:
                        AddMulti(token!);
                        break;
                    case StatKind.SingleChoice:
                        AddSingle(token!);
                        break;
                    default:
                        AddUnknown(token!);
                        break;
                }
            }

            public StatKind InferKind()
            {
                if (MatrixCounts.Count > 0) return StatKind.Matrix;
                if (ObjectCounts.Count > 0) return StatKind.Object;
                if (TextCounts.Count > 0 && OptionCounts.Count == 0) return StatKind.Text;
                if (TotalSelections > AnsweredCount) return StatKind.MultiChoice;
                if (OptionCounts.Count > 0) return StatKind.SingleChoice;
                return StatKind.Unknown;
            }

            private static bool IsMissing(JToken? token)
            {
                if (token is null || token.Type == JTokenType.Null || token.Type == JTokenType.Undefined)
                {
                    return true;
                }
                if (token.Type == JTokenType.String)
                {
                    return string.IsNullOrWhiteSpace(token.Value<string>());
                }
                if (token is JArray arr)
                {
                    return arr.Count == 0;
                }
                if (token is JObject obj)
                {
                    return !obj.Properties().Any();
                }
                return false;
            }

            private void AddUnknown(JToken token)
            {
                if (token is JArray) { AddMulti(token); return; }
                if (token is JObject) { AddObject(token); return; }
                if (token.Type == JTokenType.String) { AddText(token); return; }
                AddSingle(token);
            }

            private void AddSingle(JToken token)
            {
                if (token is JArray) { AddMulti(token); return; }
                if (token is JObject)
                {
                    Inc(OptionCounts, Trim(token.ToString(Formatting.None), 120));
                    return;
                }
                var raw = NormalizeScalar(token);
                Inc(OptionCounts, Def.MapChoice(raw));
            }
            private void AddMulti(JToken token)
            {
                if (token is not JArray arr)
                {
                    AddSingle(token);
                    return;
                }
                foreach (var item in arr.Where(i => !IsMissing(i)))
                {
                    var raw = item is JObject ? Trim(item.ToString(Formatting.None), 120) : NormalizeScalar(item!);
                    var val = item is JObject ? raw : Def.MapChoice(raw);
                    Inc(OptionCounts, val);
                    TotalSelections++;
                }
            }

            private void AddText(JToken token)
            {
                var text = token.Type == JTokenType.String ? (token.Value<string>() ?? string.Empty) : token.ToString(Formatting.None);
                text = text.Trim();
                if (string.IsNullOrWhiteSpace(text))
                {
                    AnsweredCount--;
                    MissingCount++;
                    return;
                }
                Inc(TextCounts, text);
            }

            private void AddMatrix(JToken token)
            {
                if (token is not JObject obj)
                {
                    AddUnknown(token);
                    return;
                }
                foreach (var row in obj.Properties())
                {
                    if (IsMissing(row.Value)) continue;
                    var rowName = Def.RowLabels.TryGetValue(row.Name, out var rowLabel) ? rowLabel : row.Name;
                    if (!MatrixCounts.TryGetValue(rowName, out var colMap))
                    {
                        colMap = new Dictionary<string, long>(StringComparer.Ordinal);
                        MatrixCounts[rowName] = colMap;
                    }

                    if (row.Value is JArray arr)
                    {
                        foreach (var v in arr.Where(i => !IsMissing(i)))
                        {
                            var raw = NormalizeScalar(v!);
                            Inc(colMap, Def.MapChoice(raw));
                        }
                    }
                    else
                    {
                        var raw = NormalizeScalar(row.Value);
                        var colName = Def.ColumnLabels.TryGetValue(raw, out var colLabel) ? colLabel : Def.MapChoice(raw);
                        Inc(colMap, colName);
                    }
                }
            }

            private void AddObject(JToken token)
            {
                if (token is JObject obj)
                {
                    var allValuesObject = obj.Properties().Any() && obj.Properties().All(p => p.Value is JObject);
                    if (allValuesObject)
                    {
                        foreach (var row in obj.Properties())
                        {
                            AddObjectFields((JObject)row.Value, row.Name);
                        }
                    }
                    else
                    {
                        AddObjectFields(obj, null);
                    }
                    return;
                }

                if (token is JArray arr)
                {
                    foreach (var item in arr.Where(i => !IsMissing(i)))
                    {
                        if (item is JObject rowObj)
                        {
                            AddObjectFields(rowObj, null);
                        }
                        else
                        {
                            IncObject("item", NormalizeScalar(item!));
                        }
                    }
                    return;
                }

                AddUnknown(token);
            }

            private void AddObjectFields(JObject obj, string? rowPrefix)
            {
                foreach (var prop in obj.Properties())
                {
                    if (IsMissing(prop.Value)) continue;

                    var fieldLabel = Def.FieldLabels.TryGetValue(prop.Name, out var fLabel)
                        ? fLabel
                        : (Def.ColumnLabels.TryGetValue(prop.Name, out var cLabel) ? cLabel : prop.Name);
                    var key = rowPrefix is null ? fieldLabel : $"{rowPrefix}.{fieldLabel}";

                    if (prop.Value is JArray arr)
                    {
                        foreach (var item in arr.Where(i => !IsMissing(i)))
                        {
                            var raw = NormalizeScalar(item!);
                            IncObject(key, Def.MapFieldChoice(prop.Name, raw));
                        }
                    }
                    else if (prop.Value is JObject nested)
                    {
                        IncObject(key, Trim(nested.ToString(Formatting.None), 120));
                    }
                    else
                    {
                        var raw = NormalizeScalar(prop.Value);
                        IncObject(key, Def.MapFieldChoice(prop.Name, raw));
                    }
                }
            }

            private void AddFile(JToken token)
            {
                if (token is JArray arr)
                {
                    FileItems += arr.Count;
                    Inc(OptionCounts, arr.Count > 0 ? "已上传" : "未上传");
                    return;
                }
                if (token.Type == JTokenType.String)
                {
                    var hasValue = !string.IsNullOrWhiteSpace(token.Value<string>());
                    if (hasValue) FileItems += 1;
                    Inc(OptionCounts, hasValue ? "已填写" : "未填写");
                    return;
                }
                FileItems += 1;
                Inc(OptionCounts, "已填写");
            }

            private void IncObject(string field, string value)
            {
                if (!ObjectCounts.TryGetValue(field, out var map))
                {
                    map = new Dictionary<string, long>(StringComparer.Ordinal);
                    ObjectCounts[field] = map;
                }
                Inc(map, value);
            }

            private static void Inc(Dictionary<string, long> map, string key)
            {
                if (map.TryGetValue(key, out var v))
                {
                    map[key] = v + 1;
                }
                else
                {
                    map[key] = 1;
                }
            }
        }
    }
}
