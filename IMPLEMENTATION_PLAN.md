# Accso Technical Assignment — Implementation Plan

**Stack:** C# .NET 10 · ASP.NET Core Minimal API · MediatR · EF Core · SQL Server LocalDB  
**Pattern:** Clean Architecture · CQRS  

---

## Phases at a glance

| Phase | Focus |
|---|---|
| 1 | Project foundation & solution structure |
| 2 | Domain model & database schema |
| 3 | Core integrity rules (the hard part) |
| 4 | Query handlers |
| 5 | API layer wiring |
| 6 | Automated tests |
| 7 | Change request |
| 8 | Documentation & ADRs |

---

## Phase 1 — Project Foundation

**Goal:** Runnable solution skeleton with all projects referencing each other correctly.

### Tasks
- [ ] Create solution: `ShipmentLedger.sln`
- [ ] Create projects:
  - `src/ShipmentLedger.Api` (ASP.NET Core Web API, Minimal API)
  - `src/ShipmentLedger.Application` (Class Library)
  - `src/ShipmentLedger.Domain` (Class Library)
  - `src/ShipmentLedger.Infrastructure` (Class Library)
  - `tests/ShipmentLedger.UnitTests` (xUnit)
  - `tests/ShipmentLedger.IntegrationTests` (xUnit)
- [ ] Add project references (Api → Application → Domain, Infrastructure → Domain)
- [ ] Add NuGet packages:
  - `Infrastructure`: `Microsoft.EntityFrameworkCore.SqlServer`, `Microsoft.EntityFrameworkCore.Design`
  - `Application`: `MediatR`
  - `Api`: `Microsoft.EntityFrameworkCore.Design`
  - `IntegrationTests`: `Microsoft.AspNetCore.Mvc.Testing`, `Respawn`
- [ ] Add `appsettings.Development.json` with LocalDB connection string
- [ ] Add `appsettings.Testing.json` with separate test database connection string
- [ ] Verify `dotnet build` passes across all projects

### Connection strings
```json
// appsettings.Development.json
"DefaultConnection": "Server=(localdb)\\mssqllocaldb;Database=ShipmentLedger;Trusted_Connection=True;MultipleActiveResultSets=true"

// appsettings.Testing.json
"DefaultConnection": "Server=(localdb)\\mssqllocaldb;Database=ShipmentLedgerTests;Trusted_Connection=True;MultipleActiveResultSets=true"
```

---

## Phase 2 — Domain Model & Database Schema

**Goal:** All entities, enums, and DB constraints defined. First migration applied.

### Domain layer — enums

```
ShipmentStatus:   LABEL_CREATED | HANDED_TO_CARRIER | IN_TRANSIT |
                  OUT_FOR_DELIVERY | DELIVERED | DELIVERY_EXCEPTION | RETURNED

ProcessingStatus: Accepted | RejectedDuplicate | AcceptedOutOfOrder
```

### Domain layer — entities

**`Shipment`** — current view (one row per shipment)
```
ShipmentId       string   PK
CurrentStatus    enum
StatusOccurredAt DateTime (occurredAt of the event driving current state)
Partner          string   (last courier that updated state)
Location         string?
EventCount       int      (total accepted events)
LastUpdatedAt    DateTime
```

**`ShipmentEvent`** — append-only audit trail (never deleted or updated)
```
Id               int       PK (identity)
EventId          string?   (null for partners with unstable IDs)
ContentHash      string    SHA256(partner + shipmentId + status + occurredAt)
Partner          string
ShipmentId       string    FK → Shipment
Status           enum
OccurredAt       DateTime
ReceivedAt       DateTime
Location         string?
ProcessingStatus enum      (Accepted | RejectedDuplicate | AcceptedOutOfOrder)
ProcessingNote   string?   (human-readable reason for the decision made)
```

### Infrastructure layer — DbContext & constraints

```csharp
// Unique index: stable eventId dedup
.HasIndex(e => new { e.Partner, e.EventId })
 .IsUnique()
 .HasFilter("[EventId] IS NOT NULL");

// Unique index: content-hash dedup (covers unstable-ID partners)
.HasIndex(e => e.ContentHash)
 .IsUnique();
```

### Tasks
- [ ] Define `ShipmentStatus` and `ProcessingStatus` enums in Domain
- [ ] Define `Shipment` entity in Domain
- [ ] Define `ShipmentEvent` entity in Domain
- [ ] Create `ShipmentLedgerDbContext` in Infrastructure
- [ ] Configure entity mappings and UNIQUE constraints via Fluent API
- [ ] Add first EF migration: `InitialCreate`
- [ ] Apply migration: `dotnet ef database update`
- [ ] Verify tables and constraints exist in LocalDB

