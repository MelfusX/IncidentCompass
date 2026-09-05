using IncidentCompass.Application.Notifications;

namespace IncidentCompass.UnitTests;

public sealed class NotificationRouteSelectorTests
{
    [Fact]
    public void Select_UsesNormalizedSelectorsAndFirstMatchOnly()
    {
        var routes = new[]
        {
            new NotificationRoute("primary", "telegram_notify", "checkout", "production", ["critical", "fatal"]),
            new NotificationRoute("fallback", "telegram_other", null, null, ["critical"])
        };

        var selected = NotificationRouteSelector.Select(
            routes, " Checkout ", "PRODUCTION", "Critical");

        Assert.Same(routes[0], selected);
    }

    [Theory]
    [InlineData("warning")]
    [InlineData(null)]
    [InlineData("")]
    public void Select_RejectsSeverityOutsideTheClosedRouteSet(string? severity)
    {
        var routes = new[]
        {
            new NotificationRoute("primary", "telegram_notify", null, null, ["error"])
        };

        Assert.Null(NotificationRouteSelector.Select(routes, "checkout", "production", severity));
    }
}
