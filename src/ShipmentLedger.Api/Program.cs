using System.Text.Json.Serialization;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using ShipmentLedger.Application;
using ShipmentLedger.Application.Exceptions;
using ShipmentLedger.Application.Features.ShipmentEvents.Commands.IngestShipmentEvent;
using ShipmentLedger.Application.Features.Shipments.Queries.GetShipmentHistory;
using ShipmentLedger.Application.Features.Shipments.Queries.GetShipmentState;
using ShipmentLedger.Domain.Enums;
using ShipmentLedger.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddProblemDetails();
builder.Services.ConfigureHttpJsonOptions(opts =>
    opts.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

var app = builder.Build();

app.UseExceptionHandler();
app.UseHttpsRedirection();

// POST /shipment-events
app.MapPost("/shipment-events", async (
    [FromBody] IngestShipmentEventRequest request,
    IMediator mediator,
    CancellationToken ct) =>
{
    var command = new IngestShipmentEventCommand(
        request.EventId,
        request.Partner,
        request.ShipmentId,
        request.Status,
        request.OccurredAt,
        ReceivedAt: DateTime.UtcNow,
        request.Location);

    var result = await mediator.Send(command, ct);

    return result.Outcome == IngestOutcome.Duplicate
        ? Results.Conflict(new { result.Outcome, result.ProcessingNote, result.ShipmentId })
        : Results.Accepted($"/shipments/{result.ShipmentId}",
            new { result.Outcome, result.ProcessingNote, result.ShipmentId });
});

// GET /shipments/{id}
app.MapGet("/shipments/{id}", async (
    string id,
    IMediator mediator,
    CancellationToken ct) =>
{
    try
    {
        var response = await mediator.Send(new GetShipmentStateQuery(id), ct);
        return Results.Ok(response);
    }
    catch (NotFoundException ex)
    {
        return Results.NotFound(new { error = ex.Message });
    }
});

// GET /shipments/{id}/events
app.MapGet("/shipments/{id}/events", async (
    string id,
    IMediator mediator,
    CancellationToken ct) =>
{
    try
    {
        var response = await mediator.Send(new GetShipmentHistoryQuery(id), ct);
        return Results.Ok(response);
    }
    catch (NotFoundException ex)
    {
        return Results.NotFound(new { error = ex.Message });
    }
});

// GET /health
app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));

app.Run();

public record IngestShipmentEventRequest(
    string? EventId,
    string Partner,
    string ShipmentId,
    ShipmentStatus Status,
    DateTime OccurredAt,
    string? Location
);

public partial class Program { }
