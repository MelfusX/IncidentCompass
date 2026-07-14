using System.Text.Json;
using IncidentCompass.Application.Intake.Artifacts;
using IncidentCompass.Application.Intake.Configuration;
using IncidentCompass.Application.Intake.FaultGrouping;
using IncidentCompass.Domain.Incidents;

namespace IncidentCompass.UnitTests;

public sealed class FaultGroupingCoordinatorTests
{
    [Fact]
    public async Task ResolveAsync_TwoWeakSignals_EachCreatesItsOwnFaultAndJob()
    {
        var (coordinator, _, _, jobs, _) = CreateHarness();
        var configuration = CreateConfiguration();

        var firstOutcome = await coordinator.ResolveAsync(
            CreateDraftSignal(FingerprintStrength.Weak, serviceName: "unknown"), configuration, CancellationToken.None);
        var secondOutcome = await coordinator.ResolveAsync(
            CreateDraftSignal(FingerprintStrength.Weak, serviceName: "unknown"), configuration, CancellationToken.None);

        Assert.True(firstOutcome.IsNewFault);
        Assert.True(secondOutcome.IsNewFault);
        Assert.NotEqual(firstOutcome.Fault.Id, secondOutcome.Fault.Id);
        Assert.Equal(2, jobs.InsertedCount);
    }

    [Fact]
    public async Task ResolveAsync_StrongSignalNoExistingFault_CreatesNewFaultAndJob()
    {
        var (coordinator, _, _, jobs, _) = CreateHarness();

        var outcome = await coordinator.ResolveAsync(CreateDraftSignal(), CreateConfiguration(), CancellationToken.None);

        Assert.True(outcome.IsNewFault);
        Assert.True(outcome.IsNewJob);
        Assert.False(outcome.IsSuppressed);
        Assert.Equal(1, jobs.InsertedCount);
    }

    [Fact]
    public async Task ResolveAsync_SecondStrongSignalWithSameKey_AttachesToOpenFaultWithoutNewJob()
    {
        var (coordinator, _, _, jobs, _) = CreateHarness();
        var configuration = CreateConfiguration();

        var firstOutcome = await coordinator.ResolveAsync(CreateDraftSignal(), configuration, CancellationToken.None);
        var secondOutcome = await coordinator.ResolveAsync(CreateDraftSignal(), configuration, CancellationToken.None);

        Assert.Equal(firstOutcome.Fault.Id, secondOutcome.Fault.Id);
        Assert.False(secondOutcome.IsNewJob);
        Assert.Equal(1, jobs.InsertedCount);
    }

    [Fact]
    public async Task ResolveAsync_DifferentEffectiveRuleIdentity_DoesNotAttachToOpenFault()
    {
        var (coordinator, _, _, jobs, _) = CreateHarness();
        var configuration = CreateConfiguration();

        var first = await coordinator.ResolveAsync(
            CreateDraftSignal(groupingRuleId: "checkout-route", groupingRuleVersion: 2), configuration, CancellationToken.None);
        var second = await coordinator.ResolveAsync(
            CreateDraftSignal(groupingRuleId: "payments-route", groupingRuleVersion: 2), configuration, CancellationToken.None);

        Assert.NotEqual(first.Fault.Id, second.Fault.Id);
        Assert.Equal(2, jobs.InsertedCount);
    }
    [Fact]
    public void SuppressionPolicyResolver_PrefersSeveritySpecificRuleAndUsesDeclarationOrderForTies()
    {
        var settings = new FaultGroupingSettings(
            LookbackMinutes: 15,
            SilenceWindowMinutes: 30,
            FingerprintVersion: 1,
            MassIssue: new MassIssueSettings(5, "strong"),
            SuppressionRules:
            [
                new SuppressionRuleSettings("service-first", 20, ServiceName: "payments-api"),
                new SuppressionRuleSettings("service-second", 10, ServiceName: "payments-api"),
                new SuppressionRuleSettings("service-critical", 60, ServiceName: "payments-api", Severity: "critical")
            ]);

        var criticalPolicy = SuppressionPolicyResolver.Resolve(settings, CreateDraftSignal());
        var warningPolicy = SuppressionPolicyResolver.Resolve(settings, CreateDraftSignal(severity: "warning"));

        Assert.Equal("service-critical", criticalPolicy.Id);
        Assert.Equal(60, criticalPolicy.SilenceWindowMinutes);
        Assert.Equal("service-first", warningPolicy.Id);
        Assert.Equal(20, warningPolicy.SilenceWindowMinutes);
    }

