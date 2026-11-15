namespace MedStorage.Inventory.Api.Domain.Entities;

public class MedicineItem
{
    public Guid Id { get; set; }
    public Guid KitId { get; set; }

    // Reference to the Drug owned by the Catalog Service (no cross-service FK)
    public Guid DrugRefId { get; set; }
    public int Quantity { get; set; }
    public DateTime FactoryExpiryDate { get; set; }
    public DateTime AddedAt { get; set; }

    // Saga state: Pending until downstream services process the item, Failed after compensation
    public MedicineItemStatus Status { get; set; } = MedicineItemStatus.Pending;
}

public enum MedicineItemStatus
{
    Pending,
    Active,
    Failed
}
