using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Google.Protobuf;
using IncidentCompass.Application.Intake.IngestSignal;
using OpenTelemetry.Proto.Collector.Logs.V1;
using OpenTelemetry.Proto.Logs.V1;

namespace IncidentCompass.Api;

internal static class OtlpLogRequestMapper
{
    public static IReadOnlyCollection<IngestSignalCommand> Map(ExportLogsServiceRequest request)
    {
        var commands = new List<IngestSignalCommand>();
        var fallbackDeliveryOccurrences = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var resourceLogs in request.ResourceLogs)
        {
            var resourceAttributes = OtlpAttributeMapper.ToJsonObject(resourceLogs.Resource?.Attributes ?? []);
            foreach (var scopeLogs in resourceLogs.ScopeLogs)
            {
                foreach (var logRecord in scopeLogs.LogRecords)
                {
                    commands.Add(MapLogRecord(resourceLogs, scopeLogs, resourceAttributes, logRecord, fallbackDeliveryOccurrences));
                }
            }
        }

        return commands;
    }

    private static IngestSignalCommand MapLogRecord(
        ResourceLogs resourceLogs,
        ScopeLogs scopeLogs,
        JsonObject resourceAttributes,
        LogRecord logRecord,
        Dictionary<string, int> fallbackDeliveryOccurrences)
    {
        var attributes = OtlpAttributeMapper.ToJsonObject(logRecord.Attributes);
        attributes["otel.resource"] = resourceAttributes.DeepClone();

        var errorType = OtlpAttributeMapper.GetString(attributes, "exception.type", "error.type");
        var errorMessage = OtlpAttributeMapper.GetString(attributes, "exception.message", "error.message");
        var body = OtlpAttributeMapper.ToJsonNode(logRecord.Body);
        if (body is JsonValue bodyValue && bodyValue.TryGetValue<string>(out var bodyText) && string.IsNullOrWhiteSpace(errorMessage))
        {
            errorMessage = bodyText;
        }

        var severity = string.IsNullOrWhiteSpace(logRecord.SeverityText) ? "info" : logRecord.SeverityText.ToLowerInvariant();
        attributes["errorType"] = errorType;
        attributes["errorMessage"] = errorMessage;
        attributes["operationName"] = OtlpAttributeMapper.GetString(attributes, "code.function.name", "event.name");
        attributes["httpMethod"] = OtlpAttributeMapper.GetString(attributes, "http.request.method", "http.method");
        attributes["httpRoute"] = OtlpAttributeMapper.GetString(attributes, "http.route");
        attributes["httpStatusCode"] = OtlpAttributeMapper.GetInt(attributes, "http.response.status_code", "http.status_code");

        var traceId = OtlpAttributeMapper.ToHex(logRecord.TraceId);
        var spanId = OtlpAttributeMapper.ToHex(logRecord.SpanId);
        return new IngestSignalCommand(
            SourceKind: "otel",
            ServiceName: OtlpAttributeMapper.GetString(resourceAttributes, "service.name"),
            Environment: OtlpAttributeMapper.GetString(resourceAttributes, "deployment.environment.name", "deployment.environment"),
            Severity: severity,
            Summary: null,
            Description: errorMessage,
            ObservedAtUtc: OtlpAttributeMapper.ToDateTimeOffset(logRecord.TimeUnixNano) ?? OtlpAttributeMapper.ToDateTimeOffset(logRecord.ObservedTimeUnixNano),
            TraceId: traceId,
            SpanId: spanId,
            ParentSpanId: null,
            ExternalId: ResolveExternalId(resourceLogs, scopeLogs, logRecord, attributes, traceId, spanId, fallbackDeliveryOccurrences),
            Attributes: attributes,
            Payload: new JsonObject
            {
                ["body"] = body,
                ["severityNumber"] = logRecord.SeverityNumber.ToString(),
                ["severityText"] = logRecord.SeverityText
            });
    }

    private static string ResolveExternalId(
        ResourceLogs resourceLogs,
        ScopeLogs scopeLogs,
        LogRecord logRecord,
        JsonObject attributes,
        string? traceId,
        string? spanId,
        Dictionary<string, int> fallbackDeliveryOccurrences)
    {
        var eventId = OtlpAttributeMapper.GetString(attributes, "incidentcompass.event.id");
        if (!string.IsNullOrWhiteSpace(eventId))
        {
            return eventId;
        }

        if (!string.IsNullOrWhiteSpace(traceId) && !string.IsNullOrWhiteSpace(spanId))
        {
            return $"{traceId}:{spanId}";
        }

        var fingerprint = ComputeFallbackFingerprint(resourceLogs, scopeLogs, logRecord);
        var occurrence = fallbackDeliveryOccurrences.GetValueOrDefault(fingerprint);
        fallbackDeliveryOccurrences[fingerprint] = occurrence + 1;
        return $"otlp-log:{fingerprint}:{occurrence}";
    }

    private static string ComputeFallbackFingerprint(ResourceLogs resourceLogs, ScopeLogs scopeLogs, LogRecord logRecord)
    {
        using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(hasher, resourceLogs.Resource?.ToByteArray() ?? []);
        Append(hasher, Encoding.UTF8.GetBytes(resourceLogs.SchemaUrl));
        Append(hasher, scopeLogs.Scope?.ToByteArray() ?? []);
        Append(hasher, Encoding.UTF8.GetBytes(scopeLogs.SchemaUrl));
        Append(hasher, logRecord.ToByteArray());
        return Convert.ToHexString(hasher.GetHashAndReset()).ToLowerInvariant();
    }

    private static void Append(IncrementalHash hasher, byte[] bytes)
    {
        hasher.AppendData(BitConverter.GetBytes(bytes.Length));
        hasher.AppendData(bytes);
    }
}
