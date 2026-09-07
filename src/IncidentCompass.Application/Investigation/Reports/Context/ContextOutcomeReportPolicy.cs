using System.Text.RegularExpressions;

namespace IncidentCompass.Application.Investigation.Reports.Context;

internal static partial class ContextOutcomeReportPolicy
{
    private static readonly HashSet<string> SupportedTools =
        new(["source_lookup", "ticket_search"], StringComparer.Ordinal);

    public static TriageReport Apply(
        TriageReport report,
        IReadOnlyCollection<ReadOnlyContextOutcome> outcomes)
    {
        var limitations = report.Limitations.ToList();
        var known = limitations.ToHashSet(StringComparer.Ordinal);
        foreach (var outcome in outcomes
            .Where(IsSupported)
            .OrderBy(item => item.ToolName, StringComparer.Ordinal)
            .ThenBy(item => item.Status)
            .ThenBy(item => item.Code, StringComparer.Ordinal))
        {
            var limitation = outcome.Status switch
            {
                ReadOnlyContextOutcomeStatus.NoMatch =>
                    $"Read-only context {outcome.ToolName} returned no matches ({outcome.Code}).",
                _ => $"Read-only context {outcome.ToolName} was unavailable ({outcome.Code})."
            };
            if (known.Add(limitation))
            {
                limitations.Add(limitation);
            }
        }

        return report with { Limitations = limitations };
    }

    private static bool IsSupported(ReadOnlyContextOutcome outcome) =>
        SupportedTools.Contains(outcome.ToolName) && StableCodeRegex().IsMatch(outcome.Code);

    [GeneratedRegex("^[a-z0-9_]{1,64}$", RegexOptions.CultureInvariant)]
    private static partial Regex StableCodeRegex();
}
