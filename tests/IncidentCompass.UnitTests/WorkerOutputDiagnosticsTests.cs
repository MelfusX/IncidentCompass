using IncidentCompass.Application.Investigation.Jobs;

namespace IncidentCompass.UnitTests;

public sealed class WorkerOutputDiagnosticsTests
{
    [Fact]
    public void SchemaValidator_UsesRoleNameInDiagnostics()
    {
        const string schema = """
            {
              "type": "object",
              "additionalProperties": false,
              "properties": {
                "matched": { "type": "boolean" },
                "items": { "type": "array", "items": { "type": "object" } }
              },
              "required": ["matched", "items"]
            }
            """;

        var exception = Assert.Throws<InvalidOperationException>(() =>
            AnalysisWorkerOutputSchemaValidator.Validate("{}", schema, "memory"));

        Assert.Contains("memory worker output", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Analysis worker output", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnalysisParser_UsesRoleNameInDiagnostics()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            AnalysisWorkerOutputParser.Parse("{}", "memory"));

        Assert.Contains("memory worker output", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Analysis worker output", exception.Message, StringComparison.Ordinal);
    }
}
