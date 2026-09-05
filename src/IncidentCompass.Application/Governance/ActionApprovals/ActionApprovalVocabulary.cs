using IncidentCompass.Domain.Incidents.Actions;

namespace IncidentCompass.Application.Governance.ActionApprovals;

public static class ActionApprovalVocabulary
{
    public static string ToStorageValue(this ActionCategory value) => value switch
    {
        ActionCategory.Notification => "notification",
        ActionCategory.TicketCreate => "ticket_create",
        ActionCategory.TicketUpdate => "ticket_update",
        ActionCategory.CodeWrite => "code_write",
        ActionCategory.BranchPush => "branch_push",
        ActionCategory.PrCreate => "pr_create",
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };

    public static string ToStorageValue(this ActionExecutionMode value) => value switch
    {
        ActionExecutionMode.Live => "live",
        ActionExecutionMode.DryRun => "dry_run",
        ActionExecutionMode.Disabled => "disabled",
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };

    public static string ToStorageValue(this ActionApprovalState value) => value.ToString().ToLowerInvariant();

    public static string ToStorageValue(this ActionProvenanceTrust value) => value switch
    {
        ActionProvenanceTrust.UntrustedSignal => "untrusted_signal",
        ActionProvenanceTrust.UntrustedPrior => "untrusted_prior",
        ActionProvenanceTrust.UntrustedRetrieved => "untrusted_retrieved",
        ActionProvenanceTrust.BackendFact => "backend_fact",
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };

    public static ActionApprovalState ParseState(string value)
    {
        if (Enum.TryParse<ActionApprovalState>(value, true, out var state) && state.ToStorageValue() == value)
        {
            return state;
        }

        throw new InvalidOperationException("Unknown durable action state.");
    }

    public static ActionCategory ParseCategory(string value) => value switch
    {
        "notification" => ActionCategory.Notification,
        "ticket_create" => ActionCategory.TicketCreate,
        "ticket_update" => ActionCategory.TicketUpdate,
        "code_write" => ActionCategory.CodeWrite,
        "branch_push" => ActionCategory.BranchPush,
        "pr_create" => ActionCategory.PrCreate,
        _ => throw new InvalidOperationException("Unknown durable action category.")
    };

    public static ActionExecutionMode ParseMode(string value) => value switch
    {
        "live" => ActionExecutionMode.Live,
        "dry_run" => ActionExecutionMode.DryRun,
        "disabled" => ActionExecutionMode.Disabled,
        _ => throw new InvalidOperationException("Unknown durable action mode.")
    };

    public static ActionProvenanceTrust ParseTrust(string value) => value switch
    {
        "untrusted_signal" => ActionProvenanceTrust.UntrustedSignal,
        "untrusted_prior" => ActionProvenanceTrust.UntrustedPrior,
        "untrusted_retrieved" => ActionProvenanceTrust.UntrustedRetrieved,
        "backend_fact" => ActionProvenanceTrust.BackendFact,
        _ => throw new InvalidOperationException("Unknown durable action trust class.")
    };
}
