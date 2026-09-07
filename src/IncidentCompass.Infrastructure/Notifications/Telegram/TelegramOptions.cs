namespace IncidentCompass.Infrastructure.Notifications.Telegram;

public sealed class TelegramOptions
{
    public const string SectionName = "IncidentCompass:Telegram";

    public bool Enabled { get; set; }

    public string RouteId { get; set; } = string.Empty;

    public string ChatId { get; set; } = string.Empty;

    public string BotToken { get; set; } = string.Empty;

    public int TimeoutSeconds { get; set; } = 10;
}
