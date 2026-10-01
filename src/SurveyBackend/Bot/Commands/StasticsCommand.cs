using SurveyBackend.Services.Statistics;
using MessageContext = Sisters.WudiLib.Posts.Message;

namespace SurveyBackend.Bot.Commands;
// stastics 指令
public class StasticsCommand : AuthorizedAsyncCommand
{
    public override string CommandName => "stastics";
    public override string[] Aliases => ["stats"];
    public override string Description => "使用方法: /survey stastics [SurveyId | QuestionnaireId] [all | 问题names | page:页面names]\n 获取某个Survey或Questionnaire的统计数据。不带参使用以获取更多提示。";
    public override UserGroup[] RequiredPermission => [UserGroup.VerifiedUser, UserGroup.Admin, UserGroup.SuperAdmin];
    private readonly IServiceScopeFactory _serviceScopeFactory;
    public StasticsCommand(IServiceScopeFactory serviceScopeFactory) : base(serviceScopeFactory)
    {
        _serviceScopeFactory = serviceScopeFactory;
    }
    protected async override Task<CommandResponse?> ExecuteAuthorizedAsync(MessageContext context, string[] args, CancellationToken cancellationToken = default)
    {
        if (args.Length is 1 or 2)
        {
            using var scope = _serviceScopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<MainDbContext>();
            var id = args[0];
            var surveys = await db.Surveys.Where(s => EF.Functions.Like(
                                                        s.SurveyId,
                                                        id + "%"))
                                         .ToListAsync(cancellationToken);
            var questionnaires = await db.Questionnaires.Include(q => q.Survey)
                                                        .Where(q => EF.Functions.Like(
                                                            q.QuestionnaireId,
                                                            id + "%"))
                                                        .ToListAsync(cancellationToken);
            if (surveys.Count == 0 && questionnaires.Count == 0)
            {
                return CommandResponse.FailureResponse("❌ 无法找到匹配的 Survey 或 Questionnaire，请检查输入的 ID 是否正确，并尽可能提供更完整的 ID 以获得准确匹配。");
            }

            var matchedSurvey = surveys.SingleOrDefault(s => s.SurveyId == id);
            var matchedQuestionnaire = questionnaires.SingleOrDefault(q => q.QuestionnaireId == id);

            if (matchedSurvey is not null && matchedQuestionnaire is not null)
            {
                return CommandResponse.FailureResponse("❌ ID 同时匹配到了 Survey 与 Questionnaire，请提供更完整的 ID。");
            }

            if (matchedSurvey is null && matchedQuestionnaire is null)
            {
                if (surveys.Count > 1)
                {
                    return CommandResponse.FailureResponse("❌ 找到多个匹配的 Survey，请提供更完整的 ID。");
                }
                if (questionnaires.Count > 1)
                {
                    return CommandResponse.FailureResponse("❌ 找到多个匹配的 Questionnaire，请提供更完整的 ID。");
                }
                if (surveys.Count == 1 && questionnaires.Count == 0)
                {
                    matchedSurvey = surveys[0];
                }
                else if (questionnaires.Count == 1 && surveys.Count == 0)
                {
                    matchedQuestionnaire = questionnaires[0];
                }
                else
                {
                    return CommandResponse.FailureResponse("❌ 无法唯一确定目标对象，请提供更完整的 ID。");
                }
            }

            string[]? questionNames = null;
            string[]? pageNames = null;
            var usePageFilter = false;

            if (args.Length == 2 && !string.Equals(args[1], "all", StringComparison.OrdinalIgnoreCase))
            {
                var rawFilter = args[1].Trim();
                if (rawFilter.StartsWith("page:", StringComparison.OrdinalIgnoreCase) ||
                    rawFilter.StartsWith("pages:", StringComparison.OrdinalIgnoreCase) ||
                    rawFilter.StartsWith("p:", StringComparison.OrdinalIgnoreCase))
                {
                    var splitIndex = rawFilter.IndexOf(':');
                    var pageValue = splitIndex >= 0 ? rawFilter[(splitIndex + 1)..] : string.Empty;
                    pageNames = pageValue
                        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .ToArray();

                    if (pageNames.Length == 0)
                    {
                        return CommandResponse.FailureResponse("❌ 页面筛选器为空。请使用 page:页面1,页面2。");
                    }
                    usePageFilter = true;
                }
                else
                {
                    questionNames = rawFilter
                        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .ToArray();

                    if (questionNames.Length == 0)
                    {
                        return CommandResponse.FailureResponse("❌ 题目筛选器为空，请输入至少一个问题 name，或使用 all。");
                    }
                }
            }
            var tool = new SurveyStatisticsTool(db);
            string report;

            if (matchedSurvey is not null)
            {
                report = usePageFilter
                    ? await tool.BuildReportByPageNamesAsync(matchedSurvey, pageNames!, "zh-cn", cancellationToken)
                    : await tool.BuildReportAsync(matchedSurvey, questionNames, "zh-cn", cancellationToken);
            }
            else if (matchedQuestionnaire is not null)
            {
                report = usePageFilter
                    ? await tool.BuildReportByPageNamesAsync(matchedQuestionnaire, pageNames!, "zh-cn", cancellationToken)
                    : await tool.BuildReportAsync(matchedQuestionnaire, questionNames, "zh-cn", cancellationToken);
            }
            else
            {
                return CommandResponse.FailureResponse("❌ 未知错误：目标对象为空。");
            }

            return CommandResponse.SuccessResponse(report);
        }
        else
        {
            var msg = """
            参数不正确。
            本命令用于获取某个Survey或Questionnaire的统计数据。

            使用方法:
            /survey stastics [SurveyId | QuestionnaireId] [筛选的问题names]
            /survey stastics [SurveyId | QuestionnaireId] [all]
            /survey stastics [SurveyId | QuestionnaireId] [page:页面1,页面2]

            其中 SurveyId 或 QuestionnaireId 支持前缀匹配但应尽可能使用完整 ID 以获得准确匹配。
            该命令会返回该 Survey 或 Questionnaire 的问卷统计数据。

            你可以提供一个问题筛选器，用以指定你想要统计的题目。筛选器是一个逗号分隔的问题名称列表，应以引号包裹。例如 "问题1,问题2"。
            你也可以使用 all 表示统计所有可识别题目。
            你还可以使用 page:页面名列表 按页面统计其中所有题目。例如 "page:1,2,3"。
            page 过滤依据 SurveyJS 中 page.name 字段。
            问题的name以 SurveyJS 定义的 name 字段为准，你可以通过查看问卷的 JSON 定义来获取问题的 name。

            更多信息请查阅文档或联系管理员。
            """;
            return CommandResponse.SuccessResponse(msg);
        }
    }

}
