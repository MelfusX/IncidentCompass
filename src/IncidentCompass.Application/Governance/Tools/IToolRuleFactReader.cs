namespace IncidentCompass.Application.Governance.Tools;

internal interface IToolRuleFactReader
{
    Task<int> CountAcceptedUsesAsync(
        string toolName,
        string scope,
        CancellationToken cancellationToken);

    Task<bool> HasSuccessfulToolResultAsync(
        string toolName,
        string scope,
        CancellationToken cancellationToken);
}
