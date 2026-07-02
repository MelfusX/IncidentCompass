using System.Text.Json.Nodes;
using IncidentCompass.Application.Intake.Normalization;
using IncidentCompass.Application.Intake.Redaction;

namespace IncidentCompass.UnitTests;

public sealed class SecretRedactorTests
{
    [Fact]
    public void RedactText_RedactsBearerTokenButKeepsSurroundingText()
    {
        const string input = "Auth failed calling upstream with Bearer abcdef1234567890ghijklmnop, retrying";

        var result = SecretRedactor.RedactText(input)!;

        Assert.Contains("Auth failed calling upstream with Bearer [REDACTED], retrying", result, StringComparison.Ordinal);
        Assert.DoesNotContain("abcdef1234567890ghijklmnop", result, StringComparison.Ordinal);
    }

    [Fact]
    public void RedactText_RedactsAwsAccessKey()
    {
        const string input = "leaked key AKIAABCDEFGHIJKLMNOP in log line";

        var result = SecretRedactor.RedactText(input)!;

        Assert.Contains("[REDACTED]", result, StringComparison.Ordinal);
        Assert.DoesNotContain("AKIAABCDEFGHIJKLMNOP", result, StringComparison.Ordinal);
    }

    [Fact]
    public void RedactText_NullInput_ReturnsNullWithoutThrowing()
    {
        var result = SecretRedactor.RedactText(null);

        Assert.Null(result);
    }

    [Fact]
    public void RedactText_TextWithNoSecrets_IsReturnedUnchanged()
    {
        const string input = "Checkout timed out after 30000ms calling payments-api";

        var result = SecretRedactor.RedactText(input)!;

        Assert.Equal(input, result);
    }

    [Fact]
    public void Redact_PreservesNullOptionalTextFields()
    {
        var signal = new NormalizedSignal(
            Source: "tester",
            ExternalId: null,
            TraceId: null,
            SpanId: null,
            ParentSpanId: null,
            ServiceName: "payments-api",
            Environment: "prod",
            OperationName: null,
            Severity: null,
            ErrorType: null,
            ErrorMessage: null,
            Summary: "Checkout failed",
            Description: null,
            HttpMethod: null,
            HttpRoute: null,
            HttpStatusCode: null,
            DurationMs: null,
            Attributes: new JsonObject(),
            Body: new JsonObject(),
            ObservedAtUtc: DateTimeOffset.UtcNow);

        var redacted = SecretRedactor.Redact(signal);

        Assert.Null(redacted.ErrorMessage);
        Assert.Null(redacted.Description);
        Assert.Equal("Checkout failed", redacted.Summary);
    }

    [Fact]
    public void RedactJsonNode_RedactsPasswordKeyAtNestedDepth()
    {
        var node = JsonNode.Parse("""
            {
                "level1": {
                    "level2": {
                        "password": "hunter2",
                        "keep": "value"
                    }
                }
            }
            """)!;

        var redacted = SecretRedactor.RedactJsonNode(node);

        Assert.Equal("[REDACTED]", redacted["level1"]!["level2"]!["password"]!.GetValue<string>());
        Assert.Equal("value", redacted["level1"]!["level2"]!["keep"]!.GetValue<string>());
    }

    [Fact]
    public void RedactJsonNode_RedactsSecretInsideArrayElement()
    {
        var node = JsonNode.Parse("""
            { "lines": ["normal line", "Bearer abcdef1234567890ghijklmnop leaked here"] }
            """)!;

        var redacted = SecretRedactor.RedactJsonNode(node);

        var lines = redacted["lines"]!.AsArray();
        Assert.Equal("normal line", lines[0]!.GetValue<string>());
        Assert.Contains("Bearer [REDACTED]", lines[1]!.GetValue<string>(), StringComparison.Ordinal);
    }

    [Fact]
    public void RedactJsonNode_NoSecrets_ContentIsUnchanged()
    {
        var node = JsonNode.Parse("""{"serviceName":"payments-api","count":3}""")!;

        var redacted = SecretRedactor.RedactJsonNode(node);

        Assert.Equal("payments-api", redacted["serviceName"]!.GetValue<string>());
        Assert.Equal(3, redacted["count"]!.GetValue<int>());
    }
}
