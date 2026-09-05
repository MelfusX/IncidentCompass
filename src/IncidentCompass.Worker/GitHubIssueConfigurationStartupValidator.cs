using IncidentCompass.Application.Intake.Configuration;
using IncidentCompass.Application.Tickets;
using IncidentCompass.Infrastructure.Tickets;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace IncidentCompass.Worker;

internal sealed class GitHubIssueConfigurationStartupValidator(
    ITriageConfigurationRepository configurationRepository,
    IOptions<GitHubIssuesOptions> githubOptions) : IHostedService
{
    private const string BindingError =
        "GitHub issue host binding does not match the enabled public ticket-create action.";

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var configuration = await configurationRepository.GetCurrentAsync(cancellationToken);
        if (!configuration.Actions.AllowedTools.Contains(
                TicketCreateTool.ToolId, StringComparer.Ordinal))
        {
            return;
        }

        if (!configuration.Tools.TryGetValue(TicketCreateTool.ToolId, out var tool) ||
            !string.Equals(tool.Kind, "external_action", StringComparison.Ordinal) ||
            !string.Equals(tool.Category, "ticket_create", StringComparison.Ordinal) ||
            !string.Equals(tool.LogicalTargetId, TicketCreateTool.LogicalTargetId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(BindingError);
        }

        if (string.Equals(configuration.Actions.DefaultMode, "disabled", StringComparison.Ordinal) ||
            string.Equals(tool.Mode, "disabled", StringComparison.Ordinal))
        {
            return;
        }

        if (!githubOptions.Value.IsConfigured)
        {
            throw new InvalidOperationException(BindingError);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
