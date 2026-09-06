namespace IncidentCompass.Application.Intake.Configuration;

/// <summary>
/// The per-attempt orchestrator budget. <see cref="MaxTurns"/> bounds the orchestrator work turns;
/// the loop adds <see cref="MaxReprompts"/> on top, so a correction turn never costs a work turn.
/// </summary>
public sealed record OrchestratorBudgetSettings(
    int MaxWorkers,
    int MaxTokens,
    int MaxWallClockSeconds,
    int MaxReprompts = 1,
    int MaxTurns = OrchestratorBudgetSettings.DefaultMaxTurns)
{
    /// <summary>
    /// The work-turn allowance used when a configuration does not set one. It is the constant the
    /// loop used before the limit became configurable, so the effective bound is unchanged.
    /// </summary>
    public const int DefaultMaxTurns = 16;

    /// <summary>
    /// Below one work turn the orchestrator could never reach <c>publish_report</c>, so the loop would
    /// dead-letter every job on the turn limit.
    /// </summary>
    public const int MinimumMaxTurns = 1;

    /// <summary>
    /// Every turn is a billed model call. The token and wall-clock budgets already bound an attempt,
    /// but this caps the worst-case call count so a typo cannot turn a bounded run into a long one.
    /// </summary>
    public const int MaximumMaxTurns = 64;
}
