namespace IncidentCompass.Application.Investigation.Jobs;

/// <summary>
/// The result of one turn of the orchestrator investigation loop: what the turn did, and the reprompt
/// count the next turn starts from. Carrying the count here is what lets the loop own the counter as
/// an ordinary local instead of mutating it through a <c>ref</c> parameter or reading it back out of
/// a nullable return value.
/// </summary>
/// <param name="Disposition">What the turn did.</param>
/// <param name="Reprompts">Reprompts spent on this attempt after the turn, never lower than before it.</param>
internal readonly record struct OrchestratorTurnOutcome(OrchestratorTurnDisposition Disposition, int Reprompts)
{
    /// <summary>
    /// True only for <see cref="OrchestratorTurnDisposition.ReportPublished"/>: the single turn
    /// outcome that leaves the loop successfully rather than taking another turn.
    /// </summary>
    public bool InvestigationFinished => Disposition == OrchestratorTurnDisposition.ReportPublished;
}
