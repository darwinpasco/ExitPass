using Microsoft.AspNetCore.Mvc;

namespace ExitPass.CentralPms.Api.Controllers;

[ApiController]
[Route("api/core/smoke")]
public sealed class CoreSmokeController : ControllerBase
{
    [HttpGet]
    public IActionResult Get()
    {
        return Ok(new
        {
            service = "CoreSmokeController",
            status = "OK",
            utcTimestamp = DateTime.UtcNow
        });
    }
}
