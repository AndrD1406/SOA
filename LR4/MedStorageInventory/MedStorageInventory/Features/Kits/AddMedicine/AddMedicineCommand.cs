using MediatR;

namespace MedStorage.Inventory.Api.Features.Kits.AddMedicine;

public record AddMedicineCommand(Guid KitId, string TradeName, int Quantity, DateTime ExpiryDate) : IRequest<Guid>;

public class DrugNotFoundException(string tradeName)
    : Exception($"Drug '{tradeName}' not found in Catalog.");
