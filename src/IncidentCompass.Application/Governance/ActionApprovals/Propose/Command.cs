using System.Text.Json;
using IncidentCompass.Application.Core.Dispatching;

namespace IncidentCompass.Application.Governance.ActionApprovals.Propose;

public sealed record ProposePostReportActionCommand(
    string TenantId,
    Guid OriginReportId,
    string ToolId,
    string ProposalKey,
    JsonElement Arguments) : IRequest<PostReportActionProposalResponse>;
