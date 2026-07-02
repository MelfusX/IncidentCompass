namespace IncidentCompass.Application.Governance.Ledger;

public sealed record TriageBudgetLedgerUsage(
    int TokensSpent,
    int WorkerCalls);