namespace IncidentCompass.Infrastructure.Tickets;

internal static class GitHubIssuesHttpMessageHandlerFactory
{
    public static HttpMessageHandler Create() => new SocketsHttpHandler
    {
        AllowAutoRedirect = false
    };
}
