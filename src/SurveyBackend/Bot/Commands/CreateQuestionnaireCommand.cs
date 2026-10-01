using Sisters.WudiLib.Posts;
using MessageContext = Sisters.WudiLib.Posts.Message;
using Request = SurveyBackend.Models.Request;

namespace SurveyBackend.Bot.Commands;
// qnew 指令
public class CreateQuestionnaireCommand : AuthorizedAsyncCommand
{
    public override string CommandName => "qnew";

    public override string Description => "为指定 Survey 创建一个新的 Questionnaire 版本, 仅私聊可用";

    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly ApiOptions _apiOptions;
    public CreateQuestionnaireCommand(IServiceScopeFactory dbScopeFactory, IOptions<ApiOptions> apiOptions) : base(dbScopeFactory)
    {
        _serviceScopeFactory = dbScopeFactory;
        _apiOptions = apiOptions.Value;
    }

    protected override async Task<CommandResponse?> ExecuteAuthorizedAsync(MessageContext context, string[] args, CancellationToken cancellationToken = default)
    {
        if (context is not PrivateMessage)
        {
            return CommandResponse.FailureResponse("❌ 出于安全考虑，本命令仅可在私聊中使用。");
        }
        if (args.Length == 1)
        {
            var surveyId = args[0];
            using var scope = _serviceScopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<MainDbContext>();
            var survey = await db.Surveys.Where(s => s.SurveyId == surveyId)
                                         .SingleOrDefaultAsync(cancellationToken);
            if (survey is null)
            {
                return CommandResponse.FailureResponse("❌ 无法找到这个 Survey，请检查输入的 SurveyId 是否正确。");
            }
            var user = await db.Users.Where(u => u.QQId == context.UserId.ToString())
                                      .SingleOrDefaultAsync(cancellationToken);
            if (user is null)
            {
                return CommandResponse.FailureResponse("❌ 无法找到您的用户信息，请检查数据库。");
            }
            var request = new Request
            {
                RequestType = RequestType.QuestionnaireCreate,
                IsDisabled = false,
                User = user,
            };
            db.Requests.Add(request);
            await db.SaveChangesAsync(cancellationToken);
            var surveyLinkEndpoint = _apiOptions.SurveyLinkBase;

            var link = $"{surveyLinkEndpoint}actions/uploadQuestionnaire?surveyId={surveyId}&requestId={request.RequestId}";
            return CommandResponse.SuccessResponse($"""
                ✅ 问卷版本创建请求已生成！
                请访问以下链接来编辑问卷题面:
                {link}

                请勿泄露链接，该链接2小时内有效。
                在编辑完成并发布后，你将在此看到回执。
                """);
        }
        else
        {
            var msg = """
            本命令将为指定 Survey 创建一个新的 Questionnaire 版本。
            你需要先使用 /survey new 创建一个 Survey 对象来获取 SurveyId，才能使用本命令创建 Questionnaire 版本。
            ===================
            本命令必须带参使用。参数如包含空格应使用引号包裹。
            使用方法:
            /survey qnew [SurveyId]
            """;
            return CommandResponse.SuccessResponse(msg);
        }
    }
}
