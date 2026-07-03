using System.Text.Json;
using System.Text.Json.Nodes;
using IncidentCompass.Application.Core.Serialization;
using IncidentCompass.Application.Intake.Configuration;
using IncidentCompass.Domain.Incidents;

namespace IncidentCompass.Application.Intake.Artifacts;

public sealed class GroundedFactsAssembler(
    ITriageArtifactRepository artifactRepository,
    IPriorReportSummaryProvider priorReportSummaryProvider,
    TimeProvider timeProvider)
{
    public async Task AssembleAsync(
        TriageJob job,
        Signal triggerSignal,
        Fault fault,
        int neighborCount,
        bool? isMassIssue,
        FaultGroupingSettings settings,
        CancellationToken cancellationToken)
    {
        await InsertTriggerSignalArtifactAsync(job, triggerSignal, cancellationToken);
        await InsertNeighborSetArtifactAsync(job, fault, triggerSignal, neighborCount, isMassIssue, settings, cancellationToken);
        await InsertPriorReportArtifactIfRecurrenceAsync(job, fault, cancellationToken);
    }

    private async Task InsertTriggerSignalArtifactAsync(TriageJob job, Signal triggerSignal, CancellationToken cancellationToken)
    {
        var payload = new JsonObject
        {
            ["signalId"] = triggerSignal.Id.ToString(),
            ["source"] = triggerSignal.Source,
            ["serviceName"] = triggerSignal.ServiceName,
            ["environment"] = triggerSignal.Environment,
            ["severity"] = triggerSignal.Severity,
            ["errorType"] = triggerSignal.ErrorType,
            ["errorMessage"] = triggerSignal.ErrorMessage,
            ["summary"] = triggerSignal.Summary,
            ["observedAtUtc"] = triggerSignal.ObservedAtUtc.ToString("O"),
        };

        await InsertArtifactAsync(job.Id, attempt: null, ArtifactKind.TriggerSignal, $"signal:{triggerSignal.Id}", payload, cancellationToken);
    }

    private async Task InsertNeighborSetArtifactAsync(
        TriageJob job,
        Fault fault,
        Signal triggerSignal,
        int neighborCount,
        bool? isMassIssue,
        FaultGroupingSettings settings,
        CancellationToken cancellationToken)
    {
        var payload = new JsonObject
        {
            ["neighborCount"] = neighborCount,
            ["neighborCountApplies"] = triggerSignal.CanGroup,
            ["isMassIssue"] = isMassIssue,
            ["lookbackMinutes"] = settings.LookbackMinutes,
            ["minNeighborCountThreshold"] = settings.MassIssue.MinNeighborCount,
            ["minFingerprintStrengthThreshold"] = settings.MassIssue.MinFingerprintStrength,
            ["fingerprintStrength"] = triggerSignal.FingerprintStrength.ToString(),
        };

        await InsertArtifactAsync(job.Id, attempt: null, ArtifactKind.NeighborSet, $"fault:{fault.Id}", payload, cancellationToken);
    }

    private async Task InsertPriorReportArtifactIfRecurrenceAsync(TriageJob job, Fault fault, CancellationToken cancellationToken)
    {
        if (fault.RecurrenceOf is not Guid recurrenceOfFaultId)
        {
            return;
        }

        var prior = await priorReportSummaryProvider.FindLatestAsync(recurrenceOfFaultId, cancellationToken);
        if (prior is null)
        {
            return;
        }

        var payload = new JsonObject
        {
            ["summary"] = prior.Summary,
            ["limitations"] = new JsonArray(prior.Limitations.Select(limitation => JsonValue.Create(limitation) as JsonNode).ToArray()),
            ["trust"] = "untrusted-prior-hypothesis",
        };

        var domainRef = prior.ReportId is Guid reportId ? $"report:{reportId}" : $"fault:{recurrenceOfFaultId}";
        await InsertArtifactAsync(job.Id, attempt: null, ArtifactKind.PriorReport, domainRef, payload, cancellationToken);
    }

    private async Task InsertArtifactAsync(
        Guid jobId,
        int? attempt,
        ArtifactKind kind,
        string domainRef,
        JsonObject payload,
        CancellationToken cancellationToken)
    {
        var canonicalPayload = CanonicalJsonSerializer.Canonicalize(payload);
        using var payloadDocument = JsonDocument.Parse(payload.ToJsonString());
        var artifact = new TriageArtifact(
            Id: Guid.NewGuid(),
            JobId: jobId,
            Attempt: attempt,
            Kind: kind,
            DomainRef: domainRef,
            RedactedPayload: payloadDocument.RootElement.Clone(),
            ContentHash: CanonicalJsonSerializer.ComputeSha256Hex(canonicalPayload),
            CreatedAtUtc: timeProvider.GetUtcNow());

        await artifactRepository.InsertAsync(artifact, cancellationToken);
    }
}
