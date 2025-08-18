using MySqlConnector;
using Sisters.WudiLib;

namespace SurveyBackend
{
    public class BackgroundPushingService : BackgroundService
    {
        private readonly ILogger<BackgroundPushingService> _logger;
        private readonly IOnebotService _onebot;
        private readonly IConfiguration _configuration;
        private readonly string _connStr;
        private readonly long _mainGroupId;
        public BackgroundPushingService(ILogger<BackgroundPushingService> logger, IOnebotService onebot, IConfiguration configuration)
        {
            _logger = logger;
            _onebot = onebot;
            _configuration = configuration;
            var connStr = _configuration.GetConnectionString("DefaultConnection");

            if (string.IsNullOrWhiteSpace(connStr))
            {
                _logger.LogError("连接字符串未配置。请前往 appsettings.json 添加 \"DefaultConnection\" 连接字符串。");
                _connStr = string.Empty;
            }
            else
            {
                _connStr = connStr;
            }

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
                if (string.IsNullOrWhiteSpace(_connStr))
                {
                    _logger.LogError("连接字符串未配置。请前往 appsettings.json 添加 \"DefaultConnection\" 连接字符串。");
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
                await ProcessUnpushedResponsesAsync(stoppingToken);
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
                var query = "SELECT ResponseId, ShortId FROM EntranceSurveyResponses WHERE IsReviewed = false";
                var responses = new List<(string responseId, string shortId)>();


                await using (var connection = new MySqlConnection(_connStr))
                {
                    await connection.OpenAsync(cancellationToken);

                    await using var command = new MySqlCommand(query, connection);
                    // 执行查询获取所有未完成审核的记录
                    await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                    while (await reader.ReadAsync(cancellationToken))
                    {
                        responses.Add((reader.GetString("ResponseId"), (reader.GetString("ShortId"))));
                    }
                }

                _logger.LogInformation("共检测到 {Count} 条未完成审核的问卷响应", responses.Count);

                // 针对每一个未审核的问卷响应，尝试调用推送逻辑
                foreach (var (responseId, shortId) in responses)
                {
                    var pushResult = await TryPushSurveyResponseAsync(responseId, shortId, false, cancellationToken);
                    if (pushResult)
                    {
                        _logger.LogInformation("问卷响应 {ResponseId} 推送成功。", responseId);
                    }
                    else
                    {
                        _logger.LogWarning("问卷响应 {ResponseId} 推送失败，将在下次重试。", responseId);
                    }
                }
            }
            catch (MySqlException sqlEx)
            {
                _logger.LogError(sqlEx, "数据库操作失败，可能是连接字符串错误或数据库不可用。请检查连接字符串和数据库状态。");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "处理未推送问卷响应时发生异常。");
            }
        }
        private async Task ProcessUnpushedResponsesAsync(CancellationToken cancellationToken)
        {
            try
            {
                var query = "SELECT ResponseId, ShortId FROM EntranceSurveyResponses WHERE IsPushed = false";
                var responses = new List<(string responseId, string shortId)>();


                await using (var connection = new MySqlConnection(_connStr))
                {
                    await connection.OpenAsync(cancellationToken);

                    await using var command = new MySqlCommand(query, connection);
                    // 执行查询获取所有未推送的记录
                    await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                    while (await reader.ReadAsync(cancellationToken))
                    {
                        responses.Add((reader.GetString("ResponseId"), (reader.GetString("ShortId"))));
                    }
                }

                _logger.LogInformation("共检测到 {Count} 条未推送的问卷响应", responses.Count);

                // 针对每一个未推送的问卷响应，尝试调用推送逻辑
                foreach (var (responseId, shortId) in responses)
                {
                    var pushResult = await TryPushSurveyResponseAsync(responseId, shortId, true, cancellationToken);
                    if (pushResult)
                    {
                        _logger.LogInformation("问卷响应 {ResponseId} 推送成功。", responseId);
                    }
                    else
                    {
                        _logger.LogWarning("问卷响应 {ResponseId} 推送失败，将在下次重试。", responseId);
                    }
                }
            }
            catch (MySqlException sqlEx)
            {
                _logger.LogError(sqlEx, "数据库操作失败，可能是连接字符串错误或数据库不可用。请检查连接字符串和数据库状态。");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "处理未推送问卷响应时发生异常。");
            }
        }

        /// <summary>
        /// 尝试对指定的问卷响应进行推送，如果推送成功则更新数据库中的标记。
        /// </summary>
        private async Task<bool> TryPushSurveyResponseAsync(string responseId, string shortId, bool isUnpushed, CancellationToken cancellationToken)
        {
            try
            {
                string source = isUnpushed ? "[来自未推送检查服务]" : "[来自未审核检查服务]";
                if (isUnpushed)
                {
                    const string lockSql = "UPDATE EntranceSurveyResponses SET IsPushed = true WHERE ResponseId = @responseId AND IsPushed = false";
                    await using (var connection = new MySqlConnection(_connStr))
                    {
                        await connection.OpenAsync(cancellationToken);
                        await using (var cmd = new MySqlCommand(lockSql, connection))
                        {
                            cmd.Parameters.AddWithValue("@responseId", responseId);
                            var affected = await cmd.ExecuteNonQueryAsync(cancellationToken);
                            if (affected == 0)
                            {
                                // 说明已经被其他任务处理了
                                _logger.LogInformation("跳过重复推送：ResponseId {ResponseId} 已经在其他地方处理。", responseId);
                                return true;
                            }
                        }
                    }
                }


                // 构造消息内容
                var link = $"https://ltyyb.auntstudio.com/survey/entr/review?surveyId={responseId}";
                var atAll = SendingMessage.AtAll();
                var message = new SendingMessage($"""

                        {source}
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
                        """);

                // 调用消息发送接口进行推送
                var pushResult = await _onebot.SendGroupMessageAsync(_mainGroupId, atAll + message);
                _logger.LogInformation("{source} 问卷响应 {ResponseId} ({shortId}) 已发送到群 {MainGroupId}，消息ID={MessageId}", source, responseId, shortId, _mainGroupId, pushResult?.MessageId);

                return true;
            }
            catch (MySqlException sqlEx)
            {
                _logger.LogError(sqlEx, "在重推问卷响应 {ResponseId} 时发生数据库错误。请检查连接字符串和数据库状态。", responseId);
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "在重推问卷响应 {ResponseId} 时发生异常。", responseId);
                return false;
            }
        }

    }
}
