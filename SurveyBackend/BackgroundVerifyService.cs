
using Microsoft.EntityFrameworkCore;
using Sisters.WudiLib;
using SurveyBackend.Models;
using System.Data;

namespace SurveyBackend
{
    public class BackgroundVerifyService : BackgroundService
    {
        private readonly ILogger<BackgroundVerifyService> _logger;
        private readonly IOnebotService _onebot;
        private readonly IConfiguration _configuration;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly long _mainGroupId;
        private readonly long _verifyGroupId;
        private readonly List<(string responseId, DateTime delTime)> responseClearList = [];
        public BackgroundVerifyService(ILogger<BackgroundVerifyService> logger, IOnebotService onebot, IConfiguration configuration, IServiceScopeFactory scopeFactory)
        {
            _logger = logger;
            _onebot = onebot;
            _configuration = configuration;
            _scopeFactory = scopeFactory;


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
            if (string.IsNullOrEmpty(_configuration["Bot:verifyGroupId"]))
            {
                _logger.LogError("审核群组群号未配置。请前往 appsettings.json 配置 \"Bot:verifyGroupId\" 为审核群组群号。");
                _verifyGroupId = 0; // 设置为0表示未配置
            }
            else
            {
                if (!long.TryParse(_configuration["Bot:verifyGroupId"], out _verifyGroupId))
                {
                    _logger.LogError($"审核群组群号配置无效，无法将 \"{_configuration["Bot:verifyGroupId"]}\" 转换为 long .请前往 appsettings.json 配置 \"Bot:verifyGroupId\" 为正确的群号。");
                    _verifyGroupId = 0; // 设置为0表示无效
                }
            }
        }
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("BackgroundVerifyService Started.");

            while (!stoppingToken.IsCancellationRequested)
            {
                if (_mainGroupId == 0)
                {
                    _logger.LogError($"主群组群号配置无效，无法将 \"{_configuration["Bot:mainGroupId"]}\" 转换为 long .请前往 appsettings.json 配置 \"Bot:mainGroupId\" 为正确的群号。");
                    return;
                }

                if (!_onebot.IsAvailable)
                {
                    await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
                    continue;
                }
                await VerifyResponse(stoppingToken);
                await TryClearResponse(stoppingToken);

                // 10分钟检查一次未审核的问卷响应
                await Task.Delay(TimeSpan.FromMinutes(10), stoppingToken);
            }

            _logger.LogWarning("BackgroundVerifyService Stopped.");
        }



        private async Task VerifyResponse(CancellationToken cancellationToken)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var _db = scope.ServiceProvider.GetRequiredService<MainDbContext>();

                List<ReviewSubmissionData> pendingSubmissions 
                = await _db.ReviewSubmissions.Where(r => r.Status == ReviewStatus.Pending)
                                             .Include(r => r.Submission)
                                                .ThenInclude(s => s.User)
                                             .ToListAsync(cancellationToken);
                if (pendingSubmissions.Count > 0)
                {
                    _logger.LogInformation("共检测到 {Count} 条待审核的问卷提交", pendingSubmissions.Count);
                }
                foreach (var reviewData in pendingSubmissions)
                {
                    var submission = reviewData.Submission;
                    var user = submission.User;
                    _logger.LogInformation("正在审核 SubmissionId: {SubmissionId}, UserId: {UserId}", submission.SubmissionId, user.UserId);
                    // 从 ReviewVote 表中获取该 submission 的投票情况
                    var votes = await _db.ReviewVotes.Where(v => v.ReviewSubmissionDataId == reviewData.ReviewSubmissionDataId)
                                                     .ToListAsync(cancellationToken);
                    var agreeCount = votes.Count(v => v.VoteType == VoteType.Upvote);
                    var denyCount = votes.Count(v => v.VoteType == VoteType.Downvote);
                    if (agreeCount + denyCount < 5)
                    {
                        _logger.LogInformation("SubmissionId: {SubmissionId} 的投票数不足，跳过审核。", submission.SubmissionId);
                        continue;
                    }
                    float agreeRate = (float)agreeCount / (agreeCount + denyCount);
                    if (agreeRate > 0.6)
                    {
                        _logger.LogInformation("SubmissionId: {SubmissionId} 审核通过。", submission.SubmissionId);
                        reviewData.Status = ReviewStatus.Approved;
                        user.UserGroup = UserGroup.VerifiedUser;
                        await _db.SaveChangesAsync(cancellationToken);
                        var atMessage = SendingMessage.At(long.Parse(user.QQId));
                        var message = $"""

                            ヾ(•ω•`)o 您的问卷回答已通过审核~
                            (≧∇≦)ﾉ 您现在可以向主群 {_mainGroupId} 发起加群请求，验证消息可任意填写~
                            """;
                        await _onebot.SendGroupMessageAsync(_verifyGroupId, atMessage + message);
                    }
                    else
                    {
                        _logger.LogInformation("SubmissionId: {SubmissionId} 审核未通过。", submission.SubmissionId);
                        reviewData.Status = ReviewStatus.Rejected;
                        user.UserGroup = UserGroup.NewComer;
                        await _db.SaveChangesAsync(cancellationToken);
                        var atMessage = SendingMessage.At(long.Parse(user.QQId));
                        var message = $"""
                                    
                                    w(ﾟДﾟ)w 您的问卷回答未通过审核欸
                                    (｡•́︿•̀｡) 请检查您的回答，确保符合群规要求。
                                    您的回答将在 24小时 后被清除，
                                    在这之后您可以重新执行 /survey entr
                                    并重新填写问卷。
                                    如果您有任何疑问，请联系管理员。
                                    """;
                        await _onebot.SendGroupMessageAsync(_verifyGroupId, atMessage + message);
                    }
                }                                               
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "在处理未审核问卷响应时发生异常。请检查数据库连接。");
            }
        }

        private async Task TryClearResponse(CancellationToken cancellationToken)
        {
            try
            {
                if (responseClearList.Count == 0)
                {
                    return;
                }
                var now = DateTime.Now;
                var expiredResponses = responseClearList.Where(r => r.delTime <= now).ToList();
                if (expiredResponses.Count == 0)
                {
                    _logger.LogInformation("没有过期的问卷响应需要清除。");
                    return;
                }
                _logger.LogInformation("开始删除 {Count} 条过期的问卷响应。", expiredResponses.Count);
                using var scope = _scopeFactory.CreateScope();
                var _db = scope.ServiceProvider.GetRequiredService<MainDbContext>();
                foreach (var response in expiredResponses)
                {
                    var submissionId = response.responseId;
                    var submission = await _db.Submissions.FindAsync(submissionId, cancellationToken);
                    if (submission is null)
                    {
                        _logger.LogWarning("未找到 SubmissionId: {SubmissionId} 对应的提交记录，可能已被删除。", submissionId);
                        continue;
                    }
                    _db.Submissions.Remove(submission);
                    await _db.SaveChangesAsync(cancellationToken);
                    _logger.LogInformation("已删除 SubmissionId: {SubmissionId} 的问卷响应。", submissionId);
                }

            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "在删除过期问卷响应时发生异常。");
            }
        }
    }
}
