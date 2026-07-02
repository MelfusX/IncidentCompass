using IncidentCompass.Application.Intake.Configuration;
using IncidentCompass.Application.Intake.IngestSignal;
using IncidentCompass.Application.Intake.Normalization;
using Microsoft.Extensions.Options;

namespace IncidentCompass.UnitTests;

public sealed class IngestSignalCommandValidatorTests
{
    [Fact]
    public async Task ValidateAsync_AllowedSourceWithoutRegisteredNormalizer_ReturnsValidationFailure()
    {
        var validator = new IngestSignalCommandValidator(
            new StubTriageConfigurationRepository(TestTriageConfiguration.Create(
                configHash: "hash",
                allowedSources: [SignalSourceKinds.Webhook])),
            new SignalNormalizerRegistry([
                new TesterSignalNormalizer(),
                new OtelShapedSignalNormalizer(),
                new UserReportSignalNormalizer()
            ]),
            Options.Create(new IngestionLimitsOptions()));

        var result = await validator.ValidateAsync(
            new IngestSignalCommand(
                SignalSourceKinds.Webhook,
                ServiceName: "payments-api",
                Environment: "prod",
                Severity: null,
                Summary: "Webhook probe",
                Description: null,
                ObservedAtUtc: null,
                TraceId: null,
                SpanId: null,
                ExternalId: null,
                Attributes: null,
                Payload: null),
            CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.Contains(
            result.Errors,
            error => error.PropertyName == "sourceKind" &&
                error.ErrorMessage.Contains("no registered normalizer", StringComparison.Ordinal));
    }

    private sealed class StubTriageConfigurationRepository(TriageConfiguration configuration)
        : ITriageConfigurationRepository
    {
        public Task<TriageConfiguration> GetCurrentAsync(CancellationToken cancellationToken) =>
            Task.FromResult(configuration);

        public Task<TriageConfiguration> GetByHashAsync(string configHash, CancellationToken cancellationToken) =>
            Task.FromResult(configuration);
    }
}
