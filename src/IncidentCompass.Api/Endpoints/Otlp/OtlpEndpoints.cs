using Google.Protobuf;
using IncidentCompass.Application.Core.Dispatching;
using IncidentCompass.Application.Intake.Configuration;
using IncidentCompass.Application.Intake.IngestSignal;
using IncidentCompass.Application.Intake.Normalization;
using OpenTelemetry.Proto.Collector.Logs.V1;
using OpenTelemetry.Proto.Collector.Trace.V1;

namespace IncidentCompass.Api;

internal static class OtlpEndpoints
{
    public static IEndpointRouteBuilder MapOtlpEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/v1/traces", IngestTracesAsync)
            .WithDisplayName("OTLP trace ingestion");
        endpoints.MapPost("/v1/logs", IngestLogsAsync)
            .WithDisplayName("OTLP log ingestion");
        return endpoints;
    }

    private static async Task<IResult> IngestTracesAsync(
        HttpRequest request,
        IApplicationDispatcher dispatcher,
        ITriageConfigurationRepository configurationRepository,
        OtlpPayloadReader payloadReader,
        CancellationToken cancellationToken)
    {
        var payload = await payloadReader.ReadAsync(request, cancellationToken);
        if (payload.FailureStatusCode is { } failureStatusCode)
        {
            return TypedResults.StatusCode(failureStatusCode);
        }

        IReadOnlyCollection<IngestSignalCommand> commands;
        try
        {
            var exportRequest = ExportTraceServiceRequest.Parser.ParseFrom(payload.Payload!);
            commands = OtlpTraceRequestMapper.Map(exportRequest);
        }
        catch (InvalidProtocolBufferException)
        {
            return TypedResults.BadRequest();
        }
        catch (InvalidOtlpPayloadException)
        {
            return TypedResults.BadRequest();
        }

        await DispatchAsync(commands, dispatcher, configurationRepository, cancellationToken);
        return Results.File(new ExportTraceServiceResponse().ToByteArray(), "application/x-protobuf");
    }
    private static async Task<IResult> IngestLogsAsync(
        HttpRequest request,
        IApplicationDispatcher dispatcher,
        ITriageConfigurationRepository configurationRepository,
        OtlpPayloadReader payloadReader,
        CancellationToken cancellationToken)
    {
        var payload = await payloadReader.ReadAsync(request, cancellationToken);
        if (payload.FailureStatusCode is { } failureStatusCode)
        {
            return TypedResults.StatusCode(failureStatusCode);
        }

        try
        {
            var exportRequest = ExportLogsServiceRequest.Parser.ParseFrom(payload.Payload!);
            await DispatchAsync(OtlpLogRequestMapper.Map(exportRequest), dispatcher, configurationRepository, cancellationToken);
            return Results.File(new ExportLogsServiceResponse().ToByteArray(), "application/x-protobuf");
        }
        catch (InvalidProtocolBufferException)
        {
            return TypedResults.BadRequest();
        }
    }

    private static async Task DispatchAsync(
        IReadOnlyCollection<IngestSignalCommand> commands,
        IApplicationDispatcher dispatcher,
        ITriageConfigurationRepository configurationRepository,
        CancellationToken cancellationToken)
    {
        var configuration = await configurationRepository.GetCurrentAsync(cancellationToken);
        foreach (var command in commands.Where(command => OtelTriggerPolicy.ShouldTrigger(command, configuration.Ingestion.EffectiveOtel)))
        {
            await dispatcher.DispatchAsync<IngestSignalCommand, IngestSignalResponse>(command, cancellationToken);
        }
    }
}