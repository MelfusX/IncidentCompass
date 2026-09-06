namespace IncidentCompass.Application.Investigation.Jobs;

/// <summary>
/// The single definition of the two tool names the orchestrator may call. The tool surface handed to
/// the model, the loop that dispatches a proposed call, the delegation ledger row and configuration
/// load validation all resolve the names from here so the spellings cannot drift apart. They are
/// <c>const</c> so they stay usable in <c>case</c> labels and collection expressions.
/// </summary>
internal static class OrchestratorToolNames
{
    public const string Delegate = "delegate";

    public const string PublishReport = "publish_report";

    /// <summary>
    /// Exactly the tools an orchestrator configuration may grant. A configuration that grants
    /// anything else is rejected at load time, and a proposed call outside this set is reprompted
    /// as an unknown tool rather than executed.
    /// </summary>
    public static IReadOnlyCollection<string> All { get; } = [Delegate, PublishReport];
}
