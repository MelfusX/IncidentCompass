using IncidentCompass.Application.Core.Dispatching;
using IncidentCompass.Application.Governance.Ledger.GetFaultLedger;
using IncidentCompass.Application.Intake.GetFault;
using IncidentCompass.Application.Intake.IngestSignal;

namespace IncidentCompass.Api;

internal static class IncidentEndpoints
{
    public static RouteGroupBuilder MapIncidentEndpoints(this RouteGroupBuilder api)
    {
        api.MapPost("/incidents", async (
                IncidentEnvelopeRequest request,
                IApplicationDispatcher dispatcher,
                CancellationToken cancellationToken) =>
            {
                var command = new IngestSignalCommand(
                    request.SourceKind,
                    request.ServiceName,
                    request.Environment,
                    request.Severity,
                    request.Summary,
                    request.Description,
                    request.ObservedAtUtc,
                    request.Correlation?.TraceId,
                    request.Correlation?.SpanId,
                    request.Correlation?.ExternalId,
                    request.Attributes,
                    request.Payload);

                var result = await dispatcher.DispatchAsync<IngestSignalCommand, IngestSignalResponse>(
                    command,
                    cancellationToken);

                return Results.Created($"/api/v1/faults/{result.FaultId}", result);
            })
            .WithName("IngestIncidentSignal")
            .WithSummary("Ingest an incident signal envelope and resolve it to a fault and triage job.")
            .Produces<IngestSignalResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        api.MapGet("/faults/{id:guid}", async (
                Guid id,
                IApplicationDispatcher dispatcher,
                CancellationToken cancellationToken) =>
            {
                var result = await dispatcher.DispatchAsync<GetFaultQuery, FaultDetailsResponse>(
                    new GetFaultQuery(id),
                    cancellationToken);

                return Results.Ok(result);
            })
            .WithName("GetFaultById")
            .WithSummary("Return fault details, including its current triage job summary.")
            .Produces<FaultDetailsResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);


        api.MapGet("/faults/{id:guid}/ledger", async (
                Guid id,
                IApplicationDispatcher dispatcher,
                CancellationToken cancellationToken) =>
            {
                var result = await dispatcher.DispatchAsync<GetFaultLedgerQuery, FaultLedgerResponse>(
                    new GetFaultLedgerQuery(id),
                    cancellationToken);

                return Results.Ok(result);
            })
            .WithName("GetFaultLedgerByFaultId")
            .WithSummary("Return DB-ordered governed investigation ledger events for a fault.")
            .Produces<FaultLedgerResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);
        return api;
    }
}
