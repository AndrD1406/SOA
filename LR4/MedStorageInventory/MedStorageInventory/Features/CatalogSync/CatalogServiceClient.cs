using System.Net;
using System.Net.Http.Json;

namespace MedStorage.Inventory.Api.Features.CatalogSync;

public class CatalogServiceClient(HttpClient httpClient)
{
    public async Task<Guid?> GetDrugIdByNameAsync(string tradeName, CancellationToken cancellationToken)
    {
        // Synchronous call to the Catalog microservice
        using var response = await httpClient.GetAsync(
            $"/api/v1/drugs/search?name={Uri.EscapeDataString(tradeName)}",
            cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;

        response.EnsureSuccessStatusCode();

        var drug = await response.Content.ReadFromJsonAsync<CatalogDrugDto>(cancellationToken);
        return drug?.Id;
    }
}

public record CatalogDrugDto(Guid Id, string TradeName);