---

## Phase 3 — Core Integrity Rules (IngestShipmentEventCommand)

**Goal:** `POST /shipment-events` correctly handles all three integrity cases.  
This is the most important phase. The integrity rules must be explicit, testable, and documented.

### MediatR command

```
IngestShipmentEventCommand
  EventId      string?
  Partner      string
  ShipmentId   string
  Status       ShipmentStatus
  OccurredAt   DateTime
  ReceivedAt   DateTime
  Location     string?

IngestShipmentEventResult
  Outcome      enum  (Accepted | Duplicate | AcceptedOutOfOrder)
  ProcessingNote string
  ShipmentId   string
```

### Handler logic — decision tree

```
1. COMPUTE content hash: SHA256(partner + shipmentId + status.ToString() + occurredAt.ToString("O"))

2. DUPLICATE CHECK (reject early, return 200 with outcome = Duplicate):
   a. EventId is not null AND EXISTS ShipmentEvent WHERE Partner = ? AND EventId = ?
   b. OR EXISTS ShipmentEvent WHERE ContentHash = ?

3. LOAD or CREATE Shipment row for shipmentId

4. OUT-OF-ORDER CHECK:
   - If shipment exists AND incoming occurredAt <= shipment.StatusOccurredAt
     → ProcessingStatus = AcceptedOutOfOrder
     → Persist event, do NOT update Shipment state
     → Return outcome = AcceptedOutOfOrder

5. STATE UPDATE (occurredAt > current, or new shipment):
   - Persist ShipmentEvent with ProcessingStatus = Accepted
   - Update Shipment: CurrentStatus, StatusOccurredAt, Partner, Location, EventCount++
   - Return outcome = Accepted

6. ALL DB WRITES in a single transaction (atomic dedup + state update)
```

### Conflict rule (document explicitly)
When two events share the same `occurredAt` but different status values:
- The **higher enum ordinal wins** (more terminal state takes precedence)
- Example: `DELIVERY_EXCEPTION` (index 5) beats `IN_TRANSIT` (index 2) at same timestamp
- This rule is applied only when updating Shipment state, not for dedup

### Tasks
- [ ] Create `IngestShipmentEventCommand` and result type in Application
- [ ] Implement content hash computation (static helper)
- [ ] Implement duplicate detection logic (both strategies)
- [ ] Implement out-of-order detection and conditional state update
- [ ] Wrap all DB writes in `IDbContextTransaction`
- [ ] Implement conflict resolution rule (same `occurredAt`, different status)
- [ ] Return structured result with `Outcome` and `ProcessingNote`
- [ ] Register MediatR in DI

---

## Phase 4 — Query Handlers

**Goal:** `GET /shipments/{id}` and `GET /shipments/{id}/events` return correct, well-shaped responses.

### GetShipmentStateQuery → response shape

```json
{
  "shipmentId": "ship-456",
  "currentStatus": "IN_TRANSIT",
  "statusOccurredAt": "2026-03-10T12:00:00Z",
  "partner": "dhl",
  "location": "Amsterdam",
  "eventCount": 3,
  "stateReason": "Latest accepted event by occurredAt timestamp"
}
```

### GetShipmentHistoryQuery → response shape

```json
{
  "shipmentId": "ship-456",
  "events": [
    {
      "eventId": "evt-123",
      "partner": "dhl",
      "status": "IN_TRANSIT",
      "occurredAt": "2026-03-10T12:00:00Z",
      "receivedAt": "2026-03-10T12:00:05Z",
      "location": "Amsterdam",
      "processingStatus": "Accepted",
      "processingNote": "Event accepted and updated shipment state"
    }
  ],
  "orderingNote": "Events ordered by occurredAt ascending (real-world event time), then receivedAt as tiebreaker"
}
```

### Ordering decision (document this)
Order by `occurredAt ASC`, then `ReceivedAt ASC` as tiebreaker.  
Reason: history should reflect real-world event sequence, not the order we received them.

### Tasks
- [ ] Create `GetShipmentStateQuery` + handler in Application
- [ ] Create `GetShipmentHistoryQuery` + handler in Application
- [ ] Return 404 with problem details if shipmentId not found
- [ ] Include `orderingNote` in history response explaining the ordering choice

---

## Phase 5 — API Layer

**Goal:** Three endpoints wired to MediatR, returning correct HTTP status codes.

### Endpoints

