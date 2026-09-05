using IncidentCompass.Application.Tickets;

namespace IncidentCompass.UnitTests;

public sealed class TicketPortArchitectureTests
{
    [Fact]
    public void ApplicationTicketPort_IsSearchOnlyAndProviderNeutral()
    {
        var methods = typeof(ITicketSearch).GetMethods();
        var applicationTypes = typeof(ITicketSearch).Assembly.GetTypes()
            .Where(type => type.Namespace == "IncidentCompass.Application.Tickets")
            .ToArray();
        var requestProperties = typeof(TicketSearchRequest).GetProperties()
            .Select(property => property.Name)
            .ToArray();

        Assert.Equal("SearchAsync", Assert.Single(methods).Name);
        Assert.DoesNotContain(applicationTypes,
            type => ContainsProviderOrTransportConcept(type.Name));
        Assert.DoesNotContain(requestProperties,
            property => property is "Repository" or "Owner" or "TenantId" or "Credential" or "ApiUrl");
    }

    private static bool ContainsProviderOrTransportConcept(string name) =>
        name.Contains("GitHub", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("Jira", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("Http", StringComparison.OrdinalIgnoreCase);
}
