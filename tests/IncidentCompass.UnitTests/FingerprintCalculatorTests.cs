using System.Text.Json;
using System.Text.Json.Nodes;
using IncidentCompass.Application.Intake.Fingerprinting;
using IncidentCompass.Application.Intake.Normalization;
using IncidentCompass.Domain.Incidents;

namespace IncidentCompass.UnitTests;

public sealed class FingerprintCalculatorTests
{
    [Fact]
    public void Compute_MasksVariablePartsInErrorMessage_SoStructurallyIdenticalSignalsMatch()
    {
        var first = CreateSignal(errorMessage:
            "Timeout calling order 123e4567-e89b-12d3-a456-426614174000 after 30000ms at 2026-06-01T10:00:00Z");
        var second = CreateSignal(errorMessage:
            "Timeout calling order 99999999-9999-9999-9999-999999999999 after 45000ms at 2026-07-02T11:30:00Z");

        var firstResult = FingerprintCalculator.Compute(first, fingerprintVersion: 1);
        var secondResult = FingerprintCalculator.Compute(second, fingerprintVersion: 1);

        Assert.Equal(firstResult.Value, secondResult.Value);
    }

    [Fact]
    public void Compute_DifferentErrorType_ProducesDifferentFingerprint()
    {
        var first = CreateSignal(errorType: "TimeoutException");
        var second = CreateSignal(errorType: "NullReferenceException");

        var firstResult = FingerprintCalculator.Compute(first, fingerprintVersion: 1);
        var secondResult = FingerprintCalculator.Compute(second, fingerprintVersion: 1);

        Assert.NotEqual(firstResult.Value, secondResult.Value);
    }

    [Fact]
    public void Compute_UnknownServiceName_IsWeak()
    {
        var signal = CreateSignal(serviceName: "unknown", errorType: "TimeoutException");

        var result = FingerprintCalculator.Compute(signal, fingerprintVersion: 1);

        Assert.Equal(FingerprintStrength.Weak, result.Strength);
    }

    [Fact]
    public void Compute_RealServiceNameButNoErrorType_IsWeak()
    {
        var signal = CreateSignal(serviceName: "payments-api", errorType: null);

        var result = FingerprintCalculator.Compute(signal, fingerprintVersion: 1);

        Assert.Equal(FingerprintStrength.Weak, result.Strength);
    }

    [Fact]
    public void Compute_RealServiceNameAndErrorType_IsStrong()
    {
        var signal = CreateSignal(serviceName: "payments-api", errorType: "TimeoutException");

        var result = FingerprintCalculator.Compute(signal, fingerprintVersion: 1);

        Assert.Equal(FingerprintStrength.Strong, result.Strength);
    }

    [Fact]
    public void Compute_SameInputTwice_IsDeterministic()
    {
        var signal = CreateSignal();

        var first = FingerprintCalculator.Compute(signal, fingerprintVersion: 1);
        var second = FingerprintCalculator.Compute(signal, fingerprintVersion: 1);

        Assert.Equal(first.Value, second.Value);
    }

    private static NormalizedSignal CreateSignal(
        string serviceName = "payments-api",
        string environment = "prod",
        string? errorType = "TimeoutException",
        string? errorMessage = "Timeout calling downstream")
    {
        return new NormalizedSignal(
            Source: "tester",
            ExternalId: null,
            TraceId: null,
            SpanId: null,
            ParentSpanId: null,
            ServiceName: serviceName,
            Environment: environment,
            OperationName: "POST /checkout",
            Severity: "critical",
            ErrorType: errorType,
            ErrorMessage: errorMessage,
            Summary: "Checkout failed",
            Description: null,
            HttpMethod: "POST",
            HttpRoute: "/checkout",
            HttpStatusCode: 504,
            DurationMs: 30000,
            Attributes: new JsonObject(),
            Body: new JsonObject(),
            ObservedAtUtc: DateTimeOffset.Parse("2026-06-01T00:00:00Z"));
    }
}
