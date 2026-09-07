using System.Text;
using IncidentCompass.Application.Governance.ActionApprovals;
using IncidentCompass.Application.Governance.ActionApprovals.Propose;
using IncidentCompass.Application.Investigation.Reports;
using IncidentCompass.Domain.Incidents.Actions;
using Microsoft.Extensions.DependencyInjection;
using static IncidentCompass.IntegrationTests.PostReportActionProposalTestSupport;

namespace IncidentCompass.IntegrationTests;

[Collection(PostgresRepositoryCollection.CollectionName)]
public sealed class PostReportActionSupersessionTests(PostgresRepositoryFixture postgres)
{
    [DockerAvailableFact]
    public async Task NewNotificationSupersedesUnclaimedPriorActionInsideFaultLock()
    {
        await using var database = await ActionApprovalDatabase.CreateAsync(postgres);
        var tool = new ProposalOnlyExternalActionTool(ActionCategory.Notification);
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
        var tool = new ProposalOnlyExternalActionTool(ActionCategory.Notification);
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
            var tool = new ProposalOnlyExternalActionTool(ActionCategory.Notification);
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
        var tool = new ProposalOnlyExternalActionTool(ActionCategory.Notification);
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
        var tool = new ProposalOnlyExternalActionTool(ActionCategory.Notification);
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
        var tool = new ProposalOnlyExternalActionTool(ActionCategory.Notification);
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
}
