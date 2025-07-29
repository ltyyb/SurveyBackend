using MySqlConnector;
using Sisters.WudiLib;
using System.Data;

namespace SurveyBackend
{
    public class BackgroundVerifyService : BackgroundService
    {
        private readonly ILogger<BackgroundVerifyService> _logger;
        private readonly IOnebotService _onebot;
        private readonly IConfiguration _configuration;
        private readonly string _connStr;
        private readonly long _mainGroupId;
        private readonly long _verifyGroupId;
        private readonly List<(string responseId, DateTime delTime)> responseClearList = [];
        public BackgroundVerifyService(ILogger<BackgroundVerifyService> logger, IOnebotService onebot, IConfiguration configuration)
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
                var query = "SELECT ResponseId, UserId FROM EntranceSurveyResponses WHERE IsReviewed = false";
                var responses = new List<(string responseId, string userId)>();


                await using (var connection = new MySqlConnection(_connStr))
                {
                    await connection.OpenAsync(cancellationToken);

                    await using var command = new MySqlCommand(query, connection);
                    // 执行查询获取所有未完成审核的记录
                    await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                    while (await reader.ReadAsync(cancellationToken))
                    {
                        responses.Add((reader.GetString("ResponseId"), reader.GetString("UserId")));
                    }
                }

                _logger.LogInformation("共检测到 {Count} 条未完成审核的问卷响应", responses.Count);
                // 针对每一个未推送的问卷响应，尝试验证
                foreach (var (responseId, userId) in responses)
                {
                    await using var voteConn = new MySqlConnection(_connStr);
                    await voteConn.OpenAsync(cancellationToken);
                    const string voteQuery = "SELECT SUM(vote = 'agree') AS agreeCount, SUM(vote = 'deny') AS denyCount FROM response_votes WHERE responseId = @responseId";
                    await using var voteCmd = new MySqlCommand(voteQuery, voteConn);
                    voteCmd.Parameters.AddWithValue("@responseId", responseId);
                    await using var voteReader = await voteCmd.ExecuteReaderAsync(cancellationToken);
                    if (await voteReader.ReadAsync(cancellationToken))
                    {
                        var agreeCount = voteReader.IsDBNull("agreeCount") ? 0 : voteReader.GetInt32("agreeCount");
                        var denyCount = voteReader.IsDBNull("denyCount") ? 0 : voteReader.GetInt32("denyCount");
                        if (agreeCount + denyCount < 5)
                        {
                            _logger.LogInformation("问卷响应 {ResponseId} 的投票数不足，跳过审核。", responseId);

                        }
                        else
                        {
                            float agreeRate = (float)agreeCount / (agreeCount + denyCount);
                            if (agreeRate > 0.6)
                            {
                                _logger.LogInformation("问卷响应 {ResponseId} 审核通过。", responseId);
                                // 更新数据库标记为已审核
                                await using var connection = new MySqlConnection(_connStr);
                                await connection.OpenAsync(cancellationToken);
                                const string updateQuery = "UPDATE EntranceSurveyResponses SET IsReviewed = true WHERE ResponseId = @responseId";
                                await using var updateCmd = new MySqlCommand(updateQuery, connection);
                                updateCmd.Parameters.AddWithValue("@responseId", responseId);
                                var affected = await updateCmd.ExecuteNonQueryAsync(cancellationToken);
                                if (affected == 0)
                                {
                                    _logger.LogWarning("问卷响应 {ResponseId} 审核通过，但无法更新 IsReviewed 标记，请务必手动处理此问题！！", responseId);
                                }
                                else
                                {
                                    _logger.LogInformation("问卷响应 {ResponseId} 已标记为已审核。", responseId);
                                }
                                await using var userConnection = new MySqlConnection(_connStr);
                                await userConnection.OpenAsync(cancellationToken);
                                const string updateUserQuery = "UPDATE QQUsers SET IsVerified = true WHERE UserId = @userId";
                                await using var updateUserCmd = new MySqlCommand(updateUserQuery, userConnection);
                                updateUserCmd.Parameters.AddWithValue("@userId", userId);
                                var userAffected = await updateUserCmd.ExecuteNonQueryAsync(cancellationToken);
                                if (affected == 0)
                                {
                                    _logger.LogWarning("用户 {userId} 问卷响应 {ResponseId} 审核通过，但无法更新 QQUsers.IsVerified 标记，请务必手动处理此问题！！", userId, responseId);
                                }
                                else
                                {
                                    _logger.LogInformation("用户 {userId} 已标记为已验证。", userId);
                                    // 推送审核通过的消息
                                    var atMessage = SendingMessage.At(long.Parse(userId));
                                    var message = $"""

                                        ヾ(•ω•`)o 您的问卷回答已通过审核~
                                        (≧∇≦)ﾉ 您现在可以向主群 {_mainGroupId} 发起加群请求，验证消息可任意填写~
                                        """;
                                    await _onebot.SendGroupMessageAsync(_verifyGroupId, atMessage + message);
                                }
                            }
                            else
                            {
                                _logger.LogInformation("问卷响应 {ResponseId} 审核未通过。", responseId);
                                // 更新数据库标记为已审核
                                await using var connection = new MySqlConnection(_connStr);
                                await connection.OpenAsync(cancellationToken);
                                const string updateQuery = "UPDATE EntranceSurveyResponses SET IsReviewed = true WHERE ResponseId = @responseId";
                                await using var updateCmd = new MySqlCommand(updateQuery, connection);
                                updateCmd.Parameters.AddWithValue("@responseId", responseId);
                                var affected = await updateCmd.ExecuteNonQueryAsync(cancellationToken);
                                if (affected == 0)
                                {
                                    _logger.LogWarning("问卷响应 {ResponseId} 审核未通过，但无法更新 IsReviewed 标记，请务必手动处理此问题！！", responseId);
                                }
                                else
                                {
                                    _logger.LogInformation("问卷响应 {ResponseId} 已标记为已审核。", responseId);
                                }

                                responseClearList.Add((responseId, DateTime.Now.AddHours(24))); // 添加到清除列表

                                // 推送审核未通过的消息
                                var atMessage = SendingMessage.At(long.Parse(userId));
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
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "在处理未审核问卷响应时发生异常。请检查数据库连接和查询语句。");
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
                _logger.LogInformation("开始清除 {Count} 条过期的问卷响应。", expiredResponses.Count);
                await using (var connection = new MySqlConnection(_connStr))
                {
                    await connection.OpenAsync(cancellationToken);
                    foreach (var response in expiredResponses)
                    {
                        string responseId = response.responseId;
                        const string deleteQuery = "DELETE FROM EntranceSurveyResponses WHERE ResponseId = @responseId";
                        await using var command = new MySqlCommand(deleteQuery, connection);
                        command.Parameters.AddWithValue("@responseId", responseId);
                        var affectedRows = await command.ExecuteNonQueryAsync(cancellationToken);
                        if (affectedRows > 0)
                        {
                            _logger.LogInformation("成功清除问卷响应 {ResponseId}。", responseId);
                            // 从清除列表中移除已处理的响应
                            responseClearList.Remove(response);
                        }
                        else
                        {
                            _logger.LogError("无法删除问卷响应 {ResponseId}。", responseId);
                        }
                    }
                }

            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "在清除过期问卷响应时发生异常。");
            }
        }
    }
}
