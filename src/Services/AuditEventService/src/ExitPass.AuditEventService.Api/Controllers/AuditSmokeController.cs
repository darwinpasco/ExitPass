using Microsoft.AspNetCore.Mvc;

namespace ExitPass.AuditEventService.Api.Controllers;

[ApiController]
[Route("api/audit/smoke")]
public sealed class AuditSmokeController : ControllerBase
{
    [HttpGet]
    public IActionResult Get()
    {
        return Ok(new
        {
            service = "AuditSmokeController",
            status = "OK",
            utcTimestamp = DateTime.UtcNow
        });
    }
}
