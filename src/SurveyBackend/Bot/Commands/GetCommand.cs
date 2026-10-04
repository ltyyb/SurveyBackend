using MessageContext = Sisters.WudiLib.Posts.Message;
using Request = SurveyBackend.Models.Request;

namespace SurveyBackend.Bot.Commands;
// get 指令
public class GetCommand : AuthorizedAsyncCommand
{
    public override string CommandName => "get";

    public override string Description => "获取指定问卷的填写链接。使用方法: /survey get [SurveyId]\n" +
                                          "请注意: 仅已验证的用户可以使用此指令。应使用 /survey start 获取入群问卷链接。";
    public override UserGroup[] RequiredPermission => [UserGroup.VerifiedUser, UserGroup.Admin, UserGroup.SuperAdmin];
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly ApiOptions _apiOptions;
    public GetCommand(IServiceScopeFactory serviceScopeFactory, IOptions<ApiOptions> apiOptions) : base(serviceScopeFactory)
    {
        _serviceScopeFactory = serviceScopeFactory;
        _apiOptions = apiOptions.Value;
    }
    protected async override Task<CommandResponse?> ExecuteAuthorizedAsync(MessageContext context, string[] args, CancellationToken cancellationToken = default)
    {
        if (args.Length == 1)
        {
            var surveyId = args[0];
            using var scope = _serviceScopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<MainDbContext>();
            var questionnaire = await db.Questionnaires.Include(q => q.Survey)
                                                       .Where(q => q.Survey.SurveyId == surveyId)
                                                       .ToListAsync(cancellationToken);
            if (questionnaire.Count == 0)
            {
                return CommandResponse.FailureResponse("❌ 无法找到对应的问卷。请检查输入的 SurveyId 是否正确。");
            }
            else
            {
                var latestQuestionnaire = questionnaire.MaxBy(q => q.ReleaseDate)!;
                if (latestQuestionnaire.Survey.UniquePerUser)
                {
                    var existingSubmission = await db.Submissions.Include(s => s.User)
                                                                 .Include(s => s.Questionnaire)
                                                                 .Where(s => s.User.QQId == context.UserId.ToString()
                                                                          && s.Questionnaire.SurveyId == latestQuestionnaire.SurveyId)
                                                                 .SingleOrDefaultAsync(cancellationToken);
                    if (existingSubmission is not null)
                    {
                        return CommandResponse.FailureResponse($"❌ 该问卷仅允许一次提交，您已有提交记录了哦~\n已有的提交ID: {existingSubmission.SubmissionId}\n如果需要重新获取链接，请联系管理员。");
                    }
                }
                var surveyLinkEndpoint = _apiOptions.SurveyLinkBase;
                var user = await db.Users.Where(u => u.QQId == context.UserId.ToString())
                                         .SingleOrDefaultAsync(cancellationToken);
                if (user is null)
                {
                    return CommandResponse.FailureResponse("❌ 无法找到您的用户信息，请确保您已正确注册。这是一个wtf错误，如有疑问请联系管理员。");
                }
                var link = await GenerateSurveyLinkForUserAsync(user, latestQuestionnaire, surveyLinkEndpoint, cancellationToken);
                return CommandResponse.SuccessResponse($"""
                    ✅ 获取链接成功！
                    问卷标题: {latestQuestionnaire.Survey.Title}
                    问卷描述: {latestQuestionnaire.Survey.Description}
                    问卷链接: {link}

                    请注意，问卷链接2小时内有效。
                    请勿更改问卷url中的任何内容。
                    """);
            }
        }
        else
        {
            var msg = """
            参数不正确。
            本命令用于获取指定问卷的填写链接。
            本命令不推荐用于获取入群问卷链接，入群问卷链接应通过 /survey start 获取。
            使用方法:
            /survey get [SurveyId]

            例如:
            /survey get abcdef12
            """;
            return CommandResponse.SuccessResponse(msg);
        }
    }
    private async Task<string> GenerateSurveyLinkForUserAsync(User user, Questionnaire questionnaire,
                                                              string? surveyLinkEndpoint, CancellationToken cancellationToken)
    {
        using var scope = _serviceScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MainDbContext>();
        var lastRequest = await db.Requests.Include(r => r.User)
                                           .Where(r => r.UserId == user.UserId
                                            && r.RequestType == RequestType.SurveyAccess
                                            && !r.IsDisabled).ToListAsync(cancellationToken);
        if (lastRequest.Count > 0)
        {
            foreach (var r in lastRequest)
            {
                r.IsDisabled = true;
            }
        }

        db.Users.Attach(user);
        var request = new Request
        {
            User = user,
            RequestType = RequestType.SurveyAccess
        };
        db.Requests.Add(request);
        await db.SaveChangesAsync(cancellationToken);

        var link = $"{surveyLinkEndpoint}?questionnaireId={questionnaire.QuestionnaireId}&requestId={request.RequestId}";
        return link;
    }
}
