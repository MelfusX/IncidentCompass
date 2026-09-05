using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using IncidentCompass.Application.Core.Serialization;
using IncidentCompass.Domain.Incidents.Actions;

namespace IncidentCompass.Application.Governance.ActionApprovals;

public static class ActionTerminalValidator
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static void Validate(ActionTerminalRequest request)
    {
        if (request.TerminalState is not (ActionApprovalState.Executed or ActionApprovalState.Failed) ||
            request.ResultPayload.Length is < 1 or > ActionApprovalLimits.MaximumResultBytes ||
            StrictUtf8.GetByteCount(request.ResultSummary) is < 1 or > ActionApprovalLimits.MaximumSummaryBytes ||
            (request.TerminalState == ActionApprovalState.Failed) != !string.IsNullOrWhiteSpace(request.FailureCode) ||
            request.FailureCode?.Length > 128)
        {
            throw new ActionProposalValidationException("Action terminal result is invalid or exceeds its bound.");
        }

        try
        {
            var text = StrictUtf8.GetString(request.ResultPayload);
            var node = JsonNode.Parse(text) ?? throw new JsonException();
            if (!request.ResultPayload.AsSpan().SequenceEqual(
                    Encoding.UTF8.GetBytes(CanonicalJsonSerializer.Canonicalize(node))))
            {
                throw new ActionProposalValidationException("Action terminal result must be canonical JSON.");
            }
        }
        catch (Exception exception) when (exception is JsonException or DecoderFallbackException)
        {
            throw new ActionProposalValidationException("Action terminal result must be valid canonical UTF-8 JSON.", exception);
        }
    }
}
