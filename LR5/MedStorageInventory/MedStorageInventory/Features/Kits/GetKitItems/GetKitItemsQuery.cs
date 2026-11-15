using MediatR;
using Microsoft.EntityFrameworkCore;
using MedStorage.Inventory.Api.Infrastructure.Database;

namespace MedStorage.Inventory.Api.Features.Kits.GetKitItems;

// Returns null when the kit does not exist or belongs to another user
public record GetKitItemsQuery(Guid KitId, Guid OwnerUserId, Guid? DrugId) : IRequest<List<KitItemDto>?>;

public record KitItemDto(Guid ItemId, Guid DrugId, int Quantity, DateTime ExpiryDate, string Status);

public class GetKitItemsHandler(InventoryDbContext dbContext) : IRequestHandler<GetKitItemsQuery, List<KitItemDto>?>
{
    public async Task<List<KitItemDto>?> Handle(GetKitItemsQuery request, CancellationToken cancellationToken)
    {
        var owned = await dbContext.Kits.AnyAsync(
            k => k.KitId == request.KitId && k.OwnerUserId == request.OwnerUserId, cancellationToken);
        if (!owned)
            return null;

        var query = dbContext.MedicineItems
            .AsNoTracking()
            .Where(i => i.KitId == request.KitId);

        // Optional filter used by AI clients to answer "is drug X in my kit?"
        if (request.DrugId is { } drugId)
            query = query.Where(i => i.DrugRefId == drugId);

        return await query
            .OrderBy(i => i.FactoryExpiryDate)
            .Select(i => new KitItemDto(i.Id, i.DrugRefId, i.Quantity, i.FactoryExpiryDate, i.Status.ToString()))
            .ToListAsync(cancellationToken);
    }
}