    [Fact]
    public void SuppressionPolicyResolver_UnmatchedSignalUsesGlobalFallback()
    {
        var settings = new FaultGroupingSettings(
            LookbackMinutes: 15,
            SilenceWindowMinutes: 30,
            FingerprintVersion: 1,
            MassIssue: new MassIssueSettings(5, "strong"),
            SuppressionRules: [new SuppressionRuleSettings("checkout", 60, ServiceName: "checkout")]);

        var policy = SuppressionPolicyResolver.Resolve(settings, CreateDraftSignal(serviceName: "payments-api"));

        Assert.Equal(SuppressionPolicyResolver.DefaultPolicyId, policy.Id);
        Assert.Equal(30, policy.SilenceWindowMinutes);
    }

    [Fact]
    public async Task ResolveAsync_ClosedFaultAtExactSuppressionBoundary_IsSuppressed()
    {
        var now = new DateTimeOffset(2026, 7, 13, 12, 0, 0, TimeSpan.Zero);
        var (coordinator, _, faults, jobs, _) = CreateHarness(new FixedTimeProvider(now));
        var configuration = CreateConfiguration(silenceWindowMinutes: 30);

        var firstOutcome = await coordinator.ResolveAsync(CreateDraftSignal(), configuration, CancellationToken.None);
        faults.CloseFault(firstOutcome.Fault.Id, now.AddMinutes(-30));

        var secondOutcome = await coordinator.ResolveAsync(CreateDraftSignal(), configuration, CancellationToken.None);

        Assert.True(secondOutcome.IsSuppressed);
        Assert.Equal(1, jobs.InsertedCount);
    }
    [Fact]
    public async Task ResolveAsync_ClosedFaultWithinSilenceWindow_AttachesSuppressedWithoutNewJob()
    {
        var (coordinator, signals, faults, jobs, _) = CreateHarness();
        var configuration = CreateConfiguration(silenceWindowMinutes: 30);

        var firstOutcome = await coordinator.ResolveAsync(CreateDraftSignal(), configuration, CancellationToken.None);
        faults.CloseFault(firstOutcome.Fault.Id, DateTimeOffset.UtcNow);

        var secondOutcome = await coordinator.ResolveAsync(CreateDraftSignal(), configuration, CancellationToken.None);

        Assert.Equal(firstOutcome.Fault.Id, secondOutcome.Fault.Id);
        Assert.True(secondOutcome.IsSuppressed);
        Assert.False(secondOutcome.IsNewJob);
        Assert.Equal(1, jobs.InsertedCount);
        var suppressedSignal = Assert.Single(signals.Inserted, s => s.IsSuppressed);
        Assert.Equal(firstOutcome.Fault.Id, suppressedSignal.SuppressedByFaultId);
    }

    [Fact]
    public async Task ResolveAsync_ServiceScopedSuppressionPolicyPersistsEffectivePolicy()
    {
        var (coordinator, signals, faults, jobs, _) = CreateHarness();
        var configuration = CreateConfiguration(silenceWindowMinutes: 1) with
        {
            FaultGrouping = CreateConfiguration(silenceWindowMinutes: 1).FaultGrouping with
            {
                SuppressionRules = [new SuppressionRuleSettings("payments-extended", 60, ServiceName: "payments-api")]
            }
        };

        var firstOutcome = await coordinator.ResolveAsync(CreateDraftSignal(), configuration, CancellationToken.None);
        faults.CloseFault(firstOutcome.Fault.Id, DateTimeOffset.UtcNow.AddMinutes(-10));

        var secondOutcome = await coordinator.ResolveAsync(CreateDraftSignal(), configuration, CancellationToken.None);

        Assert.True(secondOutcome.IsSuppressed);
        Assert.Equal(1, jobs.InsertedCount);
        var suppressedSignal = Assert.Single(signals.Inserted, signal => signal.IsSuppressed);
        Assert.Equal("payments-extended", suppressedSignal.SuppressionRuleId);
        Assert.Equal(60, suppressedSignal.EffectiveSuppressionWindowMinutes);
        Assert.Equal("silence_window", suppressedSignal.SuppressionReason);
    }
    [Fact]
    public async Task ResolveAsync_OpenFaultTerminalizedBeforeAttachment_ReevaluatesAsSuppressed()
    {
        var (coordinator, signals, faults, jobs, _) = CreateHarness();
        var configuration = CreateConfiguration(silenceWindowMinutes: 30);

        var firstOutcome = await coordinator.ResolveAsync(CreateDraftSignal(), configuration, CancellationToken.None);
        faults.CloseOnNextLock(firstOutcome.Fault.Id, DateTimeOffset.UtcNow);

        var secondOutcome = await coordinator.ResolveAsync(CreateDraftSignal(), configuration, CancellationToken.None);

        Assert.Equal(firstOutcome.Fault.Id, secondOutcome.Fault.Id);
        Assert.True(secondOutcome.IsSuppressed);
        Assert.False(secondOutcome.IsNewJob);
        Assert.Equal(1, jobs.InsertedCount);
        var suppressedSignal = Assert.Single(signals.Inserted, signal => signal.IsSuppressed);
        Assert.Equal(firstOutcome.Fault.Id, suppressedSignal.SuppressedByFaultId);
    }

