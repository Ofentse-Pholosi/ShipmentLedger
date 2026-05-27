# ShipmentLedger

A focused backend service that maintains a reliable, consistent view of shipment state
across multiple courier partners — handling late, out-of-order, duplicated, and conflicting
webhook events.

---

## Implementation progress

| Phase | Focus | Status |
|---|---|---|
| 1 | Project foundation & solution structure | Complete |
| 2 | Domain model & database schema | Complete |
| 3 | Core integrity rules (`IngestShipmentEventCommand`) | Complete |
| 4 | Query handlers | Complete |
| 5 | API layer wiring | Complete |
| 6 | Automated tests | In Progresss |
| 7 | Change request | Pending |
| 8 | Documentation & ADRs | Pending |

---

## Problem framing

The client receives shipment events from courier partners via webhook. The core challenge
is not data volume — it is **data quality**: events arrive late, out of order, duplicated,
and sometimes with conflicting status values for the same moment in time.

This is structurally identical to a payment event ledger problem:

| Payment domain | Shipment domain |
|---|---|
| Idempotency key on payment request | `eventId` (or content hash) on shipment event |
| Ledger entries — append-only, never mutated | `ShipmentEvents` table — immutable audit trail |
| Running account balance | `Shipments` table — current state derived from events |
| Duplicate payment submission blocked | Duplicate event rejected, state unchanged |
| Out-of-sequence transaction recorded | Out-of-order event stored, state not regressed |

Two guarantees are being enforced:

**Idempotency** — `POST /shipment-events` called multiple times with the same event
produces the same outcome. The second call is detected and rejected before any state change.
Enforced by database `UNIQUE` constraints, not application-level logic, so it holds under
concurrent requests.

**Immutability** — `ShipmentEvent` rows are never `UPDATE`d or `DELETE`d. The audit trail
is a permanent record. If the current state ever looks wrong, the event history can be
replayed to reconstruct it. The `Shipments` table is a materialised view of that truth.

---

## Assumptions

- `occurredAt` is provided by the courier and represents when the physical event happened.
  It is the authoritative timestamp for ordering, not `receivedAt` (which reflects our
  network latency and courier retry behaviour).
- A shipment's current status should never regress due to a late-arriving older event.
- Duplicate events are an expected operational condition, not an error. The response for
  a detected duplicate is `200` with `outcome: Duplicate` rather than `409` — the caller's
  intent has already been satisfied.
- `eventId` values are scoped per partner. The same `eventId` from two different partners
  is not a duplicate.
- All timestamps are treated as UTC.
- The `ShipmentStatus` enum ordinal defines terminal-state priority for conflict resolution
  (higher ordinal = more terminal state).

---

## Data integrity rules

### 1. Duplicate events
**Rule:** An event is a duplicate if either:
- `(partner, eventId)` already exists in the event log (strategy 1 — stable ID partners), or
- The `contentHash` — `SHA256(partner|shipmentId|status|occurredAt)` — already exists
  (strategy 2 — partners without stable IDs, and the change request scenario)

**Outcome:** Event is not persisted. `Shipments` state is unchanged. Response returns
`outcome: Duplicate`.

**Why database constraints and not application logic:**
The `UNIQUE` constraints on `(partner, eventId)` and `contentHash` are enforced at the
database level. The application-level pre-check is an optimisation. Even if two concurrent
requests both pass the pre-check, the database constraint ensures exactly one succeeds.

### 2. Out-of-order events
**Rule:** If an incoming event's `occurredAt` is earlier than the shipment's current
`StatusOccurredAt`, the event is accepted into the audit trail but does not update the
current state.

**Outcome:** Event is persisted with `processingStatus: AcceptedOutOfOrder`. `Shipments`
state is unchanged. Response returns `outcome: AcceptedOutOfOrder`.

### 3. Conflicting updates
**Rule:** If two events share the same `occurredAt` but carry different status values,
the event with the **higher enum ordinal** wins (more terminal state takes precedence).

```
LabelCreated=0 < HandedToCarrier=1 < InTransit=2 < OutForDelivery=3
< Delivered=4 < DeliveryException=5 < Returned=6
```

The losing event is persisted with `processingStatus: AcceptedOutOfOrder` and a note
explaining why it did not update state.

---

## Design choices and trade-offs

### Stack
**C# .NET 10 · ASP.NET Core Minimal API · MediatR · EF Core · SQL Server LocalDB**

