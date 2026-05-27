using ShipmentLedger.Domain.Enums;

namespace ShipmentLedger.Domain.Entities;

public class Shipment
{
    public string ShipmentId { get; set; } = default!;
    public ShipmentStatus CurrentStatus { get; set; }
    public DateTime StatusOccurredAt { get; set; }
    public string Partner { get; set; } = default!;
    public string? Location { get; set; }
    public int EventCount { get; set; }
    public DateTime LastUpdatedAt { get; set; }

    public ICollection<ShipmentEvent> Events { get; set; } = [];
}
