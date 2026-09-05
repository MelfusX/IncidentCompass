using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using IncidentCompass.Application.Core.Dispatching;
using IncidentCompass.Application.Core.Exceptions;
using IncidentCompass.Application.Core.Serialization;
using IncidentCompass.Application.Governance.ActionApprovals;
using IncidentCompass.Application.Governance.ActionApprovals.Propose;
using IncidentCompass.Application.Governance.Tools;
using IncidentCompass.Application.Governance.Validation;
using IncidentCompass.Application.Investigation.Reports;
using IncidentCompass.Domain.Incidents.Actions;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace IncidentCompass.IntegrationTests;

[Collection(PostgresRepositoryCollection.CollectionName)]
public sealed class PostReportActionProposalTests(PostgresRepositoryFixture postgres)
{
    [DockerAvailableFact]
    public async Task NotificationAutoApprovesWriteRequestsApprovalAndNeitherInvokesAdapter()
    {
        await using var database = await ActionApprovalDatabase.CreateAsync(postgres);
        var notificationTool = new SyntheticExternalActionTool(ActionCategory.Notification);
        var notificationOrigin = await ActionApprovalTestSupport.SeedOriginAsync(
            database.ConnectionString, serializedConfigJson: Configuration(ActionCategory.Notification));
        using var notificationServices = Services(database.ConnectionString, notificationTool);
        var notification = await ProposeAsync(
            notificationServices, notificationOrigin, "notify-1", Arguments("incident ready"));

        var writeTool = new SyntheticExternalActionTool(ActionCategory.TicketCreate);
        var writeOrigin = await ActionApprovalTestSupport.SeedOriginAsync(
            database.ConnectionString, serializedConfigJson: Configuration(ActionCategory.TicketCreate));
        using var writeServices = Services(database.ConnectionString, writeTool);
        var write = await ProposeAsync(writeServices, writeOrigin, "ticket-1", Arguments("create ticket"));

        Assert.Equal(PostReportActionProposalOutcome.Approved, notification.Outcome);
        Assert.Equal(ActionApprovalState.Approved, notification.Action!.State);
        Assert.Equal(PostReportActionProposalOutcome.Requested, write.Outcome);
        Assert.Equal(ActionApprovalState.Requested, write.Action!.State);
        Assert.Equal(0, notificationTool.ExecutionCalls);
        Assert.Equal(0, writeTool.ExecutionCalls);
        Assert.Equal(0, await ActionApprovalTestSupport.CountAsync(
            database.ConnectionString,
            """
            SELECT count(*) FROM incidentcompass.action_approvals a
            JOIN incidentcompass.triage_artifacts artifact ON artifact.id = a.proposal_artifact_id
            WHERE convert_from(a.canonical_payload, 'UTF8') LIKE '%api.example.test%'
               OR artifact.redacted_payload::text LIKE '%api.example.test%';
            """));
    }

    [DockerAvailableFact]
    public async Task ExactReplayIsIdempotentAndChangedPayloadFailsClosed()
    {
        await using var database = await ActionApprovalDatabase.CreateAsync(postgres);
        var tool = new SyntheticExternalActionTool(ActionCategory.Notification);
        var origin = await ActionApprovalTestSupport.SeedOriginAsync(
            database.ConnectionString, serializedConfigJson: Configuration(ActionCategory.Notification));
        using var services = Services(database.ConnectionString, tool);

        var first = await ProposeAsync(services, origin, "same-key", Arguments("same"));
        var replay = await ProposeAsync(services, origin, "same-key", Arguments("same"));
        await Assert.ThrowsAsync<ConflictException>(() =>
            ProposeAsync(services, origin, "same-key", Arguments("changed")));

        Assert.False(first.IsReplay);
        Assert.True(replay.IsReplay);
        Assert.Equal(first.Action!.Id, replay.Action!.Id);
        Assert.Equal(1, await ActionApprovalTestSupport.CountAsync(
            database.ConnectionString,
            "SELECT count(*) FROM incidentcompass.action_approvals WHERE proposal_key = 'same-key';"));
    }

