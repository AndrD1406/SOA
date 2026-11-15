namespace MedStorage.Catalog.Api.Messages;

public record CustomDrugCreatedEvent(
    Guid DrugId, 
    string TradeName, 
    DateTime CreatedAt);
