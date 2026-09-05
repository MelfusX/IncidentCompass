using System.Text.Json.Nodes;
using IncidentCompass.Application.Core.Exceptions;
using IncidentCompass.Application.Governance.PostReportActions;
using IncidentCompass.Application.Governance.Tools;
using IncidentCompass.Application.Investigation.Reports;
using IncidentCompass.Domain.Incidents.Actions;
using IncidentCompass.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace IncidentCompass.IntegrationTests;

[Collection(PostgresRepositoryCollection.CollectionName)]
public sealed class PostReportActionEvaluationTests(PostgresRepositoryFixture postgres)
{
    [DockerAvailableFact]
    public async Task PublicationCommitsOneCanonicalIntentAndInjectedFailureRollsEverythingBack()
    {
        await using var database = await ActionApprovalDatabase.CreateAsync(postgres);
        using var services = CreateServices(database.ConnectionString);
        var origin = await SeedOriginAsync(database.ConnectionString);
        var successor = await ActionApprovalTestSupport.SeedSuccessorJobAsync(
            database.ConnectionString, origin, "intent-publisher");

        var reportId = await PublishAsync(services, successor);

        Assert.Equal(1, await CountAsync(database.ConnectionString,
            "SELECT count(*) FROM incidentcompass.post_report_action_intents WHERE origin_report_id = @id;",
            ("id", reportId)));
        var canonical = (byte[])(await ActionApprovalTestSupport.ScalarAsync(
            database.ConnectionString,
            "SELECT workflow_input FROM incidentcompass.post_report_action_intents WHERE origin_report_id = @id;",
            ("id", reportId)))!;
        Assert.Equal(
            $"{{\"originReportId\":\"{reportId:N}\",\"routeId\":\"test-route\",\"toolId\":\"action_test\",\"workflowVersion\":1}}",
            System.Text.Encoding.UTF8.GetString(canonical));

        await using var rollbackDatabase = await ActionApprovalDatabase.CreateAsync(postgres);
        using var rollbackServices = CreateServices(
            rollbackDatabase.ConnectionString,
            new ThrowingReportPublicationIntentFaultInjector(
                ReportPublicationIntentFaultPoint.AfterIntentInserted));
        var rollbackOrigin = await SeedOriginAsync(rollbackDatabase.ConnectionString);
        var rollbackSuccessor = await ActionApprovalTestSupport.SeedSuccessorJobAsync(
            rollbackDatabase.ConnectionString, rollbackOrigin, "rollback-publisher");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            PublishAsync(rollbackServices, rollbackSuccessor));

