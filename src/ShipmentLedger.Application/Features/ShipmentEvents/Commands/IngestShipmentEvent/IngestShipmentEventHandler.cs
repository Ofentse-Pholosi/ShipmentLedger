using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using ShipmentLedger.Application.Helpers;
using ShipmentLedger.Domain.Entities;
using ShipmentLedger.Domain.Enums;
using ShipmentLedger.Infrastructure.Persistence;

namespace ShipmentLedger.Application.Features.ShipmentEvents.Commands.IngestShipmentEvent;

public class IngestShipmentEventHandler(ShipmentLedgerDbContext db)
    : IRequestHandler<IngestShipmentEventCommand, IngestShipmentEventResult>
{
    public async Task<IngestShipmentEventResult> Handle(
        IngestShipmentEventCommand command,
        CancellationToken cancellationToken)
    {
        var contentHash = ContentHashHelper.Compute(
            command.Partner, command.ShipmentId, command.Status, command.OccurredAt);

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            // --- Duplicate detection (two strategies) ---

            // Strategy 1: stable eventId provided by the courier
            if (command.EventId is not null)
            {
                var eventIdExists = await db.ShipmentEvents.AnyAsync(
                    e => e.Partner == command.Partner && e.EventId == command.EventId,
                    cancellationToken);

                if (eventIdExists)
                    return await Rollback(transaction,
                        new IngestShipmentEventResult(
                            IngestOutcome.Duplicate,
                            $"Already processed eventId '{command.EventId}' for partner '{command.Partner}'",
                            command.ShipmentId),
                        cancellationToken);
            }

            // Strategy 2: content hash — covers partners without stable eventIds
            var hashExists = await db.ShipmentEvents.AnyAsync(
                e => e.ContentHash == contentHash, cancellationToken);

            if (hashExists)
                return await Rollback(transaction,
                    new IngestShipmentEventResult(
                        IngestOutcome.Duplicate,
                        $"Duplicate content detected for shipment '{command.ShipmentId}' via content-hash match",
                        command.ShipmentId),
                    cancellationToken);

            // --- Load or create shipment ---
            var shipment = await db.Shipments
                .FirstOrDefaultAsync(s => s.ShipmentId == command.ShipmentId, cancellationToken);

            ProcessingStatus processingStatus;
            string processingNote;

            if (shipment is null)
            {
                shipment = new Shipment
                {
                    ShipmentId = command.ShipmentId,
                    CurrentStatus = command.Status,
                    StatusOccurredAt = command.OccurredAt,
                    Partner = command.Partner,
                    Location = command.Location,
                    EventCount = 0,
                    LastUpdatedAt = DateTime.UtcNow
                };
                db.Shipments.Add(shipment);
                processingStatus = ProcessingStatus.Accepted;
                processingNote = $"New shipment created with initial status '{command.Status}'";
            }
            else
            {
                (processingStatus, processingNote) = ResolveUpdate(command, shipment);
            }

            // All non-duplicate events count — includes out-of-order (they are real events)
            shipment.EventCount++;

            db.ShipmentEvents.Add(new ShipmentEvent
            {
                EventId = command.EventId,
                ContentHash = contentHash,
                Partner = command.Partner,
                ShipmentId = command.ShipmentId,
                Status = command.Status,
                OccurredAt = command.OccurredAt,
                ReceivedAt = command.ReceivedAt,
                Location = command.Location,
                ProcessingStatus = processingStatus,
                ProcessingNote = processingNote
            });

            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return new IngestShipmentEventResult(
                processingStatus == ProcessingStatus.Accepted
                    ? IngestOutcome.Accepted
                    : IngestOutcome.AcceptedOutOfOrder,
                processingNote,
                command.ShipmentId);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    // Determines how to update (or not update) shipment state for an existing shipment
    private static (ProcessingStatus, string) ResolveUpdate(
        IngestShipmentEventCommand command,
        Shipment shipment)
    {
        // Normal forward progress
        if (command.OccurredAt > shipment.StatusOccurredAt)
        {
            shipment.CurrentStatus = command.Status;
            shipment.StatusOccurredAt = command.OccurredAt;
            shipment.Partner = command.Partner;
            shipment.Location = command.Location;
            shipment.LastUpdatedAt = DateTime.UtcNow;
            return (ProcessingStatus.Accepted,
                $"Shipment state updated to '{command.Status}' at {command.OccurredAt:O}");
        }

        // Conflict: same timestamp from different sources — higher enum ordinal wins
        // Rationale: higher ordinal = more terminal state (e.g. Delivered > InTransit)
        if (command.OccurredAt == shipment.StatusOccurredAt)
        {
            if ((int)command.Status > (int)shipment.CurrentStatus)
            {
                var displaced = shipment.CurrentStatus;
                shipment.CurrentStatus = command.Status;
                shipment.Partner = command.Partner;
                shipment.Location = command.Location;
                shipment.LastUpdatedAt = DateTime.UtcNow;
                return (ProcessingStatus.Accepted,
                    $"Conflict: '{command.Status}' (ordinal {(int)command.Status}) supersedes '{displaced}' (ordinal {(int)displaced}) at same occurredAt");
            }

            return (ProcessingStatus.AcceptedOutOfOrder,
                $"Conflict: existing '{shipment.CurrentStatus}' (ordinal {(int)shipment.CurrentStatus}) retained over '{command.Status}' (ordinal {(int)command.Status}) at same occurredAt");
        }

        // Out-of-order: event predates current state — store for audit, do not regress state
        return (ProcessingStatus.AcceptedOutOfOrder,
            $"Out-of-order: event occurredAt {command.OccurredAt:O} precedes current state time {shipment.StatusOccurredAt:O}");
    }

    private static async Task<IngestShipmentEventResult> Rollback(
        IDbContextTransaction transaction,
        IngestShipmentEventResult result,
        CancellationToken ct)
    {
        await transaction.RollbackAsync(ct);
        return result;
    }
}
