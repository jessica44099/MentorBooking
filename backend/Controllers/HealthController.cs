using Microsoft.AspNetCore.Mvc;

namespace MentorBooking.Api.Controllers
{
    [ApiController]
    [Route("api/health")]
    public class HealthController : ControllerBase
    {
        [HttpGet]
        public IActionResult GetHealth()
        {
            return Ok(new
            {
                status = "Healthy",
                application = "MentorBooking.Api",
                message = "Backend is running successfully"
            });
        }
    }
}