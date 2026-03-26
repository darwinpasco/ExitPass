using Microsoft.AspNetCore.Mvc;

namespace ExitPass.SessionService.Api.Controllers;

[ApiController]
[Route("api/sessions/smoke")]
public sealed class SessionsSmokeController : ControllerBase
{
    [HttpGet]
    public IActionResult Get()
    {
        return Ok(new
        {
            service = "SessionsSmokeController",
            status = "OK",
            utcTimestamp = DateTime.UtcNow
        });
    }
}
