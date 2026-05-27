using MediatR;

namespace ShipmentLedger.Application.Features.Shipments.Queries.GetShipmentHistory;

public record GetShipmentHistoryQuery(string ShipmentId) : IRequest<GetShipmentHistoryResponse>;

public record GetShipmentHistoryResponse(
    string ShipmentId,
    string OrderingNote,
    IReadOnlyList<ShipmentEventDto> Events
);

public record ShipmentEventDto(
    string? EventId,
    string Partner,
    string Status,
    DateTime OccurredAt,
    DateTime ReceivedAt,
    string? Location,
    string ProcessingStatus,
    string? ProcessingNote
);
