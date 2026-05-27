using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ShipmentLedger.IntegrationTests;

public class ShipmentLedgerIntegrationTests : IClassFixture<ApiFactory>, IAsyncLifetime
{
    private readonly ApiFactory _factory;
    private readonly HttpClient _client;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public ShipmentLedgerIntegrationTests(ApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public async Task InitializeAsync() => await _factory.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    // ── Ingest: new event ──────────────────────────────────────────────────────

    [Fact]
    public async Task Ingest_NewEvent_CreatesShipmentAndReturnsAccepted()
    {
        var response = await PostEvent("SHIP-NEW-001", "DHL", "InTransit", "2026-01-01T10:00:00Z", "evt-new-001");

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var body = await Read<IngestResult>(response);
        Assert.Equal("Accepted", body.Outcome);
        Assert.Equal("SHIP-NEW-001", body.ShipmentId);
    }

    // ── Ingest: duplicate by stable eventId ───────────────────────────────────

    [Fact]
    public async Task Ingest_DuplicateByEventId_ReturnsConflict()
    {
        await PostEvent("SHIP-DUP-001", "DHL", "InTransit", "2026-01-01T10:00:00Z", "evt-dup-001");

        var duplicate = await PostEvent("SHIP-DUP-001", "DHL", "InTransit", "2026-01-01T10:00:00Z", "evt-dup-001");

        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        var body = await Read<IngestResult>(duplicate);
        Assert.Equal("Duplicate", body.Outcome);
    }

    // ── Ingest: duplicate by content hash (no eventId) ────────────────────────
    // Change-request scenario: partners with unstable/absent eventIds are still
    // deduplicated via SHA-256(partner|shipmentId|status|occurredAt).

    [Fact]
    public async Task Ingest_DuplicateByContentHash_NoEventId_ReturnsConflict()
    {
        // First ingest — no eventId
        await PostEvent("SHIP-HASH-001", "FedEx", "HandedToCarrier", "2026-01-01T08:00:00Z", eventId: null);

        // Exact same content, still no eventId — must be detected via content hash
        var duplicate = await PostEvent("SHIP-HASH-001", "FedEx", "HandedToCarrier", "2026-01-01T08:00:00Z", eventId: null);

        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        var body = await Read<IngestResult>(duplicate);
        Assert.Equal("Duplicate", body.Outcome);
    }

    // ── Ingest: forward progress ───────────────────────────────────────────────

    [Fact]
    public async Task Ingest_ForwardProgress_AdvancesShipmentState()
    {
        await PostEvent("SHIP-FWD-001", "DHL", "InTransit", "2026-01-01T10:00:00Z", "evt-fwd-001");
        var second = await PostEvent("SHIP-FWD-001", "DHL", "OutForDelivery", "2026-01-01T14:00:00Z", "evt-fwd-002");

        Assert.Equal(HttpStatusCode.Accepted, second.StatusCode);
        var body = await Read<IngestResult>(second);
        Assert.Equal("Accepted", body.Outcome);

        var state = await GetState("SHIP-FWD-001");
        Assert.Equal("OutForDelivery", state.CurrentStatus);
        Assert.Equal(2, state.EventCount);
    }

    // ── Ingest: out-of-order event ────────────────────────────────────────────

    [Fact]
    public async Task Ingest_OutOfOrderEvent_AcceptedButStateNotRegressed()
    {
        // Current state: OutForDelivery at 14:00
        await PostEvent("SHIP-OOO-001", "DHL", "OutForDelivery", "2026-01-01T14:00:00Z", "evt-ooo-001");

        // Late arrival: InTransit at 10:00 — predates current state
        var late = await PostEvent("SHIP-OOO-001", "DHL", "InTransit", "2026-01-01T10:00:00Z", "evt-ooo-000");

        Assert.Equal(HttpStatusCode.Accepted, late.StatusCode);
        var body = await Read<IngestResult>(late);
        Assert.Equal("AcceptedOutOfOrder", body.Outcome);

        // State must NOT have regressed to InTransit
        var state = await GetState("SHIP-OOO-001");
        Assert.Equal("OutForDelivery", state.CurrentStatus);
        Assert.Equal(2, state.EventCount);
    }

    // ── Ingest: conflict at same timestamp — higher enum ordinal wins ──────────

    [Fact]
    public async Task Ingest_Conflict_SameTimestamp_HigherOrdinalWins()
    {
        // InTransit (ordinal 2) arrives first
        await PostEvent("SHIP-CON-001", "DHL", "InTransit", "2026-01-01T12:00:00Z", "evt-con-001");

        // OutForDelivery (ordinal 3) at the same timestamp — should supersede
        var higher = await PostEvent("SHIP-CON-001", "DHL", "OutForDelivery", "2026-01-01T12:00:00Z", "evt-con-002");

        Assert.Equal(HttpStatusCode.Accepted, higher.StatusCode);
        var state = await GetState("SHIP-CON-001");
        Assert.Equal("OutForDelivery", state.CurrentStatus);

        // Lower ordinal at same timestamp — should lose and be stored as out-of-order
        var lower = await PostEvent("SHIP-CON-001", "DHL", "HandedToCarrier", "2026-01-01T12:00:00Z", "evt-con-003");
        Assert.Equal(HttpStatusCode.Accepted, lower.StatusCode);
        var lowerBody = await Read<IngestResult>(lower);
        Assert.Equal("AcceptedOutOfOrder", lowerBody.Outcome);

        // State still OutForDelivery
        var finalState = await GetState("SHIP-CON-001");
        Assert.Equal("OutForDelivery", finalState.CurrentStatus);
    }

    // ── Query: unknown shipment returns 404 ───────────────────────────────────

    [Fact]
    public async Task GetShipment_UnknownId_Returns404()
    {
        var response = await _client.GetAsync("/shipments/DOES-NOT-EXIST");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ── Query: history ordered by occurredAt ──────────────────────────────────

    [Fact]
    public async Task GetShipmentHistory_ReturnsAllEvents_OrderedByOccurredAt()
    {
        // Insert out-of-order so the DB insertion order differs from event order
        await PostEvent("SHIP-HIST-001", "DHL", "OutForDelivery", "2026-01-01T14:00:00Z", "evt-hist-002");
        await PostEvent("SHIP-HIST-001", "DHL", "InTransit",      "2026-01-01T10:00:00Z", "evt-hist-001");

        var response = await _client.GetAsync("/shipments/SHIP-HIST-001/events");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await Read<ShipmentHistory>(response);
        Assert.Equal(2, body.Events.Count);
        // Earlier real-world time must come first regardless of insertion order
        Assert.Equal("InTransit",      body.Events[0].Status);
        Assert.Equal("OutForDelivery", body.Events[1].Status);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private Task<HttpResponseMessage> PostEvent(
        string shipmentId,
        string partner,
        string status,
        string occurredAt,
        string? eventId,
        string? location = null)
    {
        var payload = new
        {
            eventId,
            partner,
            shipmentId,
            status,
            occurredAt,
            location
        };
        return _client.PostAsJsonAsync("/shipment-events", payload);
    }

    private async Task<ShipmentState> GetState(string shipmentId)
    {
        var response = await _client.GetAsync($"/shipments/{shipmentId}");
        response.EnsureSuccessStatusCode();
        return (await Read<ShipmentState>(response))!;
    }

    private static async Task<T> Read<T>(HttpResponseMessage response)
    {
        var json = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<T>(json, JsonOpts)!;
    }

    private record IngestResult(string Outcome, string ProcessingNote, string ShipmentId);
    private record ShipmentState(string ShipmentId, string CurrentStatus, DateTime StatusOccurredAt, string Partner, string? Location, int EventCount, string StateReason);
    private record ShipmentHistory(string ShipmentId, string OrderingNote, List<EventDto> Events);
    private record EventDto(string? EventId, string Partner, string Status, DateTime OccurredAt, DateTime ReceivedAt, string? Location, string ProcessingStatus, string? ProcessingNote);
}
