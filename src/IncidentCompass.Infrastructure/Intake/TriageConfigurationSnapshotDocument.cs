using System.Text.Json.Nodes;

namespace IncidentCompass.Infrastructure.Intake;

internal sealed record TriageConfigurationSnapshotDocument(
    string ConfigHash,
    JsonNode SerializedConfig,
    JsonObject Instructions);
