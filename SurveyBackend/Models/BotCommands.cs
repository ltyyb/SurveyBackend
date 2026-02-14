using System.Text;

using Message = Sisters.WudiLib.SendingMessage;
using MessageContext = Sisters.WudiLib.Posts.Message;
using SurveyBackend;
using Sisters.WudiLib.Posts;
using Microsoft.EntityFrameworkCore;
using Sisters.WudiLib;
namespace SurveyBackend.Models
{
    // start指令
    public class StartCommand : AsyncCommandHandlerBase
    {
        private readonly IConfiguration _configuration;
        private readonly IOnebotService _onebot;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<StartCommand> _logger;
        public StartCommand(IConfiguration configuration, IOnebotService onebot, IServiceScopeFactory scopeFactory, ILogger<StartCommand> logger)
        {
            _configuration = configuration;
            _onebot = onebot;
            _scopeFactory = scopeFactory;
            _logger = logger;
        }
        public override string CommandName => "start";
        public override string[] Aliases => ["entr"];
        public override string Description => "获取入群问卷链接，仅在审核群中且未填写过入群问卷的情况下有效。";
        public override async Task<CommandResponse?> ExecuteAsync(MessageContext context, string[] args, CancellationToken cancellationToken = default)
        {
            var verifyGroupId = _configuration["Bot:verifyGroupId"];
            var surveyLinkEndpoint = _configuration["API:SurveyLinkEndpoint"];
            // 统一端点格式
            surveyLinkEndpoint = string.IsNullOrEmpty(surveyLinkEndpoint) || surveyLinkEndpoint.EndsWith('/')
                                ? surveyLinkEndpoint
                                : surveyLinkEndpoint + "/";
            if (context is GroupMessage groupMessage && groupMessage.GroupId.ToString() == verifyGroupId)
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

    // trust 指令
    public class TrustCommand : AuthorizedAsyncCommand
    {
        public override string CommandName => "trust";

        public override string Description => "无参时将本群所有成员设置为 VerifiedUser。仅应在初始化数据库时使用且仅允许在 MainGroupId 群内使用。\n" +
        "带参时将指定用户设置为 VerifiedUser。参数为用户QQ号。";

        private readonly IConfiguration _configuration;
        private readonly IOnebotService _onebot;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<TrustCommand> _logger;
        public TrustCommand(IConfiguration configuration, IOnebotService onebot, IServiceScopeFactory scopeFactory, ILogger<TrustCommand> logger)
            : base(scopeFactory)
        {
            _configuration = configuration;
            _onebot = onebot;
            _scopeFactory = scopeFactory;
            _logger = logger;
        }


        protected override async Task<CommandResponse?> ExecuteAuthorizedAsync(MessageContext context, string[] args, CancellationToken cancellationToken = default)
        {
            if (context is GroupMessage groupMsg)
            {
                if (args.Length == 1)
                {
                    if (long.TryParse(args[0], out long userQQId))
                    {
                        var result = await TrustUser(userQQId, cancellationToken);
                        if (result)
                        {
                            return CommandResponse.SuccessResponse($"成功注册并配置用户 {userQQId}。");
                        }
                        else
                        {
                            return CommandResponse.FailureResponse($"尝试注册并配置用户 {userQQId} 时发生错误。请查看日志获取详细信息。");
                        }
                    }
                    else
                    {
                        return CommandResponse.FailureResponse("参数解析失败。请确保输入的参数为有效的QQ号。");
                    }
                }
                else if (args.Length == 0 && groupMsg.GroupId.ToString() == _configuration["Bot:mainGroupId"])
                {
                    try
                    {
                        if (_onebot.onebotApi is null || !_onebot.IsAvailable)
                        {
                            _logger.LogError("Onebot API 客户端未初始化");
                            return CommandResponse.FailureResponse("Onebot API 客户端未初始化，无法执行命令");
                        }
                        var members = await _onebot.onebotApi.GetGroupMemberListAsync(groupMsg.GroupId);
                        int successCount = 0;
                        int failCount = 0;
                        foreach (var member in members)
                        {
                            if (member.UserId == context.UserId)
                            {
                                // 跳过命令执行者，避免权限问题
                                continue;
                            }
                            var result = await TrustUser(member.UserId, cancellationToken);
                            if (result)
                            {
                                successCount++;
                            }
                            else
                            {
                                failCount++;
                            }
                        }
                        return CommandResponse.SuccessResponse($"成功注册并配置 {successCount} 个用户，失败 {failCount} 个用户。"
                        + "\n 查看日志获取详细信息。");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "尝试将群 {groupId} 中的用户注册到数据库中时发生错误", groupMsg.GroupId);
                        return CommandResponse.FailureResponse("尝试将群中用户注册到数据库中时发生错误:\n" + ex.Message);
                    }
                }
                else
                {
                    return CommandResponse.FailureResponse("❌ 不正确的用法。");
                }
            }
            else if (context is PrivateMessage)
            {
                if (args.Length == 1)
                {
                    if (long.TryParse(args[0], out long userQQId))
                    {
                        var result = await TrustUser(userQQId, cancellationToken);
                        if (result)
                        {
                            return CommandResponse.SuccessResponse($"成功注册并配置用户 {userQQId}。");
                        }
                        else
                        {
                            return CommandResponse.FailureResponse($"尝试注册并配置用户 {userQQId} 时发生错误。请查看日志获取详细信息。");
                        }
                    }
                    else
                    {
                        return CommandResponse.FailureResponse("参数解析失败。请确保输入的参数为有效的QQ号。");
                    }
                }
                else
                {
                    return CommandResponse.FailureResponse("❌ 不正确的用法。");
                }
            }
            return null;
        }
        private async Task<bool> TrustUser(long userQQId, CancellationToken cancellationToken)
        {
            try
            {
                _logger.LogInformation("正在将用户 {user} 注册到数据库中。", userQQId.ToString());
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<MainDbContext>();
                User? user = await db.Users.Where(u => u.QQId == userQQId.ToString())
                                            .SingleOrDefaultAsync(cancellationToken);

                if (user is null)
                {
                    // 用户不存在，创建新用户
                    user = new User
                    {
                        QQId = userQQId.ToString(),
                        UserGroup = UserGroup.VerifiedUser
                    };
                    db.Users.Add(user);
                    await db.SaveChangesAsync(cancellationToken);
                    _logger.LogInformation("用户 {qqId} 注册并设置为 VerifiedUser。", userQQId.ToString());
                }
                else if (user.UserGroup != UserGroup.VerifiedUser)
                {
                    // 用户已存在，更新用户组
                    user.UserGroup = UserGroup.VerifiedUser;
                    db.Users.Update(user);
                    await db.SaveChangesAsync(cancellationToken);
                    _logger.LogInformation("用户 {qqId} 已存在，更新为 VerifiedUser。", userQQId.ToString());
                }
                return true;

            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "尝试添加用户 {qqId} 时发生错误", userQQId.ToString());
                return false;
            }
        }
    }
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
    // qnew 指令
    public class CreateQuestionnaireCommand : AuthorizedAsyncCommand
    {
        public override string CommandName => "qnew";

