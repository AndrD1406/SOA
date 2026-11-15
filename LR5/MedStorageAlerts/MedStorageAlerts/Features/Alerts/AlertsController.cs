using MediatR;
using Microsoft.AspNetCore.Authorization;
using MedStorage.Alerts.Api.Infrastructure.Auth;
using Microsoft.AspNetCore.Mvc;
using MedStorage.Alerts.Api.Features.Alerts.GetExpiringItems;

namespace MedStorage.Alerts.Api.Features.Alerts;

[ApiController]
[Route("api/v1/[controller]")]
[Authorize(Policy = AlertsPolicies.Read)]
public class AlertsController(IMediator mediator) : ControllerBase
{
    // Only the caller's own batches: the user id comes from the token, not from the query string
    [HttpGet("expiring")]
    public async Task<IActionResult> GetExpiringItems([FromQuery] int days = 30, CancellationToken cancellationToken = default)
    {
        if (days < 0)
            return BadRequest(new { error = "Days must be zero or positive." });

        var items = await mediator.Send(new GetExpiringItemsQuery(days, User.GetUserId()), cancellationToken);
        return Ok(items);
    }
}
