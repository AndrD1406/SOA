using MassTransit;
using Microsoft.EntityFrameworkCore;
using MedStorage.Contracts;
using MedStorage.Inventory.Api.Domain.Entities;
using MedStorage.Inventory.Api.Infrastructure.Database;

namespace MedStorage.Inventory.Api.Features.Kits.UserRegistration;

// Event-driven onboarding: every newly registered user gets a default kit.
public class UserRegisteredConsumer(
    InventoryDbContext dbContext,
    ILogger<UserRegisteredConsumer> logger) : IConsumer<UserRegisteredEvent>
{
    public const string DefaultKitName = "Основна домашня";

    public async Task Consume(ConsumeContext<UserRegisteredEvent> context)
    {
        var userId = context.Message.UserId;

        // Idempotent: a redelivered event must not create a second default kit
        if (await dbContext.Kits.AnyAsync(k => k.OwnerUserId == userId, context.CancellationToken))
        {
            logger.LogInformation("User {UserId} already has a kit, skipping", userId);
            return;
        }

        var kit = new FirstAidKit
        {
            KitId = Guid.NewGuid(),
            OwnerUserId = userId,
            Name = DefaultKitName,
            CreatedAt = DateTime.UtcNow
        };

        dbContext.Kits.Add(kit);
        await dbContext.SaveChangesAsync(context.CancellationToken);

        logger.LogInformation("Default kit {KitId} created for new user {Username} ({UserId})",
            kit.KitId, context.Message.Username, userId);
    }
}
