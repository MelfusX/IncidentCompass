namespace IncidentCompass.Infrastructure.Notifications.Telegram;

public static class TelegramHttpMessageHandlerFactory
{
    public static HttpMessageHandler Create() => new SocketsHttpHandler
    {
        AllowAutoRedirect = false,
        AutomaticDecompression = System.Net.DecompressionMethods.None,
        UseCookies = false
    };
}
