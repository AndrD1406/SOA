namespace MedStorage.Inventory.Api.Domain.Entities;

// A user's first-aid kit. Ownership is the Keycloak user id ("sub" claim) - there is no local users table.
public class FirstAidKit
{
    public Guid KitId { get; set; }
    public Guid OwnerUserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}