    [DockerAvailableFact]
    public async Task SafeOriginDenialsAuditOnceWithoutActionWhileForeignOriginWritesNothing()
    {
        await using var database = await ActionApprovalDatabase.CreateAsync(postgres);
        var tool = new SyntheticExternalActionTool(ActionCategory.Notification);
        var origin = await ActionApprovalTestSupport.SeedOriginAsync(
            database.ConnectionString,
            serializedConfigJson: Configuration(ActionCategory.Notification, allowed: false));
        using var services = Services(database.ConnectionString, tool);

        var denied = await ProposeAsync(services, origin, "unlisted", Arguments("denied"));
        var alias = await DispatchAsync(services, new ProposePostReportActionCommand(
            origin.TenantId, origin.ReportId, "action_test_alias", "alias", Arguments("alias")));
        var preconditionOrigin = await ActionApprovalTestSupport.SeedOriginAsync(
            database.ConnectionString,
            serializedConfigJson: Configuration(ActionCategory.Notification, precondition: true));
        var precondition = await ProposeAsync(
            services, preconditionOrigin, "precondition", Arguments("precondition"));
        var beforeForeign = await LedgerCountAsync(database.ConnectionString, origin.JobId);
        var foreign = await DispatchAsync(services, new ProposePostReportActionCommand(
            "other-tenant", origin.ReportId, tool.Definition.Name, "foreign", Arguments("foreign")));

        Assert.Equal(PostReportActionProposalOutcome.Denied, denied.Outcome);
        Assert.True(denied.DenialAudited);
        Assert.Equal("action_not_granted", denied.ReasonCode);
        Assert.Equal("tool_not_registered", alias.ReasonCode);
        Assert.True(alias.DenialAudited);
        Assert.Equal("precondition_unsatisfied", precondition.ReasonCode);
        Assert.True(precondition.DenialAudited);
        Assert.Equal(2, await DenialCountAsync(database.ConnectionString, origin.ReportId));
        Assert.Equal(1, await DenialCountAsync(database.ConnectionString, preconditionOrigin.ReportId));
        Assert.Equal(0, await ActionCountAsync(database.ConnectionString, origin.ReportId));
        Assert.Equal(0, await ActionCountAsync(database.ConnectionString, preconditionOrigin.ReportId));
        Assert.False(foreign.DenialAudited);
        Assert.Equal(beforeForeign, await LedgerCountAsync(database.ConnectionString, origin.JobId));
    }

    [DockerAvailableFact]
    public async Task MixedCaseSafeToolIdAuditsDisabledAndUnlistedPolicyWithoutAliasLeakage()
    {
        const string toolId = "Action_Test.v1-Edge";
        await using var database = await ActionApprovalDatabase.CreateAsync(postgres);
        var tool = new SyntheticExternalActionTool(ActionCategory.Notification, toolId: toolId);
        var unlistedOrigin = await ActionApprovalTestSupport.SeedOriginAsync(
            database.ConnectionString,
            serializedConfigJson: Configuration(ActionCategory.Notification, allowed: false, toolId: toolId));
        var disabledOrigin = await ActionApprovalTestSupport.SeedOriginAsync(
            database.ConnectionString,
            serializedConfigJson: Configuration(ActionCategory.Notification, globalMode: "disabled", toolId: toolId));
        using var services = Services(database.ConnectionString, tool);

        var unlisted = await ProposeAsync(
            services, unlistedOrigin, "mixed-unlisted", Arguments("unlisted"), toolId);
        var disabled = await ProposeAsync(
            services, disabledOrigin, "mixed-disabled", Arguments("disabled"), toolId);
        var alias = await ProposeAsync(
            services, disabledOrigin, "mixed-alias", Arguments("alias"), toolId.ToLowerInvariant());

        Assert.Equal("action_not_granted", unlisted.ReasonCode);
        Assert.Equal("action_disabled", disabled.ReasonCode);
        Assert.Equal("tool_not_registered", alias.ReasonCode);
        Assert.True(unlisted.DenialAudited);
        Assert.True(disabled.DenialAudited);
        Assert.True(alias.DenialAudited);
        Assert.Equal(2, await DenialToolCountAsync(database.ConnectionString, toolId));
        Assert.Equal(0, await DenialToolCountAsync(database.ConnectionString, toolId.ToLowerInvariant()));
        Assert.Equal(0, await ActionCountAsync(database.ConnectionString, unlistedOrigin.ReportId));
        Assert.Equal(0, await ActionCountAsync(database.ConnectionString, disabledOrigin.ReportId));
        Assert.Equal(0, tool.ExecutionCalls);
    }

