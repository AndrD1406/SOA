using System.Net;
using System.Net.Http.Json;

namespace MedStorage.AiClient.Api;

// REST client for the MedStorage microservices (LR1 Catalog + LR3 Inventory).
// The LLM never talks HTTP itself: it can only reach these typed methods through the plugin.
public class MedStorageApiClient(HttpClient catalog, HttpClient inventory)
{
    public async Task<DrugDto?> FindDrugAsync(string tradeName, CancellationToken ct = default)
    {
        using var response = await catalog.GetAsync(
            $"/api/v1/drugs/search?name={Uri.EscapeDataString(tradeName)}", ct);

        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<DrugDto>(ct);
    }

    public async Task<List<KitItemDto>> GetKitItemsAsync(Guid kitId, Guid? drugId = null, CancellationToken ct = default)
    {
        var url = $"/api/v1/inventory/kits/{kitId}/items" + (drugId is { } id ? $"?drugId={id}" : "");
        return await inventory.GetFromJsonAsync<List<KitItemDto>>(url, ct) ?? [];
    }

    public async Task<AddMedicineResult> AddMedicineAsync(
        Guid kitId, string tradeName, int quantity, DateOnly expiryDate, CancellationToken ct = default)
    {
        using var response = await inventory.PostAsJsonAsync(
            $"/api/v1/inventory/kits/{kitId}/items",
            new { TradeName = tradeName, Quantity = quantity, ExpiryDate = expiryDate.ToDateTime(TimeOnly.MinValue) },
            ct);

        if (response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadFromJsonAsync<AddMedicineResponse>(ct);
            return new AddMedicineResult(true, body?.ItemId, null);
        }

        // 400/404 from Inventory carry { error: "..." }; pass the reason back to the LLM
        var error = await response.Content.ReadAsStringAsync(ct);
        return new AddMedicineResult(false, null, $"HTTP {(int)response.StatusCode}: {error}");
    }

    private record AddMedicineResponse(Guid ItemId);
}

public record DrugDto(Guid Id, string TradeName);

public record KitItemDto(Guid ItemId, Guid DrugId, int Quantity, DateTime ExpiryDate, string Status);

public record AddMedicineResult(bool Success, Guid? ItemId, string? Error);
