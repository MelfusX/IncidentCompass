using IncidentCompass.Application.Core.Dispatching;
using IncidentCompass.Application.Governance.Ledger.GetFaultLedger;
using IncidentCompass.Application.Governance.ActionApprovals.Approve;
using IncidentCompass.Application.Governance.ActionApprovals.Get;
using IncidentCompass.Application.Governance.ActionApprovals.List;
using IncidentCompass.Application.Governance.ActionApprovals.Reject;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace IncidentCompass.Application.Governance;

internal static class GovernanceSetup
{
    public static IServiceCollection AddGovernanceCore(this IServiceCollection services)
    {
        services.TryAddScoped<IRequestHandler<GetFaultLedgerQuery, FaultLedgerResponse>, GetFaultLedgerQueryHandler>();
        services.TryAddScoped<IRequestHandler<ListActionApprovalsQuery, ActionApprovalListResponse>, ListActionApprovalsQueryHandler>();
        services.TryAddScoped<IRequestHandler<GetActionApprovalQuery, ActionApprovalDetailsResponse>, GetActionApprovalQueryHandler>();
        services.TryAddScoped<IRequestHandler<ApproveActionCommand, ActionApprovalDetailsResponse>, ApproveActionCommandHandler>();
        services.TryAddScoped<IRequestHandler<RejectActionCommand, ActionApprovalDetailsResponse>, RejectActionCommandHandler>();
        return services;
    }
}
