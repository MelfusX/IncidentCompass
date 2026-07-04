using System.Text.Json;
using IncidentCompass.Application.Intake.Configuration;
using IncidentCompass.Application.Intake.Normalization;
using static IncidentCompass.Infrastructure.Intake.TriageConfigurationValidationGuards;

namespace IncidentCompass.Infrastructure.Intake;

internal sealed class TriageConfigurationLoadValidator(SignalNormalizerRegistry normalizerRegistry)
{
    private static readonly HashSet<string> RouteKinds = new(["Chat", "Embedding"], StringComparer.Ordinal);
    private static readonly HashSet<string> ProviderKinds = new(["Mock", "OpenAICompatible"], StringComparer.Ordinal);
    private static readonly HashSet<string> ToolKinds = new(["internal"], StringComparer.Ordinal);
    private const string MemoryRoleName = "memory";
    private const string MemorySearchToolName = "memory_search";
    private static readonly HashSet<string> OrchestratorTools = new(["delegate", "publish_report"], StringComparer.Ordinal);

    public void Validate(TriageConfiguration configuration)
    {
        FaultGroupingSettingsLoadValidator.Validate(configuration.FaultGrouping);
        ValidateAllowedSources(configuration.Ingestion);
        ValidateProviders(configuration.Providers);
        ValidateRoutes(configuration.Providers, configuration.Routes);
        ValidateOrchestrator(configuration.Routes, configuration.Orchestrator);
        ValidateRoles(configuration.Routes, configuration.Tools, configuration.Roles);
        ValidateTools(configuration.Routes, configuration.Tools);
        TriageRuleLoadValidator.Validate(configuration.Tools, configuration.Rules);
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

            if (route.MaxOutputTokens is <= 0)
            {
                throw Invalid("Routes." + routeId + ".MaxOutputTokens", route.MaxOutputTokens.Value.ToString(), "a positive integer when set");
            }

            if (route.ContextWindowTokens is <= 0)
            {
                throw Invalid("Routes." + routeId + ".ContextWindowTokens", route.ContextWindowTokens.Value.ToString(), "a positive integer when set");
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

        if (orchestrator.Budget.MaxTokens <= 0)
        {
            throw Invalid("Orchestrator.Budget.MaxTokens", orchestrator.Budget.MaxTokens.ToString(), "a positive integer");
        }

        if (orchestrator.Budget.MaxWallClockSeconds <= 0)
        {
            throw Invalid("Orchestrator.Budget.MaxWallClockSeconds", orchestrator.Budget.MaxWallClockSeconds.ToString(), "a positive integer");
        }

        if (orchestrator.Budget.MaxReprompts < 0)
        {
            throw Invalid("Orchestrator.Budget.MaxReprompts", orchestrator.Budget.MaxReprompts.ToString(), "zero or a positive integer");
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
            ValidateOutputSchema(roleName, role.OutputSchema);
            foreach (var toolName in role.Tools)
            {
                if (!tools.ContainsKey(toolName))
                {
                    throw Invalid("Roles." + roleName + ".Tools", toolName, "a configured worker tool id");
                }

                if (string.Equals(toolName, MemorySearchToolName, StringComparison.Ordinal) &&
                    !string.Equals(roleName, MemoryRoleName, StringComparison.Ordinal))
                {
                    throw Invalid("Roles." + roleName + ".Tools", toolName, "memory_search granted only to the memory role");
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
            RequireKnown("Tools." + toolName + ".Kind", tool.Kind, ToolKinds);
            if (string.Equals(toolName, MemorySearchToolName, StringComparison.Ordinal))
            {
                RequireNonBlank("Tools." + toolName + ".EmbeddingRouteId", tool.EmbeddingRouteId ?? string.Empty);
                RequireEmbeddingRoute(routes, tool.EmbeddingRouteId!, "Tools." + toolName + ".EmbeddingRouteId");
                if (tool.TopK is <= 0)
                {
                    throw Invalid("Tools." + toolName + ".TopK", tool.TopK.Value.ToString(), "a positive integer when set");
                }

                if (tool.MinScore is < -1 or > 1)
                {
                    throw Invalid("Tools." + toolName + ".MinScore", tool.MinScore.Value.ToString(), "a score between -1 and 1 when set");
                }

                continue;
            }

            if (!string.IsNullOrWhiteSpace(tool.EmbeddingRouteId))
            {
                RequireEmbeddingRoute(routes, tool.EmbeddingRouteId, "Tools." + toolName + ".EmbeddingRouteId");
            }
        }
    }

    private static void ValidateOutputSchema(string roleName, string outputSchema)
    {
        try
        {
            using var document = JsonDocument.Parse(outputSchema);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw Invalid("Roles." + roleName + ".OutputSchema", "non-object", "a JSON object schema");
            }
        }
        catch (JsonException exception)
        {
            throw TriageConfigurationLoadException.InvalidJson("Roles." + roleName + ".OutputSchema", exception);
        }
    }
}
