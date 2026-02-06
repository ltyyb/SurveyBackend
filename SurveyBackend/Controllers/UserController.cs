using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SurveyBackend.Models;

namespace SurveyBackend.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [EnableCors("AllowAll")]
    public class UserController : ControllerBase
    {
        private readonly ILogger<UserController> _logger;
        private readonly IConfiguration _configuration;
        private readonly MainDbContext _db;

        public UserController(ILogger<UserController> logger, IConfiguration configuration, MainDbContext db)
        {
            _logger = logger;
            _configuration = configuration;
            _db = db;
        }



        /// <summary>
        /// 从 RequestId 获取 UserId
        /// </summary>
        [HttpGet("request/{id}")]
        public async Task<ActionResult<object>> GetUserIdFromRequestId(string id)
        {
            var request = await _db.Requests
                                .FirstOrDefaultAsync(r => r.RequestId == id && r.RequestType == RequestType.SurveyAccess);
            if (request is null)
            {
                return NotFound(new
                {
                    status = 404,
                    error = "Cannot find avaliable user with the provided RequestId.\n" +
                    "Is RequestId out-dated?"
                }
                );
            }
            
            return Ok(new
            {
                status = 0,
                userId = request.UserId
            });
        }
    }

}
