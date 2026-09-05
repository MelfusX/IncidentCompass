using System.Text;
using System.Text.Json;
using IncidentCompass.Application.Notifications;

namespace IncidentCompass.UnitTests;

public sealed class TelegramNotificationPayloadFactoryTests
{
    [Fact]
    public void Create_ProducesBoundedCanonicalBackendOwnedPayload()
    {
        var reportId = Guid.Parse("11111111-2222-3333-4444-555555555555");

        var prepared = TelegramNotificationPayloadFactory.Create(
            new TelegramNotificationWorkflowInput(reportId, "telegram_ops"));

        Assert.Equal(
            "{\"originReportId\":\"11111111222233334444555555555555\",\"routeId\":\"telegram_ops\",\"schemaVersion\":1,\"text\":\"Incident report 11111111222233334444555555555555 is ready for review.\"}",
            Encoding.UTF8.GetString(prepared.CanonicalPayload));
        Assert.True(prepared.CanonicalPayload.Length < 4096);
        Assert.DoesNotContain("chat", prepared.ReviewSummary, StringComparison.OrdinalIgnoreCase);
        using var document = JsonDocument.Parse(prepared.CanonicalPayload);
        Assert.Equal(4, document.RootElement.EnumerateObject().Count());
    }

    [Fact]
    public void Create_RejectsMissingBackendIdentity()
    {
        Assert.Throws<ArgumentException>(() => TelegramNotificationPayloadFactory.Create(
            new TelegramNotificationWorkflowInput(Guid.Empty, "telegram_ops")));
    }
}
