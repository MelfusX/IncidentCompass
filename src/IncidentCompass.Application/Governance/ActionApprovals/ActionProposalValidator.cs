using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using IncidentCompass.Application.Core.Exceptions;
using IncidentCompass.Application.Core.Serialization;
using IncidentCompass.Domain.Incidents.Actions;

namespace IncidentCompass.Application.Governance.ActionApprovals;

public static class ActionProposalValidator
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static string ValidateAndComputePayloadHash(PreparedActionProposal proposal)
    {
        if (string.IsNullOrWhiteSpace(proposal.TenantId) ||
            string.IsNullOrWhiteSpace(proposal.ToolId) || proposal.ToolId.Length > 128 ||
            string.IsNullOrWhiteSpace(proposal.ProposalKey) || proposal.ProposalKey.Length > 256)
        {
            throw new ActionProposalValidationException("Action proposal identity is invalid.");
        }

        if (proposal.Mode == ActionExecutionMode.Disabled ||
            !Enum.IsDefined(proposal.Category) || !Enum.IsDefined(proposal.Mode))
        {
            throw new ActionProposalValidationException("Action proposal category or mode is invalid.");
        }

        if (proposal.LogicalTargetId.Length is < 1 or > ActionApprovalLimits.MaximumLogicalTargetCharacters ||
            !IsLowerHexSha256(proposal.AdapterBindingFingerprint))
        {
            throw new ActionProposalValidationException("Action proposal target binding is invalid.");
        }

        if (proposal.ApprovalTtlMinutes is < ActionApprovalLimits.MinimumTtlMinutes or > ActionApprovalLimits.MaximumTtlMinutes ||
            proposal.EvidenceArtifactIds.Count is < ActionApprovalLimits.MinimumEvidenceCount or > ActionApprovalLimits.MaximumEvidenceCount ||
            proposal.EvidenceArtifactIds.Distinct().Count() != proposal.EvidenceArtifactIds.Count)
        {
            throw new ActionProposalValidationException("Action proposal lifetime or evidence count is invalid.");
        }

        if (proposal.CanonicalPayload.Length is < 1 or > ActionApprovalLimits.MaximumPayloadBytes ||
            StrictUtf8.GetByteCount(proposal.ReviewSummary) is < 1 or > ActionApprovalLimits.MaximumSummaryBytes ||
            StrictUtf8.GetByteCount(proposal.PolicyDecisionReason) is < 1 or > ActionApprovalLimits.MaximumSummaryBytes)
        {
            throw new ActionProposalValidationException("Action proposal payload or summary exceeds its bound.");
        }

        ValidateCanonicalJson(proposal.CanonicalPayload);
        return ActionApprovalContractV1.ComputePayloadSha256(proposal.CanonicalPayload);
    }

    public static bool IsLowerHexSha256(string value) =>
        value.Length == 64 && value.All(static character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static void ValidateCanonicalJson(byte[] payload)
    {
        try
        {
            var text = StrictUtf8.GetString(payload);
            var node = JsonNode.Parse(text) ?? throw new JsonException();
            var canonical = CanonicalJsonSerializer.Canonicalize(node);
            if (!payload.AsSpan().SequenceEqual(Encoding.UTF8.GetBytes(canonical)))
            {
                throw new ActionProposalValidationException("Action proposal payload must be canonical JSON.");
            }
        }
        catch (Exception exception) when (exception is JsonException or DecoderFallbackException)
        {
            throw new ActionProposalValidationException("Action proposal payload must be valid canonical UTF-8 JSON.", exception);
        }
    }
}
