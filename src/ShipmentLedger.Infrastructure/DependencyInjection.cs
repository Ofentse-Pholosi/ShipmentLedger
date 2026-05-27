using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ShipmentLedger.Application.Interfaces;
using ShipmentLedger.Infrastructure.Persistence;

namespace ShipmentLedger.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddDbContext<ShipmentLedgerDbContext>(options =>
            options.UseSqlServer(configuration.GetConnectionString("DefaultConnection")));

        services.AddScoped<IShipmentLedgerDbContext>(
            sp => sp.GetRequiredService<ShipmentLedgerDbContext>());

        return services;
    }
}
