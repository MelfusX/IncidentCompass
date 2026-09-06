namespace IncidentCompass.Application.Governance.Tools;

/// <summary>
/// The single definition of the configured tool-rule type names. Load-time validation, the shared
/// <see cref="ToolRuleEngine"/> decision path and post-report dispatch all resolve rule types from
/// here so the spellings cannot drift apart across the governance boundary.
/// </summary>
internal static class TriageRuleTypes
{
    public const string RateCap = "rate_cap";
    public const string Precondition = "precondition";
    public const string Grounding = "grounding";
    public const string RequiresApproval = "requires_approval";

    /// <summary>
    /// Every rule type the engine can evaluate. Anything outside this set is denied by the engine,
    /// so extending this list without extending the engine switch fails closed rather than open.
    /// </summary>
    public static IReadOnlyCollection<string> All { get; } =
        [RateCap, Precondition, Grounding, RequiresApproval];
}
