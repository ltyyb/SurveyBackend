using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using MySqlConnector;
using System.Text.Json;

namespace SurveyBackend.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [EnableCors("AllowAll")]
    public class SurveyController : ControllerBase
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<SurveyController> _logger;

        public SurveyController(ILogger<SurveyController> logger, IConfiguration configuration)
        {
            _configuration = configuration;
            _logger = logger;
        }



        [HttpGet("getSurvey")]
        public async Task<ActionResult<string>> GetSurveyAsync([FromHeader(Name = "SURVEY-USER-ID")] string userId)
        {
            if (string.IsNullOrWhiteSpace(userId))
            {
                return BadRequest("UserId cannot be null or empty.");
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
                return BadRequest($"Unable to find user with the provided UserId {userId}.");
            }

            var survey = Survey.Instance;
            if (survey is null)
            {
                return NotFound("Server-side Error: No active survey found.");
            }
            var surveyJson = survey.GetSpecificSurveyJson(surveyUser.QQId);

            return Ok(surveyJson);
        }

        public class SurveySubmission
        {
            public string userId { get; set; } = string.Empty;
            public string Answers { get; set; } = string.Empty;
        }

        // 一个接受POST的方法, 前端通过此接口提供 userId 及 问卷结果JSON 提交结果
        [HttpPost("submitSurvey")]
        public async Task<ActionResult> SubmitSurveyAsync([FromBody] SurveySubmission submission)
        {
            if (submission == null || string.IsNullOrWhiteSpace(submission.userId) || string.IsNullOrWhiteSpace(submission.Answers))
            {
                _logger.LogWarning("Invalid survey submission data.\n {payload}", submission);
                return BadRequest(new {status = -1, error = "Invalid survey submission data." });
            }
            var connStr = _configuration.GetConnectionString("DefaultConnection");

            if (string.IsNullOrWhiteSpace(connStr))
            {
                _logger.LogError("连接字符串未配置。请前往 appsettings.json 添加 \"DefaultConnection\" 连接字符串。");
                return StatusCode(500, new { status = -500, error = "Server Config Error" });
            }
            var surveyUser = await SurveyUser.GetUserByIdAsync(submission.userId, _logger, connStr);
            if (surveyUser is null)
            {
                return NotFound(new { status = -2, error = $"No user found with UserId: {submission.userId}" });
            }

            // 检查 Answers 是否为JSON避免数据库CAST出错
            if (!IsValidJson(submission.Answers))
            {
                return BadRequest(new { status = -3, error = "Answers is not a valid JSON." });
            }

            // 存储结果到数据库
            (bool success, string? responseId) = await SaveSurvey(surveyUser, submission.Answers);

            if (success && !string.IsNullOrWhiteSpace(responseId))
            {
                _logger.LogInformation($"Survey submitted for UserId: {submission.userId} ({surveyUser.QQId}), the responseId is {responseId}");
                return Ok(new { status = 0, responseId });
            }
            else
            {
                _logger.LogError($"Failed to save survey for UserId: {submission.userId} ({surveyUser.QQId})");
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
                return BadRequest(new { error = "Invaild SurveyId."});
            }

            var response = await GetResponseByResponseId(dataSubmission.surveyId);
            if (response is null)
            {
                return NotFound(new { error = $"Something went wrong or no survey response found with the provided SurveyId: {dataSubmission.surveyId}"});
            }
            return Ok(new
            {
                userId = response.UserId,
                Answers = response.SurveyAnswer
            });

        }

        public class SurveyResponse
        {
            public string UserId { get; set; } = string.Empty;
            public string SurveyAnswer { get; set; } = string.Empty;
        }
        private async Task<SurveyResponse?> GetResponseByResponseId (string responseId)
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

                const string sql = "SELECT SurveyAnswer, UserId FROM EntranceSurveyResponses WHERE ResponseId = @responseId LIMIT 1";
                await using var cmd = new MySqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@responseId", responseId);

                await using var reader = await cmd.ExecuteReaderAsync();

                if (await reader.ReadAsync())  // 只读取第一行
                {
                    string response = reader.GetString("SurveyAnswer");
                    string userId = reader.GetString("userId");
                    var responseData = new SurveyResponse
                    {
                        UserId = userId,
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
                return null ;
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


        private async Task<(bool succ, string? responseId)> SaveSurvey(SurveyUser user, string answerJson)
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

                const string sql = "INSERT INTO EntranceSurveyResponses (ResponseId, UserId, QQId, SurveyAnswer) VALUES (@responseId, @userId, @qqId, CAST(@surveyData AS JSON))";

                await using var cmd = new MySqlCommand(sql, conn);
                // 生成一个唯一30字随机字符串 ResponseId
                var responseId = Guid.NewGuid().ToString("N")[..30];
                cmd.Parameters.AddWithValue("@responseId", responseId);
                cmd.Parameters.AddWithValue("@userId", user.UserId);
                cmd.Parameters.AddWithValue("@qqId", user.QQId);
                cmd.Parameters.AddWithValue("@surveyData", answerJson);

                var rowsAffected = await cmd.ExecuteNonQueryAsync();
                if (rowsAffected > 0)
                {
                    _logger.LogInformation($"Survey submitted to the database successfully for UserId: {user.UserId} ({user.QQId})");
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


    }
}
