using IncidentCompass.Application.Intake.Configuration;
using IncidentCompass.Application.Intake.Normalization;

namespace IncidentCompass.Application.Intake.Fingerprinting;

internal static class FingerprintRuleResolver
{
    public const string DefaultRuleId = "default";

    public static EffectiveFingerprintRule Resolve(FaultGroupingSettings settings, NormalizedSignal signal)
    {
        var selected = settings.Rules
            .Select((rule, index) => new { Rule = rule, Index = index, Specificity = Specificity(rule, signal) })
            .Where(candidate => candidate.Specificity >= 0)
            .OrderByDescending(candidate => candidate.Specificity)
            .ThenBy(candidate => candidate.Index)
            .FirstOrDefault();

        return selected is null
            ? new EffectiveFingerprintRule(DefaultRuleId, settings.FingerprintVersion, FingerprintInputNames.Default)
            : new EffectiveFingerprintRule(selected.Rule.Id, selected.Rule.Version, selected.Rule.Inputs.ToArray());
    }

    private static int Specificity(FingerprintRuleSettings rule, NormalizedSignal signal)
    {
        var specificity = 0;
        if (!Matches(rule.ServiceName, signal.ServiceName))
        {
            return -1;
        }

        specificity += !string.IsNullOrWhiteSpace(rule.ServiceName) ? 1 : 0;
        if (!Matches(rule.OperationName, signal.OperationName))
        {
            return -1;
        }

        specificity += !string.IsNullOrWhiteSpace(rule.OperationName) ? 1 : 0;
        if (!Matches(rule.Source, signal.Source))
        {
            return -1;
        }

        return specificity + (!string.IsNullOrWhiteSpace(rule.Source) ? 1 : 0);
    }

    private static bool Matches(string? selector, string? value) =>
        string.IsNullOrWhiteSpace(selector) ||
        string.Equals(selector.Trim(), value?.Trim(), StringComparison.OrdinalIgnoreCase);
}