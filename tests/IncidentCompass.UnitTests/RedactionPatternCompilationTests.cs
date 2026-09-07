using System.Text.Json.Nodes;
using IncidentCompass.Application.Intake.Configuration;
using IncidentCompass.Application.Intake.Normalization;
using IncidentCompass.Application.Intake.Redaction;
using Microsoft.Extensions.Logging;

namespace IncidentCompass.UnitTests;

/// <summary>
/// Configured redaction patterns must be compiled once per configuration snapshot, and a pattern
/// that exceeds its match timeout must fail closed instead of escaping intake or leaking the value
/// it failed to clean.
/// </summary>
public sealed class RedactionPatternCompilationTests
{
    private const string CatastrophicPattern = @"(a+)+$";
    private static readonly string HostileValue = new string('a', 40) + "!";

    [Fact]
    public void RedactText_CompilesConfiguredPatternsOncePerSettingsInstance()
    {
        var settings = new RedactionSettings(
            AttributeKeys: [],
            Patterns: [new RedactionPatternSettings("internal-id", @"INC-[0-9]+")],
            UserIdentifierAttributes: []);

        Assert.False(CompiledRedactionPatterns.TryGetExisting(settings, out _));

        SecretRedactor.RedactText("first call INC-1", settings);
        Assert.True(CompiledRedactionPatterns.TryGetExisting(settings, out var afterFirst));

        SecretRedactor.RedactText("second call INC-2", settings);
        SecretRedactor.RedactJsonNode(JsonNode.Parse("""{"message":"third call INC-3"}""")!, settings);
        Assert.True(CompiledRedactionPatterns.TryGetExisting(settings, out var afterMore));

        Assert.Same(afterFirst, afterMore);
        Assert.Same(afterFirst!.Patterns[0], afterMore!.Patterns[0]);
        Assert.Same(afterFirst.Patterns[0].Matcher, afterMore.Patterns[0].Matcher);
    }

    [Fact]
    public void ForSettings_DistinctSettingsInstancesNeverShareCompiledPatterns()
    {
        var first = new RedactionSettings([], [new RedactionPatternSettings("internal-id", @"INC-[0-9]+")], []);
        var second = new RedactionSettings([], [new RedactionPatternSettings("internal-id", @"INC-[0-9]+")], []);

        var firstPatterns = CompiledRedactionPatterns.ForSettings(first);
        var secondPatterns = CompiledRedactionPatterns.ForSettings(second);

        Assert.NotSame(firstPatterns, secondPatterns);
        Assert.NotSame(firstPatterns.Patterns[0].Matcher, secondPatterns.Patterns[0].Matcher);
        Assert.Same(firstPatterns, CompiledRedactionPatterns.ForSettings(first));
    }

    [Fact]
    public void Redact_PatternTimeoutReplacesTheWholeFieldWithTheTimeoutMarker()
    {
        var settings = TimeoutSettings("boom-timeout-marker");
        var signal = CreateSignal() with { ErrorMessage = "alpha " + HostileValue };

        var redacted = SecretRedactor.Redact(signal, settings);

        Assert.Equal(SecretRedactor.PatternTimeoutMarker, redacted.ErrorMessage);
        Assert.NotEqual("[REDACTED]", redacted.ErrorMessage);
        Assert.DoesNotContain("alpha", redacted.ErrorMessage!, StringComparison.Ordinal);
        Assert.DoesNotContain("aaa", redacted.ErrorMessage!, StringComparison.Ordinal);
        Assert.Equal("payments-api", redacted.ServiceName);
    }

    [Fact]
    public void Redact_PatternTimeoutLogsThePatternAndFieldPathButNeverTheValue()
    {
        var logger = new RecordingLogger<RedactionPatternCompilationTests>();
        var settings = TimeoutSettings("boom-log-content");
        var signal = CreateSignal() with { ErrorMessage = HostileValue };

        var redacted = SecretRedactor.Redact(signal, settings, canonicalPseudonymVerifier: null, logger);

        Assert.Equal(SecretRedactor.PatternTimeoutMarker, redacted.ErrorMessage);
        var entry = logger.Single(3601);
        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.Contains("boom-log-content", entry.Message, StringComparison.Ordinal);
        Assert.Contains("signal.errorMessage", entry.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(HostileValue, entry.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("aaa", entry.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(CatastrophicPattern, entry.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Redact_PatternTimeoutIsLoggedOncePerPatternPerConfigurationSnapshot()
    {
        var logger = new RecordingLogger<RedactionPatternCompilationTests>();
        var settings = TimeoutSettings("boom-throttle");
        var signal = CreateSignal() with { ErrorMessage = HostileValue, Description = HostileValue };

        var redacted = SecretRedactor.Redact(signal, settings, canonicalPseudonymVerifier: null, logger);

        Assert.Equal(SecretRedactor.PatternTimeoutMarker, redacted.ErrorMessage);
        Assert.Equal(SecretRedactor.PatternTimeoutMarker, redacted.Description);
        Assert.Single(logger.Entries, entry => entry.EventId.Id == 3601);
    }

    private static RedactionSettings TimeoutSettings(string patternName) => new(
        AttributeKeys: [],
        Patterns:
        [
            new RedactionPatternSettings("alpha-label", "alpha"),
            new RedactionPatternSettings(patternName, CatastrophicPattern)
        ],
        UserIdentifierAttributes: []);

    private static NormalizedSignal CreateSignal() => new(
        Source: "tester",
        ExternalId: null,
        TraceId: null,
        SpanId: null,
        ParentSpanId: null,
        ServiceName: "payments-api",
        Environment: "test",
        OperationName: null,
        Severity: null,
        ErrorType: null,
        ErrorMessage: null,
        Summary: "Example failure",
        Description: null,
        HttpMethod: null,
        HttpRoute: null,
        HttpStatusCode: null,
        DurationMs: null,
        Attributes: new JsonObject(),
        Body: new JsonObject(),
        ObservedAtUtc: DateTimeOffset.UtcNow);
}
