using MediatR;
using Microsoft.EntityFrameworkCore;
using ShipmentLedger.Application.Exceptions;
using ShipmentLedger.Infrastructure.Persistence;

namespace ShipmentLedger.Application.Features.Shipments.Queries.GetShipmentHistory;

public class GetShipmentHistoryHandler(ShipmentLedgerDbContext db)
    : IRequestHandler<GetShipmentHistoryQuery, GetShipmentHistoryResponse>
{
    private const string OrderingNote =
        "Events ordered by occurredAt ascending (real-world event time), " +
        "then receivedAt ascending as tiebreaker. " +
        "Rejected duplicates are not stored. " +
        "Out-of-order and conflict-losing events appear in the audit trail with their processingStatus noted.";

    public async Task<GetShipmentHistoryResponse> Handle(
        GetShipmentHistoryQuery query,
        CancellationToken cancellationToken)
    {
        var shipmentExists = await db.Shipments
            .AsNoTracking()
            .AnyAsync(s => s.ShipmentId == query.ShipmentId, cancellationToken);

        if (!shipmentExists)
            throw new NotFoundException(query.ShipmentId);

        var events = await db.ShipmentEvents
            .AsNoTracking()
            .Where(e => e.ShipmentId == query.ShipmentId)
            .OrderBy(e => e.OccurredAt)
            .ThenBy(e => e.ReceivedAt)
            .Select(e => new ShipmentEventDto(
                e.EventId,
                e.Partner,
                e.Status.ToString(),
                e.OccurredAt,
                e.ReceivedAt,
                e.Location,
                e.ProcessingStatus.ToString(),
                e.ProcessingNote))
            .ToListAsync(cancellationToken);

        return new GetShipmentHistoryResponse(query.ShipmentId, OrderingNote, events);
    }
}
