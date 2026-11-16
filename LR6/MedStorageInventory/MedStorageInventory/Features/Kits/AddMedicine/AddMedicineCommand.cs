using MediatR;

namespace MedStorage.Inventory.Api.Features.Kits.AddMedicine;

// OwnerUserId comes from the caller's token; the kit must belong to that user
public record AddMedicineCommand(Guid KitId, Guid OwnerUserId, string TradeName, int Quantity, DateTime ExpiryDate) : IRequest<Guid>;

public class KitNotFoundException(Guid kitId)
    : Exception($"Kit {kitId} not found.");

public class DrugNotFoundException(string tradeName)
    : Exception($"Drug '{tradeName}' not found in Catalog.");
