using Microsoft.EntityFrameworkCore;
using MedStorage.Alerts.Api.Infrastructure.Database;
using MedStorage.Alerts.Api.Domain.Entities;
using MedStorage.Contracts;

namespace MedStorage.Alerts.Api.Features.Tracking;

public record TrackingResult(TrackedBatch Batch, bool AlreadyTracked);

// Registers a medicine batch for expiry tracking. Shared by the RabbitMQ consumer (LR3)
// and the A2A agent endpoint (LR4), so both transports apply the same business rules.
public class ExpiryTracker(AlertsDbContext dbContext)
{
    public const int ExpiringSoonDays = 30;

    public async Task<TrackingResult> RegisterAsync(MedicineItemAddedEvent message, CancellationToken cancellationToken)
    {
        // Delivery is at least once (and A2A may race with RabbitMQ); skip items that are already tracked
        var existing = await dbContext.TrackedBatches
            .FirstOrDefaultAsync(b => b.ItemId == message.ItemId, cancellationToken);
        if (existing is not null)
            return new TrackingResult(existing, AlreadyTracked: true);

        // Npgsql only accepts UTC for timestamptz
        var expiryDate = message.ExpiryDate.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(message.ExpiryDate, DateTimeKind.Utc)
            : message.ExpiryDate.ToUniversalTime();

        var batch = new TrackedBatch
        {
            Id = Guid.NewGuid(),
            ItemId = message.ItemId,
            KitId = message.KitId,
            DrugId = message.DrugId,
            OwnerUserId = message.OwnerUserId,
            EffectiveExpiryDate = expiryDate,
            Status = expiryDate <= DateTime.UtcNow ? "Expired" : "Valid",
            CreatedAt = DateTime.UtcNow
        };

        dbContext.TrackedBatches.Add(batch);
        await dbContext.SaveChangesAsync(cancellationToken);
        return new TrackingResult(batch, AlreadyTracked: false);
    }

    public static string Classify(DateTime expiryDate) => (expiryDate - DateTime.UtcNow).TotalDays switch
    {
        <= 0 => "Expired",
        <= ExpiringSoonDays => "ExpiringSoon",
        _ => "Valid"
    };
}
