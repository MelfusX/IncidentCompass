using System.Text;
using IncidentCompass.Application.Governance.ActionApprovals;
using IncidentCompass.Application.Tickets;
using IncidentCompass.Domain.Incidents.Actions;
using Microsoft.Extensions.DependencyInjection;

namespace IncidentCompass.IntegrationTests;

[Collection(PostgresRepositoryCollection.CollectionName)]
public sealed class GitHubIssueActionHistoryTests(PostgresRepositoryFixture postgres)
{
    [DockerAvailableFact]
    public async Task HistoryReturnsConfirmedPendingAndUnknownStatesAndFailsClosedAtBound()
    {
        await using var database = await ActionApprovalDatabase.CreateAsync(postgres);
        var clock = new ManualTimeProvider(DateTimeOffset.UtcNow);
        using var services = ActionApprovalTestSupport.CreateServices(
            database.ConnectionString, timeProvider: clock);

        var confirmedOrigin = await ActionApprovalTestSupport.SeedOriginAsync(database.ConnectionString);
        var confirmedPrior = await CreateAsync(services, confirmedOrigin, "confirmed-prior");
        await CompleteAsync(database.ConnectionString, confirmedPrior.Id, true);
        clock.Advance(TimeSpan.FromSeconds(1));
        var confirmedCurrent = await CreateAsync(services, confirmedOrigin, "confirmed-current");
        var confirmed = await ReadAsync(services, confirmedCurrent.Id);

        Assert.Equal(
            Encoding.UTF8.GetBytes("{\"issueNumber\":\"42\",\"provider\":\"github\"}"),
            confirmed.ConfirmedCanonicalResult);
        Assert.False(confirmed.HasPendingAction);

        var changedBindingOrigin = await ActionApprovalTestSupport.SeedOriginAsync(database.ConnectionString);
        var changedBindingPrior = await CreateAsync(
            services, changedBindingOrigin, "changed-binding-prior", binding: new string('b', 64));
        await CompleteAsync(database.ConnectionString, changedBindingPrior.Id, true);
        clock.Advance(TimeSpan.FromSeconds(1));
        var changedBindingCurrent = await CreateAsync(
            services, changedBindingOrigin, "changed-binding-current", binding: new string('c', 64));
        var changedBinding = await ReadAsync(services, changedBindingCurrent.Id);

        Assert.Null(changedBinding.ConfirmedCanonicalResult);

        var pendingOrigin = await ActionApprovalTestSupport.SeedOriginAsync(database.ConnectionString);
        await CreateAsync(services, pendingOrigin, "pending-prior");
        clock.Advance(TimeSpan.FromSeconds(1));
        var pendingCurrent = await CreateAsync(services, pendingOrigin, "pending-current");
        var pending = await ReadAsync(services, pendingCurrent.Id);

        Assert.True(pending.HasPendingAction);
        Assert.Null(pending.ConfirmedCanonicalResult);

        var unknownOrigin = await ActionApprovalTestSupport.SeedOriginAsync(database.ConnectionString);
        var unknownPrior = await CreateAsync(services, unknownOrigin, "unknown-prior", new string('b', 64));
        await CompleteAsync(database.ConnectionString, unknownPrior.Id, false);
        clock.Advance(TimeSpan.FromSeconds(1));
        var unknownCurrent = await CreateAsync(services, unknownOrigin, "unknown-current");
        var unknown = await ReadAsync(services, unknownCurrent.Id);

        Assert.Equal(new string('b', 64), Assert.Single(unknown.OutcomeUnknownMarkers));

        var unreadableOrigin = await ActionApprovalTestSupport.SeedOriginAsync(database.ConnectionString);
        var unreadablePrior = await CreateAsync(services, unreadableOrigin, "unreadable-prior");
        await CompleteAsync(database.ConnectionString, unreadablePrior.Id, false);
        clock.Advance(TimeSpan.FromSeconds(1));
        var unreadableCurrent = await CreateAsync(services, unreadableOrigin, "unreadable-current");
        var unreadable = await ReadAsync(services, unreadableCurrent.Id);

        Assert.True(unreadable.HasUnsafeHistory);

        var boundedOrigin = await ActionApprovalTestSupport.SeedOriginAsync(database.ConnectionString);
        ActionApprovalRecord? latest = null;
        for (var index = 0; index < 18; index++)
        {
            clock.Advance(TimeSpan.FromSeconds(1));
            latest = await CreateAsync(services, boundedOrigin, $"bounded-{index}");
        }

        var bounded = await ReadAsync(services, latest!.Id);
        Assert.True(bounded.HistoryLimitExceeded);
    }

    private static async Task<ActionApprovalRecord> CreateAsync(
        ServiceProvider services,
        ActionApprovalOriginFixture origin,
        string proposalKey,
        string? marker = null,
        string? binding = null)
    {
        var proposal = ActionApprovalTestSupport.Proposal(origin, proposalKey);
        if (binding is not null)
        {
            proposal = proposal with { AdapterBindingFingerprint = binding };
        }

        if (marker is not null)
        {
            proposal = proposal with
            {
                CanonicalPayload = Encoding.UTF8.GetBytes($"{{\"marker\":\"{marker}\"}}")
            };
        }

        using var scope = services.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<IActionProposalRepository>()
            .CreateAsync(proposal, TestContext.Current.CancellationToken);
        return result.Action;
    }

    private static async Task<TicketActionHistorySnapshot> ReadAsync(
        ServiceProvider services,
        Guid actionId)
    {
        using var scope = services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ITicketActionHistory>()
            .ReadPriorAsync(actionId, TestContext.Current.CancellationToken);
    }

    private static async Task CompleteAsync(string connectionString, Guid actionId, bool succeeded)
    {
        var fence = Guid.NewGuid();
        await ActionApprovalTestSupport.ExecuteAsync(connectionString, """
            UPDATE incidentcompass.action_approvals
            SET state = 'approved', decision_actor = 'user:test',
                decision_at_utc = clock_timestamp()
            WHERE id = @id;

            UPDATE incidentcompass.action_approvals
            SET dispatch_owner = 'history-test', dispatch_fence = @fence,
                dispatch_started_at = clock_timestamp(),
                dispatch_deadline_at = clock_timestamp() + interval '1 minute'
            WHERE id = @id;

            UPDATE incidentcompass.action_approvals
            SET state = @state, result_payload = @result_payload,
                result_summary = @result_summary, failure_code = @failure_code,
                completed_at_utc = clock_timestamp()
            WHERE id = @id;
            """,
            ("id", actionId),
            ("fence", fence),
            ("state", succeeded ? "executed" : "failed"),
            ("result_payload", Encoding.UTF8.GetBytes(succeeded
                ? "{\"issueNumber\":\"42\",\"provider\":\"github\"}"
                : "{\"code\":\"dispatch_outcome_unknown\",\"provider\":\"github\"}")),
            ("result_summary", succeeded ? "GitHub accepted the issue." : "Creation was not confirmed."),
            ("failure_code", succeeded ? DBNull.Value : "dispatch_outcome_unknown"));
    }
}
