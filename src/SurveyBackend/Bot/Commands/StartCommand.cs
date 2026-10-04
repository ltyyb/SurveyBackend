using System.Text;
using Sisters.WudiLib.Posts;
using Message = Sisters.WudiLib.SendingMessage;
using MessageContext = Sisters.WudiLib.Posts.Message;
using Request = SurveyBackend.Models.Request;

namespace SurveyBackend.Bot.Commands;

// start指令
public class StartCommand : AsyncCommandHandlerBase
{
    private readonly BotOptions _botOptions;
    private readonly ApiOptions _apiOptions;
    private readonly IOnebotService _onebot;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<StartCommand> _logger;
    public StartCommand(IOptions<BotOptions> botOptions, IOptions<ApiOptions> apiOptions, IOnebotService onebot, IServiceScopeFactory scopeFactory, ILogger<StartCommand> logger)
    {
        _botOptions = botOptions.Value;
        _apiOptions = apiOptions.Value;
        _onebot = onebot;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }
    public override string CommandName => "start";
    public override string[] Aliases => ["entr"];
    public override string Description => "获取入群问卷链接，仅在审核群中且未填写过入群问卷的情况下有效。";
    public override async Task<CommandResponse?> ExecuteAsync(MessageContext context, string[] args, CancellationToken cancellationToken = default)
    {
        var surveyLinkEndpoint = _apiOptions.SurveyLinkBase;
        if (context is GroupMessage groupMessage && groupMessage.GroupId == _botOptions.VerifyGroupId)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<MainDbContext>();
            var user = await db.Users
                               .Where(u => u.QQId == context.UserId.ToString())
                               .SingleOrDefaultAsync(cancellationToken);
            if (user is null)
            {
                _logger.LogInformation("用户 {qqId} 尚未注册，开始注册流程。", context.UserId);
                var registerResult = await RegisterUser(context.UserId, cancellationToken);
                if (registerResult.isSucc && registerResult.user is not null)
                {
                    _logger.LogInformation("用户 {qqId} 注册成功", context.UserId);
                    user = registerResult.user;
                }
                else
                {
                    _logger.LogError("用户 {qqId} 注册失败: {err}", context.UserId, registerResult.err);
                    await _onebot.ReplyMessageWithAtAsync(context, $"注册用户时出现异常。请@管理员。\n 原因: " + registerResult.err);
                    return CommandResponse.FailureResponse($"注册用户时出现异常。请@管理员。\n 原因: " + registerResult.err);
                }
            }
            if (user.UserGroup != UserGroup.NewComer)
            {
                return CommandResponse.FailureResponse("您已通过审核或为待定身份组，无需重复填写问卷。");
            }

            var verifySurveys = await db.Surveys
                                        .Where(s => s.IsVerifySurvey)
                                        .ToListAsync(cancellationToken);
            if (verifySurveys.Count == 0)
            {
                _logger.LogError("未找到任何用于审核的问卷。请先在数据库中添加一份 IsVerifySurvey = true 的问卷。");
                return CommandResponse.FailureResponse("系统未配置审核问卷，请联系管理员。");
            }
            else if (verifySurveys.Count == 1)
            {
                var questionnaires = await db.Questionnaires
                                            .Where(q => q.SurveyId == verifySurveys[0].SurveyId)
                                            .ToListAsync(cancellationToken);
                // 选择最新发布的问卷
                var questionnaire = questionnaires
                                    .MaxBy(q => q.ReleaseDate)!;
                var surveyLink = await GenerateSurveyLinkForUserAsync(user, questionnaire, surveyLinkEndpoint, cancellationToken);
                var message = $"""

                    o(*￣▽￣*)ブ 你的问卷链接制作完成啦~
                    请访问链接下方链接:

                    {surveyLink}

                    完成问卷~
                    如复制到浏览器中访问，请务必确保链接完整。请不要修改链接任何内容。
                    请注意, 此链接2小时内有效哦。
                    如果您在1小时以内获取过问卷, 则该链接继承上一链接有效期。

                    ⚠ 请注意看清链接所属用户，请勿填写他人问卷链接。
                    你知道吗？每份问卷链接都对应唯一用户哦~
                    你将稍后在问卷中确认你的QQ号ヾ(•ω•`)o
                    本消息对应用户:
                    """;
                var atMessage = Message.At(context.UserId);
                return CommandResponse.SuccessResponse(new Message(message) + atMessage);
            }
            else if (args.Length == 1
                    && int.TryParse(args[0], out int index)
                    && index >= 1 && index <= verifySurveys.Count)
            {
                var selectedSurvey = verifySurveys[index - 1];
                var questionnaires = await db.Questionnaires
                                            .Where(q => q.SurveyId == selectedSurvey.SurveyId)
                                            .ToListAsync(cancellationToken);
                // 选择最新发布的问卷
                var questionnaire = questionnaires
                                    .MaxBy(q => q.ReleaseDate)!;
                var surveyLink = await GenerateSurveyLinkForUserAsync(user, questionnaire, surveyLinkEndpoint, cancellationToken);
                var message = $"""

                    o(*￣▽￣*)ブ 你的问卷链接制作完成啦~
                    请访问链接下方链接:

                    {surveyLink}

                    完成问卷~
                    如复制到浏览器中访问，请务必确保链接完整。请不要修改链接任何内容。
                    请注意, 此链接2小时内有效哦。
                    如果您在1小时以内获取过问卷, 则该链接继承上一链接有效期。

                    (/▽＼) 您选择的问卷为 "{selectedSurvey.Title}"
                    请注意核对~

                    ⚠ 请注意看清链接所属用户，请勿填写他人问卷链接。
                    你知道吗？每份问卷链接都对应唯一用户哦~
                    你将稍后在问卷中确认你的QQ号ヾ(•ω•`)o
                    本消息对应用户:
                    """;
                var atMessage = Message.At(context.UserId);
                return CommandResponse.SuccessResponse(new Message(message) + atMessage);
            }
            else
            {
                string additionalHelp = "六同学生请选择本校问卷，非六同学生(含东渡校区学生)请选择非本校区问卷。";

                var sb = new StringBuilder();
                sb.AppendLine($"数据库中存在 {verifySurveys.Count} 份用于审核的问卷。");
                sb.AppendLine("请参考问卷标题及描述选择最符合您情况的问卷:\n=================");
                for (int i = 0; i < verifySurveys.Count; i++)
                {
                    sb.AppendLine($"[{i + 1}] {verifySurveys[i].Title} ");
                    sb.AppendLine($"  |描述: {verifySurveys[i].Description}");
                    sb.AppendLine($"  |使用指令 /survey start {i + 1} 来选择此问卷");
                }
                if (!string.IsNullOrEmpty(additionalHelp))
                {
                    sb.AppendLine("=================");
                    sb.AppendLine(additionalHelp);
                }
                return CommandResponse.SuccessResponse(sb.ToString());
            }

        }
        return null;
    }

    private async Task<string> GenerateSurveyLinkForUserAsync(User user, Questionnaire questionnaire,
                                                              string? surveyLinkEndpoint, CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MainDbContext>();
        var lastRequest = await db.Requests.Where(r => r.User.UserId == user.UserId
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

    private async Task<(bool isSucc, string? err, User? user)> RegisterUser(long qqId, CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<MainDbContext>();
            var existingUser = await db.Users.SingleOrDefaultAsync(u => u.QQId == qqId.ToString(), cancellationToken);
            if (existingUser is not null)
            {
                return (true, null, existingUser);
            }
            var user = new User { QQId = qqId.ToString() };
            db.Users.Add(user);
            await db.SaveChangesAsync(cancellationToken);
            return (true, null, user);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "注册用户时发生错误");
            return (false, ex.Message, null);
        }

    }
}
