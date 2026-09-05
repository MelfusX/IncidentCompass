namespace IncidentCompass.Application.Governance.Tools;

public interface IAgentToolRegistry
{
    IReadOnlyCollection<AgentToolDescriptor> Tools { get; }

    bool TryGet(string toolId, out AgentToolDescriptor descriptor);
}
