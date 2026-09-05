using System.Text.Json.Nodes;
using IncidentCompass.Application.Intake.IngestSignal;

namespace IncidentCompass.Application.Intake.Normalization;

// Shared normalization body for the "minimal structured" sources (tester, otel): both read
// the same conventional attribute keys (StructuredEnvelopeAttributes) and differ only in the
// SourceKinds they register under, so the field-extraction logic lives here once.
internal static class StructuredSignalNormalization
{
    public static NormalizedSignal Normalize(IngestSignalCommand command, string source, DateTimeOffset receivedAtUtc)
    {
        var serviceName = NormalizationDefaults.UnknownIfBlank(command.ServiceName);
        var environment = NormalizationDefaults.UnknownIfBlank(command.Environment);

        var errorType = StructuredEnvelopeAttributes.GetString(command.Attributes, "errorType");
        var errorMessage = StructuredEnvelopeAttributes.GetString(command.Attributes, "errorMessage");
        var operationName = StructuredEnvelopeAttributes.GetString(command.Attributes, "operationName");
        var httpMethod = StructuredEnvelopeAttributes.GetString(command.Attributes, "httpMethod");
        var httpRoute = StructuredEnvelopeAttributes.GetString(command.Attributes, "httpRoute");
        var httpStatusCode = StructuredEnvelopeAttributes.GetInt(command.Attributes, "httpStatusCode");
        var durationMs = StructuredEnvelopeAttributes.GetInt(command.Attributes, "durationMs");

        var summary = string.IsNullOrWhiteSpace(command.Summary)
            ? SummarySynthesizer.ForStructuredSignal(serviceName, operationName, httpRoute, errorType, errorMessage)
            : SignalTextTruncator.TruncateSummary(command.Summary);

        return new NormalizedSignal(
            Source: source,
            ExternalId: command.ExternalId,
            TraceId: command.TraceId,
            SpanId: command.SpanId,
            ParentSpanId: command.ParentSpanId,
            ServiceName: serviceName,
            Environment: environment,
            OperationName: operationName,
            Severity: command.Severity,
            ErrorType: errorType,
            ErrorMessage: errorMessage,
            Summary: summary,
            Description: SignalTextTruncator.TruncateDescription(command.Description),
            HttpMethod: httpMethod,
            HttpRoute: httpRoute,
            HttpStatusCode: httpStatusCode,
            DurationMs: durationMs,
            Attributes: command.Attributes ?? new JsonObject(),
            Body: command.Payload ?? new JsonObject(),
            ObservedAtUtc: command.ObservedAtUtc?.ToUniversalTime() ?? receivedAtUtc);
    }
}
