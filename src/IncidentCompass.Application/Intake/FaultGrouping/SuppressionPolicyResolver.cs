using IncidentCompass.Application.Intake.Configuration;
using IncidentCompass.Domain.Incidents;

namespace IncidentCompass.Application.Intake.FaultGrouping;

internal static class SuppressionPolicyResolver
{
    public const string DefaultPolicyId = "default";

    public static EffectiveSuppressionPolicy Resolve(FaultGroupingSettings settings, Signal signal)
    {
        var selected = settings.SuppressionPolicies
            .Select((rule, index) => new { Rule = rule, Index = index, Specificity = Specificity(rule, signal) })
            .Where(candidate => candidate.Specificity >= 0)
            .OrderByDescending(candidate => candidate.Specificity)
            .ThenBy(candidate => candidate.Index)
            .FirstOrDefault();
        return selected is null
            ? new EffectiveSuppressionPolicy(DefaultPolicyId, settings.SilenceWindowMinutes)
            : new EffectiveSuppressionPolicy(selected.Rule.Id, selected.Rule.SilenceWindowMinutes);
    }

    private static int Specificity(SuppressionRuleSettings rule, Signal signal)
    {
        var specificity = 0;
        if (!Matches(rule.ServiceName, signal.ServiceName))
        {
            return -1;
        }

        specificity += !string.IsNullOrWhiteSpace(rule.ServiceName) ? 1 : 0;
        if (!Matches(rule.Severity, signal.Severity))
        {
            return -1;
        }

        return specificity + (!string.IsNullOrWhiteSpace(rule.Severity) ? 1 : 0);
    }

    private static bool Matches(string? selector, string? value) =>
        string.IsNullOrWhiteSpace(selector) ||
        string.Equals(selector.Trim(), value?.Trim(), StringComparison.OrdinalIgnoreCase);
}
