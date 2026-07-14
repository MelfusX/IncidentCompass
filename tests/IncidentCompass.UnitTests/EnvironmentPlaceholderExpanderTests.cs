using System.Text.Json.Nodes;
using IncidentCompass.Infrastructure.Intake;

namespace IncidentCompass.UnitTests;

public sealed class EnvironmentPlaceholderExpanderTests
{
    [Fact]
    public void Expand_UsesEnvironmentValuesAndFallbacks()
    {
        const string modelVariable = "INCIDENTCOMPASS_TEST_ROUTE_MODEL";
        var previous = Environment.GetEnvironmentVariable(modelVariable);
        try
        {
            Environment.SetEnvironmentVariable(modelVariable, "configured-model");
            var node = JsonNode.Parse("""
                {
                  "Routes": {
                    "analysis-chat": { "Model": "${INCIDENTCOMPASS_TEST_ROUTE_MODEL:-fallback-model}" },
                    "memory-embed": { "Model": "${INCIDENTCOMPASS_TEST_MISSING_EMBEDDING_MODEL:-fallback-embedding}" }
                  }
                }
                """)!;

            EnvironmentPlaceholderExpander.Expand(node);

            Assert.Equal("configured-model", node["Routes"]!["analysis-chat"]!["Model"]!.GetValue<string>());
            Assert.Equal("fallback-embedding", node["Routes"]!["memory-embed"]!["Model"]!.GetValue<string>());
        }
        finally
        {
            Environment.SetEnvironmentVariable(modelVariable, previous);
        }
    }
}