    [Fact]
    public async Task ResolveAsync_WeakSignalMatchingClosedFaultWithinSilenceWindow_CreatesUnsuppressedNewFault()
    {
        var (coordinator, signals, faults, jobs, _) = CreateHarness();
        var configuration = CreateConfiguration(silenceWindowMinutes: 30);

        var firstOutcome = await coordinator.ResolveAsync(
            CreateDraftSignal(FingerprintStrength.Weak, serviceName: "unknown"), configuration, CancellationToken.None);
        faults.CloseFault(firstOutcome.Fault.Id, DateTimeOffset.UtcNow);

        var secondOutcome = await coordinator.ResolveAsync(
            CreateDraftSignal(FingerprintStrength.Weak, serviceName: "unknown"), configuration, CancellationToken.None);

        Assert.True(secondOutcome.IsNewFault);
        Assert.True(secondOutcome.IsNewJob);
        Assert.False(secondOutcome.IsSuppressed);
        Assert.NotEqual(firstOutcome.Fault.Id, secondOutcome.Fault.Id);
        Assert.Equal(2, jobs.InsertedCount);
        Assert.DoesNotContain(signals.Inserted, signal => signal.IsSuppressed);
    }

    [Fact]
    public async Task ResolveAsync_StrongSignalWithWeakMassIssueThreshold_CanMarkMassIssue()
    {
        var (coordinator, _, faults, _, artifacts) = CreateHarness();
        var configuration = CreateConfiguration(minNeighborCount: 2, minFingerprintStrength: "weak");

        var firstOutcome = await coordinator.ResolveAsync(
            CreateDraftSignal(externalId: "first"), configuration, CancellationToken.None);
        faults.CloseFault(firstOutcome.Fault.Id, DateTimeOffset.UtcNow.AddMinutes(-60));

        var recurrenceOutcome = await coordinator.ResolveAsync(
            CreateDraftSignal(externalId: "second"), configuration, CancellationToken.None);

        Assert.True(recurrenceOutcome.IsNewJob);
        var neighborSet = artifacts.Inserted.Last(artifact => artifact.Kind == ArtifactKind.NeighborSet);
        Assert.True(neighborSet.RedactedPayload.GetProperty("isMassIssue").GetBoolean());
        Assert.Equal(2, neighborSet.RedactedPayload.GetProperty("neighborCount").GetInt32());
    }

    [Fact]
    public async Task ResolveAsync_NeighborCount_DeduplicatesRepeatedExternalId()
    {
        var (coordinator, _, faults, _, artifacts) = CreateHarness();
        var configuration = CreateConfiguration(minNeighborCount: 3);

        var firstOutcome = await coordinator.ResolveAsync(
            CreateDraftSignal(externalId: "duplicate"), configuration, CancellationToken.None);
        var attachedOutcome = await coordinator.ResolveAsync(
            CreateDraftSignal(externalId: "duplicate"), configuration, CancellationToken.None);
        Assert.Equal(firstOutcome.Fault.Id, attachedOutcome.Fault.Id);
        faults.CloseFault(firstOutcome.Fault.Id, DateTimeOffset.UtcNow.AddMinutes(-60));

        await coordinator.ResolveAsync(
            CreateDraftSignal(externalId: "unique"), configuration, CancellationToken.None);

        var neighborSet = artifacts.Inserted.Last(artifact => artifact.Kind == ArtifactKind.NeighborSet);
        Assert.Equal(2, neighborSet.RedactedPayload.GetProperty("neighborCount").GetInt32());
        Assert.False(neighborSet.RedactedPayload.GetProperty("isMassIssue").GetBoolean());
    }

