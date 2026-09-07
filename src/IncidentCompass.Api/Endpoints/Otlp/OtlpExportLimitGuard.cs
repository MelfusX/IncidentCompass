using IncidentCompass.Application.Intake.Configuration;
using Microsoft.Extensions.Options;
using OpenTelemetry.Proto.Collector.Logs.V1;
using OpenTelemetry.Proto.Collector.Trace.V1;

namespace IncidentCompass.Api;

/// <summary>
/// The per-request record bound for OTLP ingestion. <see cref="OtlpPayloadReader" /> caps how many
/// bytes one export may carry, but protobuf is compact: a payload well inside that byte cap can
/// still hold a very large number of spans or log records, and each record becomes a dispatched
/// <c>IngestSignalCommand</c> that can open a fault and a triage job. This guard counts the records
/// the parsed export carries and rejects the whole export before mapping and before any dispatch,
/// so an over-limit export stores nothing at all rather than leaving a caller with a partially
/// ingested batch it cannot reason about. It is independent of, and additional to, request rate
/// limiting, which bounds callers per window rather than work per request.
/// </summary>
internal sealed partial class OtlpExportLimitGuard(
    IOptions<IngestionLimitsOptions> limits,
    ILogger<OtlpExportLimitGuard> logger)
{
    private const string TraceSignalKind = "traces";
    private const string LogSignalKind = "logs";

    public bool ExceedsSignalLimit(ExportTraceServiceRequest request) =>
        ExceedsSignalLimit(TraceSignalKind, CountSpans(request));

    public bool ExceedsSignalLimit(ExportLogsServiceRequest request) =>
        ExceedsSignalLimit(LogSignalKind, CountLogRecords(request));

    private bool ExceedsSignalLimit(string signalKind, int recordCount)
    {
        var maxSignalsPerExport = limits.Value.MaxSignalsPerExport;
        if (recordCount <= maxSignalsPerExport)
        {
            return false;
        }

        LogExportRejected(
            logger,
            signalKind,
            recordCount,
            maxSignalsPerExport,
            ApiErrorMapping.OtlpExportSignalLimitExceededCode);
        return true;
    }

    private static int CountSpans(ExportTraceServiceRequest request)
    {
        var count = 0;
        foreach (var resourceSpans in request.ResourceSpans)
        {
            foreach (var scopeSpans in resourceSpans.ScopeSpans)
            {
                count += scopeSpans.Spans.Count;
            }
        }

        return count;
    }

    private static int CountLogRecords(ExportLogsServiceRequest request)
    {
        var count = 0;
        foreach (var resourceLogs in request.ResourceLogs)
        {
            foreach (var scopeLogs in resourceLogs.ScopeLogs)
            {
                count += scopeLogs.LogRecords.Count;
            }
        }

        return count;
    }

    // Counts and the configured limit only. Record bodies, span names, attributes and resource
    // attributes never reach this log.
    [LoggerMessage(
        EventId = 4003,
        Level = LogLevel.Warning,
        Message = "An OTLP {SignalKind} export carrying {RecordCount} records exceeded the configured limit of {MaxSignalsPerExport} records per export and was rejected before any signal was ingested, with error code {ErrorCode}.")]
    private static partial void LogExportRejected(
        ILogger logger,
        string signalKind,
        int recordCount,
        int maxSignalsPerExport,
        string errorCode);
}
