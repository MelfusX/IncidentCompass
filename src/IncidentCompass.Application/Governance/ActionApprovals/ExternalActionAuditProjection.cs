using System.Text;
using IncidentCompass.Domain.Incidents.Actions;

namespace IncidentCompass.Application.Governance.ActionApprovals;

public sealed record ExternalActionAuditProjection(
    string ResourceKind,
    string ResourceId,
    string BeforeState,
    string AfterState)
{
    public const string TelegramMessageKind = "telegram_message";
    public const string GitHubIssueKind = "github_issue";
    public const int MaximumStateBytes = 2048;

    public static ExternalActionAuditProjection TelegramMessage(string messageId) =>
        new(TelegramMessageKind, messageId, "not_sent", "sent");

    public static ExternalActionAuditProjection GitHubIssueCreated(string issueNumber) =>
        new(GitHubIssueKind, issueNumber, "absent", "open");

    public static ExternalActionAuditProjection GitHubIssueCommentAdded(string issueNumber) =>
        new(GitHubIssueKind, issueNumber, "open", "comment_added");

    public void Validate()
    {
        if (!IsValidResourceIdentity(ResourceKind, ResourceId) ||
            Encoding.UTF8.GetByteCount(BeforeState) is < 1 or > MaximumStateBytes ||
            Encoding.UTF8.GetByteCount(AfterState) is < 1 or > MaximumStateBytes ||
            !HasClosedStateTransition())
        {
            throw new ActionProposalValidationException(
                "External action audit projection is invalid or exceeds its bound.");
        }
    }

    public static bool IsValidResourceIdentity(string? resourceKind, string? resourceId) =>
        resourceKind is TelegramMessageKind or GitHubIssueKind &&
        resourceId is { Length: >= 1 and <= 20 } &&
        resourceId[0] is >= '1' and <= '9' &&
        resourceId.All(static character => character is >= '0' and <= '9');

    internal bool Matches(ActionCategory category) => category switch
    {
        ActionCategory.Notification =>
            ResourceKind == TelegramMessageKind && BeforeState == "not_sent" && AfterState == "sent",
        ActionCategory.TicketCreate =>
            ResourceKind == GitHubIssueKind && BeforeState == "absent" && AfterState == "open",
        ActionCategory.TicketUpdate =>
            ResourceKind == GitHubIssueKind && BeforeState == "open" && AfterState == "comment_added",
        _ => false
    };

    private bool HasClosedStateTransition() =>
        ResourceKind == TelegramMessageKind && BeforeState == "not_sent" && AfterState == "sent" ||
        ResourceKind == GitHubIssueKind && BeforeState == "absent" && AfterState == "open" ||
        ResourceKind == GitHubIssueKind && BeforeState == "open" && AfterState == "comment_added";
}