    [DockerAvailableFact]
    public async Task ExternalPreconditionReadWaitsForFaultLockAndUsesTransactionBoundFacts()
    {
        await using var database = await ActionApprovalDatabase.CreateAsync(postgres);
        var tool = new SyntheticExternalActionTool(ActionCategory.Notification);
        var origin = await ActionApprovalTestSupport.SeedOriginAsync(
            database.ConnectionString,
            serializedConfigJson: Configuration(ActionCategory.Notification, precondition: true));
        using var services = Services(database.ConnectionString, tool);
        await using var lockConnection = new NpgsqlConnection(database.ConnectionString);
        await lockConnection.OpenAsync(TestContext.Current.CancellationToken);
        await using var lockTransaction = await lockConnection.BeginTransactionAsync(TestContext.Current.CancellationToken);
        await using (var lockCommand = new NpgsqlCommand("""
            SELECT id FROM incidentcompass.faults WHERE id = @fault_id FOR UPDATE;
            """, lockConnection, lockTransaction))
        {
            lockCommand.Parameters.AddWithValue("fault_id", origin.FaultId);
            Assert.Equal(origin.FaultId, await lockCommand.ExecuteScalarAsync(TestContext.Current.CancellationToken));
        }

        var proposalTask = ProposeAsync(
            services, origin, "locked-precondition", Arguments("after lock"));
        await WaitForFaultLockWaiterAsync(database.ConnectionString);
        Assert.False(proposalTask.IsCompleted);

        await using (var factCommand = new NpgsqlCommand("""
            INSERT INTO incidentcompass.triage_ledger (
                fault_id, job_id, attempt, event_type, tool_name, tool_status,
                payload_ref, config_hash, created_at_utc)
            VALUES (@fault, @job, @attempt, 'ToolResult', 'action_test', 'Succeeded',
                    @reference, @config, clock_timestamp());
            """, lockConnection, lockTransaction))
        {
            factCommand.Parameters.AddWithValue("fault", origin.FaultId);
            factCommand.Parameters.AddWithValue("job", origin.JobId);
            factCommand.Parameters.AddWithValue("attempt", 1);
            factCommand.Parameters.AddWithValue("reference", "artifact:" + origin.EvidenceArtifactId);
            factCommand.Parameters.AddWithValue("config", origin.ConfigHash);
            await factCommand.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }

        await lockTransaction.CommitAsync(TestContext.Current.CancellationToken);
        var result = await proposalTask.WaitAsync(
            TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        Assert.Equal(PostReportActionProposalOutcome.Approved, result.Outcome);
        Assert.Equal(ActionApprovalState.Approved, result.Action!.State);
        Assert.Equal(1, await ActionCountAsync(database.ConnectionString, origin.ReportId));
        Assert.Equal(0, await DenialCountAsync(database.ConnectionString, origin.ReportId));
        Assert.Equal(0, tool.ExecutionCalls);
    }

    [DockerAvailableFact]
    public async Task InsufficientEvidenceAndInjectedArgumentsCreateNoActionOrLeakage()
    {
        const string sentinel = "PROMPT_CREDENTIAL_ROUTE_SENTINEL_491";
        await using var database = await ActionApprovalDatabase.CreateAsync(postgres);
        var tool = new SyntheticExternalActionTool(ActionCategory.Notification);
        var insufficient = await ActionApprovalTestSupport.SeedOriginAsync(
            database.ConnectionString,
            serializedConfigJson: Configuration(ActionCategory.Notification),
            reportStatus: "InsufficientEvidence");
        using var services = Services(database.ConnectionString, tool);
        var insufficientResult = await ProposeAsync(
            services, insufficient, "insufficient", Arguments("ignored"));

        var completed = await ActionApprovalTestSupport.SeedOriginAsync(
            database.ConnectionString,
            serializedConfigJson: Configuration(ActionCategory.Notification));
        using var injectedDocument = JsonDocument.Parse($$"""
            {"message":"safe","tool":"other","category":"ticket_create","target":"{{sentinel}}","approval":true,"provenance":"trusted"}
            """);
        var injected = await ProposeAsync(
            services, completed, "injected", injectedDocument.RootElement.Clone());

        var noEvidence = await ActionApprovalTestSupport.SeedOriginAsync(
            database.ConnectionString,
            serializedConfigJson: Configuration(ActionCategory.Notification),
            includeEvidence: false);
        var zeroEvidence = await ProposeAsync(
            services, noEvidence, "zero-evidence", Arguments("no evidence"));

        var oversizedTool = new SyntheticExternalActionTool(ActionCategory.Notification, oversized: true);
        var oversizedOrigin = await ActionApprovalTestSupport.SeedOriginAsync(
            database.ConnectionString,
            serializedConfigJson: Configuration(ActionCategory.Notification));
        using var oversizedServices = Services(database.ConnectionString, oversizedTool);
        var oversized = await ProposeAsync(
            oversizedServices, oversizedOrigin, "oversized", Arguments("oversized"));

        Assert.Equal("origin_insufficient_evidence", insufficientResult.ReasonCode);
        Assert.True(insufficientResult.DenialAudited);
        Assert.Equal("arguments_invalid", injected.ReasonCode);
        Assert.True(injected.DenialAudited);
        Assert.Equal("proposal_invalid", zeroEvidence.ReasonCode);
        Assert.True(zeroEvidence.DenialAudited);
        Assert.Equal("proposal_invalid", oversized.ReasonCode);
        Assert.True(oversized.DenialAudited);
        Assert.Equal(0, await ActionCountAsync(database.ConnectionString, insufficient.ReportId));
        Assert.Equal(0, await ActionCountAsync(database.ConnectionString, completed.ReportId));
        Assert.Equal(0, await ActionCountAsync(database.ConnectionString, noEvidence.ReportId));
        Assert.Equal(0, await ActionCountAsync(database.ConnectionString, oversizedOrigin.ReportId));
        Assert.Equal(0, tool.ExecutionCalls);
        Assert.Equal(0, oversizedTool.ExecutionCalls);
        Assert.Equal(0, await ActionApprovalTestSupport.CountAsync(
            database.ConnectionString,
            """
            SELECT count(*) FROM incidentcompass.triage_ledger
            WHERE coalesce(tool_name, '') LIKE '%' || @sentinel || '%'
               OR coalesce(rationale, '') LIKE '%' || @sentinel || '%'
               OR coalesce(decision_reason, '') LIKE '%' || @sentinel || '%';
            """, ("sentinel", sentinel)));
    }

    [DockerAvailableFact]
    public async Task ConcurrentDistinctKeysCannotExceedAcceptedProposalRateCap()
    {
        await using var database = await ActionApprovalDatabase.CreateAsync(postgres);
        foreach (var scope in new[] { "attempt", "job" })
        {
            var tool = new SyntheticExternalActionTool(ActionCategory.Notification);
            var origin = await ActionApprovalTestSupport.SeedOriginAsync(
                database.ConnectionString,
                serializedConfigJson: Configuration(ActionCategory.Notification, rateCap: 1, rateScope: scope));
            using var services = Services(database.ConnectionString, tool);
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var first = StartAfterAsync(start.Task, () =>
                ProposeAsync(services, origin, "cap-a", Arguments("first")));
            var second = StartAfterAsync(start.Task, () =>
                ProposeAsync(services, origin, "cap-b", Arguments("second")));

            start.SetResult();
            var results = await Task.WhenAll(first, second)
                .WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

            Assert.Single(results, result => result.Outcome == PostReportActionProposalOutcome.Approved);
            Assert.Single(results, result => result.ReasonCode == "rate_cap_exceeded" && result.DenialAudited);
            Assert.Equal(1, await ActionCountAsync(database.ConnectionString, origin.ReportId));
            Assert.Equal(1, await DenialCountAsync(database.ConnectionString, origin.ReportId));
        }

        var writeTool = new SyntheticExternalActionTool(ActionCategory.TicketCreate);
        var writeOrigin = await ActionApprovalTestSupport.SeedOriginAsync(
            database.ConnectionString,
            serializedConfigJson: Configuration(ActionCategory.TicketCreate, rateCap: 1));
        using var writeServices = Services(database.ConnectionString, writeTool);
        var requested = await ProposeAsync(writeServices, writeOrigin, "write-a", Arguments("first"));
        var capped = await ProposeAsync(writeServices, writeOrigin, "write-b", Arguments("second"));
        Assert.Equal(PostReportActionProposalOutcome.Requested, requested.Outcome);
        Assert.Equal("rate_cap_exceeded", capped.ReasonCode);
    }

    [DockerAvailableFact]
    public async Task ProposalFailureInjectionRollsBackUseCaseWrites()
    {
        await using var database = await ActionApprovalDatabase.CreateAsync(postgres);
        var tool = new SyntheticExternalActionTool(ActionCategory.Notification);
        var origin = await ActionApprovalTestSupport.SeedOriginAsync(
            database.ConnectionString,
            serializedConfigJson: Configuration(ActionCategory.Notification));
        using var services = Services(
            database.ConnectionString,
            tool,
            new ThrowingActionApprovalFaultInjector(ActionApprovalFaultPoint.AfterProposalArtifact));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ProposeAsync(services, origin, "rollback-use-case", Arguments("rollback")));

        Assert.Equal(0, await ActionCountAsync(database.ConnectionString, origin.ReportId));
        Assert.Equal(0, await ActionApprovalTestSupport.CountAsync(
            database.ConnectionString,
            "SELECT count(*) FROM incidentcompass.triage_artifacts WHERE job_id = @job AND kind = 'ProposedAction';",
            ("job", origin.JobId)));
    }

