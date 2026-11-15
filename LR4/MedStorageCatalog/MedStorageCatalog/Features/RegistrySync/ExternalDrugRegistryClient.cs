using System.Net.Http.Json;

namespace MedStorage.Catalog.Api.Features.RegistrySync;

public class ExternalDrugRegistryClient(HttpClient httpClient)
{
    public async Task<ExternalDrugDto?> FetchDrugMetadataAsync(string tradeName, CancellationToken cancellationToken)
    {
        // Makes a synchronous HTTP GET request to another service/API
        return await httpClient.GetFromJsonAsync<ExternalDrugDto>(
            $"/api/registry/search?name={Uri.EscapeDataString(tradeName)}", 
            cancellationToken);
    }
}

public record ExternalDrugDto(string Name, string Substance);
