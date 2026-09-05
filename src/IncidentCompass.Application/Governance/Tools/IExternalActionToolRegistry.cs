namespace IncidentCompass.Application.Governance.Tools;

public interface IExternalActionToolRegistry
{
    bool TryGet(string toolId, out IExternalActionTool tool);
}
