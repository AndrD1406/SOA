using MediatR;
using Microsoft.EntityFrameworkCore;
using MedStorage.Catalog.Api.Infrastructure.Database;
using MedStorage.Catalog.Api.Features.Drugs.SearchDrug;

namespace MedStorage.Catalog.Api.Features.Drugs.GetDrugById;

public record GetDrugByIdQuery(Guid Id) : IRequest<DrugDto?>;

public class GetDrugByIdHandler(CatalogDbContext dbContext) : IRequestHandler<GetDrugByIdQuery, DrugDto?>
{
    public async Task<DrugDto?> Handle(GetDrugByIdQuery request, CancellationToken cancellationToken) =>
        await dbContext.Drugs
            .AsNoTracking()
            .Where(d => d.Id == request.Id)
            .Select(d => new DrugDto(d.Id, d.TradeName))
            .FirstOrDefaultAsync(cancellationToken);
}
