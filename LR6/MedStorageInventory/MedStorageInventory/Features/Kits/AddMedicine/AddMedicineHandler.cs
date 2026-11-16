using MediatR;
using Microsoft.EntityFrameworkCore;
using MassTransit;
using MedStorage.Inventory.Api.Infrastructure.Database;
using MedStorage.Inventory.Api.Domain.Entities;
using MedStorage.Contracts;
using MedStorage.Inventory.Api.Features.CatalogSync;

namespace MedStorage.Inventory.Api.Features.Kits.AddMedicine;

public class AddMedicineHandler(
    InventoryDbContext dbContext,
    CatalogServiceClient catalogClient,
    IPublishEndpoint publishEndpoint) : IRequestHandler<AddMedicineCommand, Guid>
{
    public async Task<Guid> Handle(AddMedicineCommand request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.TradeName))
            throw new ArgumentException("Trade name is required.", nameof(request.TradeName));
        if (request.Quantity <= 0)
            throw new ArgumentOutOfRangeException(nameof(request.Quantity), "Quantity must be positive.");

        // 0. Ownership: another user's kit looks exactly like a missing one (no information leak)
        var kitExists = await dbContext.Kits.AnyAsync(
            k => k.KitId == request.KitId && k.OwnerUserId == request.OwnerUserId, cancellationToken);
        if (!kitExists)
            throw new KitNotFoundException(request.KitId);

        // 1. Synchronous verification: Get Drug metadata from Catalog Service
        var drugId = await catalogClient.GetDrugIdByNameAsync(request.TradeName.Trim(), cancellationToken)
            ?? throw new DrugNotFoundException(request.TradeName);

        // 2. State modification: Create the inventory item in Pending state until the saga completes
        var item = new MedicineItem
        {
            Id = Guid.NewGuid(),
            KitId = request.KitId,
            DrugRefId = drugId,
            Quantity = request.Quantity,
            // Npgsql only accepts UTC for timestamptz; a date-only JSON value arrives as Unspecified
            FactoryExpiryDate = request.ExpiryDate.Kind == DateTimeKind.Unspecified
                ? DateTime.SpecifyKind(request.ExpiryDate, DateTimeKind.Utc)
                : request.ExpiryDate.ToUniversalTime(),
            AddedAt = DateTime.UtcNow,
            Status = MedicineItemStatus.Pending
        };

        dbContext.MedicineItems.Add(item);

        // 3. Asynchronous notification via the Transactional Outbox: with UseBusOutbox the scoped
        // IPublishEndpoint does not touch RabbitMQ, it adds an OutboxMessage row to this DbContext
        await publishEndpoint.Publish(new MedicineItemAddedEvent(
            item.Id,
            item.KitId,
            item.DrugRefId,
            item.Quantity,
            item.FactoryExpiryDate,
            item.AddedAt,
            request.OwnerUserId
        ), cancellationToken);

        // 4. One EF Core transaction commits both the item and the outbox message;
        // the outbox delivery service forwards the event to RabbitMQ afterwards
        await dbContext.SaveChangesAsync(cancellationToken);

        return item.Id;
    }
}
