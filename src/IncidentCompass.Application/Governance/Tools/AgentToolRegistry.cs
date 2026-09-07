namespace IncidentCompass.Application.Governance.Tools;

internal sealed class AgentToolRegistry : IAgentToolRegistry
{
    private readonly IReadOnlyDictionary<string, AgentToolDescriptor> tools;

    public AgentToolRegistry(IEnumerable<AgentToolDescriptor> descriptors)
    {
        var materialized = descriptors.ToArray();
        var invalid = materialized.FirstOrDefault(static descriptor =>
            !AgentToolIdentity.IsValid(descriptor.ToolId));
        if (invalid is not null)
        {
            throw new InvalidOperationException($"Agent tool '{invalid.ToolId}' has an invalid backend identity.");
        }

        var duplicate = materialized
            .GroupBy(static descriptor => descriptor.ToolId, StringComparer.Ordinal)
            .FirstOrDefault(static group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new InvalidOperationException($"Agent tool '{duplicate.Key}' is registered more than once.");
        }

        tools = materialized.ToDictionary(static descriptor => descriptor.ToolId, StringComparer.Ordinal);
    }

    public IReadOnlyCollection<AgentToolDescriptor> Tools => tools.Values.ToArray();

    public bool TryGet(string toolId, out AgentToolDescriptor descriptor) =>
        tools.TryGetValue(toolId, out descriptor!);
}
