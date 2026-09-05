using System.Net;
using System.Text;
using System.Text.Json;
using IncidentCompass.Application.Notifications;
using IncidentCompass.Infrastructure.Notifications.Telegram;
using Microsoft.Extensions.Options;

namespace IncidentCompass.IntegrationTests;

public sealed class TelegramNotificationDispatchTests
{
    [Fact]
    public async Task FrozenPayloadAndFixedBindingProduceOnePlainTextRequest()
    {
        var handler = new RecordingTelegramHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"ok\":true,\"result\":{\"message_id\":12345}}")
        });
        var options = TelegramOptionsFixture.Valid();
        using var tool = new TelegramNotificationActionTool(Options.Create(options), handler);
        var reportId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        var validation = tool.Validate(JsonSerializer.SerializeToElement(new
        {
            originReportId = reportId.ToString("N"),
            routeId = options.RouteId
        }));
        var prepared = tool.Prepare(validation.SanitizedArguments);

        var result = await tool.ExecuteAsync(
            Guid.NewGuid(), prepared.CanonicalPayload, TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        Assert.Equal(1, handler.Calls);
        Assert.Equal(HttpMethod.Post, handler.Method);
        Assert.Equal(
            "https://api.telegram.org/bot123456:abcdefghijklmnopqrstuvwxyz/sendMessage",
            handler.RequestUri);
        Assert.DoesNotContain("parse_mode", handler.Body, StringComparison.Ordinal);
        Assert.Contains("\"chat_id\":\"-100123456\"", handler.Body, StringComparison.Ordinal);
        Assert.Contains("\"protect_content\":true", handler.Body, StringComparison.Ordinal);
        Assert.DoesNotContain(options.BotToken, handler.Body, StringComparison.Ordinal);
        Assert.Equal(
            "{\"messageId\":\"12345\",\"provider\":\"telegram\"}",
            Encoding.UTF8.GetString(result.CanonicalResult));
    }

    [Fact]
    public void ModelControlledRecipientTextAndWrongRouteFailValidation()
    {
        var options = TelegramOptionsFixture.Valid();
        using var tool = new TelegramNotificationActionTool(Options.Create(options), new RecordingTelegramHandler());

        var injected = tool.Validate(JsonSerializer.SerializeToElement(new
        {
            originReportId = Guid.NewGuid().ToString("N"),
            routeId = options.RouteId,
            chatId = "attacker",
            text = "redirect"
        }));
        var wrongRoute = tool.Validate(JsonSerializer.SerializeToElement(new
        {
            originReportId = Guid.NewGuid().ToString("N"),
            routeId = "attacker_route"
        }));

        Assert.False(injected.IsValid);
        Assert.False(wrongRoute.IsValid);
    }
}

internal sealed class RecordingTelegramHandler(HttpResponseMessage? response = null) : HttpMessageHandler
{
    private readonly HttpResponseMessage response = response ?? new(HttpStatusCode.OK)
    {
        Content = new StringContent("{\"ok\":true,\"result\":{\"message_id\":1}}")
    };

    public int Calls { get; private set; }

    public HttpMethod? Method { get; private set; }

    public string? RequestUri { get; private set; }

    public string Body { get; private set; } = string.Empty;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        Calls++;
        Method = request.Method;
        RequestUri = request.RequestUri?.AbsoluteUri;
        Body = await request.Content!.ReadAsStringAsync(cancellationToken);
        return response;
    }
}

internal static class TelegramOptionsFixture
{
    public static TelegramOptions Valid() => new()
    {
        Enabled = true,
        RouteId = "telegram_ops",
        ChatId = "-100123456",
        BotToken = "123456:abcdefghijklmnopqrstuvwxyz",
        TimeoutSeconds = 5
    };
}
