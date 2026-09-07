using Microsoft.AspNetCore.Mvc.Testing;

namespace IncidentCompass.IntegrationTests;

internal sealed record IngestionTestScope(
    WebApplicationFactory<Program> Factory,
    HttpClient Client,
    string ConnectionString) : IDisposable
{
    public void Dispose()
    {
        Client.Dispose();
        Factory.Dispose();
    }
}
