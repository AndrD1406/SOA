using MediatR;
using Microsoft.EntityFrameworkCore;
using MedStorage.Catalog.Api.Infrastructure.Database;

namespace MedStorage.Catalog.Api.Features.Drugs.SearchDrug;

public record SearchDrugQuery(string Name) : IRequest<DrugDto?>;

public record DrugDto(Guid Id, string TradeName);

public class SearchDrugHandler(CatalogDbContext dbContext) : IRequestHandler<SearchDrugQuery, DrugDto?>
{
    public async Task<DrugDto?> Handle(SearchDrugQuery request, CancellationToken cancellationToken)
    {
        var name = request.Name.Trim().ToLower();

        // Case-insensitive exact match on trade name
        return await dbContext.Drugs
            .AsNoTracking()
            .Where(d => d.TradeName.ToLower() == name)
            .Select(d => new DrugDto(d.Id, d.TradeName))
            .FirstOrDefaultAsync(cancellationToken);
    }
}
