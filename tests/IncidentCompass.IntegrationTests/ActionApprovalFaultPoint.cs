namespace IncidentCompass.IntegrationTests;

internal enum ActionApprovalFaultPoint
{
    None,
    AfterActionRow,
    AfterProposalArtifact,
    AfterProvenanceRow,
    BeforeProposalLedger,
    BeforeDecisionLedger,
    BeforeDispatchLedger,
    BeforeTerminalLedger
}
