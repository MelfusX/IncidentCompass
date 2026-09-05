using System.Text.Json;
using System.Text.Json.Nodes;
using IncidentCompass.Infrastructure.Postgres;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace IncidentCompass.Infrastructure.Intake;

internal sealed partial class TriageConfigurationSnapshotStore(
    PostgresDataSourceProvider dataSourceProvider,
    TimeProvider timeProvider,
    ILogger<TriageConfigurationSnapshotStore> logger) : ITriageConfigurationSnapshotStore
{
    public Task PersistAsync(
        string configHash,
        JsonNode configNode,
        JsonObject instructionsNode,
        CancellationToken cancellationToken) =>
        PostgresOperation.ExecuteAsync(
            "persist triage configuration snapshot",
            () => PersistCoreAsync(configHash, configNode, instructionsNode, cancellationToken));

    private async Task PersistCoreAsync(
        string configHash,
        JsonNode configNode,
        JsonObject instructionsNode,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = await dataSourceProvider.OpenConnectionAsync(cancellationToken);
            await using var command = new NpgsqlCommand("""
                INSERT INTO incidentcompass.triage_config_snapshots (config_hash, serialized_config, instructions, created_at_utc)
                VALUES (@config_hash, @serialized_config::jsonb, @instructions::jsonb, @created_at_utc)
                ON CONFLICT (config_hash) DO NOTHING;
                """, connection);

            command.AddParameter("config_hash", configHash);
            command.AddJsonbParameter("serialized_config", configNode.ToJsonString());
            command.AddJsonbParameter("instructions", instructionsNode.ToJsonString());
            command.AddParameter("created_at_utc", timeProvider.GetUtcNow());

            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (PostgresConnectionConfigurationException exception)
        {
            LogSnapshotNotPersisted(logger, exception, configHash);
        }
    }

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Triage configuration snapshot for hash '{ConfigHash}' was not persisted because PostgreSQL is not configured.")]
    private static partial void LogSnapshotNotPersisted(ILogger logger, Exception exception, string configHash);

    public Task<TriageConfigurationSnapshotDocument?> GetAsync(
        string configHash,
        CancellationToken cancellationToken) =>
        PostgresOperation.ExecuteAsync(
            "read triage configuration snapshot",
            () => GetCoreAsync(configHash, cancellationToken));

    private async Task<TriageConfigurationSnapshotDocument?> GetCoreAsync(string configHash, CancellationToken cancellationToken)
    {
        await using var connection = await dataSourceProvider.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            SELECT serialized_config::text, instructions::text
            FROM incidentcompass.triage_config_snapshots
            WHERE config_hash = @config_hash;
            """, connection);

        command.AddParameter("config_hash", configHash);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new TriageConfigurationSnapshotDocument(
            configHash,
            ParseNode(reader.GetString(0), "serialized_config"),
            ParseObject(reader.GetString(1), "instructions"));
    }

    private static JsonNode ParseNode(string json, string columnName)
    {
        try
        {
            return JsonNode.Parse(json) ?? throw TriageConfigurationLoadException.InvalidSnapshot(columnName);
        }
        catch (JsonException exception)
        {
            throw TriageConfigurationLoadException.InvalidSnapshot(columnName, exception);
        }
    }

    private static JsonObject ParseObject(string json, string columnName)
    {
        return ParseNode(json, columnName) as JsonObject
            ?? throw TriageConfigurationLoadException.InvalidSnapshot(columnName);
    }
}
