namespace ShipmentLedger.Domain.Enums;

public enum ShipmentStatus
{
    LabelCreated = 0,
    HandedToCarrier = 1,
    InTransit = 2,
    OutForDelivery = 3,
    Delivered = 4,
    DeliveryException = 5,
    Returned = 6
}
