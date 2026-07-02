using System.Text.Json;

namespace IncidentCompass.Domain.Incidents;

public sealed record TriageArtifact(
    Guid Id,
    Guid JobId,
    int? Attempt,
    ArtifactKind Kind,
    string? DomainRef,
    JsonElement RedactedPayload,
    string ContentHash,
    DateTimeOffset CreatedAtUtc);
