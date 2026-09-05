using System.Globalization;
using System.Text.Json;
using IncidentCompass.Application.Tickets;
using IncidentCompass.Domain.Incidents;

namespace IncidentCompass.UnitTests;

public sealed class TicketSearchContextExtractorTests
{
    [Fact]
    public void Create_UsesFixedComponentOrderAndBoundsEveryInput()
    {
        var labels = Enumerable.Range(0, 12).Select(index => new string((char)('a' + index), 80)).ToArray();
        var signal = CreateSignal(JsonSerializer.SerializeToElement(new Dictionary<string, object?>
        {
            ["service.component"] = " ",
            ["component"] = new string('c', 200),
            ["code.namespace"] = "ignored",
            ["incident.labels"] = labels
        }));

        var request = TicketSearchContextExtractor.Create(
            new string('f', 200),
            new string('s', 200),
            signal);

        Assert.Equal(128, request.Fingerprint.Length);
        Assert.Equal(128, request.ServiceName.Length);
        Assert.Equal(128, request.Component!.Length);
        Assert.Equal(128, request.ErrorType!.Length);
        Assert.Equal(4096, request.ErrorMessage!.Length);
        Assert.Equal(10, request.KnownLabels.Count);
        Assert.All(request.KnownLabels, label => Assert.Equal(64, label.Length));
    }

    [Fact]
    public void Create_IgnoresNonStringComponentAndNonArrayLabels()
    {
        var signal = CreateSignal(JsonSerializer.SerializeToElement(new Dictionary<string, object?>
        {
            ["service.component"] = 42,
            ["incident.labels"] = "sev1"
        }));

        var request = TicketSearchContextExtractor.Create("fingerprint", "service", signal);

        Assert.Null(request.Component);
        Assert.Empty(request.KnownLabels);
    }

    [Fact]
    public void Create_DoesNotSplitSupplementaryScalarsAtSignalBounds()
    {
        const string scalar = "😀";
        var signal = CreateSignal(JsonSerializer.SerializeToElement(new Dictionary<string, object?>
        {
            ["component"] = new string('c', 127) + scalar,
            ["incident.labels"] = new[] { new string('l', 63) + scalar }
        })) with
        {
            ErrorMessage = new string('m', 4095) + scalar
        };

        var request = TicketSearchContextExtractor.Create(
            new string('f', 127) + scalar,
            new string('s', 127) + scalar,
            signal);

        Assert.Equal(127, request.Fingerprint.Length);
        Assert.Equal(127, request.ServiceName.Length);
        Assert.Equal(127, request.Component!.Length);
        Assert.Equal(4095, request.ErrorMessage!.Length);
        Assert.Equal(63, Assert.Single(request.KnownLabels).Length);
        Assert.All(new[] { request.Fingerprint, request.ServiceName, request.Component, request.ErrorMessage, request.KnownLabels[0] },
            value => Assert.False(char.IsSurrogate(value![^1])));
    }

    private static Signal CreateSignal(JsonElement attributes)
    {
        var now = DateTimeOffset.Parse("2026-08-28T00:00:00Z", CultureInfo.InvariantCulture);
        return new Signal(
            Guid.NewGuid(), "tenant", "otel", null, null, null, FingerprintStrength.Strong, true,
            null, false, null, null, null, null, null, "service", "prod", null, "Error",
            new string('e', 200), new string('m', 5000), "summary", null, null, null, null, null,
            attributes, JsonSerializer.SerializeToElement(new { }), now, now, null);
    }
}
