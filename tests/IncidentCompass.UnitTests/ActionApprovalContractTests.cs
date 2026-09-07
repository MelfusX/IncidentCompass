using System.Text;
using IncidentCompass.Application.Governance.ActionApprovals;
using IncidentCompass.Domain.Incidents.Actions;

namespace IncidentCompass.UnitTests;

public sealed class ActionApprovalContractTests
{
    private static readonly Guid ReportId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly byte[] Payload = Encoding.UTF8.GetBytes("{\"message\":\"ok\"}");

    [Fact]
    public void ApprovalHash_ChangesForEveryImmutableTupleField()
    {
        var payloadHash = ActionApprovalContractV1.ComputePayloadSha256(Payload);
        var provenanceHash = ActionApprovalContractV1.ComputeProvenanceSha256(
            ReportId,
            [new(Guid.Parse("22222222-2222-2222-2222-222222222222"), ActionProvenanceTrust.UntrustedSignal)]);
        var baseline = Hash(ReportId, "ticket_create", ActionCategory.TicketCreate, ActionExecutionMode.Live,
            "github:owner/repo", new string('a', 64), payloadHash, Payload, provenanceHash);
        var variants = new[]
        {
            Hash(Guid.NewGuid(), "ticket_create", ActionCategory.TicketCreate, ActionExecutionMode.Live,
                "github:owner/repo", new string('a', 64), payloadHash, Payload, provenanceHash),
            Hash(ReportId, "ticket_update", ActionCategory.TicketCreate, ActionExecutionMode.Live,
                "github:owner/repo", new string('a', 64), payloadHash, Payload, provenanceHash),
            Hash(ReportId, "ticket_create", ActionCategory.TicketUpdate, ActionExecutionMode.Live,
                "github:owner/repo", new string('a', 64), payloadHash, Payload, provenanceHash),
            Hash(ReportId, "ticket_create", ActionCategory.TicketCreate, ActionExecutionMode.DryRun,
                "github:owner/repo", new string('a', 64), payloadHash, Payload, provenanceHash),
            Hash(ReportId, "ticket_create", ActionCategory.TicketCreate, ActionExecutionMode.Live,
                "github:other/repo", new string('a', 64), payloadHash, Payload, provenanceHash),
            Hash(ReportId, "ticket_create", ActionCategory.TicketCreate, ActionExecutionMode.Live,
                "github:owner/repo", new string('b', 64), payloadHash, Payload, provenanceHash),
            Hash(ReportId, "ticket_create", ActionCategory.TicketCreate, ActionExecutionMode.Live,
                "github:owner/repo", new string('a', 64), new string('c', 64), Payload, provenanceHash),
            Hash(ReportId, "ticket_create", ActionCategory.TicketCreate, ActionExecutionMode.Live,
                "github:owner/repo", new string('a', 64), payloadHash,
                Encoding.UTF8.GetBytes("{\"message\":\"changed\"}"), provenanceHash),
            Hash(ReportId, "ticket_create", ActionCategory.TicketCreate, ActionExecutionMode.Live,
                "github:owner/repo", new string('a', 64), payloadHash, Payload, new string('d', 64))
        };

        Assert.Matches("^[0-9a-f]{64}$", baseline);
        Assert.All(variants, variant => Assert.NotEqual(baseline, variant));
    }

    [Fact]
    public void ProvenanceHash_IsSortedAndTrustSensitive()
    {
        var first = new ActionProvenanceIdentity(Guid.Parse("33333333-3333-3333-3333-333333333333"), ActionProvenanceTrust.UntrustedRetrieved);
        var second = new ActionProvenanceIdentity(Guid.Parse("22222222-2222-2222-2222-222222222222"), ActionProvenanceTrust.BackendFact);

        var forward = ActionApprovalContractV1.ComputeProvenanceSha256(ReportId, [first, second]);
        var reverse = ActionApprovalContractV1.ComputeProvenanceSha256(ReportId, [second, first]);
        var changed = ActionApprovalContractV1.ComputeProvenanceSha256(
            ReportId,
            [first, second with { TrustClass = ActionProvenanceTrust.UntrustedSignal }]);

        Assert.Equal(forward, reverse);
        Assert.NotEqual(forward, changed);
    }

    [Fact]
    public void ProposalValidation_RejectsNonCanonicalAndMultibyteOversizeSummary()
    {
        var valid = Proposal(Payload, "review");
        Assert.Equal(ActionApprovalContractV1.ComputePayloadSha256(Payload),
            ActionProposalValidator.ValidateAndComputePayloadHash(valid));

        Assert.Throws<ActionProposalValidationException>(() =>
            ActionProposalValidator.ValidateAndComputePayloadHash(Proposal(
                Encoding.UTF8.GetBytes("{ \"message\": \"ok\" }"), "review")));
        Assert.Throws<ActionProposalValidationException>(() =>
            ActionProposalValidator.ValidateAndComputePayloadHash(Proposal(Payload, new string('é', 1_025))));
    }

    private static PreparedActionProposal Proposal(byte[] payload, string summary) => new(
        "tenant-a", ReportId, "ticket_create", "proposal-1", ActionCategory.TicketCreate,
        ActionExecutionMode.Live, "github:owner/repo", new string('a', 64), payload, summary,
        60, [Guid.Parse("22222222-2222-2222-2222-222222222222")], false, "approval_required");

    private static string Hash(
        Guid reportId,
        string toolId,
        ActionCategory category,
        ActionExecutionMode mode,
        string target,
        string binding,
        string payloadHash,
        byte[] payload,
        string provenanceHash) =>
        ActionApprovalContractV1.ComputeApprovalSha256(
            reportId, toolId, category, mode, target, binding, payloadHash, payload, provenanceHash);
}
