using IncidentCompass.Application.Intake.Configuration;
using IncidentCompass.Application.Notifications;
using IncidentCompass.Infrastructure.Notifications.Telegram;
using Microsoft.Extensions.Options;

namespace IncidentCompass.Worker;

internal sealed class TelegramConfigurationStartupValidator(
    ITriageConfigurationRepository configurationRepository,
    IOptions<TelegramOptions> telegramOptions) : IHostedService
{
    private const string BindingError =
        "Telegram host binding does not match the enabled public notification route.";

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var configuration = await configurationRepository.GetCurrentAsync(cancellationToken);
        if (!configuration.Actions.AllowedTools.Contains(
                TelegramNotificationToolDescriptor.ToolId, StringComparer.Ordinal))
        {
            return;
        }

        if (!configuration.Tools.TryGetValue(TelegramNotificationToolDescriptor.ToolId, out var tool) ||
            !string.Equals(tool.Kind, "external_action", StringComparison.Ordinal) ||
            !string.Equals(tool.Category, "notification", StringComparison.Ordinal) ||
            !string.Equals(tool.LogicalTargetId,
                TelegramNotificationToolDescriptor.LogicalTargetId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(BindingError);
        }

        if (string.Equals(configuration.Actions.DefaultMode, "disabled", StringComparison.Ordinal) ||
            string.Equals(tool.Mode, "disabled", StringComparison.Ordinal))
        {
            return;
        }

        var routes = configuration.Actions.NotificationRoutes
            .Where(route => string.Equals(
                route.ToolId, TelegramNotificationToolDescriptor.ToolId, StringComparison.Ordinal))
            .Take(2)
            .ToArray();
        var options = telegramOptions.Value;
        if (routes.Length != 1 || !options.Enabled ||
            !string.Equals(routes[0].RouteId, options.RouteId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(BindingError);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
