using System.Text.Json;
using System.Text.Json.Nodes;
using IncidentCompass.Application.Intake.Configuration;

namespace IncidentCompass.Infrastructure.Intake;

internal sealed class TriageConfigurationMaterializer(TriageConfigurationLoadValidator validator)
{
    public TriageConfiguration Materialize(
        string configHash,
        JsonNode configNode,
        JsonObject referencesNode)
    {
        var document = Deserialize(configNode);
        var roles = ResolveRoleReferences(RequireDictionary(document.Roles, "Roles"), referencesNode);
        var orchestrator = ResolveOrchestratorReference(RequireValue(document.Orchestrator, "Orchestrator"), referencesNode);
        var rules = NormalizeRules(document.Rules ?? []);

        var configuration = new TriageConfiguration(
            configHash,
            RequireDictionary(document.Providers, "Providers"),
            RequireDictionary(document.Routes, "Routes"),
            orchestrator,
            roles,
            RequireDictionary(document.Tools, "Tools", allowEmpty: true),
            rules,
            RequireValue(document.Ingestion, "Ingestion"),
            RequireValue(document.FaultGrouping, "FaultGrouping"),
            document.Redaction ?? RedactionSettings.Default)
        {
            CurrentReleases = NormalizeCurrentReleases(document.CurrentReleases),
            Actions = document.Actions ?? TriageActionSettings.Default
        };
        validator.Validate(configuration);
        return configuration;
    }

    private static SerializedTriageConfiguration Deserialize(JsonNode configNode)
    {
        try
        {
            using var document = JsonDocument.Parse(configNode.ToJsonString());
            return JsonSerializer.Deserialize<SerializedTriageConfiguration>(document.RootElement)!
                ?? throw TriageConfigurationLoadException.InvalidJson("triage configuration", new JsonException("Empty document."));
        }
        catch (JsonException exception)
        {
            throw TriageConfigurationLoadException.InvalidJson("triage configuration", exception);
        }
    }

    private static IReadOnlyDictionary<string, T> RequireDictionary<T>(
        IReadOnlyDictionary<string, T>? values,
        string name,
        bool allowEmpty = false)
    {
        if (values is null || (!allowEmpty && values.Count == 0))
        {
            throw TriageConfigurationLoadException.MissingSection(name);
        }

        return values.ToDictionary(
            pair => pair.Key,
            pair => pair.Value,
            StringComparer.Ordinal);
    }

    private static T RequireValue<T>(T? value, string name)
        where T : class
    {
        return value ?? throw TriageConfigurationLoadException.MissingSection(name);
    }

    private static IReadOnlyDictionary<string, TriageRoleSettings> ResolveRoleReferences(
        IReadOnlyDictionary<string, TriageRoleSettings> roles,
        JsonObject referencesNode)
    {
        return roles.ToDictionary(
            pair => pair.Key,
            pair => pair.Value with
            {
                Instructions = ResolveReference(pair.Value.Instructions, referencesNode),
                OutputSchema = ResolveReference(pair.Value.OutputSchema, referencesNode)
            },
            StringComparer.Ordinal);
    }

    private static OrchestratorSettings ResolveOrchestratorReference(
        OrchestratorSettings orchestrator,
        JsonObject referencesNode)
    {
        return orchestrator with
        {
            Instructions = ResolveReference(orchestrator.Instructions, referencesNode)
        };
    }

    private static IReadOnlyCollection<TriageRuleSettings> NormalizeRules(IReadOnlyCollection<TriageRuleSettings> rules)
    {
        return rules
            .Select(rule => rule with
            {
                Scope = string.IsNullOrWhiteSpace(rule.Scope) ? "attempt" : rule.Scope
            })
            .ToArray();
    }

    private static IReadOnlyDictionary<string, string> NormalizeCurrentReleases(
        IReadOnlyDictionary<string, string>? currentReleases)
    {
        return currentReleases?.ToDictionary(
                pair => pair.Key,
                pair => pair.Value,
                StringComparer.Ordinal)
            ?? new Dictionary<string, string>(StringComparer.Ordinal);
    }

    private static string ResolveReference(string value, JsonObject referencesNode)
    {
        if (!value.StartsWith("ref:", StringComparison.Ordinal))
        {
            return value;
        }

        if (referencesNode.TryGetPropertyValue(value, out var node) &&
            node is JsonValue jsonValue &&
            jsonValue.GetValueKind() == JsonValueKind.String)
        {
            return jsonValue.GetValue<string>();
        }

        throw TriageConfigurationLoadException.SnapshotReferenceMissing(value);
    }
}