    [DockerAvailableFact]
    public async Task NewNotificationSupersedesUnclaimedPriorActionInsideFaultLock()
    {
        await using var database = await ActionApprovalDatabase.CreateAsync(postgres);
        var tool = new SyntheticExternalActionTool(ActionCategory.Notification);
        var origin = await ActionApprovalTestSupport.SeedOriginAsync(
            database.ConnectionString,
            serializedConfigJson: Configuration(ActionCategory.Notification));
        using var services = Services(database.ConnectionString, tool);
        var first = await ProposeAsync(services, origin, "notification-first", Arguments("first"));
        var successor = await PublishSuccessorAsync(services, database.ConnectionString, origin);

        var next = await ProposeAsync(
            services, successor, "notification-successor", Arguments("successor"));

        Assert.Equal(PostReportActionProposalOutcome.Approved, next.Outcome);
        Assert.Equal("failed", await ActionApprovalTestSupport.ScalarAsync(
            database.ConnectionString,
            "SELECT state FROM incidentcompass.action_approvals WHERE id = @id;",
            ("id", first.Action!.Id)));
        Assert.Equal("origin_report_superseded", await ActionApprovalTestSupport.ScalarAsync(
            database.ConnectionString,
            "SELECT failure_code FROM incidentcompass.action_approvals WHERE id = @id;",
            ("id", first.Action.Id)));
        Assert.Equal(1, await ActionApprovalTestSupport.CountAsync(
            database.ConnectionString,
            """
            SELECT count(*)
            FROM incidentcompass.triage_ledger ledger
            JOIN incidentcompass.triage_artifacts artifact
              ON ledger.payload_ref = 'artifact:' || artifact.id::text
            WHERE ledger.event_type = 'ActionCompleted'
              AND artifact.domain_ref = @action_ref;
            """,
            ("action_ref", "action:" + first.Action.Id)));
    }