```
POST   /shipment-events              → 200 (accepted or duplicate — both are valid outcomes)
GET    /shipments/{shipmentId}       → 200 | 404
GET    /shipments/{shipmentId}/events → 200 | 404

GET    /health                       → 200 (optional bonus — lightweight, worth adding)
```

### POST response HTTP status rationale
Return `200` for duplicates (not `409`) because:
- The caller's intent (record this event) has been satisfied from their perspective
- `409 Conflict` implies a corrective action is needed — nothing is needed here
- The `outcome` field in the body communicates the distinction

### Async nature — deliberate trade-off (document in README)
Shipment webhooks share the same async pressures as payment events:
- Couriers retry if no response arrives within their timeout window
- Events from multiple couriers may arrive concurrently
- The service could be unavailable when a courier sends an event

**What our design already handles (without a queue):**
- C# `async/await` throughout — DB calls are non-blocking, threads are never held
- Courier retries → DB `UNIQUE` constraint catches the duplicate, no double-processing
- Concurrent requests → single DB transaction serialises the state update atomically
- Service restart → all state is durable in SQL Server, nothing in memory

**What a production system would add (out of scope, document as known limitation):**
- Durable ingestion queue (e.g. Azure Service Bus) so events survive service downtime
- Background processor consuming from the queue, using the same handler logic
- This would be a one-layer change — handler logic is unchanged, only the trigger moves

**Key architectural point:** the design is queue-ready. The `IngestShipmentEventCommand`
handler does not know or care whether it was triggered by an HTTP request or a queue
consumer. Upgrading to async ingestion requires no changes to Phase 3 or Phase 4.

### Tasks
- [ ] Wire `POST /shipment-events` → `IngestShipmentEventCommand`
- [ ] Wire `GET /shipments/{shipmentId}` → `GetShipmentStateQuery`
- [ ] Wire `GET /shipments/{shipmentId}/events` → `GetShipmentHistoryQuery`
- [ ] Ensure all handlers use `async Task` signatures (MediatR default)
- [ ] Add global exception middleware (returns `500` with problem details, never stack traces)
- [ ] Add `GET /health` returning `{ "status": "healthy", "database": "reachable" }`
- [ ] Add `AddProblemDetails()` for consistent error shapes
- [ ] Verify all endpoints manually with curl or HTTP file

---

## Phase 6 — Automated Tests

**Goal:** Integrity rules are tested, one full integration path exists, change request scenario is covered.

### Unit tests (ShipmentLedger.UnitTests)

| Test | What it verifies |
|---|---|
| `Duplicate_WithSameEventId_IsRejected` | eventId dedup |
| `Duplicate_WithSameContentHash_IsRejected` | content hash dedup |
| `Duplicate_WithDifferentReceivedAt_SameContent_IsRejected` | change request scenario |
| `OutOfOrder_OlderEvent_DoesNotUpdateState` | out-of-order rule |
| `OutOfOrder_OlderEvent_IsStillPersisted` | audit trail preserved |
| `Conflict_SameTimestamp_HigherOrdinalWins` | conflict resolution |
| `NewShipment_FirstEvent_CreatesShipmentRow` | happy path |
| `ValidEvent_UpdatesShipmentState` | state progression |

### Integration tests (ShipmentLedger.IntegrationTests)

| Test | What it verifies |
|---|---|
| `PostEvent_ThenGetState_ReturnsCorrectStatus` | end-to-end happy path |
| `PostSameEventTwice_SecondIsRejectedAsDuplicate` | full dedup path through API |
| `PostOutOfOrderEvent_StateNotRegressed` | state protection through API |
| `GetShipment_NotFound_Returns404` | 404 handling |
| `GetHistory_ReturnsEventsInOccurredAtOrder` | ordering verified |
| `UnstableIdPartner_SameContentTwice_OnlyOneAccepted` | change request e2e |

### Integration test setup
```csharp
// WebApplicationFactory with Testing connection string
// Respawn resets DB between each test
// Migrations applied once at test collection startup
```

### Tasks
- [ ] Configure `WebApplicationFactory` with `appsettings.Testing.json`
- [ ] Configure Respawn in integration test base class
- [ ] Implement all unit tests against handler logic directly (no HTTP)
- [ ] Implement all integration tests via `HttpClient`
- [ ] Verify all tests pass: `dotnet test`

---

## Phase 7 — Change Request

**Goal:** Handle courier partners that cannot provide a stable `eventId`.

### What changes
The `EventId` field is already `nullable` by design (Phase 2). The content hash dedup
strategy already exists (Phase 3). The change request is absorbed by:

