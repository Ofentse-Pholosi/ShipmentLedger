namespace ShipmentLedger.Application.Exceptions;

public class NotFoundException(string shipmentId)
    : Exception($"Shipment '{shipmentId}' not found");
