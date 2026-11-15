namespace MedStorage.Catalog.Api.Infrastructure.Auth;

public static class CatalogPolicies
{
    public const string Read = "RequireCatalogRead";
    public const string Write = "RequireCatalogWrite";
}
