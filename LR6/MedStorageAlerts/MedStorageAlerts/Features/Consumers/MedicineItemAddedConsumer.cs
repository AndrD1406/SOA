using MassTransit;
using MedStorage.Alerts.Api.Features.Tracking;
using MedStorage.Contracts;

namespace MedStorage.Alerts.Api.Features.Consumers;

public class MedicineItemAddedConsumer(
    ExpiryTracker tracker,
    IConfiguration configuration,
    ILogger<MedicineItemAddedConsumer> logger) : IConsumer<MedicineItemAddedEvent>
{
    public async Task Consume(ConsumeContext<MedicineItemAddedEvent> context)
    {
        var message = context.Message;
        logger.LogInformation("Processing tracking registration for item {ItemId}", message.ItemId);

        // SAGA test: fail on purpose so that, after the retries are exhausted, MassTransit moves the
        // message to the _error queue (DLQ) and publishes Fault<MedicineItemAddedEvent>, which the
        // Inventory Service compensates. Turn off with FaultSimulation__FailMedicineItemAdded=false.
        if (configuration.GetValue<bool>("FaultSimulation:FailMedicineItemAdded"))
            throw new Exception("Simulated DB failure");

        var result = await tracker.RegisterAsync(message, context.CancellationToken);

        if (result.AlreadyTracked)
            logger.LogInformation("Item {ItemId} is already tracked, skipping", message.ItemId);
        else
            logger.LogInformation("Tracked batch created with ID {BatchId}", result.Batch.Id);
    }
}
