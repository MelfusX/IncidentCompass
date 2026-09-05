using System.Text.RegularExpressions;
using IncidentCompass.Application.Governance.Tools;
using Microsoft.Extensions.Options;

namespace IncidentCompass.Infrastructure.Notifications.Telegram;

public sealed partial class TelegramOptionsValidator : IValidateOptions<TelegramOptions>
{
    public ValidateOptionsResult Validate(string? name, TelegramOptions options)
    {
        if (options.TimeoutSeconds is < 1 or > 120)
        {
            return ValidateOptionsResult.Fail("Telegram timeout must be from 1 through 120 seconds.");
        }

        var hasAnyBinding = !string.IsNullOrEmpty(options.RouteId) ||
            !string.IsNullOrEmpty(options.ChatId) || !string.IsNullOrEmpty(options.BotToken);
        if (!options.Enabled && !hasAnyBinding)
        {
            return ValidateOptionsResult.Success;
        }

        if (!AgentToolIdentity.IsValid(options.RouteId) ||
            !ChatIdPattern().IsMatch(options.ChatId) ||
            !BotTokenPattern().IsMatch(options.BotToken))
        {
            return ValidateOptionsResult.Fail(
                "Telegram requires one safe route id, chat id and bot token when a binding is configured.");
        }

        return ValidateOptionsResult.Success;
    }

    [GeneratedRegex("^-?[1-9][0-9]{0,19}$", RegexOptions.CultureInvariant)]
    private static partial Regex ChatIdPattern();

    [GeneratedRegex("^[0-9]{5,20}:[A-Za-z0-9_-]{20,80}$", RegexOptions.CultureInvariant)]
    private static partial Regex BotTokenPattern();
}
