using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MedStorage.Inventory.Api.Domain.Entities;
using MedStorage.Inventory.Api.Infrastructure.Auth;
using MedStorage.Inventory.Api.Infrastructure.Database;

namespace MedStorage.Inventory.Api.Features.Kits;

// The owner is always the caller from the token: no UserId in DTOs or URLs,
// so one user can never list or create kits on behalf of another.
[ApiController]
[Route("api/v1/inventory/kits")]
public class KitsController(InventoryDbContext dbContext) : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = InventoryPolicies.Read)]
    public async Task<IActionResult> GetUserKits(CancellationToken cancellationToken)
    {
        var userId = User.GetUserId();

        var kits = await dbContext.Kits
            .AsNoTracking()
            .Where(k => k.OwnerUserId == userId)
            .OrderBy(k => k.CreatedAt)
            .Select(k => new KitDto(k.KitId, k.Name, k.CreatedAt))
            .ToListAsync(cancellationToken);

        return Ok(kits);
    }

    [HttpPost]
    [Authorize(Policy = InventoryPolicies.Write)]
    public async Task<IActionResult> CreateKit([FromBody] CreateKitDto dto, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(dto.Name) || dto.Name.Trim().Length > 100)
            return BadRequest(new { Error = "Name is required and must be at most 100 characters." });

        var kit = new FirstAidKit
        {
            KitId = Guid.NewGuid(),
            OwnerUserId = User.GetUserId(),
            Name = dto.Name.Trim(),
            CreatedAt = DateTime.UtcNow
        };

        dbContext.Kits.Add(kit);
        await dbContext.SaveChangesAsync(cancellationToken);

        return CreatedAtAction(nameof(GetUserKits), null, new KitDto(kit.KitId, kit.Name, kit.CreatedAt));
    }
}

public record CreateKitDto(string Name);

public record KitDto(Guid KitId, string Name, DateTime CreatedAt);
