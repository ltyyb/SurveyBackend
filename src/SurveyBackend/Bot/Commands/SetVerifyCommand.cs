using MessageContext = Sisters.WudiLib.Posts.Message;

namespace SurveyBackend.Bot.Commands;
// setverify 指令
public class SetVerifyCommand : AuthorizedAsyncCommand
{
    public override string CommandName => "setverify";

    public override string Description => "将一个 Survey 设置为审核问卷。使用方法: /survey setverify [SurveyId] \n" +
                                          "审核问卷的提交将开放众审和投票。";

    private readonly IServiceScopeFactory _serviceScopeFactory;
    public SetVerifyCommand(IServiceScopeFactory serviceScopeFactory) : base(serviceScopeFactory)
    {
        _serviceScopeFactory = serviceScopeFactory;
    }

    protected async override Task<CommandResponse?> ExecuteAuthorizedAsync(MessageContext context, string[] args, CancellationToken cancellationToken = default)
    {
        if (args.Length == 1)
        {
            var surveyId = args[0];
            using var scope = _serviceScopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<MainDbContext>();
            var survey = await db.Surveys.Where(s => s.SurveyId == surveyId)
                                         .SingleOrDefaultAsync(cancellationToken);
            if (survey is null)
            {
                return CommandResponse.FailureResponse("❌ 无法找到对应的 Survey，请检查输入的 SurveyId 是否正确。");
            }
            survey.IsVerifySurvey = true;
            survey.NeedReview = true;
            survey.UniquePerUser = true;
            db.Surveys.Update(survey);
            await db.SaveChangesAsync(cancellationToken);
            return CommandResponse.SuccessResponse($"✅ 已将 Survey {surveyId} 设置为审核问卷。");
        }
        else
        {
            var msg = """
            参数不正确。
            本命令用于将一个 Survey 设置为审核问卷。
            审核问卷的提交将开放众审和投票。
            使用方法:
            /survey setverify [SurveyId]

            例如:
            /survey setverify abcdef12
            """;
            return CommandResponse.SuccessResponse(msg);
        }
    }
}
