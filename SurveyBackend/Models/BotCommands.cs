using System.Text;

using Message = Sisters.WudiLib.SendingMessage;
using MessageContext = Sisters.WudiLib.Posts.Message;
using SurveyBackend;
using Sisters.WudiLib.Posts;
using Microsoft.EntityFrameworkCore;
using Sisters.WudiLib;
namespace SurveyBackend.Models
{
    public class StartCommand : AsyncCommandHandlerBase
    {
        private readonly IConfiguration _configuration;
        private readonly IOnebotService _onebot;
        private readonly MainDbContext _db;
        private readonly ILogger<StartCommand> _logger;
        public StartCommand(IConfiguration configuration, IOnebotService onebot, MainDbContext db, ILogger<StartCommand> logger)
        {
            _configuration = configuration;
            _onebot = onebot;
            _db = db;
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
                var user = await _db.Users
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
                        await _onebot.ReplyMessageWithAt(context, $"注册用户时出现异常。请@管理员。\n 原因: " + registerResult.err);
                        return CommandResponse.FailureResponse($"注册用户时出现异常。请@管理员。\n 原因: " + registerResult.err);
                    }
                }
                if (user.UserGroup != UserGroup.NewComer)
                {
                    return CommandResponse.FailureResponse("您已通过审核或为待定身份组，无需重复填写问卷。");
                }

                // 获取 verifyQuestionnaire
                var verifyQuestionnaires = await _db.Questionnaires
                                                .Where(q => q.IsVerifyQuestionnaire)
                                                .ToListAsync(cancellationToken);
                if (verifyQuestionnaires.Count == 0)
                {
                    _logger.LogError("未找到任何用于审核的问卷。请先在数据库中添加一份 IsVerifyQuestionnaire = true 的问卷。");
                    return CommandResponse.FailureResponse("系统未配置审核问卷，请联系管理员。");
                }
                // 选择最新发布的问卷
                var verifyQuestionnaire = verifyQuestionnaires
                                          .MaxBy(q => q.ReleaseDate)!;
                var surveyLink = await GenerateSurveyLinkForUserAsync(user, verifyQuestionnaire, surveyLinkEndpoint, cancellationToken);
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
            return null;
        }

        private async Task<string> GenerateSurveyLinkForUserAsync(User user, Questionnaire questionnaire, string? surveyLinkEndpoint, CancellationToken cancellationToken)
        {
            var lastRequest = await _db.Requests.Where(r => r.User.UserId == user.UserId
                                                && r.RequestType == RequestType.SurveyAccess
                                                && !r.IsDisabled).ToListAsync(cancellationToken);
            if (lastRequest.Count > 0)
            {
                foreach (var r in lastRequest)
                {
                    r.IsDisabled = true;
                }
            }

            var request = new Models.Request(user, RequestType.SurveyAccess);
            _db.Requests.Add(request);
            await _db.SaveChangesAsync(cancellationToken);

            var link = $"{surveyLinkEndpoint}{questionnaire.QuestionnaireId}?requestId={request.RequestId}";
            return link;
        }

        private async Task<(bool isSucc, string? err, User? user)> RegisterUser(long qqId, CancellationToken cancellationToken)
        {
            try
            {
                var existingUser = await _db.Users.SingleOrDefaultAsync(u => u.QQId == qqId.ToString(), cancellationToken);
                if (existingUser is not null)
                {
                    return (true, null, existingUser);
                }
                var user = new User(qqId.ToString());
                _db.Users.Add(user);
                await _db.SaveChangesAsync(cancellationToken);
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

        public override string Description => "将本群所有成员设置为 VerifiedUser。仅应在初始化数据库时使用";

        private readonly IConfiguration _configuration;
        private readonly IOnebotService _onebot;
        private readonly MainDbContext _db;
        private readonly ILogger<TrustCommand> _logger;
        public TrustCommand(IConfiguration configuration, IOnebotService onebot, MainDbContext db, ILogger<TrustCommand> logger)
            : base(db)
        {
            _configuration = configuration;
            _onebot = onebot;
            _db = db;
            _logger = logger;
        }


        protected override async Task<CommandResponse?> ExecuteAuthorizedAsync(MessageContext context, string[] args, CancellationToken cancellationToken = default)
        {
            if (context is GroupMessage groupMsg)
            {
                if (groupMsg.GroupId.ToString() == _configuration["Bot:mainGroupId"])
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
                    return CommandResponse.FailureResponse("❌ 本命令只能在主群中使用。");
                }
            }
            return null;
        }
        private async Task<bool> TrustUser(long userQQId, CancellationToken cancellationToken)
        {
            try
            {
                _logger.LogInformation("正在将用户 {user} 注册到数据库中。", userQQId.ToString());

                User? user = await _db.Users.Where(u => u.QQId == userQQId.ToString())
                                            .SingleOrDefaultAsync(cancellationToken);
                
                if (user is null)
                {
                    // 用户不存在，创建新用户
                    user = new User(userQQId.ToString())
                    {
                        UserGroup = UserGroup.VerifiedUser
                    };
                    _db.Users.Add(user);
                    await _db.SaveChangesAsync(cancellationToken);
                    _logger.LogInformation("用户 {qqId} 注册并设置为 VerifiedUser。", userQQId.ToString());
                }
                else if (user.UserGroup != UserGroup.VerifiedUser)
                {
                    // 用户已存在，更新用户组
                    user.UserGroup = UserGroup.VerifiedUser;
                    _db.Users.Update(user);
                    await _db.SaveChangesAsync(cancellationToken);
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
}
