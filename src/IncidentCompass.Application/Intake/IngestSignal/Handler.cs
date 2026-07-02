using System.Text.Json;
using System.Text.Json.Nodes;
using IncidentCompass.Application.Core.Dispatching;
using IncidentCompass.Application.Intake.Configuration;
using IncidentCompass.Application.Intake.FaultGrouping;
using IncidentCompass.Application.Intake.Fingerprinting;
using IncidentCompass.Application.Intake.Normalization;
using IncidentCompass.Application.Intake.Redaction;
using IncidentCompass.Domain.Incidents;

namespace IncidentCompass.Application.Intake.IngestSignal;

public sealed class IngestSignalCommandHandler(
    ITriageConfigurationRepository configurationRepository,
    SignalNormalizerRegistry normalizerRegistry,
    FaultGroupingCoordinator faultGroupingCoordinator,
    TimeProvider timeProvider) : IRequestHandler<IngestSignalCommand, IngestSignalResponse>
{
    public async Task<IngestSignalResponse> HandleAsync(IngestSignalCommand command, CancellationToken cancellationToken)
    {
        var configuration = await configurationRepository.GetCurrentAsync(cancellationToken);
        var receivedAtUtc = timeProvider.GetUtcNow();
        var normalizer = normalizerRegistry.Resolve(command.SourceKind);
        var normalized = normalizer.Normalize(command, receivedAtUtc);
        var redacted = SecretRedactor.Redact(normalized);
        var fingerprint = FingerprintCalculator.Compute(redacted, configuration.FaultGrouping.FingerprintVersion);
        var draftSignal = BuildSignal(
            redacted,
            fingerprint,
            configuration.FaultGrouping.FingerprintVersion,
            configuration.Ingestion.DefaultTenant,
            receivedAtUtc);

        var outcome = await faultGroupingCoordinator.ResolveAsync(draftSignal, configuration, cancellationToken);

        return new IngestSignalResponse(
            draftSignal.Id,
            outcome.Fault.Id,
            outcome.IsNewFault,
            outcome.IsNewJob,
            outcome.IsSuppressed,
            outcome.Job?.Id,
            outcome.Job?.ConfigHash);
    }

    private static Signal BuildSignal(
        NormalizedSignal redacted,
        FingerprintResult fingerprint,
        int fingerprintVersion,
        string tenantId,
        DateTimeOffset receivedAtUtc)
    {
        return new Signal(
            Id: Guid.NewGuid(),
            TenantId: tenantId,
            Source: redacted.Source,
            FaultId: null,
            Fingerprint: fingerprint.Value,
            FingerprintVersion: fingerprintVersion,
            FingerprintStrength: fingerprint.Strength,
            CanGroup: fingerprint.Strength == FingerprintStrength.Strong,
            ExternalId: redacted.ExternalId,
            IsSuppressed: false,
            SuppressedByFaultId: null,
            SuppressionReason: null,
            TraceId: redacted.TraceId,
            SpanId: redacted.SpanId,
            ParentSpanId: redacted.ParentSpanId,
            ServiceName: redacted.ServiceName,
            Environment: redacted.Environment,
            OperationName: redacted.OperationName,
            Severity: redacted.Severity,
            ErrorType: redacted.ErrorType,
            ErrorMessage: redacted.ErrorMessage,
            Summary: redacted.Summary,
            Description: redacted.Description,
            HttpMethod: redacted.HttpMethod,
            HttpRoute: redacted.HttpRoute,
            HttpStatusCode: redacted.HttpStatusCode,
            DurationMs: redacted.DurationMs,
            Attributes: ToElement(redacted.Attributes),
            Body: ToElement(redacted.Body),
            ObservedAtUtc: redacted.ObservedAtUtc,
            ReceivedAtUtc: receivedAtUtc);
    }

    private static JsonElement ToElement(JsonNode node)
    {
        using var document = JsonDocument.Parse(node.ToJsonString());
        return document.RootElement.Clone();
    }
}
