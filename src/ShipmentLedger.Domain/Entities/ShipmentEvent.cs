using ShipmentLedger.Domain.Enums;

namespace ShipmentLedger.Domain.Entities;

public class ShipmentEvent
{
    public int Id { get; set; }

    // Null for courier partners that cannot provide a stable event identifier
    public string? EventId { get; set; }

    // SHA256(partner + shipmentId + status + occurredAt) — used for content-based dedup
    public string ContentHash { get; set; } = default!;

    public string Partner { get; set; } = default!;
    public string ShipmentId { get; set; } = default!;
    public ShipmentStatus Status { get; set; }
    public DateTime OccurredAt { get; set; }
    public DateTime ReceivedAt { get; set; }
    public string? Location { get; set; }
    public ProcessingStatus ProcessingStatus { get; set; }

    // Human-readable explanation of the decision made for this event
    public string? ProcessingNote { get; set; }

    public Shipment Shipment { get; set; } = default!;
}
