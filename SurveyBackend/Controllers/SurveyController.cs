using Microsoft.AspNetCore.Mvc;

namespace SurveyBackend.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class SurveyController : ControllerBase
    {
        private readonly ILogger<SurveyController> _logger;

        public SurveyController(ILogger<SurveyController> logger)
        {
            _logger = logger;
        }

        // 一个接受POST的方法，允许前端POST UserId, 后端尝试创建 SurveyUser
        [HttpPost("checkUserId")]
        public ActionResult<SurveyUser> CheckUserId([FromBody] string userId)
        {
            if (string.IsNullOrEmpty(userId))
            {
                return BadRequest("UserId cannot be null or empty.");
            }
            // 使用 SurveyUser 的工厂方法获取用户信息
            var surveyUser = SurveyUser.GetUserById(userId);
            if (surveyUser is null)
            {
                return NotFound($"No user found with UserId: {userId}");
            }
            return Ok(surveyUser);
        }
    }
}
