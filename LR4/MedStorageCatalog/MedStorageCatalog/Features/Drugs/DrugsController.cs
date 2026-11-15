using MediatR;
using Microsoft.AspNetCore.Mvc;
using MedStorage.Catalog.Api.Features.Drugs.CreateCustomDrug;
using MedStorage.Catalog.Api.Features.Drugs.SearchDrug;
using MedStorage.Catalog.Api.Features.Drugs.GetDrugById;

namespace MedStorage.Catalog.Api.Features.Drugs;

[ApiController]
[Route("api/v1/[controller]")]
public class DrugsController(IMediator mediator) : ControllerBase
{
    [HttpPost("custom")]
    public async Task<IActionResult> CreateCustomDrug([FromBody] CreateCustomDrugCommand command)
    {
        var drugId = await mediator.Send(command);
        return Ok(new { Id = drugId });
    }

    // Used synchronously by the Inventory Service to resolve drug identifiers
    [HttpGet("search")]
    public async Task<IActionResult> Search([FromQuery] string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return BadRequest(new { Error = "Query parameter 'name' is required." });

        var drug = await mediator.Send(new SearchDrugQuery(name));
        return drug is null ? NotFound() : Ok(drug);
    }

    // Resolves drug names for kit items, which only hold the drug reference
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id)
    {
        var drug = await mediator.Send(new GetDrugByIdQuery(id));
        return drug is null ? NotFound() : Ok(drug);
    }
}
