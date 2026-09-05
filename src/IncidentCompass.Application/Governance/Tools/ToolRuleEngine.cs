using IncidentCompass.Application.Governance.ActionApprovals;
using IncidentCompass.Application.Governance.Ledger;
using IncidentCompass.Application.Intake.Configuration;
using IncidentCompass.Domain.Incidents;
using IncidentCompass.Domain.Incidents.Actions;
using IncidentCompass.Domain.Incidents.Statuses;

namespace IncidentCompass.Application.Governance.Tools;

internal sealed class ToolRuleEngine(ITriageLedgerReader ledgerReader)
{
    public Task<ToolRulePolicyResult> DecideImmediateAsync(
        TriageJob job,
        TriageConfiguration configuration,
        string roleName,
        string toolName,
        CancellationToken cancellationToken)
    {
        if (!configuration.Tools.TryGetValue(toolName, out var settings) ||
            !string.Equals(settings.Kind, "internal", StringComparison.Ordinal))
        {
            return Task.FromResult(ToolRulePolicyResult.Denied("unknown_or_unconfigured_tool"));
        }

        if (!configuration.Roles.TryGetValue(roleName, out var role) ||
            !role.Tools.Contains(toolName, StringComparer.Ordinal))
        {
            return Task.FromResult(ToolRulePolicyResult.Denied("tool_not_granted_to_role"));
        }

        return EvaluateRulesAsync(
            configuration.Rules,
            toolName,
            new TriageLedgerToolRuleFactReader(ledgerReader, job),
            cancellationToken);
    }

    public async Task<ToolRulePolicyResult> DecideExternalAsync(
        TriageConfiguration configuration,
        AgentToolDescriptor registeredTool,
        IToolRuleFactReader factReader,
        CancellationToken cancellationToken)
    {
        if (registeredTool.Capability != AgentToolCapability.ExternalAction ||
            !configuration.Tools.TryGetValue(registeredTool.ToolId, out var settings) ||
            !string.Equals(settings.Kind, "external_action", StringComparison.Ordinal) ||
            !configuration.Actions.AllowedTools.Contains(registeredTool.ToolId, StringComparer.Ordinal))
        {
            return ToolRulePolicyResult.Denied("action_not_granted");
        }

        if (!MatchesRegistration(settings, registeredTool))
        {
            return ToolRulePolicyResult.Denied("action_registration_mismatch");
        }

        var effectiveMode = EffectiveMode(configuration.Actions.DefaultMode, settings.Mode);
        if (effectiveMode == ActionExecutionMode.Disabled)
        {
            return ToolRulePolicyResult.Denied("action_disabled");
        }

        var rules = await EvaluateRulesAsync(
            configuration.Rules, registeredTool.ToolId, factReader, cancellationToken);
        if (!rules.MayProceed)
        {
            return rules;
        }

        var approvalRequired = rules.Decision == TriageLedgerDecision.ApprovalRequired ||
            configuration.Actions.RequireApprovalForAll ||
            registeredTool.Category != ActionCategory.Notification;
        var reason = approvalRequired
            ? CombineReason(rules.Reason, "approval required by action ceiling")
            : rules.Reason;
        return approvalRequired
            ? ToolRulePolicyResult.ApprovalRequired(reason, effectiveMode)
            : ToolRulePolicyResult.Allowed(reason, effectiveMode);
    }

    private async Task<ToolRulePolicyResult> EvaluateRulesAsync(
        IEnumerable<TriageRuleSettings> configuredRules,
        string toolName,
        IToolRuleFactReader factReader,
        CancellationToken cancellationToken)
    {
        var decision = TriageLedgerDecision.Allowed;
        var reasons = new List<string>();
        foreach (var rule in MatchingRules(configuredRules, toolName))
        {
            switch (rule.Type)
            {
                case "rate_cap":
                    var count = await factReader.CountAcceptedUsesAsync(
                        toolName, rule.Scope, cancellationToken);
                    if (count >= rule.Max.GetValueOrDefault())
                    {
                        return ToolRulePolicyResult.Denied(
                            $"rate_cap exceeded for {toolName}: {count}/{rule.Max.GetValueOrDefault()} in {rule.Scope} scope");
                    }

                    reasons.Add($"rate_cap {count}/{rule.Max.GetValueOrDefault()} in {rule.Scope} scope");
                    break;
                case "precondition":
                    var prerequisite = rule.RequiresSuccessfulToolResult!;
                    if (!await factReader.HasSuccessfulToolResultAsync(
                            prerequisite, rule.Scope, cancellationToken))
                    {
                        return ToolRulePolicyResult.Denied(
                            $"precondition unsatisfied: {prerequisite} has no successful ToolResult in {rule.Scope} scope");
                    }

                    reasons.Add($"precondition satisfied by {prerequisite} in {rule.Scope} scope");
                    break;
                case "requires_approval":
                    decision = TriageLedgerDecision.ApprovalRequired;
                    reasons.Add("approval required by rule");
                    break;
                case "grounding":
                    reasons.Add("grounding required by rule");
                    break;
            }
        }

        var reason = reasons.Count == 0 ? "no matching rule denied execution" : string.Join("; ", reasons);
        return decision == TriageLedgerDecision.ApprovalRequired
            ? ToolRulePolicyResult.ApprovalRequired(reason)
            : ToolRulePolicyResult.Allowed(reason);
    }

    private static bool MatchesRegistration(TriageToolSettings settings, AgentToolDescriptor registeredTool) =>
        string.Equals(settings.Category, registeredTool.Category?.ToStorageValue(), StringComparison.Ordinal) &&
        string.Equals(settings.LogicalTargetId, registeredTool.LogicalTargetId, StringComparison.Ordinal);

    private static ActionExecutionMode EffectiveMode(string globalMode, string? overrideMode)
    {
        var global = ActionApprovalVocabulary.ParseMode(globalMode);
        var perTool = overrideMode is null ? global : ActionApprovalVocabulary.ParseMode(overrideMode);
        return (ActionExecutionMode)Math.Max((int)global, (int)perTool);
    }

    private static string CombineReason(string first, string second) =>
        string.IsNullOrWhiteSpace(first) ? second : first + "; " + second;

    private static IEnumerable<TriageRuleSettings> MatchingRules(
        IEnumerable<TriageRuleSettings> rules,
        string toolName) => rules.Where(rule =>
            string.Equals(rule.Tool, "*", StringComparison.Ordinal) ||
            string.Equals(rule.Tool, toolName, StringComparison.Ordinal));
}
