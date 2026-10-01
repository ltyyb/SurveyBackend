using MessageContext = Sisters.WudiLib.Posts.Message;

namespace SurveyBackend.Bot.Commands;
// new 指令
public class CreateSurveyCommand : AuthorizedAsyncCommand
{
    public override string CommandName => "new";

    public override string[] Aliases => ["create", "add"];

    public override string Description => "创建一个调查问卷";

    private readonly IServiceScopeFactory _serviceScopeFactory;
    public CreateSurveyCommand(IServiceScopeFactory dbScopeFactory) : base(dbScopeFactory)
    {
        _serviceScopeFactory = dbScopeFactory;
    }

    protected override async Task<CommandResponse?> ExecuteAuthorizedAsync(MessageContext context, string[] args, CancellationToken cancellationToken = default)
    {
        if (args.Length == 2)
        {
            var title = args[0];
            var description = args[1];
            var survey = new Survey
            {
                Title = title,
                Description = description
            };
            using var scope = _serviceScopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<MainDbContext>();
            db.Surveys.Add(survey);
            await db.SaveChangesAsync(cancellationToken);
            return CommandResponse.SuccessResponse($"""
                ✅ 问卷创建成功！
                问卷ID: {survey.SurveyId}
                标题: {survey.Title}
                描述: {survey.Description}

                请注意，问卷目前没有对应的 Questionnaire 版本，无法被用户访问。
                请私信使用 /survey qnew {survey.SurveyId} 来为该问卷创建一个版本。
                """);
        }
        else
        {
            var msg = """
            本命令将创建一个新的 Survey 对象并保存至数据库。
            请注意，Survey 对象 与 Questionnaire 对象不同。一个 Survey 对象代表一个调查项目的基本定义，而 Questionnaire 对象则代表一个具体的问卷版本。
            当多个 Questionnaire 指向同一个 Survey 对象时，在获取 Survey 对应的问卷题面时，将默认使用最新发布的 Questionnaire 版本。
            如希望指定该问卷为审核问卷，请创建获得 SurveyId 后使用 /survey setverify [SurveyId] 来设置。
            如希望指定该问卷为需投票众审问卷，请创建获得 SurveyId 后使用 /survey setreview [SurveyId] 来设置。
            ===================
            本命令必须带参使用。参数如包含空格应使用引号包裹。
            使用方法:
            /survey new [问卷标题] [问卷描述]
            例如:
            /survey new "我的新问卷" "新添加的问卷，仅供测试"
            """;
            return CommandResponse.SuccessResponse(msg);
        }
    }
}