    [Fact]
    public async Task ResolveAsync_ClosedFaultOutsideSilenceWindow_CreatesRecurrenceFaultAndNewJob()
    {
        var (coordinator, _, faults, jobs, _) = CreateHarness();
        var configuration = CreateConfiguration(silenceWindowMinutes: 30);

        var firstOutcome = await coordinator.ResolveAsync(CreateDraftSignal(), configuration, CancellationToken.None);
        faults.CloseFault(firstOutcome.Fault.Id, DateTimeOffset.UtcNow.AddMinutes(-60));

        var secondOutcome = await coordinator.ResolveAsync(CreateDraftSignal(), configuration, CancellationToken.None);

        Assert.True(secondOutcome.IsNewFault);
        Assert.True(secondOutcome.IsNewJob);
        Assert.NotEqual(firstOutcome.Fault.Id, secondOutcome.Fault.Id);
        Assert.Equal(firstOutcome.Fault.Id, secondOutcome.Fault.RecurrenceOf);
        Assert.Equal(2, jobs.InsertedCount);
    }

    [Fact]
    public async Task ResolveAsync_OpenRecurrenceAttachment_RefreshesCitableRecurrenceState()
    {
        var (coordinator, _, faults, _, artifacts) = CreateHarness();
        var configuration = CreateConfiguration() with
        {
            FaultGrouping = CreateConfiguration().FaultGrouping with { Recurrence = new RecurrenceSettings(2) }
        };

        var initial = await coordinator.ResolveAsync(CreateDraftSignal(externalId: "initial"), configuration, CancellationToken.None);
        faults.CloseFault(initial.Fault.Id, DateTimeOffset.UtcNow.AddMinutes(-60));
        var recurrence = await coordinator.ResolveAsync(CreateDraftSignal(externalId: "recurrence-one"), configuration, CancellationToken.None);
        artifacts.Replaced.Clear();

        var attached = await coordinator.ResolveAsync(CreateDraftSignal(externalId: "recurrence-two"), configuration, CancellationToken.None);

        Assert.Equal(recurrence.Fault.Id, attached.Fault.Id);
        var state = Assert.Single(artifacts.Replaced, artifact => artifact.Kind == ArtifactKind.RecurrenceState);
        Assert.Equal(2, state.RedactedPayload.GetProperty("recurrenceCount").GetInt32());
        Assert.True(state.RedactedPayload.GetProperty("escalationIntentCreated").GetBoolean());
    }
    [Fact]
    public async Task ResolveAsync_LostFaultCreationRace_AttachesToWinnerInsteadOfThrowing()
    {
        var (coordinator, _, faults, jobs, _) = CreateHarness();
        var configuration = CreateConfiguration();

        var winnerOutcome = await coordinator.ResolveAsync(CreateDraftSignal(), configuration, CancellationToken.None);

        // Simulate the loser's initial "no open fault" check racing before the winner committed,
        // and its own INSERT ... ON CONFLICT DO NOTHING losing to the winner's row.
        faults.SuppressNextFindOpenFaultCall();
        faults.ForceNextTryInsertToLoseRace();

        var loserOutcome = await coordinator.ResolveAsync(CreateDraftSignal(), configuration, CancellationToken.None);

        Assert.Equal(winnerOutcome.Fault.Id, loserOutcome.Fault.Id);
        Assert.False(loserOutcome.IsNewJob);
        Assert.Equal(1, jobs.InsertedCount);
    }

    [Fact]
    public async Task ResolveAsync_NewJobBranch_InvokesGroundedFactsAssembler()
    {
        var (coordinator, _, _, _, artifacts) = CreateHarness();

        await coordinator.ResolveAsync(CreateDraftSignal(), CreateConfiguration(), CancellationToken.None);

        Assert.Contains(artifacts.Inserted, a => a.Kind == ArtifactKind.TriggerSignal);
        Assert.Contains(artifacts.Inserted, a => a.Kind == ArtifactKind.NeighborSet);
    }

