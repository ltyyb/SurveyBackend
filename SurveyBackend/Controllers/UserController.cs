using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;

namespace SurveyBackend.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [EnableCors("AllowAll")]
    public class UserController : ControllerBase
    {
    }

}
