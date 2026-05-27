using MediatR;
using Microsoft.EntityFrameworkCore;
using ShipmentLedger.Application.Exceptions;
using ShipmentLedger.Infrastructure.Persistence;

namespace ShipmentLedger.Application.Features.Shipments.Queries.GetShipmentState;

public class GetShipmentStateHandler(ShipmentLedgerDbContext db)
    : IRequestHandler<GetShipmentStateQuery, GetShipmentStateResponse>
{
    public async Task<GetShipmentStateResponse> Handle(
        GetShipmentStateQuery query,
        CancellationToken cancellationToken)
    {
        var shipment = await db.Shipments
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.ShipmentId == query.ShipmentId, cancellationToken);

        if (shipment is null)
            throw new NotFoundException(query.ShipmentId);

        return new GetShipmentStateResponse(
            shipment.ShipmentId,
            shipment.CurrentStatus.ToString(),
            shipment.StatusOccurredAt,
            shipment.Partner,
            shipment.Location,
            shipment.EventCount,
            "Most recent accepted event by occurredAt; out-of-order and lower-ordinal conflicts do not regress state"
        );
    }
}
