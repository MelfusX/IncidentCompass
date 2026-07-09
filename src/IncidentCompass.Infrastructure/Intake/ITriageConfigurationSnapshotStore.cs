using System.Text.Json.Nodes;

namespace IncidentCompass.Infrastructure.Intake;

internal interface ITriageConfigurationSnapshotStore
{
    Task PersistAsync(
        string configHash,
        JsonNode configNode,
        JsonObject instructionsNode,
        CancellationToken cancellationToken);

    Task<TriageConfigurationSnapshotDocument?> GetAsync(
        string configHash,
        CancellationToken cancellationToken);
}
