namespace IncidentCompass.Application.Investigation.Jobs;

internal sealed record WorkerDelegateResult(
    string Rationale,
    string SerializedPayload);
