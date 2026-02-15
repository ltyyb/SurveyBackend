
using Microsoft.EntityFrameworkCore;
using Sisters.WudiLib;
using SurveyBackend.Models;

namespace SurveyBackend
{
    public class BackgroundPushingService : BackgroundService
    {
        private readonly ILogger<BackgroundPushingService> _logger;
        private readonly IOnebotService _onebot;
        private readonly IConfiguration _configuration;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly long _mainGroupId;
        private readonly string? surveyLinkEndpoint;
        public BackgroundPushingService(ILogger<BackgroundPushingService> logger, IOnebotService onebot, IConfiguration configuration, IServiceScopeFactory scopeFactory)
        {
            _logger = logger;
            _onebot = onebot;
            _scopeFactory = scopeFactory;
            _configuration = configuration;
            surveyLinkEndpoint = _configuration["API:SurveyLinkEndpoint"];
            // 统一端点格式
            surveyLinkEndpoint = string.IsNullOrEmpty(surveyLinkEndpoint) || surveyLinkEndpoint.EndsWith('/')
                                ? surveyLinkEndpoint
                                : surveyLinkEndpoint + "/";
            

            if (string.IsNullOrEmpty(_configuration["Bot:mainGroupId"]))
            {
                _logger.LogError("主群组群号未配置。请前往 appsettings.json 配置 \"Bot:mainGroupId\" 为主群组群号。");
                _mainGroupId = 0; // 设置为0表示未配置
            }
            else
            {
                if (!long.TryParse(_configuration["Bot:mainGroupId"], out _mainGroupId))
                {
                    _logger.LogError($"主群组群号配置无效，无法将 \"{_configuration["Bot:mainGroupId"]}\" 转换为 long .请前往 appsettings.json 配置 \"Bot:mainGroupId\" 为正确的群号。");
                    _mainGroupId = 0; // 设置为0表示无效
                }
            }
        }
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("BackgroundPushingService Started.");

            while (!stoppingToken.IsCancellationRequested)
            {
                if (_mainGroupId == 0)
                {
                    _logger.LogError($"主群组群号配置无效，无法将 \"{_configuration["Bot:mainGroupId"]}\" 转换为 long .请前往 appsettings.json 配置 \"Bot:mainGroupId\" 为正确的群号。");
                    return;
                }
                if (string.IsNullOrWhiteSpace(surveyLinkEndpoint))
                {
                    _logger.LogError("问卷链接端点未配置。请前往 appsettings.json 配置 \"API:SurveyLinkEndpoint\" 为正确的端点URL。");
                    return;
                }
                if (!_onebot.IsAvailable)
                {
                    await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
                    continue;
                }
                TimeSpan timeDifference = DateTime.Now - _onebot.LastMessageTime;
                if (timeDifference.TotalHours > 48)
                {
                    _logger.LogInformation("上次收到消息距离已达2天，将跳过本次推送。");
                    await Task.Delay(TimeSpan.FromHours(6), stoppingToken);
                    continue;
                }
                if (DateTime.Now.Hour < 9 || DateTime.Now.Hour >= 23)
                {
                    _logger.LogInformation("当前时间不在推送时间段内（9:00-23:00），将跳过本次推送。");
                    // 如果当前时间不在推送时间段内，则等待1小时后再检查
                    await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
                    continue;
                }
                await ProcessUnverifiedResponsesAsync(stoppingToken);

                // 3小时检查一次未推送的问卷响应
                await Task.Delay(TimeSpan.FromHours(3), stoppingToken);
            }

            _logger.LogWarning("BackgroundPushingService Stopped.");
        }

        private async Task ProcessUnverifiedResponsesAsync(CancellationToken cancellationToken)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<MainDbContext>();
                var unverifiedSubmissions = await db.ReviewSubmissions
                                                 .Where(r => r.Status == ReviewStatus.Pending)
                                                 .Include(r => r.Submission)
                                                    .ThenInclude(s => s.User)
                                                 .ToListAsync(cancellationToken);
                if (unverifiedSubmissions.Count > 0)
                {
                    _logger.LogInformation("共检测到 {Count} 条未审核的问卷提交", unverifiedSubmissions.Count);
                }
                foreach (var reviewData in unverifiedSubmissions)
                {
                    // 构造消息内容
                    var link = $"{surveyLinkEndpoint}?review=true&questionnaireId={reviewData.Submission.QuestionnaireId}&submissionId={reviewData.SubmissionId}";
                    var atAll = SendingMessage.AtAll();
                    var message = new SendingMessage($"""

                        有还未审核完毕的问卷提交 ヾ(•ω•`)o
                        请各位群友抽空审核 ( •̀ ω •́ )✧
                        -
                        审阅链接:

                        {link}

                        如复制到浏览器中访问，请务必确保链接完整。请不要修改链接任何内容。
                        请不要将此页面任何内容及链接分享给他人哦~
                        -

                        群内/私聊发送指令以投票:
                            /survey vote {reviewData.Submission.ShortSubmissionId} a - 同意
                            /survey vote {reviewData.Submission.ShortSubmissionId} d - 拒绝
                        你可以随时更新您的投票结果。
                        """);

                    // 调用消息发送接口进行推送
                    var pushResult = await _onebot.SendGroupMessageAsync(_mainGroupId, atAll + message);
                    _logger.LogInformation("未审核问卷提交 SubmissionId: {SubmissionId} (ShortId: {ShortId}) 已发送到群 {MainGroupId}，消息ID={MessageId}", reviewData.Submission.SubmissionId, reviewData.Submission.ShortSubmissionId, _mainGroupId, pushResult?.MessageId);
                }

            }

            catch (Exception ex)
            {
                _logger.LogError(ex, "处理未推送问卷响应时发生异常。");
            }
        }

    }
}
