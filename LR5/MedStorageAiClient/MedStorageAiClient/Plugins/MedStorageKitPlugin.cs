using System.ComponentModel;
using Microsoft.SemanticKernel;
using MedStorage.AiClient.Api;
using MedStorage.AiClient.Guardrails;

namespace MedStorage.AiClient.Plugins;

// Tools exposed to the LLM (Function Calling, Task 1 - base level).
// Semantic Kernel turns every [KernelFunction] into a JSON-schema tool definition; the
// [Description] attributes are what the model reads to decide when and how to call a tool.
public class MedStorageKitPlugin(MedStorageApiClient api, Guid kitId, TurnState turnState)
{
    [KernelFunction("check_medicine_exists")]
    [Description("Checks whether a medicine is in the user's home medicine kit. " +
                 "ALWAYS call this before add_medicine_to_kit.")]
    public async Task<object> CheckMedicineExistsAsync(
        [Description("Trade name of the medicine, e.g. 'Paracetamol'")] string tradeName,
        CancellationToken ct = default)
    {
        // Guardrail: the arguments come from the model, so they are validated like any untrusted input
        if (ToolArgumentValidator.ValidateTradeName(tradeName) is { } error)
            return new { error };

        var name = tradeName.Trim();
        var drug = await api.FindDrugAsync(name, ct);
        if (drug is null)
        {
            turnState.MarkChecked(name, existsInCatalog: false, existsInKit: false);
            return new { tradeName = name, existsInCatalog = false, existsInKit = false,
                         note = "The drug is not in the catalog, so it cannot be added to the kit." };
        }

        var items = await api.GetKitItemsAsync(kitId, drug.Id, ct);
        var active = items.Where(i => i.Status != "Failed").ToList();
        turnState.MarkChecked(name, existsInCatalog: true, existsInKit: active.Count > 0);

        return new
        {
            tradeName = drug.TradeName,
            existsInCatalog = true,
            existsInKit = active.Count > 0,
            totalQuantity = active.Sum(i => i.Quantity),
            batches = active.Select(i => new { i.Quantity, expiryDate = DateOnly.FromDateTime(i.ExpiryDate), i.Status })
        };
    }

    [KernelFunction("add_medicine_to_kit")]
    [Description("Adds a medicine batch to the user's home medicine kit. " +
                 "Only allowed after check_medicine_exists was called for the same medicine in this turn.")]
    public async Task<object> AddMedicineToKitAsync(
        [Description("Trade name of the medicine exactly as known to the catalog")] string tradeName,
        [Description("Number of packages, 1..100")] int quantity,
        [Description("Expiry date printed on the package, format yyyy-MM-dd")] string expiryDate,
        CancellationToken ct = default)
    {
        if ((ToolArgumentValidator.ValidateTradeName(tradeName)
             ?? ToolArgumentValidator.ValidateQuantity(quantity)) is { } error)
            return new { error };
        if (!ToolArgumentValidator.TryParseExpiryDate(expiryDate, out var expiry, out var dateError))
            return new { error = dateError };

        var result = await api.AddMedicineAsync(kitId, tradeName.Trim(), quantity, expiry, ct);

        return result.Success
            ? new { added = true, itemId = result.ItemId, tradeName, quantity, expiryDate = expiry,
                    note = "The item is Pending until the Alerts service registers it (LR3 saga)." }
            : new { added = false, error = result.Error };
    }

    [KernelFunction("get_kit_inventory")]
    [Description("Lists all medicine batches in the user's home medicine kit, ordered by expiry date.")]
    public async Task<object> GetKitInventoryAsync(CancellationToken ct = default)
    {
        var items = await api.GetKitItemsAsync(kitId, ct: ct);
        return new
        {
            count = items.Count,
            items = items.Select(i => new { i.ItemId, i.DrugId, i.Quantity, expiryDate = DateOnly.FromDateTime(i.ExpiryDate), i.Status })
        };
    }
}
