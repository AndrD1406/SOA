using MediatR;

namespace MedStorage.Alerts.Api.Features.Alerts.GetExpiringItems;

// OwnerUserId = null returns batches of all users (service-to-service A2A calls only)
public record GetExpiringItemsQuery(int DaysThreshold = 30, Guid? OwnerUserId = null) : IRequest<List<ExpiringBatchDto>>;

public record ExpiringBatchDto(Guid ItemId, Guid KitId, DateTime ExpiryDate, string Status);
