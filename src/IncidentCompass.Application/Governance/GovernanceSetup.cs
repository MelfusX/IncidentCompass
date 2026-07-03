using IncidentCompass.Application.Core.Dispatching;
using IncidentCompass.Application.Governance.Ledger.GetFaultLedger;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace IncidentCompass.Application.Governance;

internal static class GovernanceSetup
{
    public static IServiceCollection AddGovernanceCore(this IServiceCollection services)
    {
        services.TryAddScoped<IRequestHandler<GetFaultLedgerQuery, FaultLedgerResponse>, GetFaultLedgerQueryHandler>();
        return services;
    }
}