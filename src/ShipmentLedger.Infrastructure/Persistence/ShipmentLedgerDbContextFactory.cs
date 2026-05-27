using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ShipmentLedger.Infrastructure.Persistence;

// Used by dotnet-ef tooling only — not registered in DI
public class ShipmentLedgerDbContextFactory : IDesignTimeDbContextFactory<ShipmentLedgerDbContext>
{
    public ShipmentLedgerDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<ShipmentLedgerDbContext>()
            .UseSqlServer("Server=(localdb)\\mssqllocaldb;Database=ShipmentLedger;Trusted_Connection=True;")
            .Options;

        return new ShipmentLedgerDbContext(options);
    }
}
