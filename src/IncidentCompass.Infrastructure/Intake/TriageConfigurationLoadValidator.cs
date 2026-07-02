using IncidentCompass.Application.Intake.Configuration;
using IncidentCompass.Application.Intake.Normalization;
using static IncidentCompass.Infrastructure.Intake.TriageConfigurationValidationGuards;

namespace IncidentCompass.Infrastructure.Intake;

internal sealed class TriageConfigurationLoadValidator(SignalNormalizerRegistry normalizerRegistry)
{
    private static readonly HashSet<string> RouteKinds = new(["Chat", "Embedding"], StringComparer.Ordinal);
    private static readonly HashSet<string> ProviderKinds = new(["Mock", "OpenAICompatible"], StringComparer.Ordinal);
    private static readonly HashSet<string> RuleTypes = new(["rate_cap", "precondition", "grounding", "requires_approval"], StringComparer.Ordinal);
    private static readonly HashSet<string> RuleScopes = new(["attempt", "job", "fault"], StringComparer.Ordinal);
    private static readonly HashSet<string> OrchestratorTools = new(["delegate", "publish_report"], StringComparer.Ordinal);

    public void Validate(TriageConfiguration configuration)
    {
        ValidateFaultGroupingSettings(configuration.FaultGrouping);
        ValidateAllowedSources(configuration.Ingestion);
        ValidateProviders(configuration.Providers);
        ValidateRoutes(configuration.Providers, configuration.Routes);
        ValidateOrchestrator(configuration.Routes, configuration.Orchestrator);
        ValidateRoles(configuration.Routes, configuration.Tools, configuration.Roles);
        ValidateTools(configuration.Routes, configuration.Tools);
        ValidateRules(configuration.Tools, configuration.Rules);
    }

    private static void ValidateFaultGroupingSettings(FaultGroupingSettings settings)
    {
        if (!settings.MassIssue.TryGetMinimumFingerprintStrength(out _))
        {
            throw Invalid("FaultGrouping.MassIssue.MinFingerprintStrength", settings.MassIssue.MinFingerprintStrength, "one of: weak, strong");
        }
    }

    private void ValidateAllowedSources(IngestionSettings settings)
    {
        foreach (var source in settings.AllowedSources)
        {
            if (!normalizerRegistry.HasNormalizer(source))
            {
                throw Invalid("Ingestion.AllowedSources", source, "only source kinds with registered normalizers");
            }
        }
    }

    private static void ValidateProviders(IReadOnlyDictionary<string, TriageProviderSettings> providers)
    {
        foreach (var (providerId, provider) in providers)
        {
            RequireKey(providerId, "Providers");
            RequireKnown("Providers." + providerId + ".Kind", provider.Kind, ProviderKinds);
        }
    }

    private static void ValidateRoutes(
        IReadOnlyDictionary<string, TriageProviderSettings> providers,
        IReadOnlyDictionary<string, TriageRouteSettings> routes)
    {
        foreach (var (routeId, route) in routes)
        {
            RequireKey(routeId, "Routes");
            RequireKnown("Routes." + routeId + ".Kind", route.Kind, RouteKinds);
            RequireNonBlank("Routes." + routeId + ".ProviderId", route.ProviderId);
            RequireNonBlank("Routes." + routeId + ".Model", route.Model);
            if (!providers.ContainsKey(route.ProviderId))
            {
                throw Invalid("Routes." + routeId + ".ProviderId", route.ProviderId, "a configured provider id");
            }
        }
    }

    private static void ValidateOrchestrator(
        IReadOnlyDictionary<string, TriageRouteSettings> routes,
        OrchestratorSettings orchestrator)
    {
        RequireChatRoute(routes, orchestrator.RouteId, "Orchestrator.RouteId");
        RequireNonBlank("Orchestrator.Instructions", orchestrator.Instructions);

        var tools = orchestrator.Tools.ToHashSet(StringComparer.Ordinal);
        if (tools.Count != OrchestratorTools.Count || !tools.SetEquals(OrchestratorTools))
        {
            throw Invalid("Orchestrator.Tools", string.Join(",", orchestrator.Tools), "exactly: delegate, publish_report");
        }

        if (orchestrator.Budget.MaxWorkers <= 0)
        {
            throw Invalid("Orchestrator.Budget.MaxWorkers", orchestrator.Budget.MaxWorkers.ToString(), "a positive integer");
        }
    }

    private static void ValidateRoles(
        IReadOnlyDictionary<string, TriageRouteSettings> routes,
        IReadOnlyDictionary<string, TriageToolSettings> tools,
        IReadOnlyDictionary<string, TriageRoleSettings> roles)
    {
        foreach (var (roleName, role) in roles)
        {
            RequireKey(roleName, "Roles");
            RequireChatRoute(routes, role.RouteId, "Roles." + roleName + ".RouteId");
            RequireNonBlank("Roles." + roleName + ".Instructions", role.Instructions);
            RequireNonBlank("Roles." + roleName + ".OutputSchema", role.OutputSchema);
            foreach (var toolName in role.Tools)
            {
                if (!tools.ContainsKey(toolName))
                {
                    throw Invalid("Roles." + roleName + ".Tools", toolName, "a configured worker tool id");
                }
            }
        }
    }

    private static void ValidateTools(
        IReadOnlyDictionary<string, TriageRouteSettings> routes,
        IReadOnlyDictionary<string, TriageToolSettings> tools)
    {
        foreach (var (toolName, tool) in tools)
        {
            RequireKey(toolName, "Tools");
            RequireNonBlank("Tools." + toolName + ".Kind", tool.Kind);
            if (!string.IsNullOrWhiteSpace(tool.EmbeddingRouteId))
            {
                RequireEmbeddingRoute(routes, tool.EmbeddingRouteId, "Tools." + toolName + ".EmbeddingRouteId");
            }
        }
    }

    private static void ValidateRules(
        IReadOnlyDictionary<string, TriageToolSettings> tools,
        IReadOnlyCollection<TriageRuleSettings> rules)
    {
        foreach (var rule in rules)
        {
            RequireKnown("Rules.Type", rule.Type, RuleTypes);
            RequireKnown("Rules.Scope", rule.Scope, RuleScopes);
            if (!string.Equals(rule.Tool, "*", StringComparison.Ordinal) && !tools.ContainsKey(rule.Tool))
            {
                throw Invalid("Rules.Tool", rule.Tool, "'*' or a configured worker tool id");
            }

            if (!string.IsNullOrWhiteSpace(rule.RequiresSuccessfulToolResult) &&
                !tools.ContainsKey(rule.RequiresSuccessfulToolResult))
            {
                throw Invalid("Rules.RequiresSuccessfulToolResult", rule.RequiresSuccessfulToolResult, "a configured worker tool id");
            }
        }
    }
}
