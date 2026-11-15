using MediatR;

namespace MedStorage.Alerts.Api.Features.Alerts.GetExpiringItems;

public record GetExpiringItemsQuery(int DaysThreshold = 30) : IRequest<List<ExpiringBatchDto>>;

public record ExpiringBatchDto(Guid ItemId, Guid KitId, DateTime ExpiryDate, string Status);
