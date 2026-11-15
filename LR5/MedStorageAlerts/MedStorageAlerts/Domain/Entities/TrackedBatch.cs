namespace MedStorage.Alerts.Api.Domain.Entities;

public class TrackedBatch
{
    public Guid Id { get; set; }

    // References to entities owned by the Inventory and Catalog services (no cross-service FKs)
    public Guid ItemId { get; set; }
    public Guid KitId { get; set; }
    public Guid DrugId { get; set; }

    // Keycloak user id of the kit owner: users only see alerts for their own kits
    public Guid? OwnerUserId { get; set; }
    public DateTime EffectiveExpiryDate { get; set; }
    public string Status { get; set; } = "Valid";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
