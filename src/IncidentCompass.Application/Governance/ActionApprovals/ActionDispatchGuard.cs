using IncidentCompass.Application.Governance.Tools;
using IncidentCompass.Application.Intake.Configuration;
using IncidentCompass.Domain.Incidents.Actions;

namespace IncidentCompass.Application.Governance.ActionApprovals;

internal static class ActionDispatchGuard
{
    public static ActionDispatchGuardResult Evaluate(
        ActionApprovalRecord action,
        AgentToolDescriptor descriptor,
        TriageConfiguration currentConfiguration)
    {
        if (descriptor.Capability != AgentToolCapability.ExternalAction ||
            descriptor.Category != action.Category ||
            !string.Equals(descriptor.LogicalTargetId, action.LogicalTargetId, StringComparison.Ordinal))
        {
            return ActionDispatchGuardResult.Fail("action_registration_changed");
        }

        if (!currentConfiguration.Tools.TryGetValue(action.ToolId, out var settings) ||
            !string.Equals(settings.Kind, "external_action", StringComparison.Ordinal) ||
            !currentConfiguration.Actions.AllowedTools.Contains(action.ToolId, StringComparer.Ordinal) ||
            !string.Equals(settings.Category, action.Category.ToStorageValue(), StringComparison.Ordinal) ||
            !string.Equals(settings.LogicalTargetId, action.LogicalTargetId, StringComparison.Ordinal))
        {
            return ActionDispatchGuardResult.Fail("action_configuration_changed");
        }

        var currentMode = EffectiveMode(currentConfiguration.Actions.DefaultMode, settings.Mode);
        if (action.Mode == ActionExecutionMode.Disabled || currentMode == ActionExecutionMode.Disabled)
        {
            return ActionDispatchGuardResult.Fail("action_disabled");
        }

        if (string.Equals(action.DecisionActor, "system:policy", StringComparison.Ordinal) &&
            RequiresApproval(currentConfiguration, action))
        {
            return ActionDispatchGuardResult.Fail("approval_policy_changed");
        }

        return action.Mode == ActionExecutionMode.DryRun || currentMode == ActionExecutionMode.DryRun
            ? ActionDispatchGuardResult.DryRun
            : ActionDispatchGuardResult.Invoke;
    }

    private static bool RequiresApproval(TriageConfiguration configuration, ActionApprovalRecord action) =>
        configuration.Actions.RequireApprovalForAll ||
        action.Category != ActionCategory.Notification ||
        configuration.Rules.Any(rule =>
            string.Equals(rule.Type, "requires_approval", StringComparison.Ordinal) &&
            string.Equals(rule.Tool, action.ToolId, StringComparison.Ordinal));

    private static ActionExecutionMode EffectiveMode(string globalMode, string? overrideMode)
    {
        var global = ActionApprovalVocabulary.ParseMode(globalMode);
        var perTool = overrideMode is null ? global : ActionApprovalVocabulary.ParseMode(overrideMode);
        return (ActionExecutionMode)Math.Max((int)global, (int)perTool);
    }
}
