using MediatR;

namespace ShipmentLedger.Application.Features.Shipments.Queries.GetShipmentState;

public record GetShipmentStateQuery(string ShipmentId) : IRequest<GetShipmentStateResponse>;

public record GetShipmentStateResponse(
    string ShipmentId,
    string CurrentStatus,
    DateTime StatusOccurredAt,
    string Partner,
    string? Location,
    int EventCount,
    string StateReason
);
