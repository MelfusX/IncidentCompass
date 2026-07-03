namespace IncidentCompass.Application.Governance.Ledger.GetFaultLedger;

public sealed record FaultLedgerResponse(
    Guid FaultId,
    IReadOnlyList<FaultLedgerEventResponse> Events);