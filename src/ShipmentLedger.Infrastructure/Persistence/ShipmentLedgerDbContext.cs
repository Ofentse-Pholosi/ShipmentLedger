using Microsoft.EntityFrameworkCore;
using ShipmentLedger.Application.Interfaces;
using ShipmentLedger.Domain.Entities;

namespace ShipmentLedger.Infrastructure.Persistence;

public class ShipmentLedgerDbContext(DbContextOptions<ShipmentLedgerDbContext> options)
    : DbContext(options), IShipmentLedgerDbContext
{
    public DbSet<Shipment> Shipments => Set<Shipment>();
    public DbSet<ShipmentEvent> ShipmentEvents => Set<ShipmentEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Shipment>(entity =>
        {
            entity.HasKey(s => s.ShipmentId);
            entity.Property(s => s.ShipmentId).HasMaxLength(100);
            entity.Property(s => s.Partner).HasMaxLength(100).IsRequired();
            entity.Property(s => s.Location).HasMaxLength(200);
            entity.Property(s => s.CurrentStatus).HasConversion<string>().HasMaxLength(50);
        });

        modelBuilder.Entity<ShipmentEvent>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedOnAdd();

            entity.Property(e => e.EventId).HasMaxLength(200);
            entity.Property(e => e.ContentHash).HasMaxLength(64).IsRequired();
            entity.Property(e => e.Partner).HasMaxLength(100).IsRequired();
            entity.Property(e => e.ShipmentId).HasMaxLength(100).IsRequired();
            entity.Property(e => e.Location).HasMaxLength(200);
            entity.Property(e => e.Status).HasConversion<string>().HasMaxLength(50);
            entity.Property(e => e.ProcessingStatus).HasConversion<string>().HasMaxLength(50);
            entity.Property(e => e.ProcessingNote).HasMaxLength(500);

            // Dedup strategy 1: stable eventId — only enforced when EventId is present
            entity.HasIndex(e => new { e.Partner, e.EventId })
                .IsUnique()
                .HasFilter("[EventId] IS NOT NULL");

            // Dedup strategy 2: content hash — covers partners with unstable eventIds
            entity.HasIndex(e => e.ContentHash)
                .IsUnique();

            entity.HasOne(e => e.Shipment)
                .WithMany(s => s.Events)
                .HasForeignKey(e => e.ShipmentId);
        });
    }
}