    [DockerAvailableFact]
    public async Task StartedNotificationMakesSuccessorFailClosedWithoutSupersedingClaim()
    {
        await using var database = await ActionApprovalDatabase.CreateAsync(postgres);
        var tool = new SyntheticExternalActionTool(ActionCategory.Notification);
        var origin = await ActionApprovalTestSupport.SeedOriginAsync(
            database.ConnectionString,
            serializedConfigJson: Configuration(ActionCategory.Notification));
        using var services = Services(database.ConnectionString, tool);
        var first = await ProposeAsync(services, origin, "notification-in-flight", Arguments("first"));
        using (var scope = services.CreateScope())
        {
            var claim = await scope.ServiceProvider.GetRequiredService<IApprovedActionDispatcher>()
                .TryClaimAsync(first.Action!.Id, "notification-test", TimeSpan.FromSeconds(10),
                    TestContext.Current.CancellationToken);
            Assert.NotNull(claim);
        }
        var successor = await PublishSuccessorAsync(services, database.ConnectionString, origin);

        var next = await ProposeAsync(
            services, successor, "notification-blocked", Arguments("successor"));

        Assert.Equal(PostReportActionProposalOutcome.Denied, next.Outcome);
        Assert.Equal("notification_in_flight", next.ReasonCode);
        Assert.Equal("approved", await ActionApprovalTestSupport.ScalarAsync(
            database.ConnectionString,
            "SELECT state FROM incidentcompass.action_approvals WHERE id = @id;",
            ("id", first.Action!.Id)));
    }

    [DockerAvailableFact]
    public async Task ConfirmedSuccessAndOutcomeUnknownStartDatabaseClockCooldown()
    {
        foreach (var succeeded in new[] { true, false })
        {
            await using var database = await ActionApprovalDatabase.CreateAsync(postgres);
            var tool = new SyntheticExternalActionTool(ActionCategory.Notification);
            var origin = await ActionApprovalTestSupport.SeedOriginAsync(
                database.ConnectionString,
                serializedConfigJson: Configuration(ActionCategory.Notification));
            using var services = Services(database.ConnectionString, tool);
            var first = await ProposeAsync(services, origin, "notification-terminal", Arguments("first"));
            using (var scope = services.CreateScope())
            {
                var dispatcher = scope.ServiceProvider.GetRequiredService<IApprovedActionDispatcher>();
                var claim = await dispatcher.TryClaimAsync(
                    first.Action!.Id, "notification-test", TimeSpan.FromSeconds(10),
                    TestContext.Current.CancellationToken);
                Assert.NotNull(claim);
                var request = new ActionTerminalRequest(
                    first.Action.Id,
                    claim!.Fence,
                    succeeded ? ActionApprovalState.Executed : ActionApprovalState.Failed,
                    Encoding.UTF8.GetBytes(succeeded
                        ? "{\"delivered\":true}"
                        : "{\"code\":\"dispatch_outcome_unknown\"}"),
                    succeeded ? "Telegram accepted the notification." : "The external action outcome is unknown.",
                    succeeded ? null : "dispatch_outcome_unknown");
                Assert.True(await scope.ServiceProvider.GetRequiredService<IActionDispatchRepository>()
                    .CompleteAsync(request, TestContext.Current.CancellationToken));
            }
            var successor = await PublishSuccessorAsync(services, database.ConnectionString, origin);

            var next = await ProposeAsync(
                services, successor, "notification-cooldown", Arguments("successor"));

            Assert.Equal(PostReportActionProposalOutcome.Denied, next.Outcome);
            Assert.Equal("notification_cooldown", next.ReasonCode);
            Assert.Equal(0, await ActionCountAsync(database.ConnectionString, successor.ReportId));
        }
    }

