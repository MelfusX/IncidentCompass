using System.Text.RegularExpressions;

namespace IncidentCompass.Application.Intake.Redaction;

/// <summary>
/// One configured redaction pattern with its <see cref="Regex"/> materialized once for the
/// configuration snapshot that declared it.
/// </summary>
internal sealed class CompiledRedactionPattern(string name, Regex matcher, string replacement)
{
    private int _timeoutReported;

    public string Name { get; } = name;

    public Regex Matcher { get; } = matcher;

    public string Replacement { get; } = replacement;

    /// <summary>
    /// Returns true for the first match timeout observed for this pattern within this
    /// configuration snapshot, so a hostile signal cannot produce one log line per field.
    /// </summary>
    public bool TryClaimTimeoutReport() => Interlocked.Exchange(ref _timeoutReported, 1) == 0;
}
