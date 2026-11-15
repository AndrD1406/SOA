namespace MedStorage.Inventory.Api.Infrastructure.Auth;

public static class InventoryPolicies
{
    public const string Read = "RequireInventoryRead";
    public const string Write = "RequireInventoryWrite";
}
