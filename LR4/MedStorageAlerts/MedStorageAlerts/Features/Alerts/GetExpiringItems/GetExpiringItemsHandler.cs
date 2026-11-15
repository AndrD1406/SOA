using MediatR;
using Microsoft.EntityFrameworkCore;
using MedStorage.Alerts.Api.Infrastructure.Database;

namespace MedStorage.Alerts.Api.Features.Alerts.GetExpiringItems;

// Returns items requiring user attention within a specific day threshold.
public class GetExpiringItemsHandler(AlertsDbContext dbContext)
    : IRequestHandler<GetExpiringItemsQuery, List<ExpiringBatchDto>>
{
    public async Task<List<ExpiringBatchDto>> Handle(GetExpiringItemsQuery request, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var targetDate = now.AddDays(request.DaysThreshold);

        return await dbContext.TrackedBatches
            .AsNoTracking()
            .Where(b => b.EffectiveExpiryDate <= targetDate)
            .OrderBy(b => b.EffectiveExpiryDate)
            // Status is stored at intake; report batches that have since expired as such
            .Select(b => new ExpiringBatchDto(
                b.ItemId,
                b.KitId,
                b.EffectiveExpiryDate,
                b.EffectiveExpiryDate <= now ? "Expired" : b.Status))
            .ToListAsync(cancellationToken);
    }
}
