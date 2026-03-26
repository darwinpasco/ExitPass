using Microsoft.AspNetCore.Mvc;

namespace ExitPass.CouponService.Api.Controllers;

[ApiController]
[Route("api/coupons/smoke")]
public sealed class CouponsSmokeController : ControllerBase
{
    [HttpGet]
    public IActionResult Get()
    {
        return Ok(new
        {
            service = "CouponsSmokeController",
            status = "OK",
            utcTimestamp = DateTime.UtcNow
        });
    }
}
