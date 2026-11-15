using MediatR;
using Microsoft.AspNetCore.Mvc;
using MedStorage.Inventory.Api.Features.Kits.AddMedicine;
using MedStorage.Inventory.Api.Features.Kits.GetKitItems;

namespace MedStorage.Inventory.Api.Features.Kits;

[ApiController]
[Route("api/v1/inventory/kits")]
public class InventoryController(IMediator mediator) : ControllerBase
{
    [HttpPost("{kitId:guid}/items")]
    public async Task<IActionResult> AddMedicine(Guid kitId, [FromBody] AddMedicineDto dto)
    {
        try
        {
            var command = new AddMedicineCommand(kitId, dto.TradeName, dto.Quantity, dto.ExpiryDate);
            var itemId = await mediator.Send(command);
            return Ok(new { ItemId = itemId });
        }
        catch (DrugNotFoundException ex)
        {
            return NotFound(new { Error = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { Error = ex.Message });
        }
    }

    // Read side of the kit: used by the LLM client (LR4, Task 1) and the MCP server (LR4, Task 2)
    [HttpGet("{kitId:guid}/items")]
    public async Task<IActionResult> GetItems(Guid kitId, [FromQuery] Guid? drugId, CancellationToken cancellationToken)
    {
        var items = await mediator.Send(new GetKitItemsQuery(kitId, drugId), cancellationToken);
        return Ok(items);
    }
}

public record AddMedicineDto(string TradeName, int Quantity, DateTime ExpiryDate);
