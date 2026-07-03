using System.Text.Json;
using IncidentCompass.Application.Intake.Artifacts;
using IncidentCompass.Application.Intake.Configuration;
using IncidentCompass.Domain.Incidents;

namespace IncidentCompass.UnitTests;

public sealed class GroundedFactsAssemblerTests
{
    [Fact]
    public async Task AssembleAsync_NonRecurrence_InsertsTriggerSignalAndNeighborSetOnly()
    {
        var artifacts = new RecordingTriageArtifactRepository();
        var assembler = new GroundedFactsAssembler(artifacts, new StubPriorReportSummaryProvider(null), TimeProvider.System);

        await assembler.AssembleAsync(
            CreateJob(), CreateSignal(), CreateFault(recurrenceOf: null),
            neighborCount: 1, isMassIssue: false, CreateSettings(), CancellationToken.None);

        Assert.Equal(2, artifacts.Inserted.Count);
        Assert.Contains(artifacts.Inserted, a => a.Kind == ArtifactKind.TriggerSignal);
        Assert.Contains(artifacts.Inserted, a => a.Kind == ArtifactKind.NeighborSet);
        Assert.DoesNotContain(artifacts.Inserted, a => a.Kind == ArtifactKind.PriorReport);
    }

    [Fact]
    public async Task AssembleAsync_RecurrenceWithPriorReport_AlsoInsertsPriorReportArtifact()
    {
        var artifacts = new RecordingTriageArtifactRepository();
        var priorReportProvider = new StubPriorReportSummaryProvider(
            new PriorReportSummary(Guid.NewGuid(), "Prior summary text", ["limitation one"]));
        var assembler = new GroundedFactsAssembler(artifacts, priorReportProvider, TimeProvider.System);

        await assembler.AssembleAsync(
            CreateJob(), CreateSignal(), CreateFault(recurrenceOf: Guid.NewGuid()),
            neighborCount: 3, isMassIssue: true, CreateSettings(), CancellationToken.None);

        Assert.Equal(3, artifacts.Inserted.Count);
        var priorReportArtifact = Assert.Single(artifacts.Inserted, a => a.Kind == ArtifactKind.PriorReport);
        Assert.Equal("Prior summary text", priorReportArtifact.RedactedPayload.GetProperty("summary").GetString());
        Assert.Equal("untrusted-prior-hypothesis", priorReportArtifact.RedactedPayload.GetProperty("trust").GetString());
    }

    [Fact]
    public async Task AssembleAsync_RecurrenceWithoutPriorReport_DoesNotInsertPriorReportArtifact()
    {
        var artifacts = new RecordingTriageArtifactRepository();
        var assembler = new GroundedFactsAssembler(artifacts, new StubPriorReportSummaryProvider(null), TimeProvider.System);

        await assembler.AssembleAsync(
            CreateJob(), CreateSignal(), CreateFault(recurrenceOf: Guid.NewGuid()),
            neighborCount: 1, isMassIssue: null, CreateSettings(), CancellationToken.None);

        Assert.Equal(2, artifacts.Inserted.Count);
        Assert.DoesNotContain(artifacts.Inserted, a => a.Kind == ArtifactKind.PriorReport);
    }

    [Fact]
    public async Task AssembleAsync_AllInsertedArtifacts_AreJobLevelWithNonEmptyContentHash()
    {
        var artifacts = new RecordingTriageArtifactRepository();
        var assembler = new GroundedFactsAssembler(artifacts, new StubPriorReportSummaryProvider(null), TimeProvider.System);

        await assembler.AssembleAsync(
            CreateJob(), CreateSignal(), CreateFault(recurrenceOf: null),
            neighborCount: 0, isMassIssue: null, CreateSettings(), CancellationToken.None);

        Assert.All(artifacts.Inserted, artifact =>
        {
            Assert.Null(artifact.Attempt);
            Assert.False(string.IsNullOrWhiteSpace(artifact.ContentHash));
        });
    }

    [Fact]
    public async Task AssembleAsync_NeighborSetPayload_CarriesIsMassIssueIncludingNullCase()
    {
        var artifacts = new RecordingTriageArtifactRepository();
        var assembler = new GroundedFactsAssembler(artifacts, new StubPriorReportSummaryProvider(null), TimeProvider.System);

        await assembler.AssembleAsync(
            CreateJob(), CreateSignal(), CreateFault(recurrenceOf: null),
            neighborCount: 7, isMassIssue: null, CreateSettings(), CancellationToken.None);

        var neighborSet = Assert.Single(artifacts.Inserted, a => a.Kind == ArtifactKind.NeighborSet);
        Assert.Equal(JsonValueKind.Null, neighborSet.RedactedPayload.GetProperty("isMassIssue").ValueKind);
        Assert.Equal(7, neighborSet.RedactedPayload.GetProperty("neighborCount").GetInt32());
        Assert.True(neighborSet.RedactedPayload.GetProperty("neighborCountApplies").GetBoolean());
    }

