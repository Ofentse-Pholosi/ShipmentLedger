using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using ShipmentLedger.Domain.Entities;

namespace ShipmentLedger.Application.Interfaces;

public interface IShipmentLedgerDbContext
{
    DbSet<Shipment> Shipments { get; }
    DbSet<ShipmentEvent> ShipmentEvents { get; }
    DatabaseFacade Database { get; }
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
