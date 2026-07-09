using System.Text.Json.Nodes;
using IncidentCompass.Application.Intake.Normalization;
using IncidentCompass.Infrastructure.Intake;

namespace IncidentCompass.UnitTests;

public sealed class TriageConfigurationMaterializerTests
{
    [Fact]
    public void Materialize_ResolvesReferencesAndDefaultsRuleScope()
    {
        var configuration = CreateMaterializer().Materialize(
            "hash-1",
            ValidConfigNode(),
            ResolvedReferences());

        Assert.Equal("hash-1", configuration.ConfigHash);
        Assert.Equal("orchestrator body", configuration.Orchestrator.Instructions);
        Assert.Equal("analysis body", configuration.Roles["analysis"].Instructions);
        Assert.Equal("{ \"type\": \"object\" }", configuration.Roles["analysis"].OutputSchema);
        var rule = Assert.Single(configuration.Rules);
        Assert.Equal("attempt", rule.Scope);
        Assert.Empty(configuration.Redaction.Patterns);
    }

    [Fact]
    public void Materialize_RoleToolNotConfigured_FailsLoadValidation()
    {
        var node = ValidConfigNode();
        var roles = (JsonObject)node["Roles"]!;
        var analysis = (JsonObject)roles["analysis"]!;
        analysis["Tools"] = new JsonArray("unknown_tool");

        var exception = Assert.Throws<TriageConfigurationLoadException>(() =>
            CreateMaterializer().Materialize("hash-1", node, ResolvedReferences()));

        Assert.Contains("Roles.analysis.Tools", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Materialize_AllowedSourceWithoutRegisteredNormalizer_FailsLoadValidation()
    {
        var node = ValidConfigNode();
        var ingestion = (JsonObject)node["Ingestion"]!;
        ingestion["AllowedSources"] = new JsonArray("webhook");

        var exception = Assert.Throws<TriageConfigurationLoadException>(() =>
            CreateMaterializer().Materialize("hash-1", node, ResolvedReferences()));

        Assert.Contains("Ingestion.AllowedSources", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Materialize_RoleRouteMustBeChatRoute()
    {
        var node = ValidConfigNode();
        var roles = (JsonObject)node["Roles"]!;
        var analysis = (JsonObject)roles["analysis"]!;
        analysis["RouteId"] = "memory-embed";

        var exception = Assert.Throws<TriageConfigurationLoadException>(() =>
            CreateMaterializer().Materialize("hash-1", node, ResolvedReferences()));

        Assert.Contains("Roles.analysis.RouteId", exception.Message, StringComparison.Ordinal);
    }


    [Fact]
    public void Materialize_RateCapWithoutPositiveMax_FailsLoadValidation()
    {
        var node = ValidConfigNode();
        node["Rules"] = new JsonArray(new JsonObject
        {
            ["Type"] = "rate_cap",
            ["Tool"] = "*",
            ["Scope"] = "attempt",
            ["Max"] = 0
        });

        var exception = Assert.Throws<TriageConfigurationLoadException>(() =>
            CreateMaterializer().Materialize("hash-1", node, ResolvedReferences()));

        Assert.Contains("Rules.rate_cap.Max", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Materialize_PreconditionReferencingUnknownTool_FailsLoadValidation()
    {
        var node = ValidConfigNode();
        node["Rules"] = new JsonArray(new JsonObject
        {
            ["Type"] = "precondition",
            ["Tool"] = "memory_search",
            ["Scope"] = "attempt",
            ["RequiresSuccessfulToolResult"] = "unknown_tool"
        });

        var exception = Assert.Throws<TriageConfigurationLoadException>(() =>
            CreateMaterializer().Materialize("hash-1", node, ResolvedReferences()));

        Assert.Contains("Rules.RequiresSuccessfulToolResult", exception.Message, StringComparison.Ordinal);
    }


    [Theory]
    [InlineData("LookbackMinutes", "FaultGrouping.LookbackMinutes")]
    [InlineData("SilenceWindowMinutes", "FaultGrouping.SilenceWindowMinutes")]
    public void Materialize_NonPositiveFaultGroupingWindow_FailsLoadValidation(string settingName, string expectedMessage)
    {
        var node = ValidConfigNode();
        var faultGrouping = (JsonObject)node["FaultGrouping"]!;
        faultGrouping[settingName] = 0;

        var exception = Assert.Throws<TriageConfigurationLoadException>(() =>
            CreateMaterializer().Materialize("hash-1", node, ResolvedReferences()));

        Assert.Contains(expectedMessage, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Materialize_NonPositiveMassIssueMinNeighborCount_FailsLoadValidation()
    {
        var node = ValidConfigNode();
        var faultGrouping = (JsonObject)node["FaultGrouping"]!;
        var massIssue = (JsonObject)faultGrouping["MassIssue"]!;
        massIssue["MinNeighborCount"] = 0;

        var exception = Assert.Throws<TriageConfigurationLoadException>(() =>
            CreateMaterializer().Materialize("hash-1", node, ResolvedReferences()));

        Assert.Contains("FaultGrouping.MassIssue.MinNeighborCount", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Materialize_NegativeMaxReprompts_FailsLoadValidation()
    {
        var node = ValidConfigNode();
        var orchestrator = (JsonObject)node["Orchestrator"]!;
        var budget = (JsonObject)orchestrator["Budget"]!;
        budget["MaxReprompts"] = -1;

        var exception = Assert.Throws<TriageConfigurationLoadException>(() =>
            CreateMaterializer().Materialize("hash-1", node, ResolvedReferences()));

        Assert.Contains("Orchestrator.Budget.MaxReprompts", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("fault")]
    [InlineData("bogus")]
    public void Materialize_PostMvpOrUnknownRuleScope_FailsLoadValidation(string scope)
    {
        var node = ValidConfigNode();
        node["Rules"] = new JsonArray(new JsonObject
        {
            ["Type"] = "rate_cap",
            ["Tool"] = "*",
            ["Scope"] = scope,
            ["Max"] = 50
        });

        var exception = Assert.Throws<TriageConfigurationLoadException>(() =>
            CreateMaterializer().Materialize("hash-1", node, ResolvedReferences()));

        Assert.Contains("Rules.rate_cap.Scope", exception.Message, StringComparison.Ordinal);
        Assert.Contains("attempt, job", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Materialize_MemorySearchRouteMustBeEmbeddingRoute()
    {
        var node = ValidConfigNode();
        var tools = (JsonObject)node["Tools"]!;
        var memorySearch = (JsonObject)tools["memory_search"]!;
        memorySearch["EmbeddingRouteId"] = "analysis-chat";

        var exception = Assert.Throws<TriageConfigurationLoadException>(() =>
            CreateMaterializer().Materialize("hash-1", node, ResolvedReferences()));

        Assert.Contains("Tools.memory_search.EmbeddingRouteId", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Materialize_MemorySearchRequiresEmbeddingRouteId()
    {
        var node = ValidConfigNode();
        var tools = (JsonObject)node["Tools"]!;
        var memorySearch = (JsonObject)tools["memory_search"]!;
        memorySearch.Remove("EmbeddingRouteId");

        var exception = Assert.Throws<TriageConfigurationLoadException>(() =>
            CreateMaterializer().Materialize("hash-1", node, ResolvedReferences()));

        Assert.Contains("Tools.memory_search.EmbeddingRouteId", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Materialize_MemorySearchCanOnlyBeGrantedToMemoryRole()
    {
        var node = ValidConfigNode();
        var roles = (JsonObject)node["Roles"]!;
        var analysis = (JsonObject)roles["analysis"]!;
        analysis["Tools"] = new JsonArray("memory_search");

        var exception = Assert.Throws<TriageConfigurationLoadException>(() =>
            CreateMaterializer().Materialize("hash-1", node, ResolvedReferences()));

        Assert.Contains("Roles.analysis.Tools", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Materialize_InvalidConfiguredRedactionPattern_FailsLoadValidation()
    {
        var node = ValidConfigNode();
        node["Redaction"] = JsonNode.Parse("""
            {
              "AttributeKeys": [],
              "Patterns": [{ "Name": "broken", "Pattern": "[" }],
              "UserIdentifierAttributes": []
            }
            """);

        var exception = Assert.Throws<TriageConfigurationLoadException>(() =>
            CreateMaterializer().Materialize("hash-1", node, ResolvedReferences()));

        Assert.Contains("Redaction.Patterns[0].Pattern", exception.Message, StringComparison.Ordinal);
    }

    private static TriageConfigurationMaterializer CreateMaterializer()
    {
        var registry = new SignalNormalizerRegistry([
            new TesterSignalNormalizer(),
            new OtelShapedSignalNormalizer(),
            new UserReportSignalNormalizer()
        ]);

        return new TriageConfigurationMaterializer(new TriageConfigurationLoadValidator(registry));
    }

    private static JsonObject ResolvedReferences() => new()
    {
        ["ref:instructions/orchestrator.md"] = "orchestrator body",
        ["ref:instructions/analysis.md"] = "analysis body",
        ["ref:schemas/analysis.json"] = "{ \"type\": \"object\" }"
    };

    private static JsonObject ValidConfigNode()
    {
        return (JsonObject)JsonNode.Parse("""
            {
              "Providers": {
                "local-oai": { "Kind": "OpenAICompatible", "Endpoint": "http://localhost:1234/v1", "ApiKeySecretRef": "LOCAL_OAI_KEY" }
              },
              "Routes": {
                "analysis-chat": { "Kind": "Chat", "ProviderId": "local-oai", "Model": "local-model", "Temperature": 0.1, "MaxOutputTokens": 2000, "ContextWindowTokens": 8192 },
                "report-chat": { "Kind": "Chat", "ProviderId": "local-oai", "Model": "local-model", "Temperature": 0.2, "MaxOutputTokens": 4000, "ContextWindowTokens": 8192 },
                "memory-embed": { "Kind": "Embedding", "ProviderId": "local-oai", "Model": "mock-memory-embedding-v1" }
              },
              "Orchestrator": {
                "Instructions": "ref:instructions/orchestrator.md",
                "RouteId": "report-chat",
                "Tools": ["delegate", "publish_report"],
                "Budget": { "MaxWorkers": 6, "MaxTokens": 200000, "MaxWallClockSeconds": 120 }
              },
              "Roles": {
                "analysis": { "RouteId": "analysis-chat", "Instructions": "ref:instructions/analysis.md", "Tools": [], "OutputSchema": "ref:schemas/analysis.json" }
              },
              "Tools": {
                "memory_search": { "Kind": "internal", "EmbeddingRouteId": "memory-embed", "TopK": 5, "MinScore": 0.25 }
              },
              "Rules": [
                { "Type": "rate_cap", "Tool": "*", "Max": 50 }
              ],
              "Ingestion": { "DefaultTenant": "local", "AllowedSources": ["otel", "user", "tester", "manual"] },
              "FaultGrouping": {
                "LookbackMinutes": 15,
                "SilenceWindowMinutes": 30,
                "FingerprintVersion": 1,
                "MassIssue": { "MinNeighborCount": 5, "MinFingerprintStrength": "strong" }
              }
            }
            """)!;
    }
}
