using MediatR;
using MassTransit;
using MedStorage.Catalog.Api.Infrastructure.Database;
using MedStorage.Catalog.Api.Domain.Entities;
using MedStorage.Catalog.Api.Messages;

namespace MedStorage.Catalog.Api.Features.Drugs.CreateCustomDrug;

public class CreateCustomDrugHandler(
    CatalogDbContext dbContext, 
    IPublishEndpoint publishEndpoint) : IRequestHandler<CreateCustomDrugCommand, Guid>
{
    public async Task<Guid> Handle(CreateCustomDrugCommand request, CancellationToken cancellationToken)
    {
        // 1. Create and save entity
        var drug = Drug.CreateManualEntry(request.TradeName, request.ActiveSubstance, "Tablet", 24);
        dbContext.Drugs.Add(drug);
        await dbContext.SaveChangesAsync(cancellationToken);

        // 2. Publish async event to RabbitMQ
        await publishEndpoint.Publish(new CustomDrugCreatedEvent(
            drug.Id, 
            drug.TradeName, 
            DateTime.UtcNow
        ), cancellationToken);

        return drug.Id;
    }
}