        public override string Description => "为指定 Survey 创建一个新的 Questionnaire 版本, 仅私聊可用";

        private readonly IServiceScopeFactory _serviceScopeFactory;
        private readonly IConfiguration _configuration;
        public CreateQuestionnaireCommand(IServiceScopeFactory dbScopeFactory, IConfiguration configuration) : base(dbScopeFactory)
        {
            _serviceScopeFactory = dbScopeFactory;
            _configuration = configuration;
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
                var surveyLinkEndpoint = _configuration["API:SurveyLinkEndpoint"];
                // 统一端点格式
                surveyLinkEndpoint = string.IsNullOrEmpty(surveyLinkEndpoint) || surveyLinkEndpoint.EndsWith('/')
                                    ? surveyLinkEndpoint
                                    : surveyLinkEndpoint + "/";

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
    // vote 指令
    public class VoteCommand : AsyncCommandHandlerBase
    {
        private readonly IServiceScopeFactory _serviceScopeFactory;
        public VoteCommand(IServiceScopeFactory serviceScopeFactory)
        {
            _serviceScopeFactory = serviceScopeFactory;
        }
        public override string CommandName => "vote";

        public override string Description => "投票一个需要审核的问卷。使用方法: /survey vote [SubmissionId] [a/d]\n " +
        "其中 a 代表通过，d 代表拒绝。SubmissionId 可以简写为前8位，具体可参考审核推送消息。";

        public async override Task<CommandResponse?> ExecuteAsync(MessageContext context, string[] args, CancellationToken cancellationToken = default)
        {
            if (args.Length == 2)
            {
                var submissionIdInput = args[0];
                var voteInput = args[1].ToLower();
                if (voteInput != "a" && voteInput != "d")
                {
                    return CommandResponse.FailureResponse("❌ 无效的投票选项。请使用 'a' 代表通过，'d' 代表拒绝。");
                }
                var isApprove = voteInput == "a";

                using var scope = _serviceScopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<MainDbContext>();
                var reviewSubmissionData = await db.ReviewSubmissions.Include(r => r.Submission)
                                                   .Where(s => s.Submission.SubmissionId.StartsWith(submissionIdInput))
                                                   .SingleOrDefaultAsync(cancellationToken);
                if (reviewSubmissionData is null)
                {
                    return CommandResponse.FailureResponse("❌ 无法找到对应的提交。请检查输入的 SubmissionId 是否正确。");
                }
                var submission = reviewSubmissionData.Submission;
                if (submission.IsDisabled)
                {
                    return CommandResponse.FailureResponse("❌ 该提交已被禁用，无法投票。");
                }
                if (reviewSubmissionData.Status != ReviewStatus.Pending)
                {
                    return CommandResponse.FailureResponse("❌ 该提交已审核完毕，无法投票。");
                }
                var existingVote = await db.ReviewVotes.Include(v => v.User)
                                                       .Where(v => v.ReviewSubmissionDataId == reviewSubmissionData.ReviewSubmissionDataId
                                                                && v.User.QQId == context.UserId.ToString())
                                                       .SingleOrDefaultAsync(cancellationToken);
                if (existingVote is not null)
                {
                    existingVote.VoteType = isApprove ? VoteType.Upvote : VoteType.Downvote;
                    db.ReviewVotes.Update(existingVote);
                    await db.SaveChangesAsync(cancellationToken);
                    return CommandResponse.SuccessResponse("✅ 已更新您的投票。");
                }
                else
                {
                    var user = await db.Users.Where(u => u.QQId == context.UserId.ToString())
                                             .SingleOrDefaultAsync(cancellationToken);
                    if (user is null)
                    {
                        return CommandResponse.FailureResponse("❌ 无法找到您的用户信息，请确保您已加入正确用户组。如有疑问请联系管理员。");
                    }
                    if (user.UserGroup != UserGroup.VerifiedUser && user.UserGroup != UserGroup.Admin && user.UserGroup != UserGroup.SuperAdmin)
                    {
                        return CommandResponse.FailureResponse("❌ 您没有权限投票。请确保您已加入正确用户组。如有疑问请联系管理员。");
                    }
                    var vote = new ReviewVote
                    {
                        ReviewSubmissionData = reviewSubmissionData,
                        User = user,
                        VoteType = isApprove ? VoteType.Upvote : VoteType.Downvote
                    };
                    db.ReviewVotes.Add(vote);
                    await db.SaveChangesAsync(cancellationToken);
                    return CommandResponse.SuccessResponse("✅ 已记录您的投票。");
                }
            }
            else
            {
                var msg = """
                参数不正确。
                本命令用于投票一个需要审核的问卷提交。你只能为审核群中待审核的提交投票。
                使用方法:
                /survey vote [SubmissionId] [a/d]

                其中 SubmissionId 是提交的 ID，可以简写为前8位，具体可参考审核推送消息。
                [a/d] 代表你的投票选项，a 代表通过，d 代表拒绝。

                例如:
                /survey vote abcdef12 a
                """;
                return CommandResponse.SuccessResponse(msg);
            }
        }
    }
    // SetUser 指令
    public class SetUserCommand : AuthorizedAsyncCommand
    {
        public override string CommandName => "setuser";

        public override string Description => """
                                              设置指定用户的权限组。
                                              用法:
                                              /survey setuser [QQ号] [权限组]
                                              不带参使用以查看详细用法。
                                              """;

        private readonly IServiceScopeFactory _serviceScopeFactory;
        private readonly ILogger<SetUserCommand> _logger;
        public SetUserCommand(IServiceScopeFactory serviceScopeFactory, ILogger<SetUserCommand> logger) : base(serviceScopeFactory)
        {
            _serviceScopeFactory = serviceScopeFactory;
            _logger = logger;
        }

        protected override async Task<CommandResponse?> ExecuteAuthorizedAsync(MessageContext context, string[] args, CancellationToken cancellationToken = default)
        {
            if (args.Length == 2)
            {
                var qqIdInput = args[0];
                var userGroupInput = args[1];
                if (!long.TryParse(qqIdInput, out long qqId))
                {
                    return CommandResponse.FailureResponse("❌ 无效的QQ号。请确保输入的QQ号为数字。");
                }
                if (!Enum.TryParse(userGroupInput, true, out UserGroup userGroup) || !Enum.IsDefined(typeof(UserGroup), userGroup))
                {
                    return CommandResponse.FailureResponse("❌ 无效的用户组。请使用 VerifiedUser, NewComer, PendingUser, Admin 或 SuperAdmin。");
                }
                using var scope = _serviceScopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<MainDbContext>();
                var user = await db.Users.Where(u => u.QQId == qqIdInput)
                                         .SingleOrDefaultAsync(cancellationToken);
                if (user is null)
                {
                    return CommandResponse.FailureResponse("❌ 无法找到对应的用户。请检查输入的QQ号是否正确。");
                }
                user.UserGroup = userGroup;
                db.Users.Update(user);
                await db.SaveChangesAsync(cancellationToken);
                return CommandResponse.SuccessResponse($"✅ 已将用户 {qqIdInput} 的用户组设置为 {userGroup}。");
            }
            else
            {
                var msg = """
                参数不正确。
                本命令用于设置指定用户的权限组。
                使用方法:
                /survey setuser [QQ号] [权限组]

                其中 [权限组] 可选值为 NewComer, PendingUser, VerifiedUser, Admin 或 SuperAdmin, 或他们的值:
                NewComer = 0,
                PendingUser = 1,
                VerifiedUser = 2,
                Admin = 99,
                SuperAdmin = 100

                例如:
                /survey setuser 123456789 VerifiedUser
                /survey setuser 123456789 2
                """;
                return CommandResponse.SuccessResponse(msg);
            }
        }
    }
    // get 指令
    public class GetCommand : AuthorizedAsyncCommand
    {
        public override string CommandName => "get";

        public override string Description => "获取指定问卷的填写链接。使用方法: /survey get [SurveyId]\n" +
                                              "请注意: 仅已验证的用户可以使用此指令。应使用 /survey start 获取入群问卷链接。";
        public override UserGroup[] RequiredPermission => [UserGroup.VerifiedUser, UserGroup.Admin, UserGroup.SuperAdmin];
        private readonly IServiceScopeFactory _serviceScopeFactory;
        private readonly IConfiguration _configuration;
        public GetCommand(IServiceScopeFactory serviceScopeFactory, IConfiguration configuration) : base(serviceScopeFactory)
        {
            _serviceScopeFactory = serviceScopeFactory;
            _configuration = configuration;
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
                    var surveyLinkEndpoint = _configuration["API:SurveyLinkEndpoint"];
                    // 统一端点格式
                    surveyLinkEndpoint = string.IsNullOrEmpty(surveyLinkEndpoint) || surveyLinkEndpoint.EndsWith('/')
                                        ? surveyLinkEndpoint
                                        : surveyLinkEndpoint + "/";
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
    // disable 指令
    public class DisableSubmissionCommand : AuthorizedAsyncCommand
    {
        public override string CommandName => "disable";

        public override string Description => "禁用一个自己的提交，使其无法再被审核。使用方法: /survey disable [SubmissionId] \n" +
                                              "其中 SubmissionId 可以简写为前8位，本命令仅已审核用户可用。\n" + 
                                              "留空SubmissionId将关闭入群问卷的审核权限。";
        public override UserGroup[] RequiredPermission => [UserGroup.VerifiedUser, UserGroup.Admin, UserGroup.SuperAdmin];

        private readonly IServiceScopeFactory _serviceScopeFactory;
        public DisableSubmissionCommand(IServiceScopeFactory serviceScopeFactory) : base(serviceScopeFactory)
        {
            _serviceScopeFactory = serviceScopeFactory;
        }

        protected async override Task<CommandResponse?> ExecuteAuthorizedAsync(MessageContext context, string[] args, CancellationToken cancellationToken = default)
        {
            if (args.Length == 1)
            {
                var submissionIdInput = args[0];
                using var scope = _serviceScopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<MainDbContext>();
                var user = await db.Users.Where(u => u.QQId == context.UserId.ToString())
                                         .SingleOrDefaultAsync(cancellationToken);
                if (user is null)
                {
                    return CommandResponse.FailureResponse("❌ 无法找到您的用户信息，请确保您已正确注册。这是一个wtf错误，如有疑问请联系管理员。");
                }

                Submission? submission;
                if (user.UserGroup == UserGroup.SuperAdmin || user.UserGroup == UserGroup.Admin)
                {
                    submission = await db.Submissions
                                                    .Include(s => s.User)
                                                    .Where(s => s.SubmissionId.StartsWith(submissionIdInput))
                                                    .SingleOrDefaultAsync(cancellationToken);
                }
                else
                {
                    submission = await db.Submissions
                                                    .Include(s => s.User)
                                                    .Where(s => s.SubmissionId.StartsWith(submissionIdInput)
                                                            && s.UserId == user.UserId)
                                                    .SingleOrDefaultAsync(cancellationToken);
                }
                if (submission is null)
                {
                    return CommandResponse.FailureResponse("❌ 无法找到对应的提交。请检查输入的 SubmissionId 是否正确。");
                }
                if (submission.IsDisabled)
                {
                    return CommandResponse.FailureResponse("❌ 该提交已被禁用，无需重复操作。");
                }
                submission.IsDisabled = true;
                db.Submissions.Update(submission);
                await db.SaveChangesAsync(cancellationToken);
                return CommandResponse.SuccessResponse($"✅ 已成功禁用该提交: {submission.SubmissionId} by {submission.User.QQId}");
            }
            else if (args.Length == 0)
            {
                using var scope = _serviceScopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<MainDbContext>();
                var submissions = await db.Submissions.Include(s => s.Questionnaire)
                                                        .ThenInclude(q => q.Survey)
                                                      .Include(s => s.User)
                                                      .Where(s => s.IsDisabled == false
                                                        && s.Questionnaire.Survey.IsVerifySurvey == true
                                                        && s.User.QQId == context.UserId.ToString())
                                                      .ToListAsync(cancellationToken);
                var sb = new StringBuilder();
                sb.AppendLine($"发现 {submissions.Count} 个您的入群问卷提交");
                foreach (var submission in submissions)
                {
                    submission.IsDisabled = true;
                    db.Submissions.Update(submission);
                    sb.AppendLine($"✅ 已禁用提交 ID: {submission.SubmissionId}");
                }
                await db.SaveChangesAsync(cancellationToken);
                sb.AppendLine("已提交的更改。");
                return CommandResponse.SuccessResponse(sb.ToString());
            }
            else
            {
                var msg = """
                参数不正确。
                本命令用于禁用一个提交，使其无法再被公开获取回答或审核。
                使用方法:
                /survey disable [SubmissionId]

                其中 SubmissionId 是提交的 ID，可以简写为前8位，具体可参考审核推送消息。
                留空SubmissionId将关闭入群问卷的审核权限。

                例如:
                /survey disable abcdef12
                /survey disable
                """;
                return CommandResponse.SuccessResponse(msg);
            }
        }
    }
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
    // setreview 指令
    public class SetReviewCommand : AuthorizedAsyncCommand
    {
        public override string CommandName => "setreview";

        public override string Description => "将一个 Survey 设置为需投票众审问卷。使用方法: /survey setreview [SurveyId] \n" +
                                              "需投票众审问卷的提交将开放众审和投票，但不设置为审核问卷和唯一性。";

        private readonly IServiceScopeFactory _serviceScopeFactory;
        public SetReviewCommand(IServiceScopeFactory serviceScopeFactory) : base(serviceScopeFactory)
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
                survey.NeedReview = true;
                db.Surveys.Update(survey);
                await db.SaveChangesAsync(cancellationToken);
                return CommandResponse.SuccessResponse($"✅ 已将 Survey {surveyId} 设置为需投票众审问卷。");
            }
            else
            {
                var msg = """
                参数不正确。
                本命令用于将一个 Survey 设置为需投票众审问卷。
                需投票众审问卷的提交将开放众审和投票，但不设置为审核问卷和唯一性。
                使用方法:
                /survey setreview [SurveyId]

                例如:
                /survey setreview abcdef12
                """;
                return CommandResponse.SuccessResponse(msg);
            }
        }
    }
}
