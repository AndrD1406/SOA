using Microsoft.EntityFrameworkCore;
using MedStorage.Alerts.Api.Domain.Entities;

namespace MedStorage.Alerts.Api.Infrastructure.Database;

public class AlertsDbContext(DbContextOptions<AlertsDbContext> options) : DbContext(options)
{
    public DbSet<TrackedBatch> TrackedBatches => Set<TrackedBatch>();
    public DbSet<AlertNotification> AlertNotifications => Set<AlertNotification>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // One tracked batch per inventory item: makes redelivered events idempotent
        modelBuilder.Entity<TrackedBatch>().HasIndex(b => b.ItemId).IsUnique();
        modelBuilder.Entity<TrackedBatch>().HasIndex(b => b.EffectiveExpiryDate);

        modelBuilder.Entity<AlertNotification>()
            .HasOne<TrackedBatch>()
            .WithMany()
            .HasForeignKey(n => n.TrackedBatchId);
    }
}
