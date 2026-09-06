using System.Text.Json;
using System.Text.Json.Nodes;
using IncidentCompass.Application.Core.Exceptions;
using IncidentCompass.Application.Core.Serialization;
using IncidentCompass.Application.Intake.Configuration;
using IncidentCompass.Infrastructure.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace IncidentCompass.Infrastructure.Intake;

internal sealed class FileTriageConfigurationRepository : ITriageConfigurationRepository
{
    private readonly IHostEnvironment hostEnvironment;
    private readonly IOptions<TriageConfigSourceOptions> configSourceOptions;
    private readonly TriageConfigurationMaterializer materializer;
    private readonly ITriageConfigurationSnapshotStore snapshotStore;
    private readonly Lazy<Task<TriageConfiguration>> lazyConfiguration;

    public FileTriageConfigurationRepository(
        IHostEnvironment hostEnvironment,
        IOptions<TriageConfigSourceOptions> configSourceOptions,
        TriageConfigurationMaterializer materializer,
        ITriageConfigurationSnapshotStore snapshotStore)
    {
        this.hostEnvironment = hostEnvironment;
        this.configSourceOptions = configSourceOptions;
        this.materializer = materializer;
        this.snapshotStore = snapshotStore;
        lazyConfiguration = new Lazy<Task<TriageConfiguration>>(
            () => LoadAsync(persistSnapshot: true, CancellationToken.None),
            LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public async Task<TriageConfiguration> GetCurrentAsync(CancellationToken cancellationToken)
    {
        return await lazyConfiguration.Value.WaitAsync(cancellationToken);
    }

    internal Task<TriageConfiguration> ValidateCurrentAsync(CancellationToken cancellationToken) =>
        LoadAsync(persistSnapshot: false, cancellationToken);

    public async Task<TriageConfiguration> GetByHashAsync(string configHash, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(configHash))
        {
            throw new ArgumentException("Config hash must be non-blank.", nameof(configHash));
        }

        var snapshot = await snapshotStore.GetAsync(configHash, cancellationToken);
        if (snapshot is null)
        {
            throw new NotFoundException(
                $"Triage configuration snapshot with config_hash '{configHash}' was not found.",
                ApplicationErrorCodes.TriageConfigurationSnapshotNotFound,
                "The referenced triage configuration snapshot does not exist.");
        }

        return materializer.Materialize(configHash, snapshot.SerializedConfig, snapshot.Instructions);
    }

    private async Task<TriageConfiguration> LoadAsync(bool persistSnapshot, CancellationToken cancellationToken)
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

        var configNode = await ReadConfigNodeAsync(absolutePath, cancellationToken);
        var instructionsNode = await BuildInstructionsNodeAsync(configNode, absolutePath, cancellationToken);
        var configHash = CanonicalJsonSerializer.ComputeSha256Hex(
            CanonicalJsonSerializer.Canonicalize(configNode),
            CanonicalJsonSerializer.Canonicalize(instructionsNode));

        TriageConfiguration configuration;
        try
        {
            configuration = materializer.Materialize(configHash, configNode, instructionsNode);
        }
        catch (TriageConfigurationLoadException exception)
        {
            throw new InvalidOperationException(
                $"Triage configuration file '{absolutePath}' is invalid: {exception.Message}",
                exception);
        }

        if (persistSnapshot)
        {
            await snapshotStore.PersistAsync(configHash, configNode, instructionsNode, CancellationToken.None);
        }

        return configuration;
    }

    private static async Task<JsonNode> ReadConfigNodeAsync(string absolutePath, CancellationToken cancellationToken)
    {
        var text = await File.ReadAllTextAsync(absolutePath, cancellationToken);
        try
        {
            var node = JsonNode.Parse(text)
                ?? throw TriageConfigurationLoadException.InvalidJson(absolutePath, new JsonException("Empty document."));
            EnvironmentPlaceholderExpander.Expand(node);
            return node;
        }
        catch (JsonException exception)
        {
            throw TriageConfigurationLoadException.InvalidJson(absolutePath, exception);
        }
    }

    private static async Task<JsonObject> BuildInstructionsNodeAsync(
        JsonNode configNode,
        string absoluteConfigPath,
        CancellationToken cancellationToken)
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

            var content = await File.ReadAllTextAsync(resolvedPath, cancellationToken);
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
}
