using IncidentCompass.Infrastructure.Observability;
using IncidentCompass.Infrastructure.Observability.Pricing;
using IncidentCompass.Infrastructure.Observability.Logging;
using IncidentCompass.Domain.Observability;
using System.Net;
using IncidentCompass.Application.Generation.ModelGateway;
using IncidentCompass.Application.Core.ModelClients;
using IncidentCompass.Application.Core.Security;
using IncidentCompass.Application.Core.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace IncidentCompass.UnitTests;

public sealed class ObservabilityTests
{
    [Fact]
    public async Task CostEstimator_UsesPricingEffectiveAtRequestTime()
    {
        var pricingRepository = new InMemoryPricingRepository([
            new PricingRecord(
                Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                "fake",
                "test-model",
                "USD",
                InputTokenPricePerMillion: 1.00m,
                OutputTokenPricePerMillion: 2.00m,
                EmbeddingTokenPricePerMillion: null,
                DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
                DateTimeOffset.Parse("2026-05-01T00:00:00Z")),
            new PricingRecord(
                Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
                "fake",
                "test-model",
                "USD",
                InputTokenPricePerMillion: 10.00m,
                OutputTokenPricePerMillion: 20.00m,
                EmbeddingTokenPricePerMillion: null,
                DateTimeOffset.Parse("2026-05-01T00:00:00Z"),
                EffectiveToUtc: null)
        ]);
        var estimator = new AiCostEstimator(pricingRepository);

        var beforeChange = await estimator.EstimateAsync(
            "fake",
            "test-model",
            new AiModelUsage(1_000, 2_000, 3_000),
            embeddingTokens: null,
            embeddingProvider: null,
            embeddingModel: null,
            DateTimeOffset.Parse("2026-04-15T00:00:00Z"),
            CancellationToken.None);
        var afterChange = await estimator.EstimateAsync(
            "fake",
            "test-model",
            new AiModelUsage(1_000, 2_000, 3_000),
            embeddingTokens: null,
            embeddingProvider: null,
            embeddingModel: null,
            DateTimeOffset.Parse("2026-05-15T00:00:00Z"),
            CancellationToken.None);

        Assert.NotNull(beforeChange);
        Assert.Equal(0.00500000m, beforeChange.Amount);
        Assert.Equal(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), beforeChange.PricingRecordId);
        Assert.NotNull(afterChange);
        Assert.Equal(0.05000000m, afterChange.Amount);
        Assert.Equal(Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"), afterChange.PricingRecordId);
    }

    [Fact]
    public async Task CostEstimator_PricesEmbeddingTokensWithEmbeddingModelPricing()
    {
        var pricingRepository = new InMemoryPricingRepository([
            new PricingRecord(
                Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                "chat-provider",
                "chat-model",
                "USD",
                InputTokenPricePerMillion: 1.00m,
                OutputTokenPricePerMillion: 2.00m,
                EmbeddingTokenPricePerMillion: null,
                DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
                EffectiveToUtc: null),
            new PricingRecord(
                Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
                "embedding-provider",
                "embedding-model",
                "USD",
                InputTokenPricePerMillion: 0m,
                OutputTokenPricePerMillion: 0m,
                EmbeddingTokenPricePerMillion: 100.00m,
                DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
                EffectiveToUtc: null)
        ]);
        var estimator = new AiCostEstimator(pricingRepository);

        var cost = await estimator.EstimateAsync(
            "chat-provider",
            "chat-model",
            new AiModelUsage(1_000, 2_000, 3_000),
            embeddingTokens: 3_000,
            embeddingProvider: "embedding-provider",
            embeddingModel: "embedding-model",
            DateTimeOffset.Parse("2026-05-15T00:00:00Z"),
            CancellationToken.None);

        Assert.NotNull(cost);
        Assert.Equal(0.30500000m, cost.Amount);
    }

