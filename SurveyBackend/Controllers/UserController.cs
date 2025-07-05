using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using MySqlConnector;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SurveyBackend.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class UserController : ControllerBase
    {
        private readonly ILogger<UserController> _logger;
        private readonly IConfiguration _configuration;

        public UserController(ILogger<UserController> logger, IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;
        }

        // 一个接受POST的方法，允许前端POST UserId, 后端尝试创建 SurveyUser
        [HttpPost("checkUserId")]
        public async Task<ActionResult<SurveyUser>> CheckUserId([FromBody] string userId)
        {
            if (string.IsNullOrEmpty(userId))
            {
                return BadRequest("UserId cannot be null or empty.");
            }
            var connStr = _configuration.GetConnectionString("DefaultConnection");

            if (string.IsNullOrEmpty(connStr))
            {
                _logger.LogError("连接字符串未配置。请前往 appsettings.json 添加 \"DefaultConnection\" 连接字符串。");
                return StatusCode(500, "Server Config Error");
            }
            // 使用 SurveyUser 的工厂方法获取用户信息
            var surveyUser = await SurveyUser.GetUserByIdAsync(userId, _logger, connStr);
            if (surveyUser is null)
            {
                return NotFound($"No user found with UserId: {userId}");
            }
            return Ok(surveyUser);
        }
        public class RegisterUserRequest
        {
            public string UserId { get; set; } = "";
            public string QQId { get; set; } = "";
        }
        private string DecryptRsa(string base64Encrypted)
        {
            var encryptedBytes = Convert.FromBase64String(base64Encrypted);
            var keyPath = _configuration["RSA:privateKeyPath"];
            if (string.IsNullOrEmpty(keyPath))
            {
                throw new ArgumentException("Private key path is not configured in appsettings.json.");
            }
            var pem = System.IO.File.ReadAllText(keyPath);

            using RSA rsa = RSA.Create();
            
            rsa.ImportFromPem(pem);

            var decryptedBytes = rsa.Decrypt(encryptedBytes, RSAEncryptionPadding.Pkcs1);

            return Encoding.UTF8.GetString(decryptedBytes);
        }
        /// <summary>
        /// 接受一个RSA加密的 Json 参数，注册用户ID。不应对外公开。
        /// </summary>
        /// <param name="encryptedParams"></param>
        /// <returns></returns>
        [HttpPost("registerUserId")]
        public async Task<ActionResult<bool>> RegisterUserIdAsync([FromBody] string payload)
        {
            if (string.IsNullOrWhiteSpace(payload))
            {
                return BadRequest("Encrypted data cannot be null or empty.");
            }
            try
            {
                var decryptedJson = DecryptRsa(payload);

                var user = JsonSerializer.Deserialize<RegisterUserRequest>(decryptedJson);

                if (user == null || string.IsNullOrWhiteSpace(user.UserId) || string.IsNullOrWhiteSpace(user.QQId))
                {
                    return BadRequest("Invalid user data." );
                }

                var connStr = _configuration.GetConnectionString("DefaultConnection");
                if (string.IsNullOrEmpty(connStr))
                {
                    _logger.LogError("连接字符串未配置。请前往 appsettings.json 添加 \"DefaultConnection\" 连接字符串。");
                    return StatusCode(500, "Server Config Error");
                }

                SurveyUser surveyUser = new()
                {
                    UserId = user.UserId,
                    QQId = user.QQId
                };

                bool succ = await surveyUser.RegisterAsync(_logger, connStr);

                if (succ)
                {
                    return Ok();
                }
                else
                {
                    return StatusCode(500, "Failed to register user to database.");
                }

            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Invalid JSON after decrypt.");
                return BadRequest("Decrypted data is not valid JSON.");
            }
            catch (CryptographicException ex)
            {
                _logger.LogError(ex, "Decryption failed.");
                return BadRequest("Decryption failed.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error during registration.");
                return StatusCode(500,  $"Unexpected error.\n{ex.Message}");
            }
        }
    }

}
