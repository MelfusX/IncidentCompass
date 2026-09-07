using System.Collections.Concurrent;
using System.Text.Json;
using IncidentCompass.Application.Core.Dispatching;
using IncidentCompass.Application.Governance.ActionApprovals.Propose;
using IncidentCompass.Application.Governance.PostReportActions;
using IncidentCompass.Domain.Incidents.Actions;
using Microsoft.Extensions.DependencyInjection;

namespace IncidentCompass.IntegrationTests;

internal sealed class DeterministicPostReportActionWorkflow(
    IServiceScopeFactory scopeFactory,
    Func<bool>? shouldEnqueue = null,
    Func<int, CancellationToken, Task>? beforeProposal = null,
    Func<int, PostReportActionProposalResponse, CancellationToken, Task>? afterProposal = null,
    string toolId = "action_test",
    Func<int, PostReportActionWorkflowResult?>? resultOverride = null) : IPostReportActionWorkflow
{
    private readonly ConcurrentQueue<PostReportActionProposalResponse> responses = new();
    private int calls;

    public string ToolId { get; } = toolId;

    public int WorkflowVersion => 1;

    public ActionCategory Category => ActionCategory.Notification;

    public string LogicalTargetId => "test:target";

    public int Calls => Volatile.Read(ref calls);

    public IReadOnlyList<PostReportActionProposalResponse> Responses => responses.ToArray();

    public Task<(bool ShouldEnqueue, string? RouteId)> SelectAsync(
        string tenantId,
        Guid originReportId,
        Guid faultId,
        Guid jobId,
        int attempt,
        string configHash,
        string serviceName,
        string environment,
        string? severity,
        CancellationToken cancellationToken) =>
        Task.FromResult(((shouldEnqueue?.Invoke() ?? true), (string?)"test-route"));

    public async Task<PostReportActionWorkflowResult> EvaluateAsync(
        PostReportActionIntent intent,
        CancellationToken cancellationToken)
    {
        var call = Interlocked.Increment(ref calls);
        if (beforeProposal is not null)
        {
            await beforeProposal(call, cancellationToken);
        }

        var overriddenResult = resultOverride?.Invoke(call);
        if (overriddenResult is not null)
        {
            return overriddenResult;
        }

        using var scope = scopeFactory.CreateScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IApplicationDispatcher>();
        var response = await dispatcher.DispatchAsync<
            ProposePostReportActionCommand,
            PostReportActionProposalResponse>(
            new ProposePostReportActionCommand(
                intent.TenantId,
                intent.OriginReportId,
                intent.ToolId,
                intent.ProposalKey,
                JsonSerializer.SerializeToElement(new { message = "deterministic evaluation" })),
            cancellationToken);
        responses.Enqueue(response);
        if (afterProposal is not null)
        {
            await afterProposal(call, response, cancellationToken);
        }

        return PostReportActionWorkflowResult.Completed(response.Outcome switch
        {
            PostReportActionProposalOutcome.Approved => "approved",
            PostReportActionProposalOutcome.Requested => "requested",
            _ => response.ReasonCode
        });
    }
}
