using System.Text.Json;
using IncidentCompass.Domain.Incidents;

namespace IncidentCompass.Application.Investigation.Jobs;

internal sealed record TriageToolResultCommitRequest(
    TriageJob Job,
    string Role,
    string ToolName,
    JsonElement Output,
    string ContentHash,
    string Rationale,
    IReadOnlyCollection<TriageArtifact>? AdditionalArtifacts = null);
