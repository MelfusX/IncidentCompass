using System.Text;
using IncidentCompass.Application.Core.Dispatching;
using IncidentCompass.Application.Core.Serialization;
using IncidentCompass.Application.Governance.ActionApprovals.Propose;
using IncidentCompass.Application.Governance.PostReportActions;
using IncidentCompass.Application.Intake.Configuration;
using IncidentCompass.Application.Tickets;
using Microsoft.Extensions.DependencyInjection;

namespace IncidentCompass.UnitTests;

public sealed class TicketUpdatePostReportActionWorkflowTests
{
    [Fact]
    public async Task Evaluate_UsesOnlyResolvedTicketIdentityAndGovernedProposalPath()
    {
        var configuration = Configuration();
        var dispatcher = new RecordingDispatcher();
        using var services = new ServiceCollection()
            .AddSingleton<ITicketUpdateEvidenceResolver>(
                new StaticEvidenceResolver(new TicketUpdateEvidence(Guid.NewGuid(), "42")))
            .AddSingleton<IApplicationDispatcher>(dispatcher)
            .BuildServiceProvider();
        var workflow = new TicketUpdatePostReportActionWorkflow(
            new StaticConfigurationRepository(configuration),
            services.GetRequiredService<IServiceScopeFactory>());
        var intent = Intent(configuration.ConfigHash);

        var selected = await workflow.SelectAsync(
            intent.TenantId, intent.OriginReportId, intent.FaultId, intent.JobId, intent.Attempt,
            intent.ConfigHash, "orders", "test", "error", TestContext.Current.CancellationToken);
        var result = await workflow.EvaluateAsync(intent, TestContext.Current.CancellationToken);

        Assert.True(selected.ShouldEnqueue);
        Assert.Null(selected.RouteId);
        Assert.Equal("requested", result.Code);
        var command = Assert.IsType<ProposePostReportActionCommand>(dispatcher.Request);
        Assert.Equal(TicketUpdatePostReportActionWorkflow.UpdateToolId, command.ToolId);
        Assert.Equal("42", command.Arguments.GetProperty("ticketId").GetString());
        Assert.False(command.Arguments.TryGetProperty("repository", out _));
        Assert.False(command.Arguments.TryGetProperty("owner", out _));
    }

    [Fact]
    public async Task Evaluate_MissingOrInvalidTargetFailsClosedWithoutProposal()
    {
        var configuration = Configuration();
        var dispatcher = new RecordingDispatcher();
        using var services = new ServiceCollection()
            .AddSingleton<ITicketUpdateEvidenceResolver>(new StaticEvidenceResolver(null))
            .AddSingleton<IApplicationDispatcher>(dispatcher)
            .BuildServiceProvider();
        var workflow = new TicketUpdatePostReportActionWorkflow(
            new StaticConfigurationRepository(configuration),
            services.GetRequiredService<IServiceScopeFactory>());

        var missing = await workflow.EvaluateAsync(
            Intent(configuration.ConfigHash), TestContext.Current.CancellationToken);
        var invalid = await workflow.EvaluateAsync(
            Intent(configuration.ConfigHash) with { WorkflowInput = Encoding.UTF8.GetBytes("{}") },
            TestContext.Current.CancellationToken);

        Assert.Equal("ticket_update_target_required", missing.Code);
        Assert.Equal("ticket_update_intent_invalid", invalid.Code);
        Assert.Null(dispatcher.Request);
    }

    private static TriageConfiguration Configuration()
    {
        var baseConfiguration = TestTriageConfiguration.Create("ticket-update-config");
        var tools = baseConfiguration.Tools.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        tools[TicketUpdatePostReportActionWorkflow.UpdateToolId] = new TriageToolSettings(
            "external_action", null, null, null, "ticket_update",
            TicketUpdatePostReportActionWorkflow.UpdateLogicalTargetId);
        return baseConfiguration with
        {
            Tools = tools,
            Actions = new TriageActionSettings(
                [TicketUpdatePostReportActionWorkflow.UpdateToolId], "live", false, 60)
        };
    }

    private static PostReportActionIntent Intent(string configHash)
    {
        var reportId = Guid.NewGuid();
        var toolId = TicketUpdatePostReportActionWorkflow.UpdateToolId;
        var input = Encoding.UTF8.GetBytes(CanonicalJsonSerializer.Canonicalize(
            System.Text.Json.Nodes.JsonNode.Parse(
                $"{{\"originReportId\":\"{reportId:N}\",\"toolId\":\"{toolId}\",\"workflowVersion\":1}}")));
        return new PostReportActionIntent(
            Guid.NewGuid(), "tenant", reportId, Guid.NewGuid(), Guid.NewGuid(), 1,
            toolId, 1, null, configHash, $"post-report:v1:{reportId:N}:{toolId}", input,
            PostReportActionIntentState.Processing, "worker", Guid.NewGuid(),
            DateTimeOffset.UtcNow.AddMinutes(1), 1, null, null, DateTimeOffset.UtcNow, null);
    }

    private sealed class StaticEvidenceResolver(TicketUpdateEvidence? evidence)
        : ITicketUpdateEvidenceResolver
    {
        public Task<TicketUpdateEvidence?> ResolveAsync(
            string tenantId,
            Guid originReportId,
            CancellationToken cancellationToken) => Task.FromResult(evidence);
    }

    private sealed class StaticConfigurationRepository(TriageConfiguration configuration)
        : ITriageConfigurationRepository
    {
        public Task<TriageConfiguration> GetCurrentAsync(CancellationToken cancellationToken) =>
            Task.FromResult(configuration);

        public Task<TriageConfiguration> GetByHashAsync(
            string configHash,
            CancellationToken cancellationToken) => Task.FromResult(configuration);
    }

    private sealed class RecordingDispatcher : IApplicationDispatcher
    {
        public object? Request { get; private set; }

        public Task<TResponse> DispatchAsync<TRequest, TResponse>(
            TRequest request,
            CancellationToken cancellationToken = default)
            where TRequest : IRequest<TResponse>
        {
            Request = request;
            object response = new PostReportActionProposalResponse(
                PostReportActionProposalOutcome.Requested,
                "approval_required",
                null,
                false,
                false);
            return Task.FromResult((TResponse)response);
        }
    }
}
