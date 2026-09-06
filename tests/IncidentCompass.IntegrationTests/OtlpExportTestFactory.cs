using Google.Protobuf;
using OpenTelemetry.Proto.Collector.Logs.V1;
using OpenTelemetry.Proto.Collector.Trace.V1;
using OpenTelemetry.Proto.Common.V1;
using OpenTelemetry.Proto.Logs.V1;
using OpenTelemetry.Proto.Resource.V1;
using OpenTelemetry.Proto.Trace.V1;

namespace IncidentCompass.IntegrationTests;

/// <summary>
/// Builds OTLP exports carrying an exact number of error records for one service, so the
/// per-request record-limit tests can vary only the record count.
/// </summary>
internal static class OtlpExportTestFactory
{
    public static ExportTraceServiceRequest TraceExport(string serviceName, int spanCount)
    {
        var callSeed = Guid.NewGuid().ToByteArray();
        var scopeSpans = new ScopeSpans();
        for (var index = 0; index < spanCount; index++)
        {
            scopeSpans.Spans.Add(new Span
            {
                Name = "POST /limit-probe",
                TraceId = ByteString.CopyFrom(Identifier(callSeed, index, 16)),
                SpanId = ByteString.CopyFrom(Identifier(callSeed, index, 8)),
                StartTimeUnixNano = 1_700_000_000_000_000_000,
                EndTimeUnixNano = 1_700_000_000_250_000_000,
                Status = new Status { Code = Status.Types.StatusCode.Error },
                Attributes =
                {
                    Attribute("exception.type", "TimeoutException"),
                    Attribute("exception.message", "Export limit probe timed out"),
                    Attribute("http.route", "/limit-probe")
                }
            });
        }

        return new ExportTraceServiceRequest
        {
            ResourceSpans =
            {
                new ResourceSpans
                {
                    Resource = ResourceFor(serviceName),
                    ScopeSpans = { scopeSpans }
                }
            }
        };
    }

    public static ExportLogsServiceRequest LogExport(string serviceName, int recordCount)
    {
        var scopeLogs = new ScopeLogs();
        for (var index = 0; index < recordCount; index++)
        {
            scopeLogs.LogRecords.Add(new LogRecord
            {
                SeverityText = "ERROR",
                Body = new AnyValue { StringValue = "Export limit probe " + index },
                Attributes =
                {
                    Attribute("exception.type", "TimeoutException"),
                    Attribute("exception.message", "Export limit probe " + index),
                    Attribute("incidentcompass.event.id", $"{serviceName}:{index}")
                }
            });
        }

        return new ExportLogsServiceRequest
        {
            ResourceLogs =
            {
                new ResourceLogs
                {
                    Resource = ResourceFor(serviceName),
                    ScopeLogs = { scopeLogs }
                }
            }
        };
    }

    private static Resource ResourceFor(string serviceName) => new()
    {
        Attributes =
        {
            Attribute("service.name", serviceName),
            Attribute("deployment.environment.name", "test")
        }
    };

    // The OTLP trace delivery key is "traceId:spanId", uniquely indexed by (tenant_id, source,
    // delivery_key) without a service scope, so identifiers that only depended on `index` would
    // collide across different tests using this factory with the same span count and dedupe
    // against each other's rows. Mixing in a fresh per-call seed keeps identifiers unique across
    // factory calls while `index` still keeps them distinct within a single call's span list.
    private static byte[] Identifier(byte[] callSeed, int index, int length)
    {
        var bytes = new byte[length];
        for (var position = 0; position < length; position++)
        {
            bytes[position] = (byte)(callSeed[position % callSeed.Length] + index);
        }

        return bytes;
    }

    private static KeyValue Attribute(string key, string value) =>
        new() { Key = key, Value = new AnyValue { StringValue = value } };
}
