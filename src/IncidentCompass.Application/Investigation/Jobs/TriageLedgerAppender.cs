using IncidentCompass.Application.Governance.Ledger;
using IncidentCompass.Domain.Incidents;
using IncidentCompass.Domain.Incidents.Statuses;

namespace IncidentCompass.Application.Investigation.Jobs;

internal sealed class TriageLedgerAppender(ITriageLedgerWriter ledgerWriter)
{
    public async Task AppendAsync(
        TriageJob job,
        TriageLedgerEventType eventType,
        string? role,
        string? toolName,
        string? rationale,
        string? payloadRef,
        CancellationToken cancellationToken)
    {
        await ledgerWriter.AppendAsync(
            new TriageLedgerAppendRequest(
                job.FaultId,
                job.Id,
                job.Attempt,
                eventType,
                role,
                toolName,
                rationale,
                Decision: null,
                DecisionReason: null,
                payloadRef,
                job.ConfigHash),
            cancellationToken);
    }
}
