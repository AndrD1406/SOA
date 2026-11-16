using MediatR;

namespace MedStorage.Catalog.Api.Features.Drugs.CreateCustomDrug;

public record CreateCustomDrugCommand(string TradeName, string ActiveSubstance) : IRequest<Guid>;
