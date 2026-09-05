using IncidentCompass.Domain.Incidents;
using IncidentCompass.Domain.Incidents.Statuses;

namespace IncidentCompass.Application.Governance.Ledger;

public interface ITriageLedgerReader
{
    Task<TriageBudgetLedgerUsage> ReadBudgetUsageAsync(
        TriageJob job,
        CancellationToken cancellationToken);

    Task<int> CountPolicyDecisionsAsync(
        TriageJob job,
        string toolName,
        string scope,
        TriageLedgerDecision decision,
        CancellationToken cancellationToken);

    Task<bool> HasSuccessfulToolResultAsync(
        TriageJob job,
        string toolName,
        string scope,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<TriageLedgerEntry>> ReadByFaultIdAsync(
        Guid faultId,
        string tenantId,
        CancellationToken cancellationToken);
}
