using System.Text;
using MessageContext = Sisters.WudiLib.Posts.Message;

namespace SurveyBackend.Bot.Commands;

// info 指令
public class InfoCommand : AuthorizedAsyncCommand
{
    public override string CommandName => "info";

    public override string Description => "使用方法: /survey info [SubmissionId | SurveyId ...] 获取某个对象的详细信息, 将展示所有匹配项, 请尽可能提供完整ID. 留空参数获取更多提示。";
    public override UserGroup[] RequiredPermission => [UserGroup.VerifiedUser, UserGroup.Admin, UserGroup.SuperAdmin];
    private readonly IServiceScopeFactory _serviceScopeFactory;
    public InfoCommand(IServiceScopeFactory serviceScopeFactory) : base(serviceScopeFactory)
    {
        _serviceScopeFactory = serviceScopeFactory;
    }
    protected async override Task<CommandResponse?> ExecuteAuthorizedAsync(MessageContext context, string[] args, CancellationToken cancellationToken = default)
    {
        if (args.Length == 1)
        {
            var objId = args[0];
            using var scope = _serviceScopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<MainDbContext>();
            StringBuilder msgBuilder = new StringBuilder();
            if (objId.Length < 5)
            {
                var msg = "❌ 输入的 ID 过短，无法唯一识别对象。请提供更完整的 ID。";
                return CommandResponse.FailureResponse(msg);
            }
            // 检查是否为用户QQ号
            if (long.TryParse(objId, out long qqId))
            {
                var user = await db.Users.Where(u => u.QQId == objId)
                                         .SingleOrDefaultAsync(cancellationToken);
                if (user is not null)
                {
                    var submissionsCount = await db.Submissions.CountAsync(s => s.UserId == user.UserId, cancellationToken);
                    string userGroupStr = user.UserGroup switch
                    {
                        UserGroup.NewComer => "🌱未提交问卷未认证新用户",
                        UserGroup.PendingUser => "⏳已提交问卷待审核用户",
                        UserGroup.VerifiedUser => "✅已认证用户",
                        UserGroup.Admin => "🛡️管理员",
                        UserGroup.SuperAdmin => "🛠️超级管理员",
                        _ => "⁉️未知"
                    };
                    var verifySubmission = await db.ReviewSubmissions.Include(r => r.Submission)
                                                                        .ThenInclude(s => s.Questionnaire)
                                                                            .ThenInclude(q => q.Survey)
                                                                     .Include(r => r.Submission)
                                                                        .ThenInclude(s => s.User)
                                                                     .Where(r => r.Submission.UserId == user.UserId
                                                                        && r.Submission.Questionnaire.Survey.IsVerifySurvey == true)
                                                                     .SingleOrDefaultAsync(cancellationToken);
                    string msg = $"""
                    对象类型: User

                    User ID: {user.UserId}
                    QQ ID: {user.QQId}
                    用户组: {userGroupStr}
                    已提交 Submissions 数量: {submissionsCount}

                    审核问卷提交ID: {verifySubmission?.Submission.SubmissionId ?? "不可用"}
                    """;
                    msgBuilder.AppendLine(msg);
                    msgBuilder.AppendLine(new string('=', 35));
                }
            }
            // 尝试匹配 Submission
            var submissions = await db.Submissions.Include(s => s.User)
                                                 .Include(s => s.Questionnaire)
                                                    .ThenInclude(q => q.Survey)
                                                 .Where(s => EF.Functions.Like(
                                                    s.SubmissionId,
                                                    objId + "%"))
                                                 .ToListAsync(cancellationToken);
            foreach (var submission in submissions)
            {
                bool isVerifySurvey = submission.Questionnaire.Survey.IsVerifySurvey;
                bool isReviewSurvey = submission.Questionnaire.Survey.NeedReview;
                string msg;
                if (isReviewSurvey)
                {
                    var reviewData = await db.ReviewSubmissions.Where(r => r.SubmissionId == submission.SubmissionId)
                                                               .SingleOrDefaultAsync(cancellationToken);
                    string reviewStatusMsg = (reviewData?.Status) switch
                    {
                        ReviewStatus.Pending => "🟡 审核进行中",
                        ReviewStatus.Approved => "🟢 已通过",
                        ReviewStatus.Rejected => "🔴 已拒绝",
                        _ => "⚪ 未知",
                    };
                    if (reviewData is null)
                    {
                        return CommandResponse.FailureResponse("❌ 无法找到对应的审核数据，可能该提交未进入审核流程。");
                    }
                    var votes = await db.ReviewVotes.Where(v => v.ReviewSubmissionDataId == reviewData.ReviewSubmissionDataId)
                                                    .ToListAsync(cancellationToken);
                    msg = $"""
                        对象类型: Submission
                        这个提交所属的问卷要求审核。
                        {(isVerifySurvey ? "这个提交所属的问卷是入群问卷。" : "")}

                        Submission ID: {submission.SubmissionId}
                        用户 QQ: {submission.User.QQId}
                        作答的 Questionnaire ID: {submission.QuestionnaireId} ({submission.Questionnaire.ReleaseDate.ToDisplayTime()})
                        关联问卷: {submission.Questionnaire.Survey.Title} ({submission.Questionnaire.Survey.SurveyId})
                        提交时间: {submission.CreatedAt.ToDisplayTime()}
                        是否禁用: {(submission.IsDisabled ? "是" : "否")}

                        问卷审核状态: {reviewStatusMsg}
                        赞成票: {votes.Count(v => v.VoteType == VoteType.Upvote)}
                        反对票: {votes.Count(v => v.VoteType == VoteType.Downvote)}
                        赞成率: {(votes.Count == 0 ? "N/A" : $"{(double)votes.Count(v => v.VoteType == VoteType.Upvote) / votes.Count * 100:F2}%")}
                        关联用户当前身份组: {submission.User.UserGroup}
                        """;
                }
                else
                {
                    msg = $"""
                        对象类型: Submission
                        这是一个一般问卷提交。

                        Submission ID: {submission.SubmissionId}
                        用户 QQ: {submission.User.QQId}
                        作答的 Questionnaire ID: {submission.QuestionnaireId} ({submission.Questionnaire.ReleaseDate.ToDisplayTime()})
                        关联问卷: {submission.Questionnaire.Survey.Title} ({submission.Questionnaire.Survey.SurveyId})
                        提交时间: {submission.CreatedAt.ToDisplayTime()}
                        是否禁用: {(submission.IsDisabled ? "是" : "否")}
                        """;
                }
                msgBuilder.AppendLine(msg);
                msgBuilder.AppendLine(new string('=', 35));
            }
            // 尝试匹配 Questionnaire
            var questionnaires = await db.Questionnaires.Include(q => q.Survey)
                                                        .Where(q => EF.Functions.Like(
                                                            q.QuestionnaireId,
                                                            objId + "%"))
                                                        .ToListAsync(cancellationToken);
            foreach (var questionnaire in questionnaires)
            {
                var submissionsCount = await db.Submissions
                                               .CountAsync(s => s.QuestionnaireId == questionnaire.QuestionnaireId, cancellationToken);
                string msg = $"""
                    对象类型: Questionnaire

                    Questionnaire ID: {questionnaire.QuestionnaireId}
                    关联 Survey: {questionnaire.Survey.Title} ({questionnaire.Survey.SurveyId})
                    发布日期: {questionnaire.ReleaseDate.ToDisplayTime()}
                    是否为审核问卷: {(questionnaire.Survey.IsVerifySurvey ? "是" : "否")}
                    是否需要众审: {(questionnaire.Survey.NeedReview ? "是" : "否")}
                    是否唯一提交: {(questionnaire.Survey.UniquePerUser ? "是" : "否")}
                    关联提交数量: {submissionsCount}
                    """;
                msgBuilder.AppendLine(msg);
                msgBuilder.AppendLine(new string('=', 35));
            }
            // 尝试匹配 Survey
            var surveys = await db.Surveys.Where(s => EF.Functions.Like(
                                                        s.SurveyId,
                                                        objId + "%"))
                                         .ToListAsync(cancellationToken);
            foreach (var survey in surveys)
            {
                var questionnairesCount = await db.Questionnaires.CountAsync(q => q.SurveyId == survey.SurveyId, cancellationToken);
                var submissionsCount = await db.Submissions.Include(s => s.Questionnaire)
                                                           .CountAsync(s => s.Questionnaire.SurveyId == survey.SurveyId, cancellationToken);
                string msg = $"""
                    对象类型: Survey

                    Survey ID: {survey.SurveyId}
                    标题: {survey.Title}
                    描述: {survey.Description}
                    创建时间: {survey.CreatedAt.ToDisplayTime()}
                    是否为审核问卷: {(survey.IsVerifySurvey ? "是" : "否")}
                    是否需要众审: {(survey.NeedReview ? "是" : "否")}
                    是否唯一提交: {(survey.UniquePerUser ? "是" : "否")}
                    关联 Questionnaires 数量: {questionnairesCount}
                    关联 Submissions 数量: {submissionsCount}
                    """;
                msgBuilder.AppendLine(msg);
                msgBuilder.AppendLine(new string('=', 35));
            }
            // 尝试匹配 User
            var users = await db.Users.Where(u => EF.Functions.Like(
                                                    u.UserId,
                                                    objId + "%"))
                                        .ToListAsync(cancellationToken);
            foreach (var user in users)
            {
                var submissionsCount = await db.Submissions.CountAsync(s => s.UserId == user.UserId, cancellationToken);
                string userGroupStr = user.UserGroup switch
                {
                    UserGroup.NewComer => "🌱未提交问卷未认证新用户",
                    UserGroup.PendingUser => "⏳已提交问卷待审核用户",
                    UserGroup.VerifiedUser => "✅已认证用户",
                    UserGroup.Admin => "🛡️管理员",
                    UserGroup.SuperAdmin => "🛠️超级管理员",
                    _ => "⁉️未知"
                };
                var verifySubmission = await db.ReviewSubmissions.Include(r => r.Submission)
                                                                    .ThenInclude(s => s.Questionnaire)
                                                                        .ThenInclude(q => q.Survey)
                                                                 .Include(r => r.Submission)
                                                                    .ThenInclude(s => s.User)
                                                                 .Where(r => r.Submission.UserId == user.UserId
                                                                    && r.Submission.Questionnaire.Survey.IsVerifySurvey == true)
                                                                 .SingleOrDefaultAsync(cancellationToken);
                string msg = $"""
                    对象类型: User

                    User ID: {user.UserId}
                    QQ ID: {user.QQId}
                    用户组: {userGroupStr}
                    已提交 Submissions 数量: {submissionsCount}

                    审核问卷提交ID: {verifySubmission?.Submission.SubmissionId ?? "不可用"}
                    """;
                msgBuilder.AppendLine(msg);
                msgBuilder.AppendLine(new string('=', 35));
            }
            // 尝试匹配 ReviewSubmission
            var reviewSubmissions = await db.ReviewSubmissions.Include(r => r.Submission)
                                                               .ThenInclude(s => s.Questionnaire)
                                                                   .ThenInclude(q => q.Survey)
                                                               .Include(r => r.Submission)
                                                                   .ThenInclude(s => s.User)
                                                               .Where(r => EF.Functions.Like(
                                                                    r.ReviewSubmissionDataId,
                                                                    objId + "%"))
                                                               .ToListAsync(cancellationToken);
            foreach (var reviewSubmission in reviewSubmissions)
            {
                string reviewStatusMsg = reviewSubmission.Status switch
                {
                    ReviewStatus.Pending => "🟡 审核进行中",
                    ReviewStatus.Approved => "🟢 已通过",
                    ReviewStatus.Rejected => "🔴 已拒绝",
                    _ => "⚪ 未知",
                };
                var votes = await db.ReviewVotes.Where(v => v.ReviewSubmissionDataId == reviewSubmission.ReviewSubmissionDataId)
                                                .ToListAsync(cancellationToken);
                string msg = $"""
                    对象类型: ReviewSubmission

                    ReviewSubmission ID: {reviewSubmission.ReviewSubmissionDataId}
                    关联 Submission ID: {reviewSubmission.SubmissionId}
                    用户 QQ: {reviewSubmission.Submission.User.QQId}
                    作答的 Questionnaire ID: {reviewSubmission.Submission.QuestionnaireId} ({reviewSubmission.Submission.Questionnaire.ReleaseDate.ToDisplayTime()})
                    关联问卷: {reviewSubmission.Submission.Questionnaire.Survey.Title} ({reviewSubmission.Submission.Questionnaire.Survey.SurveyId})
                    提交时间: {reviewSubmission.Submission.CreatedAt.ToDisplayTime()}
                    是否属于审核问卷: {(reviewSubmission.Submission.Questionnaire.Survey.IsVerifySurvey ? "是" : "否")}

                    众审状态: {reviewStatusMsg}
                    赞成票: {votes.Count(v => v.VoteType == VoteType.Upvote)}
                    反对票: {votes.Count(v => v.VoteType == VoteType.Downvote)}
                    赞成率: {(votes.Count == 0 ? "N/A" : $"{(double)votes.Count(v => v.VoteType == VoteType.Upvote) / votes.Count * 100:F2}%")}
                    关联用户当前身份组: {reviewSubmission.Submission.User.UserGroup}
                    """;
                msgBuilder.AppendLine(msg);
                msgBuilder.AppendLine(new string('=', 35));
            }
            // 尝试匹配 Request
            var requests = await db.Requests.Include(r => r.User)
                                           .Where(r => EF.Functions.Like(
                                                r.RequestId,
                                                objId + "%"))
                                           .ToListAsync(cancellationToken);
            foreach (var request in requests)
            {
                string msg = $"""
                    对象类型: Request

                    Request ID: {request.RequestId}
                    用户 QQ: {request.User.QQId}
                    请求类型: {request.RequestType}
                    提交时间: {request.CreatedAt.ToDisplayTime()}
                    """;
                msgBuilder.AppendLine(msg);
                msgBuilder.AppendLine(new string('=', 35));
            }
            if (msgBuilder.Length == 0)
            {
                return CommandResponse.FailureResponse("❌ 无法找到匹配的对象。请检查输入的 ID 是否正确，并尽可能提供更完整的 ID 以获得准确匹配。");
            }
            return CommandResponse.SuccessResponse(msgBuilder.ToString());
        }
        else
        {
            var msg = """
            参数不正确。
            本命令用于获取某个数据库对象的详细信息。
            支持对象有: Submission 实例、Questionnaire 实例、Survey 实例、User 实例、ReviewSubmission 实例、Request 实例。
            同时 id 为纯数字时, 将优先尝试将 ID 解析为 QQ号 并匹配 User 实例。QQ号要求完全匹配。

            使用方法:
            /survey info [id]

            其中 id 参数为对象的数据库主键索引或 QQ号 。非QQ号支持前缀匹配，但应尽可能使用完整 id 匹配。
            """;
            return CommandResponse.SuccessResponse(msg);
        }

    }
}
