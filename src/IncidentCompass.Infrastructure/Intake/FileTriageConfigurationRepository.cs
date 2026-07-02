using System.Text.Json;
using System.Text.Json.Nodes;
using IncidentCompass.Application.Core.Serialization;
using IncidentCompass.Application.Intake.Configuration;
using IncidentCompass.Infrastructure.Configuration;
using IncidentCompass.Infrastructure.Postgres;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;

namespace IncidentCompass.Infrastructure.Intake;

internal sealed class FileTriageConfigurationRepository : ITriageConfigurationRepository
{
    private readonly IHostEnvironment hostEnvironment;
    private readonly IOptions<TriageConfigSourceOptions> configSourceOptions;
    private readonly PostgresDataSourceProvider dataSourceProvider;
    private readonly TimeProvider timeProvider;
    private readonly ILogger<FileTriageConfigurationRepository> logger;
    private readonly Lazy<Task<TriageConfiguration>> lazyConfiguration;

    public FileTriageConfigurationRepository(
        IHostEnvironment hostEnvironment,
        IOptions<TriageConfigSourceOptions> configSourceOptions,
        PostgresDataSourceProvider dataSourceProvider,
        TimeProvider timeProvider,
        ILogger<FileTriageConfigurationRepository> logger)
    {
        this.hostEnvironment = hostEnvironment;
        this.configSourceOptions = configSourceOptions;
        this.dataSourceProvider = dataSourceProvider;
        this.timeProvider = timeProvider;
        this.logger = logger;
        lazyConfiguration = new Lazy<Task<TriageConfiguration>>(LoadAsync, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public async Task<TriageConfiguration> GetCurrentAsync(CancellationToken cancellationToken)
    {
        return await lazyConfiguration.Value.WaitAsync(cancellationToken);
    }

    private async Task<TriageConfiguration> LoadAsync()
    {
        var kind = configSourceOptions.Value.Kind;
        if (!string.Equals(kind, "File", StringComparison.Ordinal))
        {
            throw TriageConfigurationLoadException.UnsupportedKind(kind);
        }

        var absolutePath = Path.GetFullPath(Path.Combine(hostEnvironment.ContentRootPath, configSourceOptions.Value.Path));
        if (!File.Exists(absolutePath))
        {
            throw TriageConfigurationLoadException.ConfigFileMissing(absolutePath);
        }

        var configNode = await ReadConfigNodeAsync(absolutePath);
        var instructionsNode = await BuildInstructionsNodeAsync(configNode, absolutePath);

        var configHash = CanonicalJsonSerializer.ComputeSha256Hex(
            CanonicalJsonSerializer.Canonicalize(configNode),
            CanonicalJsonSerializer.Canonicalize(instructionsNode));

        await PersistSnapshotAsync(configHash, configNode, instructionsNode);

        var ingestionSettings = Deserialize<IngestionSettings>(configNode["Ingestion"]);
        var faultGroupingSettings = Deserialize<FaultGroupingSettings>(configNode["FaultGrouping"]);
        ValidateFaultGroupingSettings(faultGroupingSettings);

        return new TriageConfiguration(configHash, ingestionSettings, faultGroupingSettings);
    }

    private static async Task<JsonNode> ReadConfigNodeAsync(string absolutePath)
    {
        var text = await File.ReadAllTextAsync(absolutePath);
        try
        {
            return JsonNode.Parse(text) ?? throw TriageConfigurationLoadException.InvalidJson(absolutePath, new JsonException("Empty document."));
        }
        catch (JsonException exception)
        {
            throw TriageConfigurationLoadException.InvalidJson(absolutePath, exception);
        }
    }

    private static async Task<JsonObject> BuildInstructionsNodeAsync(JsonNode configNode, string absoluteConfigPath)
    {
        var refValues = new HashSet<string>(StringComparer.Ordinal);
        CollectRefs(configNode, refValues);

        var configDirectory = Path.GetDirectoryName(absoluteConfigPath)!;
        var instructionsNode = new JsonObject();
        foreach (var refValue in refValues)
        {
            var relativePath = refValue["ref:".Length..];
            var resolvedPath = Path.GetFullPath(Path.Combine(configDirectory, relativePath));
            if (!File.Exists(resolvedPath))
            {
                throw TriageConfigurationLoadException.ReferencedFileMissing(refValue, resolvedPath);
            }

            var content = await File.ReadAllTextAsync(resolvedPath);
            var normalizedContent = content.Replace("\r\n", "\n").Replace("\r", "\n");
            instructionsNode[refValue] = normalizedContent;
        }

        return instructionsNode;
    }

    private static void CollectRefs(JsonNode? node, HashSet<string> refValues)
    {
        switch (node)
        {
            case JsonObject jsonObject:
                foreach (var property in jsonObject)
                {
                    CollectRefs(property.Value, refValues);
                }

                break;
            case JsonArray jsonArray:
                foreach (var element in jsonArray)
                {
                    CollectRefs(element, refValues);
                }

                break;
            case JsonValue jsonValue when jsonValue.GetValueKind() == JsonValueKind.String:
                var value = jsonValue.GetValue<string>();
                if (value.StartsWith("ref:", StringComparison.Ordinal))
                {
                    refValues.Add(value);
                }

                break;
        }
    }

    private async Task PersistSnapshotAsync(string configHash, JsonNode configNode, JsonObject instructionsNode)
    {
        // A missing connection-string configuration is a bootstrap concern distinct from "the
        // triage config file is broken" -- real Api/Worker deployments always configure
        // ConnectionStrings:IncidentCompass (every other repository needs it too), so this only
        // fires for compositions that intentionally never wire Postgres (for example, host tests
        // that exist solely to validate ModelGateway/Embeddings options). The loaded/hashed
        // configuration is still valid and usable without the snapshot row; a real deployment's
        // config_hash simply would not resolve from triage_config_snapshots until Postgres is
        // configured, which is caught by other repositories' hard dependency on it at first use.
        try
        {
            await using var connection = await dataSourceProvider.OpenConnectionAsync(CancellationToken.None);
            await using var command = new NpgsqlCommand("""
                INSERT INTO incidentcompass.triage_config_snapshots (config_hash, serialized_config, instructions, created_at_utc)
                VALUES (@config_hash, @serialized_config::jsonb, @instructions::jsonb, @created_at_utc)
                ON CONFLICT (config_hash) DO NOTHING;
                """, connection);

            command.Parameters.AddWithValue("config_hash", configHash);
            command.Parameters.AddWithValue("serialized_config", NpgsqlDbType.Jsonb, configNode.ToJsonString());
            command.Parameters.AddWithValue("instructions", NpgsqlDbType.Jsonb, instructionsNode.ToJsonString());
            command.Parameters.AddWithValue("created_at_utc", timeProvider.GetUtcNow());

            await command.ExecuteNonQueryAsync(CancellationToken.None);
        }
        catch (PostgresConnectionConfigurationException exception)
        {
            logger.LogWarning(
                exception,
                "Triage configuration snapshot for hash '{ConfigHash}' was not persisted because PostgreSQL is not configured.",
                configHash);
        }
    }

    private static void ValidateFaultGroupingSettings(FaultGroupingSettings settings)
    {
        if (!settings.MassIssue.TryGetMinimumFingerprintStrength(out _))
        {
            throw TriageConfigurationLoadException.InvalidSetting(
                "FaultGrouping.MassIssue.MinFingerprintStrength",
                settings.MassIssue.MinFingerprintStrength,
                "one of: weak, strong");
        }
    }

    private static T Deserialize<T>(JsonNode? node)
    {
        using var document = JsonDocument.Parse(node!.ToJsonString());
        return JsonSerializer.Deserialize<T>(document.RootElement)!;
    }
}
