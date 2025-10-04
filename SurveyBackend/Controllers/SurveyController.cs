using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using MySqlConnector;
using Sisters.WudiLib;
using System.Data;
using System.Text.Json;
using System.Text.RegularExpressions;

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
        private static readonly Regex SafeNameRegex = new(@"^[a-zA-Z0-9_]+$");

        public SurveyController(ILogger<SurveyController> logger, ILoggerFactory loggerFactory, IConfiguration configuration, IOnebotService onebotService)
        {
            _configuration = configuration;
            _logger = logger;
            _onebot = onebotService;
            _loggerFactory = loggerFactory;
        }




        [HttpGet("getSurvey")]
        public async Task<ActionResult<object>> GetSurveyAsync([FromHeader(Name = "SURVEY-USER-ID")] string userId)
        {
            if (string.IsNullOrWhiteSpace(userId))
            {
                return BadRequest(new { status = -1, error = "UserId cannot be null or empty." });
            }

            var connStr = _configuration.GetConnectionString("DefaultConnection");
            if (string.IsNullOrWhiteSpace(connStr))
            {
                _logger.LogError("连接字符串未配置。请前往 appsettings.json 添加 \"DefaultConnection\" 连接字符串。");
                return StatusCode(500, "Server Config Error");
            }

            var surveyUser = await SurveyUser.GetUserByIdAsync(userId, _logger, connStr);
            if (surveyUser is null)
            {
                return BadRequest(new { status = -2, error = $"Unable to find user with the provided UserId {userId}." });
            }
            if (await CheckValueExistsAsync(connStr, "EntranceSurveyResponses", "QQId", surveyUser.QQId))
            {
                return BadRequest(new { status = -3, error = "Response of this QQId is already exist." });
            }
            var surveyPkg = SurveyPkgInstance.Instance;
            if (surveyPkg is null)
            {
                return NotFound(new { status = -4, error = "Server hasn't load any survey yet." });
            }
            if (surveyPkg.TryGetSurvey(out var survey) && survey is not null)
            {
                return Ok(new
                {
                    status = 0,
                    data = new
                    {
                        surveyJson = survey.GetSpecificSurveyJsonByQQId(surveyUser.QQId),
                        version = survey.Version,
                        qqId = surveyUser.QQId
                    }
                });
            }
            else
            {
                return NotFound(new { status = -4, error = "No active survey available at the moment." });
            }
        }

        public class SurveySubmission
        {
            public string UserId { get; set; } = string.Empty;
            public string Version { get; set; } = string.Empty;
            public string Answers { get; set; } = string.Empty;
        }

        // 一个接受POST的方法, 前端通过此接口提供 UserId 及 问卷结果JSON 提交结果
        [HttpPost("submitSurvey")]
        public async Task<ActionResult> SubmitSurveyAsync([FromBody] SurveySubmission submission)
        {
            if (submission == null
                || string.IsNullOrWhiteSpace(submission.UserId)
                || string.IsNullOrWhiteSpace(submission.Answers)
                || string.IsNullOrWhiteSpace(submission.Version))
            {
                _logger.LogWarning("Invalid survey submission data.\n {payload}", submission);
                return BadRequest(new { status = -1, error = "Invalid survey submission data." });
            }
            var connStr = _configuration.GetConnectionString("DefaultConnection");

            if (string.IsNullOrWhiteSpace(connStr))
            {
                _logger.LogError("连接字符串未配置。请前往 appsettings.json 添加 \"DefaultConnection\" 连接字符串。");
                return StatusCode(500, new { status = -500, error = "Server Config Error" });
            }
            var surveyUser = await SurveyUser.GetUserByIdAsync(submission.UserId, _logger, connStr);
            if (surveyUser is null)
            {
                return NotFound(new { status = -2, error = $"No user found with UserId: {submission.UserId}" });
            }
            if (await CheckValueExistsAsync(connStr, "EntranceSurveyResponses", "QQId", surveyUser.QQId))
            {
                return BadRequest(new { status = -3, error = "Response of this QQId is already exist." });
            }
            // 检查 Answers 是否为JSON避免数据库CAST出错
            if (!IsValidJson(submission.Answers))
            {
                return BadRequest(new { status = -4, error = "Answers is not a valid JSON." });
            }
            var surveyPkg = SurveyPkgInstance.Instance;
            if (!surveyPkg.IsVersionValid(submission.Version))
            {
                return BadRequest(new { status = -5, error = $"Invalid survey version: {submission.Version}. Please check the version." });
            }

            // 存储结果到数据库
            (bool success, string? responseId) = await SaveSurvey(surveyUser, submission.Answers, submission.Version);

            if (success && !string.IsNullOrWhiteSpace(responseId))
            {
                _logger.LogInformation($"Survey submitted for UserId: {submission.UserId} ({surveyUser.QQId}), the responseId is {responseId}");
                return Ok(new { status = 0, responseId });
            }
            else
            {
                _logger.LogError($"Failed to save survey for UserId: {submission.UserId} ({surveyUser.QQId})");
                return StatusCode(500, new { status = -501, error = "Failed to save survey response." });
            }

        }

        public class SurveyDataSubmission
        {
            public string surveyId { get; set; } = string.Empty;
        }

        [HttpPost("getSurveyData")]
        public async Task<ActionResult> GetSurveyDataAsync([FromBody] SurveyDataSubmission dataSubmission)
        {
            if (string.IsNullOrWhiteSpace(dataSubmission.surveyId))
            {
                return BadRequest(new { error = "Invaild SurveyId." });
            }
            var connStr = _configuration.GetConnectionString("DefaultConnection");

            if (string.IsNullOrWhiteSpace(connStr))
            {
                _logger.LogError("连接字符串未配置。请前往 appsettings.json 添加 \"DefaultConnection\" 连接字符串。");
                return StatusCode(500, new { status = -500, error = "Server Config Error" });
            }
            if (await ResponseTools.IsResponseDisabled(dataSubmission.surveyId, _logger, connStr))
            {
                return StatusCode(403, new { error = "This survey response is disabled and cannot be accessed." });
            }

            var response = await GetResponseByResponseId(dataSubmission.surveyId);
            if (response is null)
            {
                return NotFound(new { error = $"Something went wrong or no survey response found with the provided SurveyId: {dataSubmission.surveyId}" });
            }

            var surveyPkg = SurveyPkgInstance.Instance;
            if (surveyPkg is null)
            {
                return NotFound(new { error = "Server-side Error: No active survey found." });
            }
            var surveyJson = surveyPkg.GetSurvey(response.SurveyVersion).GetSpecificSurveyJsonByQQId(response.QQId);
            if (surveyJson is null)
            {
                _logger.LogError("Cannot get survey with version {v}", response.SurveyVersion);
                return NotFound(new { error = "Cannot get survey of this response." });
            }
            return Ok(new
            {
                qqId = response.QQId,
                version = response.SurveyVersion,
                surveyJson,
                answers = response.SurveyAnswer
            });

        }

        public class SurveyResponse
        {
            public string UserId { get; set; } = string.Empty;
            public string QQId { get; set; } = string.Empty;
            public string SurveyVersion { get; set; } = string.Empty;
            public string SurveyAnswer { get; set; } = string.Empty;
        }
        private async Task<SurveyResponse?> GetResponseByResponseId(string responseId)
        {
            try
            {
                var connStr = _configuration.GetConnectionString("DefaultConnection");
                if (string.IsNullOrWhiteSpace(connStr))
                {
                    _logger.LogError("连接字符串未配置。请前往 appsettings.json 添加 \"DefaultConnection\" 连接字符串。");
                    return null;
                }
                await using var conn = new MySqlConnection(connStr);
                await conn.OpenAsync();

                const string sql = "SELECT SurveyAnswer, SurveyVersion, UserId, QQId FROM EntranceSurveyResponses WHERE ResponseId = @responseId LIMIT 1";
                await using var cmd = new MySqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@responseId", responseId);

                await using var reader = await cmd.ExecuteReaderAsync();

                if (await reader.ReadAsync())  // 只读取第一行
                {
                    string response = reader.GetString("SurveyAnswer");
                    string version = reader.GetString("SurveyVersion");
                    string userId = reader.GetString("userId");
                    string qqId = reader.GetString("QQId");
                    var responseData = new SurveyResponse
                    {
                        UserId = userId,
                        SurveyVersion = version,
                        QQId = qqId,
                        SurveyAnswer = response
                    };
                    return responseData;
                }
                else
                {
                    _logger.LogWarning("Could not find survey response with id {id}", responseId);
                    return null;
                }
            }
            catch (MySqlException ex)
            {
                _logger.LogError(ex, "Database error while reading survey response with id {id}.", responseId);
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error while reading survey response with id {id}.", responseId);
                return null;
            }
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


        private async Task<(bool succ, string? responseId)> SaveSurvey(SurveyUser user, string answerJson, string surveyVersion)
        {
            try
            {
                var connStr = _configuration.GetConnectionString("DefaultConnection");

                if (string.IsNullOrWhiteSpace(connStr))
                {
                    _logger.LogError("连接字符串未配置。请前往 appsettings.json 添加 \"DefaultConnection\" 连接字符串。");
                    return (false, null);
                }

                await using var conn = new MySqlConnection(connStr);
                await conn.OpenAsync();

                const string saveSql = "INSERT INTO EntranceSurveyResponses (ResponseId, UserId, QQId, ShortId, SurveyVersion, SurveyAnswer) VALUES (@responseId, @userId, @qqId, @shortId, @version, CAST(@surveyData AS JSON))";

                await using var cmd = new MySqlCommand(saveSql, conn);
                // 生成一个唯一30字随机字符串 ResponseId
                var responseId = Guid.NewGuid().ToString("N")[..30];
                var shortId = responseId[..8]; // 取前8位作为 shortId
                cmd.Parameters.AddWithValue("@responseId", responseId);
                cmd.Parameters.AddWithValue("@userId", user.UserId);
                cmd.Parameters.AddWithValue("@qqId", user.QQId);
                cmd.Parameters.AddWithValue("@shortId", shortId);
                cmd.Parameters.AddWithValue("@version", surveyVersion);
                cmd.Parameters.AddWithValue("@surveyData", answerJson);

                var rowsAffected = await cmd.ExecuteNonQueryAsync();
                bool saved = rowsAffected > 0;
                if (saved)
                {
                    _logger.LogInformation($"Survey submitted to the database successfully for UserId: {user.UserId} ({user.QQId}), ResponseId is {responseId} ({shortId}).");
                    await GenerateInsight(responseId);
                    await PushResponse(responseId, shortId);
                    return (true, responseId);
                }
                else
                {
                    _logger.LogWarning($"Survey submission affected 0 rows for UserId: {user.UserId} ({user.QQId})");
                    return (false, null);
                }

            }
            catch (MySqlException ex)
            {
                _logger.LogError(ex, "Database error while saving survey response.");
                return (false, null);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error while saving survey response.");
                return (false, null);
            }
        }


        public static async Task<bool> CheckValueExistsAsync(string connectionString, string tableName, string columnName, object value)
        {
            // 防止 SQL 注入：只允许合法字符
            if (!SafeNameRegex.IsMatch(tableName) || !SafeNameRegex.IsMatch(columnName))
            {
                throw new ArgumentException("表名或列名包含非法字符。只允许字母、数字和下划线。");
            }

            string query = $"SELECT EXISTS(SELECT 1 FROM `{tableName}` WHERE `{columnName}` = @value LIMIT 1);";

            await using var connection = new MySqlConnection(connectionString);
            await connection.OpenAsync();

            await using var command = new MySqlCommand(query, connection);
            command.Parameters.AddWithValue("@value", value);

            object result = await command.ExecuteScalarAsync() ?? false;

            return Convert.ToBoolean(result);
        }

        private async Task<bool> GenerateInsight(string responseId)
        {
            try
            {
                var response = await GetResponseByResponseId(responseId);
                if (response is null)
                {
                    _logger.LogError($"No survey response found with ResponseId: {responseId}");
                    return false;
                }
                var surveyPkg = SurveyPkgInstance.Instance;
                if (surveyPkg is null)
                {
                    _logger.LogError("Server-side Error: No active survey found.");
                    return false;
                }
                var surveyJson = surveyPkg.GetSurvey(response.SurveyVersion).GetSpecificSurveyJsonByQQId(response.QQId);
                if (surveyJson is null)
                {
                    _logger.LogError($"Cannot get survey with version {response.SurveyVersion}");
                    return false;
                }
                var llmTool = new LLMTools(_configuration, _loggerFactory.CreateLogger<LLMTools>());
                if (!llmTool.IsAvailable)
                {
                    _logger.LogWarning("LLM Tool is not available, skipping insight generation.");
                    return false;
                }
                var prompt = llmTool.ParseSurveyResponseToNL(surveyJson, response.SurveyAnswer);
                if (string.IsNullOrWhiteSpace(prompt))
                {
                    _logger.LogError($"Failed to parse survey response to natural language for responseId: {responseId}");
                    return false;
                }
                var insight = await llmTool.GetInsight(prompt);
                _logger.LogInformation($"Insight generated for responseId: {responseId}");
                // 更新数据库
                var connStr = _configuration.GetConnectionString("DefaultConnection");
                if (string.IsNullOrWhiteSpace(connStr))
                {
                    _logger.LogError("连接字符串未配置。请前往 appsettings.json 添加 \"DefaultConnection\" 连接字符串。");
                    return false;
                }
                const string updateSql = "UPDATE EntranceSurveyResponses SET LLMInsight = @insight WHERE ResponseId = @responseId";
                await using var updateConnection = new MySqlConnection(connStr);
                await updateConnection.OpenAsync();
                await using var updateCmd = new MySqlCommand(updateSql, updateConnection);
                updateCmd.Parameters.AddWithValue("@insight", insight);
                updateCmd.Parameters.AddWithValue("@responseId", responseId);
                var rowsAffected = await updateCmd.ExecuteNonQueryAsync();
                if (rowsAffected > 0)
                {
                    _logger.LogInformation($"Insight generated and updated successfully for responseId: {responseId}");
                    return true;
                }
                else
                {
                    _logger.LogWarning($"Failed to update insight for responseId: {responseId}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error while generating insight for responseId: {responseId}", responseId);
                return false;
            }
        }
        private async Task<bool> PushResponse(string responseId, string shortId)
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
                var connStr = _configuration.GetConnectionString("DefaultConnection");
                if (string.IsNullOrWhiteSpace(connStr))
                {
                    _logger.LogError("连接字符串未配置。请前往 appsettings.json 添加 \"DefaultConnection\" 连接字符串。");
                    return false;
                }
                const string query = "SELECT IsPushed, QQId, LLMInsight FROM EntranceSurveyResponses WHERE ResponseId = @responseId LIMIT 1";

                await using var connection = new MySqlConnection(connStr);
                await connection.OpenAsync();

                await using var command = new MySqlCommand(query, connection);
                command.Parameters.AddWithValue("@responseId", responseId);

                var reader = await command.ExecuteReaderAsync();
                bool isPushed;
                string qqId;
                string insight;
                if (await reader.ReadAsync())
                {
                    isPushed = reader.GetBoolean("IsPushed");
                    qqId = reader.GetString("QQId");
                    insight = reader.IsDBNull("LLMInsight") ? "尚无 AI 见解, 可能目前不可用。" : reader.GetString("LLMInsight");
                }
                else
                {
                    _logger.LogError($"No survey response found with ResponseId: {responseId}");
                    return false;
                }

                if (!isPushed)
                {
                    var atMsg = SendingMessage.At(long.Parse(qqId));
                    var msg = new SendingMessage($"""

                        已收到您的问卷提交 (≧∇≦)ﾉ
                        请耐心等待众审结果哦 ♪(^∇^*)

                        期间请留意审核群消息，如审核完成将发送通知（*＾-＾*）
                        """);
                    var feedbackMsg = await _onebot.SendGroupMessageAsync(verifyGroupId, atMsg + msg);
                    _logger.LogInformation($"Feedback message sent to verify group {verifyGroupId} with messageId: {feedbackMsg?.MessageId}");


                    var link = $"https://ltyyb.auntstudio.com/survey/entr/review?surveyId={responseId}";
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
                    var pushResult = await _onebot.SendGroupMessageAsync(mainGroupId, atAll + message);
                    // 发送 AI 见解
                    var insightMsg = new SendingMessage($"""
                        {shortId} 的 AI 见解：
                        以下内容由 AI 生成，仅供参考：
                        ============================
                        {insight}
                        """);
                    await _onebot.SendGroupMessageAsync(mainGroupId, insightMsg);
                    _logger.LogInformation($"Survey response {responseId} pushed to main group {mainGroupId} successfully.");
                    // 更新数据库标记为已推送
                    const string updateSql = "UPDATE EntranceSurveyResponses SET IsPushed = true WHERE ResponseId = @responseId";
                    await using var updateConnection = new MySqlConnection(connStr);
                    await updateConnection.OpenAsync();
                    await using var updateCmd = new MySqlCommand(updateSql, updateConnection);
                    updateCmd.Parameters.AddWithValue("@responseId", responseId);
                    var rowsAffected = await updateCmd.ExecuteNonQueryAsync();
                    if (rowsAffected > 0)
                    {
                        _logger.LogInformation($"Response voting data updated successfully for responseId: {responseId}");
                        return true;
                    }
                    else
                    {
                        _logger.LogWarning($"Failed to update response voting data for responseId: {responseId}");
                        return false;
                    }


                }
                else
                {
                    return true;
                }

            }
            catch (MySqlException ex)
            {
                _logger.LogError(ex, "Database error while pushing survey response.");
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error while pushing survey response.");
                return false;
            }
        }

    }
}
