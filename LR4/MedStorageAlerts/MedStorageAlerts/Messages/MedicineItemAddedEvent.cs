using MassTransit;

// Contract matching the event published by the Inventory Service.
// The namespace must match the Inventory copy: MassTransit names Fault<MedicineItemAddedEvent> after the
// CLR namespace of the inner type and ignores its MessageUrn/EntityName attributes, so a shared
// namespace is what lets the faults raised here reach the Inventory Service's compensation consumer.
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
