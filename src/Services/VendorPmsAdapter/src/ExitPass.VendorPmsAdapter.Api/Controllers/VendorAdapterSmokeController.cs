using Microsoft.AspNetCore.Mvc;

namespace ExitPass.VendorPmsAdapter.Api.Controllers;

[ApiController]
[Route("api/vendor-adapter/smoke")]
public sealed class VendorAdapterSmokeController : ControllerBase
{
    [HttpGet]
    public IActionResult Get()
    {
        return Ok(new
        {
            service = "VendorAdapterSmokeController",
            status = "OK",
            utcTimestamp = DateTime.UtcNow
        });
    }
}
