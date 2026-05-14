using Microsoft.AspNetCore.Mvc;

namespace ExitPass.GateIntegrationService.Api.Controllers;

/// <summary>
/// Exposes the Gate Integration Service smoke endpoint.
/// </summary>
[ApiController]
[Route("api/device/smoke")]
public sealed class GateSmokeController : ControllerBase
{
    /// <summary>
    /// Returns the current Gate Integration Service smoke status.
    /// </summary>
    /// <returns>The smoke status response.</returns>
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
