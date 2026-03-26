using Microsoft.AspNetCore.Mvc;

namespace ExitPass.GateIntegrationService.Api.Controllers;

[ApiController]
[Route("api/device/smoke")]
public sealed class GateSmokeController : ControllerBase
{
    [HttpGet]
    public IActionResult Get()
    {
        return Ok(new
        {
            service = "GateSmokeController",
            status = "OK",
            utcTimestamp = DateTime.UtcNow
        });
    }
}
