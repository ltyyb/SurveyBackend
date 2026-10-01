using SurveyBackend.Services.Insights;
using MessageContext = Sisters.WudiLib.Posts.Message;

namespace SurveyBackend.Bot.Commands;
// reinsight 指令
public class ReinsightCommand : AuthorizedAsyncCommand
{
    public override string CommandName => "reinsight";
    public override string Description => "使用方法: /survey reinsight [SubmissionId] \n 重新生成指定问卷提交的AI分析。SubmissionId 可以简写。";
    public override UserGroup[] RequiredPermission => [UserGroup.Admin, UserGroup.SuperAdmin];
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly IOptions<LlmOptions> _llmOptions;
    private readonly ILoggerFactory _loggerFactory;
    public ReinsightCommand(IServiceScopeFactory serviceScopeFactory, IOptions<LlmOptions> llmOptions, ILoggerFactory loggerFactory) : base(serviceScopeFactory)
    {
        _serviceScopeFactory = serviceScopeFactory;
        _llmOptions = llmOptions;
        _loggerFactory = loggerFactory;
    }
    protected async override Task<CommandResponse?> ExecuteAuthorizedAsync(MessageContext context, string[] args, CancellationToken cancellationToken = default)
    {
        if (args.Length == 1)
        {
            var submissionId = args[0];
            using var scope = _serviceScopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<MainDbContext>();
            var submissions = await db.Submissions.Include(s => s.User)
                                                 .Include(s => s.Questionnaire)
                                                    .ThenInclude(q => q.Survey)
                                                 .Where(s => EF.Functions.Like(
                                                    s.SubmissionId,
                                                    submissionId + "%"))
                                                 .ToListAsync(cancellationToken);
            if (submissions.Count == 0)
            {
                return CommandResponse.FailureResponse("❌ 无法找到对应的问卷提交数据，请检查 Submission ID 是否正确。");
            }
            if (submissions.Count > 1)
            {
                return CommandResponse.FailureResponse("❌ 找到多个匹配的问卷提交数据，请提供更完整的 Submission ID 以获得准确匹配。");
            }
            var submission = submissions[0];
            var reviewData = await db.ReviewSubmissions.Where(r => r.SubmissionId == submission.SubmissionId)
                                           .SingleOrDefaultAsync(cancellationToken);
            if (reviewData is null)
            {
                return CommandResponse.FailureResponse("❌ 无法找到对应的审核数据，可能该问卷非审核问卷。");
            }
            var (succ, message) = await GenerateInsight(reviewData, db);
            if (!succ || string.IsNullOrEmpty(message))
            {
                return CommandResponse.FailureResponse("❌ 生成失败: " + message);
            }
            reviewData.AIInsights = message;
            db.ReviewSubmissions.Update(reviewData);
            await db.SaveChangesAsync(cancellationToken);
            return CommandResponse.SuccessResponse("✅ 重新生成见解成功，并已更新数据库。\n" + message);
        }
        else
        {
            var msg = """
                参数不正确。
                本命令用于重新生成指定问卷提交的AI分析, 并写入数据库。

                使用方法:
                /survey reinsight [SubmissionId]
                SubmissionId 可以简写。
                """;
            return CommandResponse.SuccessResponse(msg);
        }
    }

    private async Task<(bool succ, string? message)> GenerateInsight(ReviewSubmissionData reviewSubmission, MainDbContext _db)
    {
        try
        {
            var llmTool = new LLMTools(_llmOptions, _loggerFactory.CreateLogger<LLMTools>());
            if (!llmTool.IsAvailable)
            {
                return (false, "AI 未能生成见解，可能目前不可用。");
            }
            _db.Entry(reviewSubmission).Reference(r => r.Submission).Load();
            var submission = reviewSubmission.Submission;
            var surveyData = submission.SurveyData;
            _db.Entry(submission).Reference(s => s.Questionnaire).Load();
            var questionnaire = submission.Questionnaire;
            if (questionnaire.LLMPageNames is null)
            {
                return (false, "未配置 LLMPageNames，无法生成见解。");
            }
            var surveyJson = questionnaire.SurveyJson;
            var prompt = llmTool.ParseSurveyResponseToNL(surveyJson, surveyData, questionnaire.LLMPageNames);
            if (string.IsNullOrWhiteSpace(prompt))
            {
                return (false, "无法解析问卷响应为自然语言。");
            }
            var insight = await llmTool.GetInsight(prompt);
            if (string.IsNullOrWhiteSpace(insight))
            {
                return (false, "AI 未能生成见解，返回结果为空。");
            }
            return (true, insight);
        }
        catch (Exception ex)
        {
            return (false, "生成见解时发生意外错误: " + ex.Message);
        }
    }
}
