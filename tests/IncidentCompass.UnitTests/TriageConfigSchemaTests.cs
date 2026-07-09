using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;

namespace IncidentCompass.UnitTests;

public sealed class TriageConfigSchemaTests
{
    private static readonly Lazy<JsonSchema> Schema = new(BuildSchema);

    [Fact]
    public void EveryShippedConfiguration_MatchesPublishedSchema()
    {
        foreach (var configurationPath in ShippedConfigurationPaths())
        {
            var result = Evaluate(LoadConfiguration(configurationPath));

            Assert.True(result.IsValid, $"Shipped configuration is schema-invalid: {configurationPath}");
        }
    }

    [Fact]
    public void MissingRequiredSection_IsRejected()
    {
        var configuration = LoadConfiguration();
        configuration.AsObject().Remove("Ingestion");

        var result = Evaluate(configuration);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void InvalidBudgetAndRedactionShape_AreRejected()
    {
        var configuration = LoadConfiguration();
        configuration["Orchestrator"]!["Budget"]!["MaxTokens"] = 0;
        configuration["Redaction"]!["Patterns"]![0]!["Name"] = string.Empty;

        var result = Evaluate(configuration);

        Assert.False(result.IsValid);
    }

    internal static EvaluationResults EvaluateFixture(JsonNode configuration) => Evaluate(configuration);

    private static EvaluationResults Evaluate(JsonNode configuration)
    {
        using var configurationDocument = JsonDocument.Parse(configuration.ToJsonString());
        return Schema.Value.Evaluate(configurationDocument.RootElement);
    }

    private static JsonSchema BuildSchema()
    {
        using var schemaDocument = JsonDocument.Parse(File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "config",
            "incidentcompass.schema.json")));
        return JsonSchema.Build(schemaDocument.RootElement.Clone());
    }

    private static JsonNode LoadConfiguration(string? path = null) =>
        JsonNode.Parse(File.ReadAllText(path ?? ShippedConfigurationPaths().First()))!;

    private static IReadOnlyCollection<string> ShippedConfigurationPaths()
    {
        var root = FindRepositoryRoot();
        return [
            Path.Combine(root, "config", "incidentcompass.config.json"),
            Path.Combine(root, "tests", "IncidentCompass.IntegrationTests", "Fixtures", "test-triage-config", "incidentcompass.config.json")
        ];
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "IncidentCompass.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
