using System.Text;
using System.Text.Json.Nodes;
using IncidentCompass.Application.Core.Serialization;
using IncidentCompass.Application.Governance.Tools;
using IncidentCompass.Domain.Incidents.Actions;

namespace IncidentCompass.Application.Governance.ActionApprovals;

internal static class ActionDispatchTerminalFactory
{
    public static ActionTerminalRequest DryRun(ActionDispatchClaim claim) =>
        CreateInternal(claim, ActionApprovalState.Executed, "dry_run_simulated",
            "Action simulated in dry-run mode.", null, true);

    public static ActionTerminalRequest Failure(ActionDispatchClaim claim, string code, string summary) =>
        CreateInternal(claim, ActionApprovalState.Failed, code, summary, code, false);

    public static ActionTerminalRequest FromAdapter(
        ActionDispatchClaim claim,
        ExternalActionExecutionResult result)
    {
        var request = new ActionTerminalRequest(
            claim.Action.Id,
            claim.Fence,
            result.Succeeded ? ActionApprovalState.Executed : ActionApprovalState.Failed,
            result.CanonicalResult,
            result.Summary,
            result.Succeeded ? null : result.FailureCode,
            result.AuditProjection);
        ActionTerminalValidator.ValidateForAction(claim.Action, request);
        return request;
    }

    private static ActionTerminalRequest CreateInternal(
        ActionDispatchClaim claim,
        ActionApprovalState state,
        string code,
        string summary,
        string? failureCode,
        bool simulated)
    {
        var payload = new JsonObject
        {
            ["code"] = code,
            ["simulated"] = simulated
        };
        return new ActionTerminalRequest(
            claim.Action.Id,
            claim.Fence,
            state,
            Encoding.UTF8.GetBytes(CanonicalJsonSerializer.Canonicalize(payload)),
            summary,
            failureCode);
    }
}
