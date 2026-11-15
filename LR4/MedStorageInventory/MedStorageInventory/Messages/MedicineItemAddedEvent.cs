using MassTransit;

// Consumed by the Alerts Service, which keeps its own copy of this contract in the same namespace.
// The namespace must match across services: MassTransit names Fault<MedicineItemAddedEvent> after the
// CLR namespace of the inner type and ignores its MessageUrn/EntityName attributes, so a shared
// namespace is what lets the Inventory Service receive the faults raised by the Alerts Service.
namespace MedStorage.Contracts;

[MessageUrn("medstorage:inventory:MedicineItemAddedEvent")]
[EntityName("medstorage.medicine-item-added")]
public record MedicineItemAddedEvent(
    Guid ItemId,
    Guid KitId,
    Guid DrugId,
    int Quantity,
    DateTime ExpiryDate,
    DateTime AddedAt);