    [Fact]
    public async Task CostEstimator_PricesEmbeddingOnlyWhenModelUsageIsAbsent()
    {
        var pricingRepository = new InMemoryPricingRepository([
            new PricingRecord(
                Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
                "embedding-provider",
                "embedding-model",
                "USD",
                InputTokenPricePerMillion: 0m,
                OutputTokenPricePerMillion: 0m,
                EmbeddingTokenPricePerMillion: 100.00m,
                DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
                EffectiveToUtc: null)
        ]);
        var estimator = new AiCostEstimator(pricingRepository);

        var cost = await estimator.EstimateAsync(
            "no-model",
            "resolved-chat-model",
            usage: null,
            embeddingTokens: 3_000,
            embeddingProvider: "embedding-provider",
            embeddingModel: "embedding-model",
            DateTimeOffset.Parse("2026-05-15T00:00:00Z"),
            CancellationToken.None);

        Assert.NotNull(cost);
        Assert.Equal(0.30000000m, cost.Amount);
        Assert.Equal("USD", cost.Currency);
        Assert.Equal(Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"), cost.PricingRecordId);
    }

    [Fact]
    public async Task CostEstimator_DoesNotBlendModelAndEmbeddingCurrencies()
    {
        var pricingRepository = new InMemoryPricingRepository([
            new PricingRecord(
                Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                "chat-provider",
                "chat-model",
                "USD",
                InputTokenPricePerMillion: 1.00m,
                OutputTokenPricePerMillion: 2.00m,
                EmbeddingTokenPricePerMillion: null,
                DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
                EffectiveToUtc: null),
            new PricingRecord(
                Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
                "embedding-provider",
                "embedding-model",
                "EUR",
                InputTokenPricePerMillion: 0m,
                OutputTokenPricePerMillion: 0m,
                EmbeddingTokenPricePerMillion: 100.00m,
                DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
                EffectiveToUtc: null)
        ]);
        var estimator = new AiCostEstimator(pricingRepository);

        var cost = await estimator.EstimateAsync(
            "chat-provider",
            "chat-model",
            new AiModelUsage(1_000, 2_000, 3_000),
            embeddingTokens: 3_000,
            embeddingProvider: "embedding-provider",
            embeddingModel: "embedding-model",
            DateTimeOffset.Parse("2026-05-15T00:00:00Z"),
            CancellationToken.None);

        Assert.Null(cost);
    }

    [Fact]
    public async Task LoggingService_StoresSanitizedRequestMetadata()
    {
        var repository = new CapturingAiRequestLogRepository();
        var service = CreateService(repository);
        var request = new AiModelRequest(
            "correlation-1",
            "test-model",
            [new AiChatMessage(AiMessageRole.User, "private prompt content")]);

        await service.CompleteAndLogAsync(
            new SuccessfulModelClient(),
            request,
            embeddingTokens: 7,
            embeddingProvider: "fake",
            embeddingModel: "test-embedding",
            CancellationToken.None);

        var entry = Assert.Single(repository.Entries);
        Assert.Equal("v1", entry.ApiVersion);
        Assert.Equal("alice", entry.UserId);
        Assert.Equal("tenant-a", entry.TenantId);
        Assert.Equal("correlation-1", entry.CorrelationId);
        Assert.Equal("Succeeded", entry.Status);
        Assert.Equal(7, entry.EmbeddingTokens);
    }

    [Fact]
    public async Task LoggingService_UsesConfiguredApplicationApiVersion()
    {
        var repository = new CapturingAiRequestLogRepository();
        var service = CreateService(
            repository,
            applicationOptions: new ApplicationOptions
            {
                ApiVersion = "v-test",
                RunnerVersion = "runner-test"
            });

        await service.CompleteAndLogAsync(
            new SuccessfulModelClient(),
            new AiModelRequest(
                "correlation-version",
                "test-model",
                [new AiChatMessage(AiMessageRole.User, "hello")]),
            embeddingTokens: null,
            embeddingProvider: null,
            embeddingModel: null,
            CancellationToken.None);

        var entry = Assert.Single(repository.Entries);
        Assert.Equal("v-test", entry.ApiVersion);
    }

    [Fact]
    public async Task LoggingService_StoresNoModelOutcomeWithEmbeddingCostOnly()
    {
        var repository = new CapturingAiRequestLogRepository();
        var service = CreateService(
            repository,
            new InMemoryPricingRepository([
                new PricingRecord(
                    Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
                    "embedding-provider",
                    "embedding-model",
                    "USD",
                    InputTokenPricePerMillion: 0m,
                    OutputTokenPricePerMillion: 0m,
                    EmbeddingTokenPricePerMillion: 100.00m,
                    DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
                    EffectiveToUtc: null)
            ]));

        await service.LogSucceededWithoutModelAsync(
            "no-context-correlation",
            "resolved-chat-model",
            TimeSpan.FromMilliseconds(15),
            embeddingTokens: 3_000,
            embeddingProvider: "embedding-provider",
            embeddingModel: "embedding-model");

        var entry = Assert.Single(repository.Entries);
        Assert.Equal("Succeeded", entry.Status);
        Assert.Equal("no-model", entry.Provider);
        Assert.Equal("resolved-chat-model", entry.Model);
        Assert.Equal("no-context-correlation", entry.CorrelationId);
        Assert.Null(entry.InputTokens);
        Assert.Null(entry.OutputTokens);
        Assert.Null(entry.TotalTokens);
        Assert.Equal(3_000, entry.EmbeddingTokens);
        Assert.Equal(0.30000000m, entry.EstimatedCost);
        Assert.Equal("USD", entry.CostCurrency);
    }

    [Fact]
    public async Task LoggingService_StoresDiscardedEmbeddingUsageWithoutPromptContent()
    {
        var repository = new CapturingAiRequestLogRepository();
        var service = CreateService(
            repository,
            new InMemoryPricingRepository([
                new PricingRecord(
                    Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
                    "embedding-provider",
                    "embedding-model",
                    "USD",
                    InputTokenPricePerMillion: 0m,
                    OutputTokenPricePerMillion: 0m,
                    EmbeddingTokenPricePerMillion: 100.00m,
                    DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
                    EffectiveToUtc: null)
            ]));

        await service.LogDiscardedEmbeddingAsync(
            "indexing-document-1-job-1",
            "embedding-provider",
            "embedding-model",
            embeddingTokens: 3_000,
            TimeSpan.FromMilliseconds(25));

        var entry = Assert.Single(repository.Entries);
        Assert.Equal("Succeeded", entry.Status);
        Assert.Equal("indexing_abandoned", entry.ErrorCode);
        Assert.Equal("embedding-provider", entry.Provider);
        Assert.Equal("embedding-model", entry.Model);
        Assert.Equal("indexing-document-1-job-1", entry.CorrelationId);
        Assert.Null(entry.InputTokens);
        Assert.Null(entry.OutputTokens);
        Assert.Null(entry.TotalTokens);
        Assert.Equal(3_000, entry.EmbeddingTokens);
        Assert.Equal(0.30000000m, entry.EstimatedCost);
        Assert.Equal("USD", entry.CostCurrency);
    }

    [Fact]
    public async Task LoggingService_RecordsNormalizedModelFailureAndRethrows()
    {
        var repository = new CapturingAiRequestLogRepository();
        var service = CreateService(repository);
        var request = new AiModelRequest(
            "correlation-2",
            "test-model",
            [new AiChatMessage(AiMessageRole.User, "message")]);

        await Assert.ThrowsAsync<AiModelException>(() =>
            service.CompleteAndLogAsync(
                new ThrowingModelClient(),
                request,
                embeddingTokens: null,
                embeddingProvider: null,
                embeddingModel: null,
                CancellationToken.None));

        var entry = Assert.Single(repository.Entries);
        Assert.Equal("Failed", entry.Status);
        Assert.Equal("fake", entry.Provider);
        Assert.Equal("rate_limited", entry.ErrorCode);
    }

    [Fact]
    public async Task LoggingService_RethrowsModelFailureWhenFailClosedFailureLogPersistenceFails()
    {
        var service = CreateService(
            new ThrowingAiRequestLogRepository(),
            failureMode: AiRequestLoggingFailureMode.FailClosed);
        var request = new AiModelRequest(
            "correlation-5",
            "test-model",
            [new AiChatMessage(AiMessageRole.User, "message")]);

        var exception = await Assert.ThrowsAsync<AiModelException>(() =>
            service.CompleteAndLogAsync(
                new ThrowingModelClient(),
                request,
                embeddingTokens: null,
                embeddingProvider: null,
                embeddingModel: null,
                CancellationToken.None));

        Assert.Equal("rate_limited", exception.ErrorCode);
    }

    [Fact]
    public async Task LoggingService_RethrowsCancellationWhenFailClosedFailureLogPersistenceFails()
    {
        var service = CreateService(
            new ThrowingAiRequestLogRepository(),
            failureMode: AiRequestLoggingFailureMode.FailClosed);
        var request = new AiModelRequest(
            "correlation-6",
            "test-model",
            [new AiChatMessage(AiMessageRole.User, "message")]);

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            service.CompleteAndLogAsync(
                new CancelingModelClient(),
                request,
                embeddingTokens: null,
                embeddingProvider: null,
                embeddingModel: null,
                CancellationToken.None));
    }

