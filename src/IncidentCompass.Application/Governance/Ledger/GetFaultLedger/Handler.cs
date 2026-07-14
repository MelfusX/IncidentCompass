using IncidentCompass.Application.Core.Dispatching;
using IncidentCompass.Application.Core.Exceptions;
using IncidentCompass.Application.Core.Tenancy;
using IncidentCompass.Application.Intake.FaultGrouping;

namespace IncidentCompass.Application.Governance.Ledger.GetFaultLedger;

public sealed class GetFaultLedgerQueryHandler(
    IFaultRepository faultRepository,
    ITriageLedgerReader ledgerReader,
    IIncidentTenantContext incidentTenantContext) : IRequestHandler<GetFaultLedgerQuery, FaultLedgerResponse>
{
    public async Task<FaultLedgerResponse> HandleAsync(
        GetFaultLedgerQuery request,
        CancellationToken cancellationToken)
    {
        var fault = await faultRepository.FindByIdAsync(request.FaultId, await incidentTenantContext.GetTenantIdAsync(cancellationToken), cancellationToken)
            ?? throw new NotFoundException($"Fault '{request.FaultId}' was not found.");
        var entries = await ledgerReader.ReadByFaultIdAsync(
            fault.Id,
            fault.TenantId,
            cancellationToken);
        return new FaultLedgerResponse(
            fault.Id,
            entries.Select(static entry => new FaultLedgerEventResponse(
                entry.Id,
                entry.JobId,
                entry.Attempt,
                entry.EventType.ToString(),
                entry.Role,
                entry.ToolName,
                entry.Decision?.ToString(),
                entry.Rationale,
                entry.DecisionReason,
                entry.ToolStatus?.ToString(),
                entry.TokensDelta,
                entry.WorkersDelta,
                entry.PayloadRef,
                entry.ConfigHash,
                entry.CreatedAtUtc)).ToArray());
    }
}