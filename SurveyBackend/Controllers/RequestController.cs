using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

using SurveyBackend.Models;

namespace SurveyBackend.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [EnableCors("AllowAll")]
    public class RequestController : ControllerBase
    {
        private readonly ILogger<RequestController> _logger;
        private readonly IConfiguration _configuration;
        private readonly MainDbContext _db;

        public RequestController(ILogger<RequestController> logger, IConfiguration configuration, MainDbContext db)
        {
            _logger = logger;
            _configuration = configuration;
            _db = db;
        }

        [HttpGet("{id}")]
        public async Task<ActionResult> GetRequestInfo(string id)
        {
            var request = await _db.Requests
                                .FirstOrDefaultAsync(r => r.RequestId == id);
            if (request is null)
            {
                return NotFound(new
                {
                    status = 404,
                    error = "Cannot find avaliable request with the provided RequestId.\n" +
                    "Is RequestId out-dated?"
                }
                );
            }
            return Ok(new
            {
                status = 0,
                requestId = request.RequestId,
                userId = request.UserId,
                requestType = request.RequestType.ToString(),
                createTime = request.CreatedAt
            });
        }

        [HttpGet("{id}/user")]
        public async Task<ActionResult> GetUserOfRequest(string id)
        {
            var request = await _db.Requests
                                .Include(r => r.User)
                                .FirstOrDefaultAsync(r => r.RequestId == id);
            if (request is null)
            {
                return NotFound(new
                {
                    status = 404,
                    error = "Cannot find avaliable request with the provided RequestId.\n" +
                    "Is RequestId out-dated?"
                }
                );
            }
            return Ok(new
            {
                status = 0,
                userId = request.User.UserId,
                qqId = request.User.QQId
            });
        }
    }

}