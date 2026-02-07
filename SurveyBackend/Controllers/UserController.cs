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



    }

}
