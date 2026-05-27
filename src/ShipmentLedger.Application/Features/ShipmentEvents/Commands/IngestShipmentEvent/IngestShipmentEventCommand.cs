using MediatR;
using ShipmentLedger.Domain.Enums;

namespace ShipmentLedger.Application.Features.ShipmentEvents.Commands.IngestShipmentEvent;

public record IngestShipmentEventCommand(
    string? EventId,
    string Partner,
    string ShipmentId,
    ShipmentStatus Status,
    DateTime OccurredAt,
    DateTime ReceivedAt,
    string? Location
) : IRequest<IngestShipmentEventResult>;

public record IngestShipmentEventResult(
    IngestOutcome Outcome,
    string ProcessingNote,
    string ShipmentId
);

public enum IngestOutcome
{
    Accepted,
    Duplicate,
    AcceptedOutOfOrder
}
