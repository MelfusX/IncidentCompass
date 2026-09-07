using System.Globalization;
using IncidentCompass.Application.Governance.Tools;
using IncidentCompass.Application.Intake.Configuration;
using static IncidentCompass.Infrastructure.Intake.TriageConfigurationValidationGuards;

namespace IncidentCompass.Infrastructure.Intake;

internal static class TriageRuleLoadValidator
{
    private static readonly HashSet<string> RuleTypes = new(TriageRuleTypes.All, StringComparer.Ordinal);
    private static readonly HashSet<string> RuleScopes = new(["attempt", "job"], StringComparer.Ordinal);

    public static void Validate(
        IReadOnlyDictionary<string, TriageToolSettings> tools,
        IReadOnlyCollection<TriageRuleSettings> rules)
    {
        foreach (var rule in rules)
        {
            RequireKnown("Rules.Type", rule.Type, RuleTypes);
            if (!RuleScopes.Contains(rule.Scope))
            {
                throw Invalid("Rules." + rule.Type + ".Scope", rule.Scope, "one of: attempt, job");
            }

            if (!string.Equals(rule.Tool, "*", StringComparison.Ordinal) && !tools.ContainsKey(rule.Tool))
            {
                throw Invalid("Rules.Tool", rule.Tool, "'*' or a configured worker tool id");
            }

            ValidateShape(tools, rule);
        }
    }

    private static void ValidateShape(
        IReadOnlyDictionary<string, TriageToolSettings> tools,
        TriageRuleSettings rule)
    {
        switch (rule.Type)
        {
            case TriageRuleTypes.RateCap:
                if (rule.Max is not > 0)
                {
                    throw Invalid("Rules.rate_cap.Max", rule.Max?.ToString(CultureInfo.InvariantCulture) ?? "", "a positive integer");
                }

                break;
            case TriageRuleTypes.Precondition:
                if (string.IsNullOrWhiteSpace(rule.RequiresSuccessfulToolResult) ||
                    !tools.ContainsKey(rule.RequiresSuccessfulToolResult))
                {
                    throw Invalid("Rules.RequiresSuccessfulToolResult", rule.RequiresSuccessfulToolResult ?? "", "a configured worker tool id");
                }

                break;
            case TriageRuleTypes.RequiresApproval:
                if (string.Equals(rule.Tool, "*", StringComparison.Ordinal) ||
                    !tools.TryGetValue(rule.Tool, out var approvalTool) ||
                    !string.Equals(approvalTool.Kind, "external_action", StringComparison.Ordinal))
                {
                    throw Invalid("Rules.requires_approval.Tool", rule.Tool, "an exact configured external action tool id");
                }

                break;
            case TriageRuleTypes.Grounding:
                break;
        }
    }
}
