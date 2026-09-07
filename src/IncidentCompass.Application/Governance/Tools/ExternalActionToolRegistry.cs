namespace IncidentCompass.Application.Governance.Tools;

internal sealed class ExternalActionToolRegistry : IExternalActionToolRegistry
{
    private readonly Dictionary<string, IExternalActionTool> tools;

    public ExternalActionToolRegistry(
        IEnumerable<IExternalActionTool> registeredTools,
        IAgentToolRegistry toolRegistry)
    {
        var materialized = registeredTools.ToArray();
        var duplicate = materialized
            .GroupBy(static tool => tool.Definition.Name, StringComparer.Ordinal)
            .FirstOrDefault(static group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new InvalidOperationException(
                $"External action tool '{duplicate.Key}' is registered more than once.");
        }

        foreach (var tool in materialized)
        {
            if (!toolRegistry.TryGet(tool.Definition.Name, out var descriptor) ||
                descriptor.Capability != AgentToolCapability.ExternalAction ||
                descriptor.Category != tool.Category ||
                !string.Equals(descriptor.LogicalTargetId, tool.LogicalTargetId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"External action tool '{tool.Definition.Name}' does not match its backend descriptor.");
            }
        }

        tools = materialized.ToDictionary(static tool => tool.Definition.Name, StringComparer.Ordinal);
    }

    public bool TryGet(string toolId, out IExternalActionTool tool) =>
        tools.TryGetValue(toolId, out tool!);
}
