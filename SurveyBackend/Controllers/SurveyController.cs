using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;
using Sisters.WudiLib;
using SurveyBackend.Models;
using System.Data;
using System.Text.Json;
using System.Text.RegularExpressions;
using static Microsoft.ApplicationInsights.MetricDimensionNames.TelemetryContext;
using User = SurveyBackend.Models.User;

namespace SurveyBackend.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [EnableCors("AllowAll")]
    public class SurveyController : ControllerBase
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<SurveyController> _logger;
        private readonly IOnebotService _onebot;
        private readonly ILoggerFactory _loggerFactory;
        private readonly MainDbContext _db;
        private static readonly Regex SafeNameRegex = new(@"^[a-zA-Z0-9_]+$");

        public SurveyController(ILogger<SurveyController> logger, ILoggerFactory loggerFactory, IConfiguration configuration, IOnebotService onebotService, MainDbContext db)
        {
            _configuration = configuration;
            _logger = logger;
            _onebot = onebotService;
            _loggerFactory = loggerFactory;
            _db = db;
        }




        [HttpGet("{questionnaireId}")]
        public async Task<ActionResult<object>> GetSurveyAsync(string questionnaireId, [FromHeader(Name = "SURVEY-USER-ID")] string userId)
        {
            if (string.IsNullOrWhiteSpace(userId))
            {
                return BadRequest(new { status = -1, error = "UserId cannot be null or empty." });
            }

            var user = await _db.Users.FindAsync(userId);
            if (user is null)
            {
                return StatusCode(403, new { status = -2, error = $"Unable to find user with the provided UserId {userId}." });
            }

            var questionnaire = await _db.Questionnaires.FindAsync(questionnaireId);
            if (questionnaire is null)
            {
                return NotFound(new { status = -4, error = $"No questionnaire found with QuestionnaireId: {questionnaireId}" });
            }
            if (questionnaire.UniquePerUser && await IsUserUnique(questionnaire, user))
            {
                return StatusCode(403, new { status = -3, error = "You already have an submission record of this questionnaire." });
            }

            return Ok(new
            {
                status = 0,
                data = new
                {
                    surveyJson = GetSpecificSurveyJson(questionnaire, user),
                    qqId = user.QQId
                }
            });


        }

        public class SurveySubmission
        {
            public string UserId { get; set; } = string.Empty;
            public string Answers { get; set; } = string.Empty;
        }

        // 一个接受POST的方法, 前端通过此接口提供 UserId 及 问卷结果JSON 提交结果
        [HttpPost("{questionnaireId}/submission")]
        public async Task<ActionResult> SubmitSurveyAsync(string questionnaireId, [FromBody] SurveySubmission surveySubmission)
        {
            if (surveySubmission == null
                || string.IsNullOrWhiteSpace(surveySubmission.UserId)
                || string.IsNullOrWhiteSpace(surveySubmission.Answers))
            {
                _logger.LogWarning("Invalid survey submission data.\n {payload}", surveySubmission);
                return BadRequest(new { status = -1, error = "Invalid survey submission data." });
            }

            var user = await _db.Users.FindAsync(surveySubmission.UserId);

            if (user is null)
            {
                return StatusCode(403, new { status = -2, error = $"No user found with UserId: {surveySubmission.UserId}" });
            }

            // 检查 Answers 是否为JSON避免数据库CAST出错
            if (!IsValidJson(surveySubmission.Answers))
            {
                return BadRequest(new { status = -4, error = "Answers is not a valid JSON." });
            }
            var questionnaire = await _db.Questionnaires.FindAsync(questionnaireId);
            if (questionnaire is null)
            {
                return NotFound(new { status = -3, error = $"No questionnaire found with QuestionnaireId: {questionnaireId}" });
            }


            // 存储结果到数据库
            (bool success, Submission? submission) = await SaveSurvey(user, questionnaire, surveySubmission.Answers);


            if (success && submission is not null)
            {
                _logger.LogInformation($"Survey submitted for UserId: {surveySubmission.UserId} ({user.QQId}), the submissionId is {submission.SubmissionId}");
                if (submission.Questionnaire.NeedReview)
                {
                    // 需要审核，创建审核数据记录
                    var reviewSubmissionData = new ReviewSubmissionData { Submission = submission };
                    _db.ReviewSubmissions.Add(reviewSubmissionData);
                    await _db.SaveChangesAsync();
                    // 生成AI见解
                    await GenerateInsight(reviewSubmissionData);
                    // 推送到群组
                    await PushResponse(reviewSubmissionData);
                }



                return Ok(new { status = 0, submissionId = submission.SubmissionId });
            }
            else
            {
                _logger.LogError($"Failed to save survey for UserId: {surveySubmission.UserId} ({user.QQId})");
                return StatusCode(500, new { status = -501, error = "Failed to save survey response." });
            }

        }


        [HttpGet("{questionnaireId}/submission/{submissionId}")]
        public async Task<ActionResult> GetSurveyDataAsync(string questionnaireId, string submissionId)
        {
            var questionnaire = await _db.Questionnaires.FindAsync(questionnaireId);
            if (questionnaire is null)
            {
                return NotFound(new { error = $"No questionnaire found with QuestionnaireId: {questionnaireId}" });
            }

            var submission = await _db.Submissions.FindAsync(submissionId);
            if (submission is null)
            {
                return NotFound(new { error = $"No submission found with SubmissionId: {submissionId}" });
            }

            if (submission.IsDisabled)
            {
                return StatusCode(403, new { error = "This survey response is disabled and cannot be accessed." });
            }

            return Ok(new
            {
                qqId = submission.User.QQId,
                surveyJson = questionnaire.SurveyJson,
                surveyData = submission.SurveyData
            });

        }



        private static bool IsValidJson(string json)
        {
            try
            {
                var jsonDoc = JsonDocument.Parse(json);
                return true;
            }
            catch (JsonException)
            {
                return false;
            }
        }


        private async Task<(bool succ, Submission? submission)> SaveSurvey(User user, Questionnaire questionnaire, string answerJson)
        {
            try
            {
                var connStr = _configuration.GetConnectionString("DefaultConnection");

                if (string.IsNullOrWhiteSpace(connStr))
                {
                    _logger.LogError("连接字符串未配置。请前往 appsettings.json 添加 \"DefaultConnection\" 连接字符串。");
                    return (false, null);
                }

                if (questionnaire.UniquePerUser)
                {
                    bool exists = await _db.Submissions
                                        .AnyAsync(s => s.Questionnaire.QuestionnaireId == questionnaire.QuestionnaireId
                                                       && s.User.UserId == user.UserId);
                    if (exists)
                    {
                        _logger.LogWarning($"User {user.UserId} ({user.QQId}) already has a submission for questionnaire {questionnaire.QuestionnaireId}.");
                        return (false, null);
                    }
                }

                // 保存到 EF Core 数据库上下文
                var submission = new Submission
                {
                    Questionnaire = questionnaire,
                    SurveyData = answerJson,
                    User = user
                };
                _db.Submissions.Add(submission);
                // 同步更改
                await _db.SaveChangesAsync();

                return (true, submission);

            }
            catch (DbUpdateException ex)
            {
                _logger.LogError(ex, "Cannot save submission to database.");
                return (false, null);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error while saving survey response.");
                return (false, null);
            }
        }

        private async Task<bool> IsUserUnique(Questionnaire questionnaire, User user)
        {
            try
            {
                bool exists = await _db.Submissions.AnyAsync(s => s.Questionnaire.QuestionnaireId == questionnaire.QuestionnaireId
                                                                  && s.User.UserId == user.UserId);

                return exists;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error while checking user uniqueness for questionnaire.");
                return false;
            }
        }


        private async Task<bool> GenerateInsight(ReviewSubmissionData reviewSubmission)
        {
            try
            {
                var submission = reviewSubmission.Submission;
                var surveyData = submission.SurveyData;

                var surveyJson = submission.Questionnaire.SurveyJson;
                var llmTool = new LLMTools(_configuration, _loggerFactory.CreateLogger<LLMTools>());
                if (!llmTool.IsAvailable)
                {
                    _logger.LogWarning("LLM Tool is not available, skipping insight generation.");
                    return false;
                }
                var prompt = llmTool.ParseSurveyResponseToNL(surveyJson, surveyData);
                if (string.IsNullOrWhiteSpace(prompt))
                {
                    _logger.LogError($"Failed to parse survey response to natural language for submission: {submission.SubmissionId}");
                    return false;
                }
                var insight = await llmTool.GetInsight(prompt);
                _logger.LogInformation($"Insight generated for submission: {submission.SubmissionId}");
                // 更新数据库
                reviewSubmission.AIInsights = insight ?? "AI 未能生成见解，可能目前不可用。";
                await _db.SaveChangesAsync();
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error while generating insight for submission: {submission}", reviewSubmission.Submission.SubmissionId);
                return false;
            }
        }
        private async Task<bool> PushResponse(ReviewSubmissionData reviewSubmission)
        {
            try
            {
                long mainGroupId;
                long verifyGroupId;
                if (string.IsNullOrEmpty(_configuration["Bot:verifyGroupId"]))
                {
                    _logger.LogError("审核群组群号未配置。请前往 appsettings.json 配置 \"Bot:verifyGroupId\" 为审核群组群号。");
                    return false;
                }
                else
                {
                    if (!long.TryParse(_configuration["Bot:verifyGroupId"], out verifyGroupId))
                    {
                        _logger.LogError($"审核群组群号配置无效，无法将 \"{_configuration["Bot:verifyGroupId"]}\" 转换为 long .请前往 appsettings.json 配置 \"Bot:verifyGroupId\" 为正确的群号。");
                        return false;
                    }
                }
                if (string.IsNullOrEmpty(_configuration["Bot:mainGroupId"]))
                {
                    _logger.LogError("主群组群号未配置。请前往 appsettings.json 配置 \"Bot:mainGroupId\" 为主群组群号。");
                    return false;
                }
                else
                {
                    if (!long.TryParse(_configuration["Bot:mainGroupId"], out mainGroupId))
                    {
                        _logger.LogError($"主群组群号配置无效，无法将 \"{_configuration["Bot:mainGroupId"]}\" 转换为 long .请前往 appsettings.json 配置 \"Bot:mainGroupId\" 为正确的群号。");
                        return false;
                    }
                }

                string qqId = reviewSubmission.Submission.User.QQId;
                string submissionId = reviewSubmission.Submission.SubmissionId;
                string shortId = reviewSubmission.Submission.ShortSubmissionId;
                string insight = reviewSubmission.AIInsights;
                var atMsg = SendingMessage.At(long.Parse(qqId));
                var msg = new SendingMessage($"""

                        已收到您的问卷提交 (≧∇≦)ﾉ
                        请耐心等待众审结果哦 ♪(^∇^*)

                        期间请留意审核群消息，如审核完成将发送通知（*＾-＾*）
                        """);
                var feedbackMsg = await _onebot.SendGroupMessageAsync(verifyGroupId, atMsg + msg);
                _logger.LogInformation($"Feedback message sent to verify group {verifyGroupId} with messageId: {feedbackMsg?.MessageId}");


                var link = $"https://ltyyb.auntstudio.com/survey/entr/review?submissionId={submissionId}";
                var atAll = SendingMessage.AtAll();
                var message = new SendingMessage($"""

                        [来自新问卷同步器]
                        有新的问卷填写提交 ヾ(•ω•`)o
                        请各位群友抽空审核 ( •̀ ω •́ )✧
                        -
                        审阅链接:

                        {link}

                        如复制到浏览器中访问，请务必确保链接完整。请不要修改链接任何内容。
                        请不要将此页面任何内容及链接分享给他人哦~
                        -

                        群内/私聊发送指令以投票:
                            /survey vote {shortId} a - 同意
                            /survey vote {shortId} d - 拒绝
                        你可以随时更新您的投票结果。
                        本问卷提交者: {qqId}
                        """);
                await _onebot.SendGroupMessageAsync(mainGroupId, atAll + message);
                // 发送 AI 见解
                var insightMsg = new SendingMessage($"""
                        {shortId} 的 AI 见解：
                        以下内容由 AI 生成，仅供参考：
                        ============================
                        {insight}
                        """);
                await _onebot.SendGroupMessageAsync(mainGroupId, insightMsg);
                return true;

            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error while pushing survey response.");
                return false;
            }
        }
        private string GetSpecificSurveyJson(Questionnaire questionnaire, User user)
        {
            // 替换 SurveyJson 中的 {Specific_QQId} 占位符
            string originalJson = questionnaire.SurveyJson;
            string specificJson = originalJson.Replace("{Specific_QQId}", user.QQId);
            specificJson = specificJson.Replace("{Survey_Release_Date}", questionnaire.ReleaseDate.ToString("yyyy-MM-dd"));
            return specificJson;
        }
    }
}
