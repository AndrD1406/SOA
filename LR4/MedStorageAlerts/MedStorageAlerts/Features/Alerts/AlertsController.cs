using MediatR;
using Microsoft.AspNetCore.Mvc;
using MedStorage.Alerts.Api.Features.Alerts.GetExpiringItems;

namespace MedStorage.Alerts.Api.Features.Alerts;

[ApiController]
[Route("api/v1/[controller]")]
public class AlertsController(IMediator mediator) : ControllerBase
{
    [HttpGet("expiring")]
    public async Task<IActionResult> GetExpiringItems([FromQuery] int days = 30, CancellationToken cancellationToken = default)
    {
        if (days < 0)
            return BadRequest(new { error = "Days must be zero or positive." });

        var items = await mediator.Send(new GetExpiringItemsQuery(days), cancellationToken);
        return Ok(items);
    }
}
