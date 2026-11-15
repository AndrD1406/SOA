using MassTransit;
using Microsoft.EntityFrameworkCore;
using MedStorage.Inventory.Api.Domain.Entities;

namespace MedStorage.Inventory.Api.Infrastructure.Database;

public class InventoryDbContext(DbContextOptions<InventoryDbContext> options) : DbContext(options)
{
    public DbSet<MedicineItem> MedicineItems => Set<MedicineItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<MedicineItem>(entity =>
        {
            entity.ToTable("medicine_items");
            entity.HasKey(i => i.Id);
            entity.Property(i => i.Quantity).IsRequired();
            entity.Property(i => i.FactoryExpiryDate).IsRequired();
            entity.Property(i => i.Status).HasConversion<string>().HasMaxLength(16).IsRequired();
            entity.HasIndex(i => i.KitId);
            entity.HasIndex(i => i.DrugRefId);
        });

        // Transactional Outbox tables (MassTransit): messages published inside a handler are
        // written to OutboxMessage in the same transaction as the business data, and a background
        // delivery service forwards them to RabbitMQ after the commit.
        modelBuilder.AddInboxStateEntity();
        modelBuilder.AddOutboxMessageEntity();
        modelBuilder.AddOutboxStateEntity();
    }
}