    [Fact]
    public async Task ResolveAsync_AttachBranch_RefreshesOpenFaultNeighborSet()
    {
        var (coordinator, _, _, _, artifacts) = CreateHarness();
        var configuration = CreateConfiguration(minNeighborCount: 2);

        await coordinator.ResolveAsync(CreateDraftSignal(externalId: "first"), configuration, CancellationToken.None);
        artifacts.Inserted.Clear();

        await coordinator.ResolveAsync(CreateDraftSignal(externalId: "second"), configuration, CancellationToken.None);

        Assert.Empty(artifacts.Inserted);
        var replacement = Assert.Single(artifacts.Replaced);
        Assert.Equal(ArtifactKind.NeighborSet, replacement.Kind);
        Assert.Equal(2, replacement.RedactedPayload.GetProperty("neighborCount").GetInt32());
        Assert.True(replacement.RedactedPayload.GetProperty("isMassIssue").GetBoolean());
    }

    private static (
        FaultGroupingCoordinator Coordinator,
        FakeSignalRepository Signals,
        FakeFaultRepository Faults,
        FakeTriageJobRepository Jobs,
        FakeTriageArtifactRepository Artifacts) CreateHarness(TimeProvider? timeProvider = null)
    {
        var signals = new FakeSignalRepository();
        var faults = new FakeFaultRepository();
        var jobs = new FakeTriageJobRepository();
        var artifacts = new FakeTriageArtifactRepository();
        var recurrenceStates = new FakeRecurrenceStateRepository();
        var clock = timeProvider ?? TimeProvider.System;
        var assembler = new GroundedFactsAssembler(artifacts, new AlwaysNullPriorReportSummaryProvider(), clock);
        var neighborSetRefresher = new OpenFaultNeighborSetRefresher(signals, jobs, assembler);
        var coordinator = new FaultGroupingCoordinator(
            signals, faults, jobs, new PassThroughIntakeUnitOfWork(), new RecurrenceTracker(recurrenceStates),
            new RecurrenceEscalationScheduler(new NoOpReTriageScheduler()), assembler, neighborSetRefresher, clock);
        return (coordinator, signals, faults, jobs, artifacts);
    }

    private static TriageConfiguration CreateConfiguration(
        int silenceWindowMinutes = 30,
        int lookbackMinutes = 15,
        int minNeighborCount = 5,
        string minFingerprintStrength = "strong") =>
        TestTriageConfiguration.Create(
            silenceWindowMinutes: silenceWindowMinutes,
            lookbackMinutes: lookbackMinutes,
            minNeighborCount: minNeighborCount,
            minFingerprintStrength: minFingerprintStrength);

    private static Signal CreateDraftSignal(
        FingerprintStrength strength = FingerprintStrength.Strong,
        string fingerprint = "fp-1",
        string serviceName = "payments-api",
        string environment = "prod",
        DateTimeOffset? observedAtUtc = null,
        string? externalId = null,
        string groupingRuleId = "default",
        int groupingRuleVersion = 1,
        string severity = "critical") => new(
        Id: Guid.NewGuid(),
        TenantId: "local",
        Source: "tester",
        FaultId: null,
        Fingerprint: fingerprint,
        FingerprintVersion: 1,
        FingerprintStrength: strength,
        CanGroup: strength == FingerprintStrength.Strong,
        ExternalId: externalId,
        IsSuppressed: false,
        SuppressedByFaultId: null,
        SuppressionReason: null,
        TraceId: null,
        SpanId: null,
        ParentSpanId: null,
        ServiceName: serviceName,
        Environment: environment,
        OperationName: "POST /checkout",
        Severity: severity,
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
        ObservedAtUtc: observedAtUtc ?? DateTimeOffset.UtcNow,
        ReceivedAtUtc: DateTimeOffset.UtcNow,
        DeliveryKey: null)
        {
            GroupingRuleId = groupingRuleId,
            GroupingRuleVersion = groupingRuleVersion
        };