Clean Architecture with CQRS via MediatR. The three API endpoints map cleanly to one
command (`IngestShipmentEventCommand`) and two queries (`GetShipmentStateQuery`,
`GetShipmentHistoryQuery`). MediatR gives each handler an independently testable boundary
without coupling the HTTP layer to business logic.

### Direct DbContext injection (no repository abstraction)
Handlers in the Application layer inject `ShipmentLedgerDbContext` directly. EF Core's
`DbContext` already implements the unit-of-work pattern. Adding a repository layer would
require either leaking `IQueryable` through the abstraction or writing one method per
query shape — both add complexity without benefit at this scale.

### Synchronous webhook processing (async nature — deliberate trade-off)
Events are processed synchronously within the HTTP request. This is sufficient for the
assignment scope. The design is **queue-ready**: `IngestShipmentEventCommand` does not
know or care whether it was triggered by an HTTP request or a queue consumer. Upgrading
to async ingestion (e.g. Azure Service Bus) would require no changes to Phase 3 or 4
handler logic — only the trigger point moves from the API endpoint to a queue processor.

What the design already handles without a queue:
- Courier retries → DB `UNIQUE` constraint catches the duplicate
- Concurrent requests → DB transaction serialises the state update
- Service restart → all state is durable in SQL Server, nothing held in memory

What a production system would add (documented known limitation):
- Durable ingestion queue so events survive service downtime
- Fast `202 Accepted` response to couriers, decoupled from processing time

---

## Known limitations

- **Race condition window on pre-check:** The duplicate pre-check and insert are wrapped
  in a transaction, but under high concurrent load with identical events, two requests could
  both pass the pre-check before either commits. The DB constraint prevents data corruption
  — the second writer receives a `DbUpdateException` which surfaces as `500` rather than a
  graceful `200 / Duplicate`. In production: catch `DbUpdateException` with SQL error
  2601/2627 and return `Duplicate` outcome.

- **Synchronous processing:** See above. Production would use a durable queue.

- **`occurredAt` trust:** The service trusts the courier's `occurredAt` timestamp entirely.
  A misbehaving courier could backdate events to regress state (bypassing the out-of-order
  check). In production: we would add a configurable staleness window (reject events older than N days).

- **No per-partner configuration:** Both dedup strategies run for all partners. A partner
  registry (with a `supportsStableEventId` flag) would make the strategy selection explicit
  rather than inferred from whether `eventId` is null.

---

## Change request

> *A new courier partner cannot provide a stable `eventId`. They resend the same update
> multiple times, sometimes with different `receivedAt` values.*

### Baseline commit before the change request

**Commit:** `b267b6c` — *"Phase 6: unit tests and integration tests"*

This is the snapshot of the complete, working system as it existed before the change
request arrived. The diff between that commit and this one is the change request response.

---

### What the change request asked for

A new courier partner (e.g. FedEx) cannot issue a stable `eventId` per event. Instead,
they retry the same event multiple times until they receive an acknowledgement — and each
retry may carry a different `receivedAt` timestamp because it is a new HTTP call. Example:

```
POST /shipment-events  { partner:"FedEx", shipmentId:"SHIP-001", status:"InTransit", occurredAt:"10:00", receivedAt:"10:05", eventId: null }
POST /shipment-events  { partner:"FedEx", shipmentId:"SHIP-001", status:"InTransit", occurredAt:"10:00", receivedAt:"10:08", eventId: null }  ← retry
POST /shipment-events  { partner:"FedEx", shipmentId:"SHIP-001", status:"InTransit", occurredAt:"10:00", receivedAt:"10:12", eventId: null }  ← retry
```

Without a deduplication strategy that accounts for absent `eventId`s, all three requests
would be accepted, creating three audit entries for one real-world event.

---

### What changed in code

**Nothing.** Zero lines of application code were modified.

The original design anticipated this scenario:

| Design decision | Why it absorbs the CR |
|---|---|
| `eventId` is `string?` (nullable) | No schema migration needed — the field was optional from day one |
| Strategy 1 is guarded by `if (command.EventId is not null)` | Null-eventId partners automatically skip to strategy 2 |
| Strategy 2 hashes `SHA256(partner\|shipmentId\|status\|occurredAt)` | `receivedAt` is deliberately excluded — retries with different `receivedAt` values produce the same hash |
| `UNIQUE` constraint on `ContentHash` is already in the schema | The database enforces deduplication even under concurrent retries |