1. Adding a `SupportsStableEventId` flag per partner (simple: check if `EventId` is null on the incoming event)
2. Ensuring the content hash UNIQUE constraint is active (already in schema)
3. Documenting that `EventId`-null events skip strategy #1 and rely solely on strategy #2

### What does NOT change
- `ShipmentEvent` schema — `EventId` was already nullable
- Content hash computation — already implemented
- State update logic — unchanged
- History API — unchanged

### Tasks
- [ ] `git commit` current working solution before applying change (required by assignment)
- [ ] Verify null `EventId` path through `IngestShipmentEventCommand` handler
- [ ] Add integration test: same content, two `POST`s with null `EventId`, different `receivedAt` → second rejected
- [ ] Add `processingNote` text that distinguishes which dedup strategy triggered
- [ ] Document in README: what changed, what stayed the same, what would still need handling in production

### What would still need handling in production
- Rate limiting per partner (unstable-ID partners could flood with slight variations)
- Configurable dedup window (e.g., ignore events older than 7 days entirely)
- Alerting when content-hash collision rate exceeds threshold (may indicate data quality issues)

---

## Phase 8 — Documentation & ADRs

**Goal:** Submission-ready README, 2 ADRs, and development process note.

### README sections
- [ ] Problem framing (how you interpreted the brief) — include idempotency/immutable ledger analogy to payment events
- [ ] Assumptions made (explicit list)
- [ ] Design choices and trade-offs
- [ ] Known limitations — include synchronous webhook processing trade-off:
  > Events are processed synchronously within the HTTP request. Under production load this
  > should be replaced with a durable ingestion queue (e.g. Azure Service Bus) so events
  > survive service downtime and couriers receive fast 202 responses. The handler logic
  > is queue-ready and requires no changes — only the trigger point moves.
- [ ] Run instructions (clone → run in < 5 minutes)
- [ ] Change request: what changed, what stayed, what remains

### ADR-1: Database constraint for deduplication
```
Decision:  Use SQL UNIQUE constraints as the primary deduplication mechanism
Alternatives considered:
  - In-memory HashSet (rejected: not durable across restarts)
  - Idempotency table with application-level check (rejected: race condition window)
  - Redis cache (rejected: adds infrastructure dependency for no benefit here)
Rationale: DB constraint is enforced atomically, survives restarts, and requires
           no additional infrastructure. Concurrent duplicate requests are handled
           correctly by the DB engine, not by application code.
```

### ADR-2: occurredAt-based state resolution
```
Decision:  Shipment current state is driven by the event with the highest occurredAt
Alternatives considered:
  - receivedAt ordering (rejected: reflects our network latency, not real-world events)
  - Append-only with no "current state" table (rejected: every read would require full replay)
  - Vector clocks / Lamport timestamps (rejected: couriers don't provide this; overkill)
Rationale: occurredAt represents when the physical event happened. It is provided
           by the courier and is the most faithful representation of real-world order.
           Out-of-order events are accepted but don't regress state.
```

### Development process note
- [ ] Document which parts were AI-assisted
- [ ] Document at least one concrete example where AI suggestion was overridden
- [ ] Explain how control was maintained throughout

---

## Functional targets checklist (assignment requirements)

### API endpoints
- [ ] `POST /shipment-events` — persists, deduplicates, returns outcome
- [ ] `GET /shipments/{shipmentId}` — current state with event count and reason
- [ ] `GET /shipments/{shipmentId}/events` — ordered history with processing status

### Data integrity rules
- [ ] Duplicate events — rejected via DB constraint (eventId) or content hash
- [ ] Out-of-order events — accepted but do not regress state
- [ ] Conflicting updates — highest enum ordinal at same timestamp wins

### Deliverables
- [ ] Working solution with run instructions (< 5 min to clone and run)
- [ ] README with framing, assumptions, design choices, known limitations, change request
- [ ] ADR-1: Deduplication strategy
- [ ] ADR-2: State resolution approach
- [ ] Unit tests covering all integrity rules
- [ ] Integration test covering at least one end-to-end path
- [ ] Integration test covering the change request scenario
- [ ] Development process note with concrete AI override example
- [ ] `GET /health` endpoint (optional bonus — included)
- [ ] Git commit before applying change request

---

## Run instructions (target state)

```bash
git clone <repo-url>
cd ShipmentLedger
dotnet ef database update --project src/ShipmentLedger.Infrastructure --startup-project src/ShipmentLedger.Api
dotnet run --project src/ShipmentLedger.Api
# API available at https://localhost:5001
```

```bash
# Run all tests
dotnet test
```
