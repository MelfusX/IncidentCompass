using System.Security.Cryptography;
using System.Text;
using IncidentCompass.Application.Governance.Tools;
using IncidentCompass.Application.Intake.Configuration;

namespace IncidentCompass.Application.Governance.ActionApprovals;

internal sealed class ApprovedActionDispatcher(
    IActionDispatchRepository repository,
    IExternalActionToolRegistry externalTools,
    IAgentToolRegistry toolRegistry,
    ITriageConfigurationRepository configurationRepository) : IApprovedActionDispatcher
{
    private static readonly TimeSpan RecoveryGrace = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan TerminalCommitBudget = TimeSpan.FromSeconds(5);

    public async Task SweepAsync(int batchSize, CancellationToken cancellationToken)
    {
        foreach (var candidate in await repository.FindExpiryCandidatesAsync(batchSize, cancellationToken))
        {
            await repository.TryExpireAsync(candidate.ActionId, cancellationToken);
        }

        foreach (var candidate in await repository.FindSupersededCandidatesAsync(batchSize, cancellationToken))
        {
            await repository.TryFailSupersededAsync(candidate.ActionId, cancellationToken);
        }

        foreach (var candidate in await repository.FindRecoveryCandidatesAsync(batchSize, cancellationToken))
        {
            await repository.TryFailOutcomeUnknownAsync(candidate.ActionId, cancellationToken);
        }
    }

    public Task<IReadOnlyList<ActionDispatchCandidate>> FindCandidatesAsync(
        int limit,
        CancellationToken cancellationToken) =>
        repository.FindCandidatesAsync(limit, cancellationToken);

    public Task<ActionDispatchClaim?> TryClaimAsync(
        Guid actionId,
        string dispatchOwner,
        TimeSpan adapterTimeout,
        CancellationToken cancellationToken) =>
        repository.TryClaimAsync(
            actionId,
            dispatchOwner,
            adapterTimeout + RecoveryGrace,
            cancellationToken);

    public async Task DispatchAsync(
        ActionDispatchClaim claim,
        TimeSpan adapterTimeout,
        CancellationToken cancellationToken)
    {
        if (!externalTools.TryGet(claim.Action.ToolId, out var tool) ||
            !toolRegistry.TryGet(claim.Action.ToolId, out var descriptor))
        {
            await CompleteAsync(ActionDispatchTerminalFactory.Failure(
                claim, "action_tool_unavailable", "External action tool is unavailable."));
            return;
        }

        TriageConfiguration configuration;
        try
        {
            configuration = await configurationRepository.GetCurrentAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await CompleteAsync(ActionDispatchTerminalFactory.Failure(
                claim, "dispatch_outcome_unknown", "Dispatch was cancelled before the adapter outcome was known."));
            return;
        }
        catch (Exception)
        {
            await CompleteAsync(ActionDispatchTerminalFactory.Failure(
                claim, "action_configuration_unavailable", "Current action policy is unavailable."));
            return;
        }

        var guard = ActionDispatchGuard.Evaluate(claim.Action, descriptor, configuration);
        if (guard.FailureCode is not null)
        {
            await CompleteAsync(ActionDispatchTerminalFactory.Failure(
                claim, guard.FailureCode, "Current action policy rejected dispatch."));
            return;
        }

        if (guard.Simulate)
        {
            await CompleteAsync(ActionDispatchTerminalFactory.DryRun(claim));
            return;
        }

        if (!BindingMatches(claim.Action.AdapterBindingFingerprint, tool.AdapterBindingFingerprint))
        {
            await CompleteAsync(ActionDispatchTerminalFactory.Failure(
                claim, "adapter_binding_changed", "External action adapter binding changed."));
            return;
        }

        ActionTerminalRequest terminal;
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(adapterTimeout);
            var result = await tool.ExecuteAsync(
                claim.Action.Id,
                claim.Action.CanonicalPayload,
                deadline.Token);
            terminal = ActionDispatchTerminalFactory.FromAdapter(claim, result);
        }
        catch (Exception exception) when (exception is not StackOverflowException and not OutOfMemoryException)
        {
            terminal = ActionDispatchTerminalFactory.Failure(
                claim,
                "dispatch_outcome_unknown",
                "The external action outcome is unknown.");
        }

        await CompleteAsync(terminal);
    }

    private async Task CompleteAsync(ActionTerminalRequest terminal)
    {
        using var commitDeadline = new CancellationTokenSource(TerminalCommitBudget);
        await repository.CompleteAsync(terminal, commitDeadline.Token);
    }

    private static bool BindingMatches(string frozen, string current)
    {
        if (!ActionProposalValidator.IsLowerHexSha256(frozen) ||
            !ActionProposalValidator.IsLowerHexSha256(current))
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(frozen),
            Encoding.ASCII.GetBytes(current));
    }
}
