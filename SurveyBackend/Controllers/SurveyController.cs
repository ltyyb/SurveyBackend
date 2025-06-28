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

        // 一个接受POST的方法, 前端通过此接口提供 userId 及 问卷结果JSON 提交结果
        [HttpPost("submitSurvey")]
        public ActionResult SubmitSurvey([FromBody] SurveySubmission submission)
        {
            if (submission == null || string.IsNullOrEmpty(submission.userId) || submission.Answers == null)
            {
                return BadRequest("Invalid survey submission data.");
            }
            var surveyUser = SurveyUser.GetUserById(submission.userId);
            if (surveyUser is null)
            {
                return NotFound($"No user found with UserId: {submission.userId}");
            }
            // 这里可以添加逻辑来处理提交的问卷结果
            // 例如，保存到数据库或进行其他处理
            _logger.LogInformation($"Survey submitted for UserId: {submission.userId} ({surveyUser.QQId})");
            return Ok("Survey submitted successfully.");
        }

    }
}
