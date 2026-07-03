using IncidentCompass.Application.Core.Dispatching;

namespace IncidentCompass.Application.Governance.Ledger.GetFaultLedger;

public sealed record GetFaultLedgerQuery(Guid FaultId) : IRequest<FaultLedgerResponse>;