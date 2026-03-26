using Microsoft.AspNetCore.Mvc;

namespace ExitPass.PaymentOrchestrator.Api.Controllers;

[ApiController]
[Route("api/payments/smoke")]
public sealed class PaymentsSmokeController : ControllerBase
{
    [HttpGet]
    public IActionResult Get()
    {
        return Ok(new
        {
            service = "PaymentsSmokeController",
            status = "OK",
            utcTimestamp = DateTime.UtcNow
        });
    }
}
