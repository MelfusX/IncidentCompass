using IncidentCompass.Application.Governance.Ledger;
using IncidentCompass.Application.Intake.Configuration;
using IncidentCompass.Domain.Incidents;
using IncidentCompass.Domain.Incidents.Statuses;

namespace IncidentCompass.Application.Investigation.Jobs;

internal sealed class WorkerToolRuleEngine(ITriageLedgerReader ledgerReader)
{
    public async Task<WorkerToolPolicyResult> DecideAsync(
        TriageJob job,
        TriageConfiguration configuration,
        string roleName,
        string toolName,
        CancellationToken cancellationToken)
    {
        if (!configuration.Tools.ContainsKey(toolName))
        {
            return WorkerToolPolicyResult.Denied("unknown_or_unconfigured_tool");
        }

        if (!configuration.Roles.TryGetValue(roleName, out var role) ||
            !role.Tools.Contains(toolName, StringComparer.Ordinal))
        {
            return WorkerToolPolicyResult.Denied("tool_not_granted_to_role");
        }

        var decision = TriageLedgerDecision.Allowed;
        var reasons = new List<string>();
        foreach (var rule in MatchingRules(configuration.Rules, toolName))
        {
            switch (rule.Type)
            {
                case "rate_cap":
                    var allowedCount = await ledgerReader.CountPolicyDecisionsAsync(
                        job,
                        toolName,
                        rule.Scope,
                        TriageLedgerDecision.Allowed,
                        cancellationToken);
                    if (allowedCount >= rule.Max.GetValueOrDefault())
                    {
                        return WorkerToolPolicyResult.Denied(
                            $"rate_cap exceeded for {toolName}: {allowedCount}/{rule.Max.GetValueOrDefault()} in {rule.Scope} scope");
                    }

                    reasons.Add($"rate_cap {allowedCount}/{rule.Max.GetValueOrDefault()} in {rule.Scope} scope");
                    break;
                case "precondition":
                    var requiredTool = rule.RequiresSuccessfulToolResult!;
                    var satisfied = await ledgerReader.HasSuccessfulToolResultAsync(
                        job,
                        requiredTool,
                        rule.Scope,
                        cancellationToken);
                    if (!satisfied)
                    {
                        return WorkerToolPolicyResult.Denied(
                            $"precondition unsatisfied: {requiredTool} has no successful ToolResult in {rule.Scope} scope");
                    }

                    reasons.Add($"precondition satisfied by {requiredTool} in {rule.Scope} scope");
                    break;
                case "requires_approval":
                    decision = TriageLedgerDecision.ApprovalRequired;
                    reasons.Add("approval required by rule");
                    break;
                case "grounding":
                    reasons.Add("grounding rule has no write-tool target in MVP");
                    break;
            }
        }

        return decision == TriageLedgerDecision.ApprovalRequired
            ? WorkerToolPolicyResult.ApprovalRequired(string.Join("; ", reasons))
            : WorkerToolPolicyResult.Allowed(reasons.Count == 0 ? "no matching rule denied execution" : string.Join("; ", reasons));
    }

    private static IEnumerable<TriageRuleSettings> MatchingRules(
        IEnumerable<TriageRuleSettings> rules,
        string toolName)
    {
        return rules.Where(rule =>
            string.Equals(rule.Tool, "*", StringComparison.Ordinal) ||
            string.Equals(rule.Tool, toolName, StringComparison.Ordinal));
    }
}