        Assert.Equal(0, await CountAsync(rollbackDatabase.ConnectionString,
            "SELECT count(*) FROM incidentcompass.triage_reports WHERE job_id = @id;",
            ("id", rollbackSuccessor.Job.Id)));
        Assert.Equal(0, await CountAsync(rollbackDatabase.ConnectionString,
            "SELECT count(*) FROM incidentcompass.triage_ledger WHERE job_id = @id AND event_type = 'ReportPublished';",
            ("id", rollbackSuccessor.Job.Id)));
        Assert.Equal(0, await CountAsync(rollbackDatabase.ConnectionString,
            "SELECT count(*) FROM incidentcompass.post_report_action_intents WHERE job_id = @id;",
            ("id", rollbackSuccessor.Job.Id)));
    }

    [DockerAvailableFact]
    public async Task InsufficientEvidenceAndConcurrentPublicationCannotCreateDuplicateIntent()
    {
        await using var database = await ActionApprovalDatabase.CreateAsync(postgres);
        using var services = CreateServices(database.ConnectionString);
        var origin = await SeedOriginAsync(database.ConnectionString);
        var successor = await ActionApprovalTestSupport.SeedSuccessorJobAsync(
            database.ConnectionString, origin, "concurrent-publisher");
        using var firstScope = services.CreateScope();
        using var secondScope = services.CreateScope();
        var first = Record.ExceptionAsync(() => firstScope.ServiceProvider
            .GetRequiredService<ITriageReportRepository>()
            .PublishAsync(successor.Job, "concurrent-publisher", successor.Report,
                TestContext.Current.CancellationToken)).AsTask();
        var second = Record.ExceptionAsync(() => secondScope.ServiceProvider
            .GetRequiredService<ITriageReportRepository>()
            .PublishAsync(successor.Job, "concurrent-publisher", successor.Report,
                TestContext.Current.CancellationToken)).AsTask();

        var errors = await Task.WhenAll(first, second)
            .WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        Assert.Single(errors, static error => error is null);
        Assert.Equal(1, await CountAsync(database.ConnectionString,
            "SELECT count(*) FROM incidentcompass.post_report_action_intents WHERE job_id = @id;",
            ("id", successor.Job.Id)));

        var current = new ActionApprovalOriginFixture(
            origin.TenantId, origin.SignalId, origin.FaultId, successor.Job.Id,
            await ReadReportIdAsync(database.ConnectionString, successor.Job.Id),
            origin.EvidenceArtifactId, origin.ConfigHash);
        var insufficient = await ActionApprovalTestSupport.SeedSuccessorJobAsync(
            database.ConnectionString, current, "insufficient-publisher");
        var insufficientReport = insufficient.Report with
        {
            Status = TriageReportStatus.InsufficientEvidence,
            Classification = "Unknown"
        };
        using var insufficientScope = services.CreateScope();
        var insufficientId = await insufficientScope.ServiceProvider
            .GetRequiredService<ITriageReportRepository>()
            .PublishAsync(insufficient.Job, "insufficient-publisher", insufficientReport,
                TestContext.Current.CancellationToken);

        Assert.Equal(0, await CountAsync(database.ConnectionString,
            "SELECT count(*) FROM incidentcompass.post_report_action_intents WHERE origin_report_id = @id;",
            ("id", insufficientId)));
    }

    [DockerAvailableFact]
    public async Task TwoPumpsHaveOneFencedWinnerAndOneProposal()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var database = await ActionApprovalDatabase.CreateAsync(postgres);
        using var services = CreateServices(
            database.ConnectionString,
            beforeProposal: async (call, cancellationToken) =>
            {
                if (call == 1)
                {
                    await release.Task.WaitAsync(cancellationToken);
                }
            });
        var reportId = await PublishSuccessorAsync(database.ConnectionString, services, "race-publisher");
        var workflow = GetWorkflow(services);
        var pumpA = CreatePump(services);
        var pumpB = CreatePump(services);
        var options = FastOptions();

        var fills = await Task.WhenAll(
            pumpA.FillAvailableSlotsAsync("pump-a", options, TestContext.Current.CancellationToken),
            pumpB.FillAvailableSlotsAsync("pump-b", options, TestContext.Current.CancellationToken));
        await WaitUntilAsync(() => workflow.Calls == 1);
        release.SetResult();
        await WaitForStateAsync(database.ConnectionString, reportId, "completed");
        await pumpA.ObserveCompletedAsync(TestContext.Current.CancellationToken);
        await pumpB.ObserveCompletedAsync(TestContext.Current.CancellationToken);

        Assert.InRange(fills.Sum(), 1, 2);
        Assert.Equal(1, workflow.Calls);
        Assert.Equal(1, await CountAsync(database.ConnectionString,
            "SELECT count(*) FROM incidentcompass.action_approvals WHERE origin_report_id = @id;",
            ("id", reportId)));
        Assert.Equal(0, GetTool(services).ExecutionCalls);
    }

    [DockerAvailableFact]
    public async Task ProposalCommitThenEvaluationFailureReplaysWithoutAdapterInvocation()
    {
        await using var database = await ActionApprovalDatabase.CreateAsync(postgres);
        using var services = CreateServices(
            database.ConnectionString,
            afterProposal: (call, response, cancellationToken) => call == 1
                ? Task.FromException(new PersistenceException(
                    "injected workflow completion gap", new TimeoutException("injected")))
                : Task.CompletedTask);
        var reportId = await PublishSuccessorAsync(database.ConnectionString, services, "replay-publisher");
        var workflow = GetWorkflow(services);
        var pump = CreatePump(services);
        var options = FastOptions();

        await pump.FillAvailableSlotsAsync("replay-pump", options, TestContext.Current.CancellationToken);
        await WaitForStateAsync(database.ConnectionString, reportId, "retry_pending");
        await pump.ObserveCompletedAsync(TestContext.Current.CancellationToken);
        await Task.Delay(TimeSpan.FromMilliseconds(1200), TestContext.Current.CancellationToken);
        await pump.FillAvailableSlotsAsync("replay-pump", options, TestContext.Current.CancellationToken);
        await WaitForStateAsync(database.ConnectionString, reportId, "completed");
        await pump.ObserveCompletedAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, workflow.Calls);
        Assert.False(workflow.Responses[0].IsReplay);
        Assert.True(workflow.Responses[1].IsReplay);
        Assert.Equal(workflow.Responses[0].Action!.Id, workflow.Responses[1].Action!.Id);
        Assert.Equal(1, await CountAsync(database.ConnectionString,
            "SELECT count(*) FROM incidentcompass.action_approvals WHERE origin_report_id = @id;",
            ("id", reportId)));
        Assert.Equal(0, GetTool(services).ExecutionCalls);
    }

    [DockerAvailableFact]
    public async Task FinalAttemptCrashAfterProposalCommitGetsOneBoundedReplay()
    {
        await using var database = await ActionApprovalDatabase.CreateAsync(postgres);
        using var services = CreateServices(
            database.ConnectionString,
            afterProposal: (call, response, cancellationToken) => call == 3
                ? Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken)
                : Task.CompletedTask,
            resultOverride: call => call switch
            {
                1 => PostReportActionWorkflowResult.Retry("proposal_recovery"),
                2 => PostReportActionWorkflowResult.Retry("transient_retry"),
                _ => null
            });
        var reportId = await PublishSuccessorAsync(
            database.ConnectionString, services, "final-attempt-publisher");
        var workflow = GetWorkflow(services);
        var pump = CreatePump(services);
        var options = FastOptions();

        for (var attempt = 1; attempt < options.MaximumAttempts; attempt++)
        {
            await pump.FillAvailableSlotsAsync(
                "final-attempt-pump", options, TestContext.Current.CancellationToken);
            await WaitUntilAsync(() => workflow.Calls == attempt);
            await WaitForStateAsync(database.ConnectionString, reportId, "retry_pending");
            await pump.ObserveCompletedAsync(TestContext.Current.CancellationToken);
            await Task.Delay(TimeSpan.FromMilliseconds(1200), TestContext.Current.CancellationToken);
        }

        await pump.FillAvailableSlotsAsync(
            "final-attempt-pump", options, TestContext.Current.CancellationToken);
        await WaitUntilAsync(() => workflow.Responses.Count == 1);
        var committedActionId = workflow.Responses[0].Action!.Id;
        await pump.DrainAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("processing", await ReadStateAsync(database.ConnectionString, reportId));
        Assert.Equal(options.MaximumAttempts, Convert.ToInt32(await ActionApprovalTestSupport.ScalarAsync(
            database.ConnectionString,
            "SELECT attempt_count FROM incidentcompass.post_report_action_intents WHERE origin_report_id = @id;",
            ("id", reportId))));

        await Task.Delay(TimeSpan.FromMilliseconds(1200), TestContext.Current.CancellationToken);
        await pump.FillAvailableSlotsAsync(
            "final-attempt-recovery", options, TestContext.Current.CancellationToken);
        await WaitForStateAsync(database.ConnectionString, reportId, "completed");
        await pump.ObserveCompletedAsync(TestContext.Current.CancellationToken);

        Assert.Equal(4, workflow.Calls);
        Assert.Equal(2, workflow.Responses.Count);
        Assert.False(workflow.Responses[0].IsReplay);
        Assert.True(workflow.Responses[1].IsReplay);
        Assert.Equal(committedActionId, workflow.Responses[1].Action!.Id);
        Assert.Equal(options.MaximumAttempts, Convert.ToInt32(await ActionApprovalTestSupport.ScalarAsync(
            database.ConnectionString,
            "SELECT attempt_count FROM incidentcompass.post_report_action_intents WHERE origin_report_id = @id;",
            ("id", reportId))));
        Assert.Equal(1, await CountAsync(database.ConnectionString,
            "SELECT count(*) FROM incidentcompass.action_approvals WHERE origin_report_id = @id;",
            ("id", reportId)));
        Assert.Equal(0, GetTool(services).ExecutionCalls);
    }

    [DockerAvailableFact]
    public async Task SecondFinalAttemptCompletionCrashDeadLettersWithoutAnotherReplay()
    {
        await using var database = await ActionApprovalDatabase.CreateAsync(postgres);
        using var services = CreateServices(
            database.ConnectionString,
            beforeProposal: (call, cancellationToken) => call < 3
                ? Task.FromException(new PersistenceException(
                    "injected pre-proposal failure", new TimeoutException("injected")))
                : Task.CompletedTask,
            afterProposal: (call, response, cancellationToken) => call >= 3
                ? Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken)
                : Task.CompletedTask);
        var reportId = await PublishSuccessorAsync(
            database.ConnectionString, services, "bounded-recovery-publisher");
        var workflow = GetWorkflow(services);
        var pump = CreatePump(services);
        var options = FastOptions();

        for (var attempt = 1; attempt < options.MaximumAttempts; attempt++)
        {
            await pump.FillAvailableSlotsAsync(
                "bounded-recovery-pump", options, TestContext.Current.CancellationToken);
            await WaitUntilAsync(() => workflow.Calls == attempt);
            await WaitForStateAsync(database.ConnectionString, reportId, "retry_pending");
            await pump.ObserveCompletedAsync(TestContext.Current.CancellationToken);
            await Task.Delay(TimeSpan.FromMilliseconds(1200), TestContext.Current.CancellationToken);
        }

        await pump.FillAvailableSlotsAsync(
            "bounded-recovery-pump", options, TestContext.Current.CancellationToken);
        await WaitUntilAsync(() => workflow.Responses.Count == 1);
        await pump.DrainAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await Task.Delay(TimeSpan.FromMilliseconds(1200), TestContext.Current.CancellationToken);
        await pump.FillAvailableSlotsAsync(
            "bounded-recovery-replay", options, TestContext.Current.CancellationToken);
        await WaitUntilAsync(() => workflow.Responses.Count == 2);
        await pump.DrainAsync().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(workflow.Responses[1].IsReplay);
        Assert.Equal(workflow.Responses[0].Action!.Id, workflow.Responses[1].Action!.Id);
        await Task.Delay(TimeSpan.FromMilliseconds(1200), TestContext.Current.CancellationToken);
        await pump.FillAvailableSlotsAsync(
            "bounded-recovery-exhaustion", options, TestContext.Current.CancellationToken);
        await WaitForStateAsync(database.ConnectionString, reportId, "dead_lettered");
        await pump.ObserveCompletedAsync(TestContext.Current.CancellationToken);

        Assert.Equal(4, workflow.Calls);
        Assert.Equal("attempts_exhausted", await ActionApprovalTestSupport.ScalarAsync(
            database.ConnectionString,
            "SELECT last_error_code FROM incidentcompass.post_report_action_intents WHERE origin_report_id = @id;",
            ("id", reportId)));
        Assert.Equal(options.MaximumAttempts, Convert.ToInt32(await ActionApprovalTestSupport.ScalarAsync(
            database.ConnectionString,
            "SELECT attempt_count FROM incidentcompass.post_report_action_intents WHERE origin_report_id = @id;",
            ("id", reportId))));
        Assert.Equal(0, GetTool(services).ExecutionCalls);
    }

    [DockerAvailableFact]
    public async Task WorkflowResultCannotForgeInternalRecoveryMarker()
    {
        await using var database = await ActionApprovalDatabase.CreateAsync(postgres);
        using var services = CreateServices(
            database.ConnectionString,
            resultOverride: _ => PostReportActionWorkflowResult.Retry(
                "internal_proposal_recovery"));
        var reportId = await PublishSuccessorAsync(
            database.ConnectionString, services, "forged-recovery-publisher");
        var workflow = GetWorkflow(services);
        var pump = CreatePump(services);

        await pump.FillAvailableSlotsAsync(
            "forged-recovery-pump", FastOptions(), TestContext.Current.CancellationToken);
        await WaitForStateAsync(database.ConnectionString, reportId, "dead_lettered");
        await pump.ObserveCompletedAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, workflow.Calls);
        Assert.Empty(workflow.Responses);
        Assert.Equal("workflow_result_invalid", await ActionApprovalTestSupport.ScalarAsync(
            database.ConnectionString,
            "SELECT last_error_code FROM incidentcompass.post_report_action_intents WHERE origin_report_id = @id;",
            ("id", reportId)));
        Assert.Equal(0, await CountAsync(database.ConnectionString,
            "SELECT count(*) FROM incidentcompass.action_approvals WHERE origin_report_id = @id;",
            ("id", reportId)));
        Assert.Equal(0, GetTool(services).ExecutionCalls);
    }

    [DockerAvailableFact]
    public async Task SupersededQueuedIntentCompletesDeniedWithoutActionOrAdapter()
    {
        var enqueue = true;
        await using var database = await ActionApprovalDatabase.CreateAsync(postgres);
        using var services = CreateServices(
            database.ConnectionString,
            shouldEnqueue: () => Volatile.Read(ref enqueue));
        var origin = await SeedOriginAsync(database.ConnectionString);
        var first = await ActionApprovalTestSupport.SeedSuccessorJobAsync(
            database.ConnectionString, origin, "first-publisher");
        var firstReportId = await PublishAsync(services, first);
        Volatile.Write(ref enqueue, false);
        var firstOrigin = new ActionApprovalOriginFixture(
            origin.TenantId, origin.SignalId, origin.FaultId, first.Job.Id,
            firstReportId, origin.EvidenceArtifactId, origin.ConfigHash);
        var newer = await ActionApprovalTestSupport.SeedSuccessorJobAsync(
            database.ConnectionString, firstOrigin, "newer-publisher");
        await PublishAsync(services, newer);
        var pump = CreatePump(services);

        await pump.FillAvailableSlotsAsync(
            "superseded-pump", FastOptions(), TestContext.Current.CancellationToken);
        await WaitForStateAsync(database.ConnectionString, firstReportId, "completed");
        await pump.ObserveCompletedAsync(TestContext.Current.CancellationToken);

        var workflow = GetWorkflow(services);
        Assert.Single(workflow.Responses);
        Assert.Equal("origin_ineligible", workflow.Responses[0].ReasonCode);
        Assert.Equal(0, await CountAsync(database.ConnectionString,
            "SELECT count(*) FROM incidentcompass.action_approvals WHERE origin_report_id = @id;",
            ("id", firstReportId)));
        Assert.Equal(0, GetTool(services).ExecutionCalls);
    }

    [DockerAvailableFact]
    public async Task MissingPersistedWorkflowMembershipDeadLettersWithoutProposal()
    {
        await using var database = await ActionApprovalDatabase.CreateAsync(postgres);
        Guid reportId;
        using (var publishingServices = CreateServices(database.ConnectionString))
        {
            reportId = await PublishSuccessorAsync(
                database.ConnectionString, publishingServices, "catalog-publisher");
        }

        using var recoveryServices = CreateServices(
            database.ConnectionString,
            registerWorkflow: false);
        var pump = CreatePump(recoveryServices);

        await pump.FillAvailableSlotsAsync(
            "catalog-pump", FastOptions(), TestContext.Current.CancellationToken);
        await WaitForStateAsync(database.ConnectionString, reportId, "dead_lettered");
        await pump.ObserveCompletedAsync(TestContext.Current.CancellationToken);

        Assert.Equal("workflow_not_registered", await ActionApprovalTestSupport.ScalarAsync(
            database.ConnectionString,
            "SELECT last_error_code FROM incidentcompass.post_report_action_intents WHERE origin_report_id = @id;",
            ("id", reportId)));
        Assert.Equal(0, await CountAsync(database.ConnectionString,
            "SELECT count(*) FROM incidentcompass.action_approvals WHERE origin_report_id = @id;",
            ("id", reportId)));
        Assert.Equal(0, GetTool(recoveryServices).ExecutionCalls);
    }

    [DockerAvailableFact]
    public async Task ClaimedIntentSupersededBeforeProposalCompletesDeniedWithoutAction()
    {
        var enqueue = true;
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var database = await ActionApprovalDatabase.CreateAsync(postgres);
        using var services = CreateServices(
            database.ConnectionString,
            shouldEnqueue: () => Volatile.Read(ref enqueue),
            beforeProposal: (call, cancellationToken) =>
                call == 1 ? release.Task.WaitAsync(cancellationToken) : Task.CompletedTask);
        var firstReportId = await PublishSuccessorAsync(
            database.ConnectionString, services, "superseded-claim-publisher");
        var workflow = GetWorkflow(services);
        var pump = CreatePump(services);

        await pump.FillAvailableSlotsAsync(
            "superseded-claim-pump", FastOptions(), TestContext.Current.CancellationToken);
        await WaitForStateAsync(database.ConnectionString, firstReportId, "processing");
        await WaitUntilAsync(() => workflow.Calls == 1);

        Volatile.Write(ref enqueue, false);
        var firstOrigin = await ReadOriginAsync(database.ConnectionString, firstReportId);
        var newer = await ActionApprovalTestSupport.SeedSuccessorJobAsync(
            database.ConnectionString, firstOrigin, "newer-claim-publisher");
        await PublishAsync(services, newer);
        release.SetResult();
        await WaitForStateAsync(database.ConnectionString, firstReportId, "completed");
        await pump.ObserveCompletedAsync(TestContext.Current.CancellationToken);

        Assert.Single(workflow.Responses);
        Assert.Equal("origin_ineligible", workflow.Responses[0].ReasonCode);
        Assert.Equal(0, await CountAsync(database.ConnectionString,
            "SELECT count(*) FROM incidentcompass.action_approvals WHERE origin_report_id = @id;",
            ("id", firstReportId)));
        Assert.Equal(0, GetTool(services).ExecutionCalls);
    }

    [DockerAvailableFact]
    public async Task ShutdownLeavesLeaseRecoverableAndObservesBlockedWorkflow()
    {
        var blockFirst = true;
        await using var database = await ActionApprovalDatabase.CreateAsync(postgres);
        using var services = CreateServices(
            database.ConnectionString,
            beforeProposal: async (call, cancellationToken) =>
            {
                if (Volatile.Read(ref blockFirst))
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                }
            });
        var firstReportId = await PublishSuccessorAsync(
            database.ConnectionString, services, "recovery-publisher");
        var workflow = GetWorkflow(services);
        var pump = CreatePump(services);
        var options = FastOptions();

        await pump.FillAvailableSlotsAsync("recovery-pump", options, TestContext.Current.CancellationToken);
        await WaitForStateAsync(database.ConnectionString, firstReportId, "processing");
        await pump.DrainAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("processing", await ReadStateAsync(database.ConnectionString, firstReportId));

        Volatile.Write(ref blockFirst, false);
        await Task.Delay(TimeSpan.FromMilliseconds(1200), TestContext.Current.CancellationToken);
        await pump.FillAvailableSlotsAsync("recovery-pump-2", options, TestContext.Current.CancellationToken);
        await WaitForStateAsync(database.ConnectionString, firstReportId, "completed");
        await pump.ObserveCompletedAsync(TestContext.Current.CancellationToken);
        Assert.Equal(2, workflow.Calls);

        Assert.Equal(1, await CountAsync(database.ConnectionString,
            "SELECT count(*) FROM incidentcompass.action_approvals WHERE origin_report_id = @id;",
            ("id", firstReportId)));
        Assert.Equal(0, GetTool(services).ExecutionCalls);
    }

    private static ServiceProvider CreateServices(
        string connectionString,
        ITriageReportPublicationIntentFaultInjector? faultInjector = null,
        Func<bool>? shouldEnqueue = null,
        Func<int, CancellationToken, Task>? beforeProposal = null,
        Func<int, IncidentCompass.Application.Governance.ActionApprovals.Propose.PostReportActionProposalResponse,
            CancellationToken, Task>? afterProposal = null,
        Func<int, PostReportActionWorkflowResult?>? resultOverride = null,
        bool registerWorkflow = true)
    {
        var tool = new SyntheticExternalActionTool(category: ActionCategory.Notification);
        return ActionApprovalTestSupport.CreateServices(
            connectionString,
            configureServices: services =>
            {
                services.AddLogging();
                services.AddSingleton(new AgentToolDescriptor(
                    tool.Definition.Name, AgentToolCapability.ExternalAction,
                    tool.Category, tool.LogicalTargetId));
                services.AddSingleton<IExternalActionTool>(tool);
                if (registerWorkflow)
                {
                    services.AddSingleton<IPostReportActionWorkflow>(provider =>
                        new DeterministicPostReportActionWorkflow(
                            provider.GetRequiredService<IServiceScopeFactory>(),
                            shouldEnqueue, beforeProposal, afterProposal,
                            resultOverride: resultOverride));
                }
                services.AddSingleton<PostReportActionEvaluationLeaseRenewer>();
                services.AddSingleton<PostReportActionEvaluationPump>();
                if (faultInjector is not null)
                {
                    services.RemoveAll<ITriageReportPublicationIntentFaultInjector>();
                    services.AddSingleton(faultInjector);
                }
            });
    }

    private static PostReportActionEvaluationPump CreatePump(IServiceProvider services) =>
        new(
            services.GetRequiredService<IServiceScopeFactory>(),
            services.GetRequiredService<PostReportActionWorkflowCatalog>(),
            services.GetRequiredService<PostReportActionEvaluationLeaseRenewer>(),
            services.GetRequiredService<ILogger<PostReportActionEvaluationPump>>());

    private static DeterministicPostReportActionWorkflow GetWorkflow(IServiceProvider services) =>
        Assert.IsType<DeterministicPostReportActionWorkflow>(
            services.GetRequiredService<IPostReportActionWorkflow>());

    private static SyntheticExternalActionTool GetTool(IServiceProvider services) =>
        Assert.IsType<SyntheticExternalActionTool>(services.GetRequiredService<IExternalActionTool>());

    private static PostReportActionEvaluationOptions FastOptions() => new()
    {
        LeaseSeconds = 1,
        MaximumAttempts = 3,
        FirstRetryDelaySeconds = 1,
        SecondRetryDelaySeconds = 1,
        ScanBatchSize = 8,
        PollIntervalSeconds = 1,
        MaxConcurrency = 4
    };

    private static async Task<Guid> PublishSuccessorAsync(
        string connectionString,
        ServiceProvider services,
        string workerId)
    {
        var origin = await SeedOriginAsync(connectionString);
        var successor = await ActionApprovalTestSupport.SeedSuccessorJobAsync(
            connectionString, origin, workerId);
        return await PublishAsync(services, successor);
    }

    private static async Task<Guid> PublishAsync(
        ServiceProvider services,
        (IncidentCompass.Domain.Incidents.TriageJob Job, TriageReport Report) publication)
    {
        using var scope = services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ITriageReportRepository>()
            .PublishAsync(publication.Job, publication.Job.LockedBy!, publication.Report,
                TestContext.Current.CancellationToken);
    }

    private static Task<ActionApprovalOriginFixture> SeedOriginAsync(string connectionString) =>
        ActionApprovalTestSupport.SeedOriginAsync(
            connectionString,
            serializedConfigJson: Configuration());

    private static string Configuration()
    {
        var root = new JsonObject
        {
            ["Providers"] = new JsonObject { ["mock"] = new JsonObject { ["Kind"] = "Mock" } },
            ["Routes"] = new JsonObject
            {
                ["chat"] = new JsonObject
                {
                    ["Kind"] = "Chat",
                    ["ProviderId"] = "mock",
                    ["Model"] = "mock-chat"
                }
            },
            ["Orchestrator"] = new JsonObject
            {
                ["Instructions"] = "orchestrator",
                ["RouteId"] = "chat",
                ["Tools"] = new JsonArray("delegate", "publish_report"),
                ["Budget"] = new JsonObject
                {
                    ["MaxWorkers"] = 1,
                    ["MaxTokens"] = 1000,
                    ["MaxWallClockSeconds"] = 30,
                    ["MaxReprompts"] = 0
                }
            },
            ["Roles"] = new JsonObject
            {
                ["analysis"] = new JsonObject
                {
                    ["RouteId"] = "chat",
                    ["Instructions"] = "analysis",
                    ["Tools"] = new JsonArray(),
                    ["OutputSchema"] = "{}"
                }
            },
            ["Tools"] = new JsonObject
            {
                ["action_test"] = new JsonObject
                {
                    ["Kind"] = "external_action",
                    ["Category"] = "notification",
                    ["LogicalTargetId"] = "test:target"
                }
            },
            ["Rules"] = new JsonArray(new JsonObject
            {
                ["Type"] = "rate_cap",
                ["Tool"] = "action_test",
                ["Scope"] = "attempt",
                ["Max"] = 10
            }),
            ["Actions"] = new JsonObject
            {
                ["AllowedTools"] = new JsonArray("action_test"),
                ["DefaultMode"] = "live",
                ["RequireApprovalForAll"] = false,
                ["ApprovalTtlMinutes"] = 60,
                ["NotificationRoutes"] = new JsonArray(new JsonObject
                {
                    ["RouteId"] = "test-route",
                    ["ToolId"] = "action_test",
                    ["Severities"] = new JsonArray("error", "critical", "fatal")
                })
            },
            ["Ingestion"] = new JsonObject
            {
                ["DefaultTenant"] = "local",
                ["AllowedSources"] = new JsonArray("tester")
            },
            ["FaultGrouping"] = new JsonObject
            {
                ["LookbackMinutes"] = 15,
                ["SilenceWindowMinutes"] = 30,
                ["FingerprintVersion"] = 1,
                ["MassIssue"] = new JsonObject
                {
                    ["MinNeighborCount"] = 5,
                    ["MinFingerprintStrength"] = "strong"
                }
            },
            ["Redaction"] = new JsonObject
            {
                ["AttributeKeys"] = new JsonArray(),
                ["Patterns"] = new JsonArray(),
                ["UserIdentifierAttributes"] = new JsonArray()
            }
        };
        return root.ToJsonString();
    }

    private static async Task<ActionApprovalOriginFixture> ReadOriginAsync(
        string connectionString,
        Guid reportId)
    {
        var jobId = (Guid)(await ActionApprovalTestSupport.ScalarAsync(
            connectionString, "SELECT job_id FROM incidentcompass.triage_reports WHERE id = @id;",
            ("id", reportId)))!;
        var faultId = (Guid)(await ActionApprovalTestSupport.ScalarAsync(
            connectionString, "SELECT fault_id FROM incidentcompass.triage_reports WHERE id = @id;",
            ("id", reportId)))!;
        var configHash = (string)(await ActionApprovalTestSupport.ScalarAsync(
            connectionString, "SELECT config_hash FROM incidentcompass.triage_reports WHERE id = @id;",
            ("id", reportId)))!;
        return new("tenant-action-tests", Guid.Empty, faultId, jobId, reportId, Guid.Empty, configHash);
    }

    private static async Task<Guid> ReadReportIdAsync(string connectionString, Guid jobId) =>
        (Guid)(await ActionApprovalTestSupport.ScalarAsync(
            connectionString, "SELECT id FROM incidentcompass.triage_reports WHERE job_id = @id;",
            ("id", jobId)))!;

    private static Task<long> CountAsync(
        string connectionString,
        string sql,
        params (string Name, object Value)[] parameters) =>
        ActionApprovalTestSupport.CountAsync(connectionString, sql, parameters);

    private static async Task<string> ReadStateAsync(string connectionString, Guid reportId) =>
        (string)(await ActionApprovalTestSupport.ScalarAsync(
            connectionString,
            "SELECT state FROM incidentcompass.post_report_action_intents WHERE origin_report_id = @id;",
            ("id", reportId)))!;

    private static async Task WaitForStateAsync(
        string connectionString,
        Guid reportId,
        string state) =>
        await WaitUntilAsync(async () =>
            string.Equals(await ReadStateAsync(connectionString, reportId), state, StringComparison.Ordinal));

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(10);
        while (!condition())
        {
            if (DateTimeOffset.UtcNow >= deadline)
            {
                throw new TimeoutException("Timed out waiting for the post-report action condition.");
            }

            await Task.Delay(25, TestContext.Current.CancellationToken);
        }
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> condition)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(10);
        while (!await condition())
        {
            if (DateTimeOffset.UtcNow >= deadline)
            {
                throw new TimeoutException("Timed out waiting for the post-report action condition.");
            }

            await Task.Delay(25, TestContext.Current.CancellationToken);
        }
    }
}
