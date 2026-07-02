using System.Text.Json;

namespace IncidentCompass.Domain.Incidents;

public sealed record TriageConfigSnapshot(
    string ConfigHash,
    JsonElement SerializedConfig,
    JsonElement Instructions,
    DateTimeOffset CreatedAtUtc);
