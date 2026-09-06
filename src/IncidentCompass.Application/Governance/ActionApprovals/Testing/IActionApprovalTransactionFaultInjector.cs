namespace IncidentCompass.Application.Governance.ActionApprovals.Testing;

/// <summary>
/// Test-only fault seam for the action proposal, decision and dispatch transactions. This is
/// production code that exists for testability, not dead code: every hook below sits <em>between</em>
/// two statements of one database transaction, so a decorator around the repository ports cannot
/// reach any of them. From outside a repository the transaction has already committed or rolled back
/// as a unit, and these seams exist precisely to prove that the action row, its proposal artifact,
/// its provenance rows and its ledger row commit or roll back together. The dispatch hooks guard the
/// at-most-once external write boundary, where a lost terminal write must surface as
/// <c>dispatch_outcome_unknown</c> rather than a second provider call. Production composition always
/// binds <see cref="NoopActionApprovalTransactionFaultInjector"/>; only the integration tests replace
/// it.
/// </summary>
internal interface IActionApprovalTransactionFaultInjector
{
    /// <summary>Invoked after the action approval row is inserted, before its proposal artifact.</summary>
    Task AfterActionRowAsync(CancellationToken cancellationToken);

    /// <summary>Invoked after the proposal artifact is inserted, before the provenance rows.</summary>
    Task AfterProposalArtifactAsync(CancellationToken cancellationToken);

    /// <summary>Invoked after the provenance row with the given ordinal is inserted.</summary>
    Task AfterProvenanceRowAsync(int ordinal, CancellationToken cancellationToken);

    /// <summary>Invoked after the proposal rows are written, before the proposal ledger row.</summary>
    Task BeforeProposalLedgerAsync(CancellationToken cancellationToken);

    /// <summary>Invoked after the approve/reject state update, before the decision ledger row.</summary>
    Task BeforeDecisionLedgerAsync(CancellationToken cancellationToken);

    /// <summary>Invoked after the dispatch claim is frozen, before the dispatch ledger row.</summary>
    Task BeforeDispatchLedgerAsync(CancellationToken cancellationToken);

    /// <summary>Invoked after the terminal outcome is recorded, before the terminal ledger row.</summary>
    Task BeforeTerminalLedgerAsync(CancellationToken cancellationToken);
}
