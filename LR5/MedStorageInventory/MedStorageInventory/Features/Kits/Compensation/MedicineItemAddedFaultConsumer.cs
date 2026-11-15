using MassTransit;
using Microsoft.EntityFrameworkCore;
using MedStorage.Contracts;
using MedStorage.Inventory.Api.Domain.Entities;
using MedStorage.Inventory.Api.Infrastructure.Database;

namespace MedStorage.Inventory.Api.Features.Kits.Compensation;

// SAGA (choreography) compensating transaction: the Alerts Service failed to process
// MedicineItemAddedEvent after all retries, so MassTransit published Fault<MedicineItemAddedEvent>
// and the original message landed in the _error queue (DLQ). Here we roll back the local change.
public class MedicineItemAddedFaultConsumer(
    InventoryDbContext dbContext,
    ILogger<MedicineItemAddedFaultConsumer> logger) : IConsumer<Fault<MedicineItemAddedEvent>>
{
    public async Task Consume(ConsumeContext<Fault<MedicineItemAddedEvent>> context)
    {
        var itemId = context.Message.Message.ItemId;
        var reason = context.Message.Exceptions.FirstOrDefault()?.Message ?? "unknown error";

        logger.LogWarning("Alerts Service failed to process item {ItemId}: {Reason}. Compensating...", itemId, reason);

        var item = await dbContext.MedicineItems.FirstOrDefaultAsync(i => i.Id == itemId, context.CancellationToken);
        if (item is null)
        {
            logger.LogWarning("Item {ItemId} not found, nothing to compensate", itemId);
            return;
        }

        // Faults can be redelivered; marking as Failed is idempotent.
        // A soft status change (instead of deleting) keeps an audit trail of the rolled-back item.
        if (item.Status == MedicineItemStatus.Failed)
            return;

        item.Status = MedicineItemStatus.Failed;
        await dbContext.SaveChangesAsync(context.CancellationToken);

        logger.LogInformation("Item {ItemId} marked as {Status}", itemId, item.Status);
    }
}
