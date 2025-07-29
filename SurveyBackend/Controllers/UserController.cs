using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;

namespace SurveyBackend.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [EnableCors("AllowAll")]
    public class UserController : ControllerBase
    {
        private readonly ILogger<UserController> _logger;
        private readonly IConfiguration _configuration;

        public UserController(ILogger<UserController> logger, IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;
        }

        public class UserIdPostData
        {
            public string UserId { get; set; } = string.Empty;
        }
        /// <summary>
        /// <para>! 已弃用 !</para>
        /// <para>一个接受POST的方法，允许前端POST UserId, 后端尝试创建 SurveyUser</para>
        /// </summary>
        /// <param name="userIdPostData"></param>
        /// <returns></returns>
        [HttpPost("checkUserId")]
        public async Task<ActionResult<SurveyUser>> CheckUserId([FromBody] UserIdPostData userIdPostData)
        {
            if (string.IsNullOrWhiteSpace(userIdPostData.UserId))
            {
                return BadRequest("UserId cannot be null or empty.");
            }
            var connStr = _configuration.GetConnectionString("DefaultConnection");

            if (string.IsNullOrWhiteSpace(connStr))
            {
                _logger.LogError("连接字符串未配置。请前往 appsettings.json 添加 \"DefaultConnection\" 连接字符串。");
                return StatusCode(500, "Server Config Error");
            }
            // 使用 SurveyUser 的工厂方法获取用户信息
            var surveyUser = await SurveyUser.GetUserByIdAsync(userIdPostData.UserId, _logger, connStr);
            if (surveyUser is null)
            {
                return NotFound($"No user found with UserId: {userIdPostData.UserId}");
            }
            return Ok(surveyUser);
        }
    }

}