    private static JsonElement EmptyJson()
    {
        using var document = JsonDocument.Parse("{}");
        return document.RootElement.Clone();
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
    private sealed class PassThroughIntakeUnitOfWork : IIntakeUnitOfWork
    {
        public Task<TResult> ExecuteAsync<TResult>(
            Func<CancellationToken, Task<TResult>> operation,
            CancellationToken cancellationToken) => operation(cancellationToken);
    }
    private sealed class NoOpReTriageScheduler : IRecurrenceEscalationReTriageScheduler
    {
        public Task ScheduleAsync(
            TriageJob recurrenceJob,
            Fault recurrenceFault,
            RecurrenceState recurrenceState,
            CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FakeRecurrenceStateRepository : IRecurrenceStateRepository
    {
        private int count;

        public Task<RecurrenceState> RecordAsync(
            RecurrenceOccurrence occurrence,
            CancellationToken cancellationToken)
        {
            count++;
            var createsEscalationIntent = occurrence.EscalateAfterCount > 0 && count >= occurrence.EscalateAfterCount;
            return Task.FromResult(new RecurrenceState(
                count,
                occurrence.OccurredAtUtc,
                occurrence.OccurredAtUtc,
                createsEscalationIntent ? occurrence.JobId : null,
                createsEscalationIntent ? occurrence.FaultId : null));
        }
    }

    private sealed class FakeSignalRepository : ISignalRepository
    {
        public List<Signal> Inserted { get; } = [];

        public Task InsertAsync(Signal signal, CancellationToken cancellationToken)
        {
            Inserted.Add(signal);
            return Task.CompletedTask;
        }

        public Task<ExistingSignalDelivery?> FindDeliveryAsync(
            string tenantId,
            string source,
            string deliveryKey,
            CancellationToken cancellationToken) => Task.FromResult<ExistingSignalDelivery?>(null);
        public Task AttachToFaultAsync(Guid signalId, Guid faultId, CancellationToken cancellationToken)
        {
            var index = Inserted.FindIndex(s => s.Id == signalId);
            Inserted[index] = Inserted[index] with { FaultId = faultId };
            return Task.CompletedTask;
        }

        public Task<int> CountDistinctNeighborsAsync(
            string tenantId,
            string serviceName,
            string environment,
            string fingerprint,
            int fingerprintVersion,
            string groupingRuleId,
            int groupingRuleVersion,
            DateTimeOffset windowStartUtc,
            DateTimeOffset windowEndUtc,
            CancellationToken cancellationToken)
        {
            var count = Inserted
                .Where(s =>
                    s.TenantId == tenantId && s.ServiceName == serviceName && s.Environment == environment &&
                    s.Fingerprint == fingerprint && s.FingerprintVersion == fingerprintVersion &&
                    s.GroupingRuleId == groupingRuleId && s.GroupingRuleVersion == groupingRuleVersion &&
                    s.ObservedAtUtc >= windowStartUtc && s.ObservedAtUtc <= windowEndUtc)
                .Select(NeighborIdentity)
                .Distinct(StringComparer.Ordinal)
                .Count();
            return Task.FromResult(count);
        }

        private static string NeighborIdentity(Signal signal)
        {
            if (!string.IsNullOrWhiteSpace(signal.ExternalId))
            {
                return $"external:{signal.ExternalId}";
            }

            if (!string.IsNullOrWhiteSpace(signal.TraceId) && !string.IsNullOrWhiteSpace(signal.SpanId))
            {
                return $"span:{signal.TraceId}:{signal.SpanId}";
            }

            return $"signal:{signal.Id}";
        }
    }

    private sealed class FakeFaultRepository : IFaultRepository
    {
        private readonly List<Fault> _faults = [];
        private int _suppressFindOpenFaultCount;
        private bool _forceNextInsertToLoseRace;
        private Guid? _closeOnNextLockFaultId;
        private DateTimeOffset _closeOnNextLockCompletedAtUtc;

        public Task<Fault?> FindOpenFaultAsync(
            string tenantId, string serviceName, string environment, string fingerprint, int fingerprintVersion,
            string groupingRuleId, int groupingRuleVersion,
            CancellationToken cancellationToken)
        {
            if (_suppressFindOpenFaultCount > 0)
            {
                _suppressFindOpenFaultCount--;
                return Task.FromResult<Fault?>(null);
            }

            var match = _faults.FirstOrDefault(f =>
                f.TenantId == tenantId && f.ServiceName == serviceName && f.Environment == environment &&
                f.Fingerprint == fingerprint && f.FingerprintVersion == fingerprintVersion &&
                f.GroupingRuleId == groupingRuleId && f.GroupingRuleVersion == groupingRuleVersion &&
                f.Status is FaultStatus.Queued or FaultStatus.Analyzing);
            return Task.FromResult(match);
        }

        public Task<Fault?> FindMostRecentClosedFaultAsync(
            string tenantId, string serviceName, string environment, string fingerprint, int fingerprintVersion,
            string groupingRuleId, int groupingRuleVersion,
            CancellationToken cancellationToken)
        {
            var match = _faults
                .Where(f => f.TenantId == tenantId && f.ServiceName == serviceName && f.Environment == environment &&
                    f.Fingerprint == fingerprint && f.FingerprintVersion == fingerprintVersion &&
                    f.GroupingRuleId == groupingRuleId && f.GroupingRuleVersion == groupingRuleVersion &&
                    f.Status is FaultStatus.Completed or FaultStatus.Failed or FaultStatus.InsufficientEvidence)
                .OrderByDescending(f => f.CreatedAtUtc)
                .FirstOrDefault();
            return Task.FromResult(match);
        }

        public Task<Fault?> TryInsertAsync(Fault fault, CancellationToken cancellationToken)
        {
            if (_forceNextInsertToLoseRace)
            {
                _forceNextInsertToLoseRace = false;
                return Task.FromResult<Fault?>(null);
            }

            var conflict = fault.CanGroup && _faults.Any(f =>
                f.TenantId == fault.TenantId && f.ServiceName == fault.ServiceName && f.Environment == fault.Environment &&
                f.Fingerprint == fault.Fingerprint && f.FingerprintVersion == fault.FingerprintVersion &&
                f.GroupingRuleId == fault.GroupingRuleId && f.GroupingRuleVersion == fault.GroupingRuleVersion &&
                f.CanGroup && f.Status is FaultStatus.Queued or FaultStatus.Analyzing);
            if (conflict)
            {
                return Task.FromResult<Fault?>(null);
            }

            _faults.Add(fault);
            return Task.FromResult<Fault?>(fault);
        }

        public Task<Fault?> FindByIdForUpdateAsync(Guid id, CancellationToken cancellationToken)
        {
            if (_closeOnNextLockFaultId == id)
            {
                CloseFault(id, _closeOnNextLockCompletedAtUtc);
                _closeOnNextLockFaultId = null;
            }

            return FindByIdAsync(id, cancellationToken);
        }

        public Task<Fault?> FindByIdAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(_faults.FirstOrDefault(f => f.Id == id));

        public void CloseOnNextLock(Guid faultId, DateTimeOffset completedAtUtc)
        {
            _closeOnNextLockFaultId = faultId;
            _closeOnNextLockCompletedAtUtc = completedAtUtc;
        }

        public void CloseFault(Guid faultId, DateTimeOffset completedAtUtc)
        {
            var index = _faults.FindIndex(f => f.Id == faultId);
            _faults[index] = _faults[index] with { Status = FaultStatus.Completed, CompletedAtUtc = completedAtUtc };
        }

        public void SuppressNextFindOpenFaultCall() => _suppressFindOpenFaultCount++;

        public void ForceNextTryInsertToLoseRace() => _forceNextInsertToLoseRace = true;
    }

    private sealed class FakeTriageJobRepository : ITriageJobRepository
    {
        private readonly Dictionary<Guid, TriageJob> _jobsByFault = [];

        public int InsertedCount { get; private set; }

        public Task<TriageJob> InsertPendingAsync(Guid faultId, string configHash, CancellationToken cancellationToken)
        {
            InsertedCount++;
            var job = new TriageJob(
                Id: Guid.NewGuid(),
                FaultId: faultId,
                Status: TriageJobStatus.Pending,
                Attempt: 1,
                LockedBy: null,
                LockedUntilUtc: null,
                NextAttemptAtUtc: null,
                LastErrorCode: null,
                LastErrorMessage: null,
                ConfigHash: configHash,
                CreatedAtUtc: DateTimeOffset.UtcNow,
                UpdatedAtUtc: DateTimeOffset.UtcNow);
            _jobsByFault[faultId] = job;
            return Task.FromResult(job);
        }

        public Task<TriageJob?> FindByFaultIdAsync(Guid faultId, CancellationToken cancellationToken) =>
            Task.FromResult(_jobsByFault.GetValueOrDefault(faultId));
    }

    private sealed class FakeTriageArtifactRepository : ITriageArtifactRepository
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

    private sealed class AlwaysNullPriorReportSummaryProvider : IPriorReportSummaryProvider
    {
        public Task<PriorReportSummary?> FindLatestAsync(Guid faultId, CancellationToken cancellationToken) =>
            Task.FromResult<PriorReportSummary?>(null);
    }
}