    [DockerAvailableFact]
    public async Task NotificationCooldownUsesDispatchStartRatherThanTerminalTime()
    {
        await using var database = await ActionApprovalDatabase.CreateAsync(postgres);
        var tool = new SyntheticExternalActionTool(ActionCategory.Notification);
        var origin = await ActionApprovalTestSupport.SeedOriginAsync(
            database.ConnectionString,
            serializedConfigJson: Configuration(ActionCategory.Notification));
        using var services = Services(database.ConnectionString, tool);
        var first = await ProposeAsync(services, origin, "notification-old-start", Arguments("first"));
        await ActionApprovalTestSupport.ExecuteAsync(
            database.ConnectionString,
            """
            INSERT INTO incidentcompass.action_approvals (
                id, tenant_id, origin_report_id, fault_id, job_id, attempt, tool_id, proposal_key,
                category, mode, logical_target_id, adapter_binding_fingerprint,
                approval_contract_version, provenance_sha256, state, canonical_payload,
                payload_sha256, approval_sha256, proposal_artifact_id, review_summary,
                created_at_utc, expires_at_utc, decision_actor, decision_at_utc,
                rejection_reason, dispatch_owner, dispatch_fence, dispatch_started_at,
                dispatch_deadline_at, result_payload, result_summary, failure_code, completed_at_utc)
            SELECT @seed_id, tenant_id, origin_report_id, fault_id, job_id, attempt, tool_id, @proposal_key,
                category, mode, logical_target_id, adapter_binding_fingerprint,
                approval_contract_version, provenance_sha256, 'executed', canonical_payload,
                payload_sha256, approval_sha256, proposal_artifact_id, review_summary,
                created_at_utc, expires_at_utc, decision_actor, decision_at_utc,
                NULL, 'seed-owner', @fence, clock_timestamp() - interval '31 minutes',
                clock_timestamp() - interval '30 minutes', @result, 'Telegram accepted the notification.',
                NULL, clock_timestamp()
            FROM incidentcompass.action_approvals
            WHERE id = @id;
            """,
            ("seed_id", Guid.NewGuid()),
            ("proposal_key", "notification-old-terminal-seed"),
            ("fence", Guid.NewGuid()),
            ("result", Encoding.UTF8.GetBytes("{\"delivered\":true}")),
            ("id", first.Action!.Id));
        var successor = await PublishSuccessorAsync(services, database.ConnectionString, origin);

        var next = await ProposeAsync(
            services, successor, "notification-after-cooldown", Arguments("successor"));

        Assert.Equal(PostReportActionProposalOutcome.Approved, next.Outcome);
        Assert.NotNull(next.Action);
    }

    [DockerAvailableFact]
    public async Task SupersededFailedMissingAndInvalidOriginsWriteNothing()
    {
        await using var database = await ActionApprovalDatabase.CreateAsync(postgres);
        var tool = new SyntheticExternalActionTool(ActionCategory.Notification);
        var origin = await ActionApprovalTestSupport.SeedOriginAsync(
            database.ConnectionString,
            serializedConfigJson: Configuration(ActionCategory.Notification));
        using var services = Services(database.ConnectionString, tool);
        var successor = await ActionApprovalTestSupport.SeedSuccessorJobAsync(
            database.ConnectionString, origin, "post-report-publisher");
        using (var scope = services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<ITriageReportRepository>().PublishAsync(
                successor.Job, "post-report-publisher", successor.Report,
                TestContext.Current.CancellationToken);
        }

        var beforeSuperseded = await LedgerCountAsync(database.ConnectionString, origin.JobId);
        var superseded = await ProposeAsync(services, origin, "superseded", Arguments("stale"));
        Assert.Equal("origin_ineligible", superseded.ReasonCode);
        Assert.False(superseded.DenialAudited);
        Assert.Equal(beforeSuperseded, await LedgerCountAsync(database.ConnectionString, origin.JobId));

        var failed = await ActionApprovalTestSupport.SeedOriginAsync(
            database.ConnectionString,
            serializedConfigJson: Configuration(ActionCategory.Notification),
            reportStatus: "Failed");
        var beforeFailed = await LedgerCountAsync(database.ConnectionString, failed.JobId);
        var failedResult = await ProposeAsync(services, failed, "failed", Arguments("failed"));
        var missing = await DispatchAsync(services, new ProposePostReportActionCommand(
            origin.TenantId, Guid.NewGuid(), "action_test", "missing", Arguments("missing")));
        var invalid = await DispatchAsync(services, new ProposePostReportActionCommand(
            origin.TenantId, origin.ReportId, new string('x', 129), "invalid", Arguments("invalid")));

        Assert.Equal("origin_ineligible", failedResult.ReasonCode);
        Assert.Equal("origin_ineligible", missing.ReasonCode);
        Assert.Equal("invalid_request", invalid.ReasonCode);
        Assert.False(failedResult.DenialAudited);
        Assert.False(missing.DenialAudited);
        Assert.False(invalid.DenialAudited);
        Assert.Equal(beforeFailed, await LedgerCountAsync(database.ConnectionString, failed.JobId));
        Assert.Equal(0, await ActionCountAsync(database.ConnectionString, failed.ReportId));
    }

