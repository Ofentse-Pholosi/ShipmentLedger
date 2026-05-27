using ShipmentLedger.Application.Helpers;
using ShipmentLedger.Domain.Enums;

namespace ShipmentLedger.UnitTests;

public class ContentHashHelperTests
{
    private static readonly DateTime BaseTime = new(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Compute_SameInputs_ReturnsSameHash()
    {
        var h1 = ContentHashHelper.Compute("DHL", "SHIP-001", ShipmentStatus.InTransit, BaseTime);
        var h2 = ContentHashHelper.Compute("DHL", "SHIP-001", ShipmentStatus.InTransit, BaseTime);

        Assert.Equal(h1, h2);
    }

    [Fact]
    public void Compute_AlwaysReturns64CharLowercaseHex()
    {
        var hash = ContentHashHelper.Compute("DHL", "SHIP-001", ShipmentStatus.InTransit, BaseTime);

        Assert.Equal(64, hash.Length);
        Assert.True(hash == hash.ToLowerInvariant(), "Hash must be lowercase");
        Assert.Matches("^[0-9a-f]+$", hash);
    }

    [Fact]
    public void Compute_DifferentPartner_ReturnsDifferentHash()
    {
        var h1 = ContentHashHelper.Compute("DHL",   "SHIP-001", ShipmentStatus.InTransit, BaseTime);
        var h2 = ContentHashHelper.Compute("FedEx", "SHIP-001", ShipmentStatus.InTransit, BaseTime);

        Assert.NotEqual(h1, h2);
    }

    [Fact]
    public void Compute_DifferentShipmentId_ReturnsDifferentHash()
    {
        var h1 = ContentHashHelper.Compute("DHL", "SHIP-001", ShipmentStatus.InTransit, BaseTime);
        var h2 = ContentHashHelper.Compute("DHL", "SHIP-002", ShipmentStatus.InTransit, BaseTime);

        Assert.NotEqual(h1, h2);
    }

    [Fact]
    public void Compute_DifferentStatus_ReturnsDifferentHash()
    {
        var h1 = ContentHashHelper.Compute("DHL", "SHIP-001", ShipmentStatus.InTransit,      BaseTime);
        var h2 = ContentHashHelper.Compute("DHL", "SHIP-001", ShipmentStatus.OutForDelivery,  BaseTime);

        Assert.NotEqual(h1, h2);
    }

    [Fact]
    public void Compute_DifferentOccurredAt_ReturnsDifferentHash()
    {
        var later = BaseTime.AddSeconds(1);
        var h1 = ContentHashHelper.Compute("DHL", "SHIP-001", ShipmentStatus.InTransit, BaseTime);
        var h2 = ContentHashHelper.Compute("DHL", "SHIP-001", ShipmentStatus.InTransit, later);

        Assert.NotEqual(h1, h2);
    }
}