    [Fact]
    public async Task LoggingService_FailsOpenWhenTelemetryPersistenceFails()
    {
        var service = CreateService(new ThrowingAiRequestLogRepository());
        var request = new AiModelRequest(
            "correlation-3",
            "test-model",
            [new AiChatMessage(AiMessageRole.User, "message")]);

        var response = await service.CompleteAndLogAsync(
            new SuccessfulModelClient(),
            request,
            embeddingTokens: null,
            embeddingProvider: null,
            embeddingModel: null,
            CancellationToken.None);

        Assert.Equal("answer", response.Content);
    }

    [Fact]
    public async Task LoggingService_FailsClosedWhenTelemetryPersistenceFailsAndConfigured()
    {
        var service = CreateService(
            new ThrowingAiRequestLogRepository(),
            failureMode: AiRequestLoggingFailureMode.FailClosed);
        var request = new AiModelRequest(
            "correlation-4",
            "test-model",
            [new AiChatMessage(AiMessageRole.User, "message")]);

        var exception = await Assert.ThrowsAsync<AiRequestLoggingException>(() =>
            service.CompleteAndLogAsync(
                new SuccessfulModelClient(),
                request,
                embeddingTokens: null,
                embeddingProvider: null,
                embeddingModel: null,
                CancellationToken.None));

        Assert.Equal("AI request logging failed.", exception.Message);
        Assert.DoesNotContain("telemetry store is unavailable", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static AiModelRequestLoggingService CreateService(
        IAiRequestLogRepository repository,
        IPricingRepository? pricingRepository = null,
        AiRequestLoggingFailureMode failureMode = AiRequestLoggingFailureMode.FailOpen,
        ApplicationOptions? applicationOptions = null)
    {
        return new AiModelRequestLoggingService(
            new AiRequestLogWriter(
                repository,
                new AiCostEstimator(pricingRepository ?? new InMemoryPricingRepository([])),
                new FakeUserContext(),
                Options.Create(applicationOptions ?? new ApplicationOptions()),
                Options.Create(new AiRequestLoggingOptions
                {
                    FailureMode = failureMode
                }),
                NullLogger<AiRequestLogWriter>.Instance),
            TimeProvider.System,
            NullLogger<AiModelRequestLoggingService>.Instance);
    }

    private sealed class SuccessfulModelClient : IAiModelClient
    {
        public Task<AiModelResponse> CompleteAsync(
            AiModelRequest request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new AiModelResponse(
                "answer",
                request.Model,
                "fake",
                new AiModelUsage(10, 5, 15),
                request.CorrelationId));
        }
    }

    private sealed class ThrowingModelClient : IAiModelClient
    {
        public Task<AiModelResponse> CompleteAsync(
            AiModelRequest request,
            CancellationToken cancellationToken)
        {
            throw new AiModelException(
                "fake",
                "rate limit",
                "rate_limited",
                HttpStatusCode.TooManyRequests);
        }
    }

    private sealed class CancelingModelClient : IAiModelClient
    {
        public Task<AiModelResponse> CompleteAsync(
            AiModelRequest request,
            CancellationToken cancellationToken)
        {
            throw new OperationCanceledException("model request canceled");
        }
    }

    private sealed class CapturingAiRequestLogRepository : IAiRequestLogRepository
    {
        public List<AiRequestLogEntry> Entries { get; } = [];

        public Task AddAsync(
            AiRequestLogEntry entry,
            CancellationToken cancellationToken)
        {
            Entries.Add(entry);
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingAiRequestLogRepository : IAiRequestLogRepository
    {
        public Task AddAsync(
            AiRequestLogEntry entry,
            CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("telemetry store is unavailable");
        }
    }

    private sealed class InMemoryPricingRepository(IReadOnlyList<PricingRecord> records) : IPricingRepository
    {
        public Task<PricingRecord?> GetEffectivePricingAsync(
            string provider,
            string model,
            DateTimeOffset usedAtUtc,
            CancellationToken cancellationToken)
        {
            var record = records
                .Where(current =>
                    current.Provider == provider &&
                    current.Model == model &&
                    current.EffectiveFromUtc <= usedAtUtc &&
                    (current.EffectiveToUtc is null || current.EffectiveToUtc > usedAtUtc))
                .OrderByDescending(static current => current.EffectiveFromUtc)
                .FirstOrDefault();

            return Task.FromResult(record);
        }
    }

    private sealed class FakeUserContext : IUserContext
    {
        public bool IsAuthenticated => true;

        public string? UserId => "alice";

        public string? TenantId => "tenant-a";

        public IReadOnlyCollection<string> Roles { get; } = [];

        public IReadOnlyCollection<string> Groups { get; } = [];
    }
}