The handler logic as written before the CR:

```csharp
// Strategy 1: stable eventId provided by the courier
if (command.EventId is not null)
{
    var eventIdExists = await db.ShipmentEvents.AnyAsync(
        e => e.Partner == command.Partner && e.EventId == command.EventId, ...);
    if (eventIdExists) return Duplicate(...);
}

// Strategy 2: content hash — covers partners without stable eventIds
var hashExists = await db.ShipmentEvents.AnyAsync(
    e => e.ContentHash == contentHash, ...);
if (hashExists) return Duplicate(...);
```

For a FedEx event with `eventId: null`, strategy 1 is skipped entirely. Strategy 2 catches
all retries because the hash of the same real-world event is always identical regardless of
`receivedAt`.

---

### Proof — test already passing before the CR

The integration test `Ingest_DuplicateByContentHash_NoEventId_ReturnsConflict` was
committed at `b267b6c` and was already green:

```csharp
[Fact]
public async Task Ingest_DuplicateByContentHash_NoEventId_ReturnsConflict()
{
    // First ingest — no eventId
    await PostEvent("SHIP-HASH-001", "FedEx", "HandedToCarrier", "2026-01-01T08:00:00Z", eventId: null);

    // Exact same content, still no eventId — must be detected via content hash
    var duplicate = await PostEvent("SHIP-HASH-001", "FedEx", "HandedToCarrier", "2026-01-01T08:00:00Z", eventId: null);

    Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
    Assert.Equal("Duplicate", body.Outcome);
}
```

This test was not added *in response to* the CR — it was part of Phase 6 because the
scenario was anticipated when the deduplication strategy was originally designed.

---

### What stayed the same

- `ShipmentEvent` schema — no migration
- Content hash computation — unchanged
- State update and out-of-order logic — unchanged
- History and query APIs — unchanged

---

### What a production system would still want

- **Per-partner configuration:** A partner registry with an explicit `supportsStableEventId`
  flag, rather than inferring intent from whether `eventId` is null. Makes the strategy
  selection auditable and testable per partner.
- **Staleness window:** Reject events with `occurredAt` older than N days. Prevents a
  misbehaving unstable-ID partner from flooding with old content that still passes the
  hash check (because `occurredAt` is part of the hash, old events produce unique hashes
  indefinitely).
- **Rate limiting per partner:** An unstable-ID partner that varies `status` or `occurredAt`
  slightly between retries would produce unique hashes and all would be accepted. Rate
  limiting provides a safety net against accidental flooding.
- **Alerting on hash collision rate:** If the content-hash rejection rate for a partner
  spikes, it may indicate data quality issues worth investigating.

---

## Architecture Decision Records

*(Full ADRs to be written in Phase 8.)*

**ADR-1: Database UNIQUE constraints for deduplication**
Decision, alternatives considered, and rationale — Phase 8.

**ADR-2: `occurredAt`-based state resolution**
Decision, alternatives considered, and rationale — Phase 8.

---

## Development process note

*(Full note to be written in Phase 8, covering AI tool usage, what was accepted,
what was overridden, and at least one concrete divergence example.)*

---

## Run instructions

*(To be finalised in Phase 5 once API endpoints are wired.)*

```bash
git clone <repo-url>
cd ShipmentLedger

# Apply database migrations (creates LocalDB database automatically)
dotnet ef database update \
  --project src/ShipmentLedger.Infrastructure \
  --startup-project src/ShipmentLedger.Api

# Run the API
dotnet run --project src/ShipmentLedger.Api
# API available at https://localhost:5001

# Run all tests
dotnet test
```

**Prerequisites:** .NET 10 SDK · SQL Server LocalDB (ships with Visual Studio)

---

## Project structure

```
ShipmentLedger.sln
├── src/
│   ├── ShipmentLedger.Api/              Minimal API endpoints, DI wiring
│   ├── ShipmentLedger.Application/      MediatR handlers, commands, queries, DTOs
│   ├── ShipmentLedger.Domain/           Entities, enums — no external dependencies
│   └── ShipmentLedger.Infrastructure/   EF Core DbContext, migrations, DI registration
└── tests/
    ├── ShipmentLedger.UnitTests/        Handler logic, integrity rules (no HTTP, no DB)
    └── ShipmentLedger.IntegrationTests/ Full API + real LocalDB via Respawn
```
