using Microsoft.EntityFrameworkCore;
using MedStorage.Catalog.Api.Domain.Entities;

namespace MedStorage.Catalog.Api.Infrastructure.Database;

public class CatalogDbContext(DbContextOptions<CatalogDbContext> options) : DbContext(options)
{
    public DbSet<Drug> Drugs => Set<Drug>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Drug>(entity =>
        {
            entity.ToTable("drugs");
            entity.HasKey(d => d.Id);
            entity.Property(d => d.TradeName).HasMaxLength(200).IsRequired();
            entity.Property(d => d.ActiveSubstance).HasMaxLength(200).IsRequired();
            entity.Property(d => d.DosageForm).HasMaxLength(100).IsRequired();
            entity.HasIndex(d => d.TradeName);
        });
    }
}