    [DockerAvailableFact]
    public async Task ConcurrentPublicationAndGovernedProposalKeepCurrentOriginLinearizable()
    {
        await using var database = await ActionApprovalDatabase.CreateAsync(postgres);
        var tool = new SyntheticExternalActionTool(ActionCategory.Notification);
        var origin = await ActionApprovalTestSupport.SeedOriginAsync(
            database.ConnectionString,
            serializedConfigJson: Configuration(ActionCategory.Notification));
        using var services = Services(database.ConnectionString, tool);
        var successor = await ActionApprovalTestSupport.SeedSuccessorJobAsync(
            database.ConnectionString, origin, "governed-race-publisher");
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var scope = services.CreateScope();

        var publishTask = StartAfterAsync(start.Task, () =>
            scope.ServiceProvider.GetRequiredService<ITriageReportRepository>().PublishAsync(
                successor.Job, "governed-race-publisher", successor.Report,
                TestContext.Current.CancellationToken));
        var proposalTask = StartAfterAsync(start.Task, () =>
            ProposeAsync(services, origin, "governed-race", Arguments("race")));
        start.SetResult();
        await Task.WhenAll(publishTask, proposalTask)
            .WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        Assert.NotEqual(origin.ReportId, publishTask.Result);
        Assert.True(proposalTask.Result.Outcome is
            PostReportActionProposalOutcome.Approved or PostReportActionProposalOutcome.Denied);
        var actionCount = await ActionCountAsync(database.ConnectionString, origin.ReportId);
        Assert.Equal(
            proposalTask.Result.Outcome == PostReportActionProposalOutcome.Approved ? 1 : 0,
            actionCount);
        Assert.InRange(await DenialCountAsync(database.ConnectionString, origin.ReportId), 0, 1);
        Assert.Equal(0, tool.ExecutionCalls);
    }

    private static ServiceProvider Services(
        string connectionString,
        SyntheticExternalActionTool tool,
        IActionApprovalTransactionFaultInjector? faultInjector = null) =>
        ActionApprovalTestSupport.CreateServices(
            connectionString,
            faultInjector,
            configureServices: services =>
            {
                services.AddSingleton(new AgentToolDescriptor(
                    tool.Definition.Name, AgentToolCapability.ExternalAction,
                    tool.Category, tool.LogicalTargetId));
                services.AddSingleton<IExternalActionTool>(tool);
            });

    private static Task<PostReportActionProposalResponse> ProposeAsync(
        ServiceProvider services,
        ActionApprovalOriginFixture origin,
        string proposalKey,
        JsonElement arguments,
        string toolId = "action_test") =>
        DispatchAsync(services, new ProposePostReportActionCommand(
            origin.TenantId, origin.ReportId, toolId, proposalKey, arguments));

    private static async Task<PostReportActionProposalResponse> DispatchAsync(
        ServiceProvider services,
        ProposePostReportActionCommand command)
    {
        using var scope = services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IApplicationDispatcher>()
            .DispatchAsync<ProposePostReportActionCommand, PostReportActionProposalResponse>(
                command, TestContext.Current.CancellationToken);
    }

    private static async Task<T> StartAfterAsync<T>(
        Task start,
        Func<Task<T>> action)
    {
        await start;
        return await action();
    }

    private static async Task<ActionApprovalOriginFixture> PublishSuccessorAsync(
        ServiceProvider services,
        string connectionString,
        ActionApprovalOriginFixture origin)
    {
        var successor = await ActionApprovalTestSupport.SeedSuccessorJobAsync(
            connectionString, origin, "notification-successor-publisher");
        Guid reportId;
        using (var scope = services.CreateScope())
        {
            reportId = await scope.ServiceProvider.GetRequiredService<ITriageReportRepository>().PublishAsync(
                successor.Job, "notification-successor-publisher", successor.Report,
                TestContext.Current.CancellationToken);
        }

        return origin with { JobId = successor.Job.Id, ReportId = reportId };
    }

    private static JsonElement Arguments(string message) =>
        JsonSerializer.SerializeToElement(new { message });

