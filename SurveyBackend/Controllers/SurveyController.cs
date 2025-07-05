using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using MySqlConnector;
using Microsoft.AspNetCore.Http.HttpResults;

namespace SurveyBackend.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
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
        public ActionResult<string> GetSurvey()
        {
            var survey = Survey.Instance;
            if (survey is null)
            {
                return NotFound("Server-side Error: No active survey found.");
            }
            return Ok(survey.SurveyJson);
        }



        // 一个接受POST的方法, 前端通过此接口提供 userId 及 问卷结果JSON 提交结果
        [HttpPost("submitSurvey")]
        public async Task<ActionResult> SubmitSurveyAsync([FromBody] SurveySubmission submission)
        {
            if (submission == null || string.IsNullOrEmpty(submission.userId) || string.IsNullOrEmpty(submission.Answers))
            {
                return BadRequest("Invalid survey submission data.");
            }
            var connStr = _configuration.GetConnectionString("DefaultConnection");

            if (string.IsNullOrEmpty(connStr))
            {
                _logger.LogError("连接字符串未配置。请前往 appsettings.json 添加 \"DefaultConnection\" 连接字符串。");
                return StatusCode(500, "Server Config Error");
            }
            var surveyUser = await SurveyUser.GetUserByIdAsync(submission.userId, _logger, connStr);
            if (surveyUser is null)
            {
                return NotFound($"No user found with UserId: {submission.userId}");
            }

            // 检查 Answers 是否为JSON避免数据库CAST出错
            if (!IsValidJson(submission.Answers))
            {
                return BadRequest("Answers is not a valid JSON.");
            }

            // 存储结果到数据库
            bool success = await SaveSurvey(surveyUser, submission.Answers);

            if (success)
            {
                _logger.LogInformation($"Survey submitted for UserId: {submission.userId} ({surveyUser.QQId})");
                return Ok("Survey submitted successfully.");
            }
            else
            {
                _logger.LogError($"Failed to save survey for UserId: {submission.userId} ({surveyUser.QQId})");
                return StatusCode(500, "Failed to save survey response.");
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

        private async Task<bool> SaveSurvey(SurveyUser user, string answerJson)
        {
            try
            {
                var connStr = _configuration.GetConnectionString("DefaultConnection");

                if (string.IsNullOrEmpty(connStr))
                {
                    _logger.LogError("连接字符串未配置。请前往 appsettings.json 添加 \"DefaultConnection\" 连接字符串。");
                    return false;
                }

                await using var conn = new MySqlConnection(connStr);
                await conn.OpenAsync();

                const string sql = "INSERT INTO EntranceSurveyResponses (UserId, QQId, SurveyAnswer) VALUES (@userId, @qqId, CAST(@surveyData AS JSON))";

                await using var cmd = new MySqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@userId", user.UserId);
                cmd.Parameters.AddWithValue("@qqId", user.QQId);
                cmd.Parameters.AddWithValue("@surveyData", answerJson);

                var rowsAffected = await cmd.ExecuteNonQueryAsync();
                if (rowsAffected > 0)
                {
                    _logger.LogInformation($"Survey submitted to the database successfully for UserId: {user.UserId} ({user.QQId})");
                    return true;
                }
                else
                {
                    _logger.LogWarning($"Survey submission affected 0 rows for UserId: {user.UserId} ({user.QQId})");
                    return false;
                }

            }
            catch (MySqlException ex)
            {
                _logger.LogError(ex, "Database error while saving survey response.");
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error while saving survey response.");
                return false;
            }
        }
    }
}
