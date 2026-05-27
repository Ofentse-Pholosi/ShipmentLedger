using System.Security.Cryptography;
using System.Text;
using ShipmentLedger.Domain.Enums;

namespace ShipmentLedger.Application.Helpers;

public static class ContentHashHelper
{
    // Produces a 64-char lowercase hex string — matches DB column length constraint
    public static string Compute(string partner, string shipmentId, ShipmentStatus status, DateTime occurredAt)
    {
        var input = $"{partner}|{shipmentId}|{status}|{occurredAt:O}";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