    [Fact]
    public async Task ReplaceNeighborSetAsync_ReplacesJobLevelNeighborSetArtifact()
    {
        var artifacts = new RecordingTriageArtifactRepository();
        var assembler = new GroundedFactsAssembler(artifacts, new StubPriorReportSummaryProvider(null), TimeProvider.System);

        await assembler.ReplaceNeighborSetAsync(
            CreateJob(), CreateFault(recurrenceOf: null), CreateSignal(),
            neighborCount: 6, isMassIssue: true, CreateSettings(), CancellationToken.None);

        Assert.Empty(artifacts.Inserted);
        var replacement = Assert.Single(artifacts.Replaced);
        Assert.Null(replacement.Attempt);
        Assert.Equal(ArtifactKind.NeighborSet, replacement.Kind);
        Assert.Equal(6, replacement.RedactedPayload.GetProperty("neighborCount").GetInt32());
        Assert.True(replacement.RedactedPayload.GetProperty("isMassIssue").GetBoolean());
    }

    [Fact]
    public async Task AssembleAsync_WeakSignalNeighborSet_MarksNeighborCountAsNotApplicable()
    {
        var artifacts = new RecordingTriageArtifactRepository();
        var assembler = new GroundedFactsAssembler(artifacts, new StubPriorReportSummaryProvider(null), TimeProvider.System);

        await assembler.AssembleAsync(
            CreateJob(), CreateSignal(FingerprintStrength.Weak, canGroup: false), CreateFault(recurrenceOf: null, FingerprintStrength.Weak, canGroup: false),
            neighborCount: 0, isMassIssue: null, CreateSettings(), CancellationToken.None);

        var neighborSet = Assert.Single(artifacts.Inserted, a => a.Kind == ArtifactKind.NeighborSet);
        Assert.False(neighborSet.RedactedPayload.GetProperty("neighborCountApplies").GetBoolean());
        Assert.Equal(0, neighborSet.RedactedPayload.GetProperty("neighborCount").GetInt32());
    }

    private static TriageJob CreateJob() => new(
        Id: Guid.NewGuid(),
        FaultId: Guid.NewGuid(),
        Status: TriageJobStatus.Pending,
        Attempt: 1,
        LockedBy: null,
        LockedUntilUtc: null,
        NextAttemptAtUtc: null,
        LastErrorCode: null,
        LastErrorMessage: null,
        ConfigHash: "config-hash-1",
        CreatedAtUtc: DateTimeOffset.UtcNow,
        UpdatedAtUtc: DateTimeOffset.UtcNow);

    private static Signal CreateSignal(FingerprintStrength strength = FingerprintStrength.Strong, bool canGroup = true) => new(
        Id: Guid.NewGuid(),
        TenantId: "local",
        Source: "tester",
        FaultId: Guid.NewGuid(),
        Fingerprint: "fingerprint-1",
        FingerprintVersion: 1,
        FingerprintStrength: strength,
        CanGroup: canGroup,
        ExternalId: null,
        IsSuppressed: false,
        SuppressedByFaultId: null,
        SuppressionReason: null,
        TraceId: null,
        SpanId: null,
        ParentSpanId: null,
        ServiceName: "payments-api",
        Environment: "prod",
        OperationName: "POST /checkout",
        Severity: "critical",
        ErrorType: strength == FingerprintStrength.Strong ? "TimeoutException" : null,
        ErrorMessage: "Timeout",
        Summary: "Checkout failed",
        Description: null,
        HttpMethod: "POST",
        HttpRoute: "/checkout",
        HttpStatusCode: 504,
        DurationMs: 30000,
        Attributes: EmptyJson(),
        Body: EmptyJson(),
        ObservedAtUtc: DateTimeOffset.UtcNow,
        ReceivedAtUtc: DateTimeOffset.UtcNow);

    private static JsonElement EmptyJson()
    {
        using var document = JsonDocument.Parse("{}");
        return document.RootElement.Clone();
    }

    private static Fault CreateFault(
        Guid? recurrenceOf,
        FingerprintStrength strength = FingerprintStrength.Strong,
        bool canGroup = true) => new(
        Id: Guid.NewGuid(),
        TriggerSignalId: Guid.NewGuid(),
        TenantId: "local",
        Status: FaultStatus.Queued,
        Fingerprint: "fingerprint-1",
        FingerprintVersion: 1,
        FingerprintStrength: strength,
        CanGroup: canGroup,
        ServiceName: "payments-api",
        Environment: "prod",
        Severity: "critical",
        CorrelationId: null,
        CreatedAtUtc: DateTimeOffset.UtcNow,
        CompletedAtUtc: null,
        RecurrenceOf: recurrenceOf);

    private static FaultGroupingSettings CreateSettings() => new(
        LookbackMinutes: 15,
        SilenceWindowMinutes: 30,
        FingerprintVersion: 1,
        MassIssue: new MassIssueSettings(MinNeighborCount: 5, MinFingerprintStrength: "strong"));

    private sealed class RecordingTriageArtifactRepository : ITriageArtifactRepository
    {
        public List<TriageArtifact> Inserted { get; } = [];

        public List<TriageArtifact> Replaced { get; } = [];

        public Task InsertAsync(TriageArtifact artifact, CancellationToken cancellationToken)
        {
            Inserted.Add(artifact);
            return Task.CompletedTask;
        }

        public Task ReplaceJobLevelAsync(TriageArtifact artifact, CancellationToken cancellationToken)
        {
            Replaced.Add(artifact);
            return Task.CompletedTask;
        }
    }

    private sealed class StubPriorReportSummaryProvider(PriorReportSummary? summary) : IPriorReportSummaryProvider
    {
        public Task<PriorReportSummary?> FindLatestAsync(Guid faultId, CancellationToken cancellationToken) =>
            Task.FromResult(summary);
    }
}