    private static string Configuration(
        ActionCategory category,
        bool allowed = true,
        int rateCap = 10,
        string rateScope = "attempt",
        bool precondition = false,
        string globalMode = "live",
        string toolId = "action_test")
    {
        var rules = new JsonArray(new JsonObject
        {
            ["Type"] = "rate_cap",
            ["Tool"] = toolId,
            ["Scope"] = rateScope,
            ["Max"] = rateCap
        });
        if (precondition)
        {
            rules.Add(new JsonObject
            {
                ["Type"] = "precondition",
                ["Tool"] = toolId,
                ["Scope"] = "attempt",
                ["RequiresSuccessfulToolResult"] = toolId
            });
        }

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
                [toolId] = new JsonObject
                {
                    ["Kind"] = "external_action",
                    ["Category"] = category.ToStorageValue(),
                    ["LogicalTargetId"] = "test:target"
                }
            },
            ["Rules"] = rules,
            ["Actions"] = new JsonObject
            {
                ["AllowedTools"] = allowed ? new JsonArray(toolId) : new JsonArray(),
                ["DefaultMode"] = globalMode,
                ["RequireApprovalForAll"] = false,
                ["ApprovalTtlMinutes"] = 60,
                ["NotificationRoutes"] = category == ActionCategory.Notification && allowed
                    ? new JsonArray(new JsonObject
                    {
                        ["RouteId"] = "test-route",
                        ["ToolId"] = toolId,
                        ["Severities"] = new JsonArray("error", "critical", "fatal")
                    })
                    : new JsonArray()
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

    private static Task<long> ActionCountAsync(string connectionString, Guid reportId) =>
        ActionApprovalTestSupport.CountAsync(
            connectionString,
            "SELECT count(*) FROM incidentcompass.action_approvals WHERE origin_report_id = @report;",
            ("report", reportId));

    private static Task<long> DenialCountAsync(string connectionString, Guid reportId) =>
        ActionApprovalTestSupport.CountAsync(
            connectionString,
            """
            SELECT count(*) FROM incidentcompass.triage_ledger
            WHERE event_type = 'PolicyDecision' AND decision = 'Denied'
              AND payload_ref = @ref;
            """, ("ref", "report:" + reportId));

    private static Task<long> DenialToolCountAsync(string connectionString, string toolId) =>
        ActionApprovalTestSupport.CountAsync(
            connectionString,
            """
            SELECT count(*) FROM incidentcompass.triage_ledger
            WHERE event_type = 'PolicyDecision' AND decision = 'Denied'
              AND tool_name = @tool_id;
            """, ("tool_id", toolId));

    private static async Task WaitForFaultLockWaiterAsync(string connectionString)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(5);
        while (DateTimeOffset.UtcNow < deadline)
        {
            var waiters = await ActionApprovalTestSupport.CountAsync(
                connectionString,
                """
                SELECT count(*) FROM pg_stat_activity
                WHERE datname = current_database()
                  AND wait_event_type = 'Lock'
                  AND query LIKE '%FROM incidentcompass.faults%';
                """);
            if (waiters > 0)
            {
                return;
            }

            await Task.Delay(25, TestContext.Current.CancellationToken);
        }

        throw new TimeoutException("The governed proposal did not reach the origin fault lock.");
    }

    private static Task<long> LedgerCountAsync(string connectionString, Guid jobId) =>
        ActionApprovalTestSupport.CountAsync(
            connectionString,
            "SELECT count(*) FROM incidentcompass.triage_ledger WHERE job_id = @job;",
            ("job", jobId));

    private sealed class SyntheticExternalActionTool(
        ActionCategory category,
        bool oversized = false,
        string toolId = "action_test") : IExternalActionTool
    {
        public int ExecutionCalls { get; private set; }

        public ActionCategory Category { get; } = category;
        public string LogicalTargetId => "test:target";
        public string AdapterBindingFingerprint { get; } = ExternalActionBinding.ComputeFingerprint(
            "synthetic", "test:target", "https://api.example.test", "resource-1");
        public IncidentCompass.Application.Core.ModelClients.AiToolDefinition Definition { get; } = new(
            toolId, "Synthetic post-report action.", "v1",
            CanonicalJsonSerializer.ToElement(new JsonObject
            {
                ["type"] = "object",
                ["additionalProperties"] = false,
                ["properties"] = new JsonObject { ["message"] = new JsonObject { ["type"] = "string" } },
                ["required"] = new JsonArray("message")
            }));

        public ToolValidationResult Validate(JsonElement arguments)
        {
            if (arguments.ValueKind != JsonValueKind.Object ||
                arguments.EnumerateObject().Any(property => property.Name != "message") ||
                !arguments.TryGetProperty("message", out var message) ||
                message.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(message.GetString()))
            {
                return ToolValidationResult.Invalid("invalid_arguments", "A message is required.");
            }

            return ToolValidationResult.Valid(JsonSerializer.SerializeToElement(new
            {
                message = message.GetString()!.Trim()
            }));
        }

        public ExternalActionPreparation Prepare(JsonElement sanitizedArguments)
        {
            if (oversized)
            {
                return new ExternalActionPreparation(
                    Encoding.UTF8.GetBytes("{\"message\":\"" + new string('x', 70_000) + "\"}"),
                    "Oversized synthetic proposal.");
            }

            var payload = new JsonObject
            {
                ["message"] = sanitizedArguments.GetProperty("message").GetString()
            };
            return new ExternalActionPreparation(
                Encoding.UTF8.GetBytes(CanonicalJsonSerializer.Canonicalize(payload)),
                "Send a synthetic bounded notification.");
        }

        public Task<ExternalActionExecutionResult> ExecuteAsync(
            Guid actionId,
            ReadOnlyMemory<byte> canonicalPayload,
            CancellationToken cancellationToken)
        {
            ExecutionCalls++;
            throw new InvalidOperationException("Proposal creation invoked the external adapter.");
        }
    }
}
