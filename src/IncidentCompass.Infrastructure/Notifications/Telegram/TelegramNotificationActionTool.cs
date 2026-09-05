using System.Text.Json;
using System.Text.Json.Nodes;
using IncidentCompass.Application.Core.ModelClients;
using IncidentCompass.Application.Core.Serialization;
using IncidentCompass.Application.Governance.Tools;
using IncidentCompass.Application.Governance.Validation;
using IncidentCompass.Application.Notifications;
using IncidentCompass.Domain.Incidents.Actions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace IncidentCompass.Infrastructure.Notifications.Telegram;

public sealed partial class TelegramNotificationActionTool : IExternalActionTool, IDisposable
{
    public static readonly Uri Authority = new("https://api.telegram.org", UriKind.Absolute);
    private readonly TelegramOptions options;
    private readonly HttpClient client;
    private readonly ILogger logger;

    public TelegramNotificationActionTool(
        IOptions<TelegramOptions> options,
        HttpMessageHandler? handler = null,
        ILogger<TelegramNotificationActionTool>? logger = null)
    {
        this.options = options.Value;
        this.logger = logger ?? NullLogger<TelegramNotificationActionTool>.Instance;
        client = new HttpClient(handler ?? TelegramHttpMessageHandlerFactory.Create(), disposeHandler: true)
        {
            BaseAddress = Authority,
            Timeout = Timeout.InfiniteTimeSpan
        };
        AdapterBindingFingerprint = ExternalActionBinding.ComputeFingerprint(
            "telegram", LogicalTargetId, Authority.AbsoluteUri, this.options.ChatId);
    }

    public ActionCategory Category => ActionCategory.Notification;

    public string LogicalTargetId => TelegramNotificationWorkflow.LogicalTargetIdValue;

    public string AdapterBindingFingerprint { get; }

    public AiToolDefinition Definition { get; } = new(
        TelegramNotificationWorkflow.ToolIdValue,
        "Send one backend-routed Telegram incident notification.",
        "v1",
        CanonicalJsonSerializer.ToElement(new JsonObject
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["properties"] = new JsonObject
            {
                ["originReportId"] = new JsonObject
                {
                    ["type"] = "string",
                    ["pattern"] = "^[0-9a-f]{32}$"
                },
                ["routeId"] = new JsonObject { ["type"] = "string" }
            },
            ["required"] = new JsonArray("originReportId", "routeId")
        }));

    public ToolValidationResult Validate(JsonElement arguments)
    {
        if (!options.Enabled || arguments.ValueKind != JsonValueKind.Object ||
            arguments.EnumerateObject().Any(static property => property.Name is not ("originReportId" or "routeId")) ||
            !arguments.TryGetProperty("originReportId", out var report) || report.ValueKind != JsonValueKind.String ||
            !Guid.TryParseExact(report.GetString(), "N", out var reportId) ||
            !arguments.TryGetProperty("routeId", out var route) || route.ValueKind != JsonValueKind.String ||
            !string.Equals(route.GetString(), options.RouteId, StringComparison.Ordinal))
        {
            return ToolValidationResult.Invalid("invalid_arguments", "Telegram notification arguments are invalid.");
        }

        return ToolValidationResult.Valid(JsonSerializer.SerializeToElement(new
        {
            originReportId = reportId.ToString("N"),
            routeId = options.RouteId
        }));
    }

    public ExternalActionPreparation Prepare(JsonElement sanitizedArguments) =>
        TelegramNotificationPayloadFactory.Create(new TelegramNotificationWorkflowInput(
            Guid.ParseExact(sanitizedArguments.GetProperty("originReportId").GetString()!, "N"),
            sanitizedArguments.GetProperty("routeId").GetString()!));

    public async Task<ExternalActionExecutionResult> ExecuteAsync(
        Guid actionId,
        ReadOnlyMemory<byte> canonicalPayload,
        CancellationToken cancellationToken)
    {
        if (!options.Enabled)
        {
            return Failure("telegram_binding_unavailable");
        }

        HttpRequestMessage request;
        try
        {
            request = TelegramNotificationRequestFactory.Create(canonicalPayload, options);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        {
            return Failure("telegram_payload_invalid");
        }

        using (request)
        using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            deadline.CancelAfter(TimeSpan.FromSeconds(options.TimeoutSeconds));
            using var response = await client.SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
            var result = await TelegramNotificationResponseParser.ParseAsync(response, deadline.Token);
            if (!result.Succeeded)
            {
                LogProviderFailure(logger, result.FailureCode ?? "telegram_failure");
            }

            return result;
        }
    }

    public void Dispose() => client.Dispose();

    private static ExternalActionExecutionResult Failure(string code)
    {
        var payload = new JsonObject { ["code"] = code, ["provider"] = "telegram" };
        return new ExternalActionExecutionResult(
            false,
            System.Text.Encoding.UTF8.GetBytes(CanonicalJsonSerializer.Canonicalize(payload)),
            "Telegram dispatch was rejected before sending.",
            code);
    }

    [LoggerMessage(
        EventId = 2401,
        Level = LogLevel.Warning,
        Message = "Telegram notification provider returned failure code {FailureCode}.")]
    private static partial void LogProviderFailure(ILogger logger, string failureCode);
}
