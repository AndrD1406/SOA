namespace MedStorage.Alerts.Api.Domain.Entities;

public class AlertNotification
{
    public Guid Id { get; set; }
    public Guid TrackedBatchId { get; set; }
    public string Message { get; set; } = string.Empty;
    public bool IsRead { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
