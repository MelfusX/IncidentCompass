using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using IncidentCompass.Application.Core.ModelClients;
using IncidentCompass.Application.Core.Serialization;
using IncidentCompass.Application.Governance.PostReportActions;
using IncidentCompass.Application.Governance.Tools;
using IncidentCompass.Application.Governance.Validation;
using IncidentCompass.Domain.Incidents.Actions;
using Microsoft.Extensions.Options;

namespace IncidentCompass.Infrastructure.Tickets;

public sealed class GitHubIssueCommentExternalActionTool : IExternalActionTool, IDisposable
{
    private readonly GitHubIssuesOptions options;
    private readonly HttpClient client;

    public GitHubIssueCommentExternalActionTool(
        IOptions<GitHubIssuesOptions> options,
        HttpMessageHandler? handler = null)
    {
        this.options = options.Value;
        client = new HttpClient(handler ?? GitHubIssuesHttpMessageHandlerFactory.Create(), disposeHandler: true)
        {
            BaseAddress = GitHubIssuesTicketSearch.Authority,
            Timeout = Timeout.InfiniteTimeSpan
        };
        AdapterBindingFingerprint = ExternalActionBinding.ComputeFingerprint(
            "github-issues",
            LogicalTargetId,
            GitHubIssuesTicketSearch.Authority.AbsoluteUri,
            this.options.ConfiguredRepository ?? "unconfigured");
    }

    public ActionCategory Category => ActionCategory.TicketUpdate;
    public string LogicalTargetId => TicketUpdatePostReportActionWorkflow.UpdateLogicalTargetId;
    public string AdapterBindingFingerprint { get; }
    public AiToolDefinition Definition { get; } = new(
        TicketUpdatePostReportActionWorkflow.UpdateToolId,
        "Add one backend-governed evidence comment to one cited ticket.",
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
                ["proposalKey"] = new JsonObject { ["type"] = "string" },
                ["ticketId"] = new JsonObject { ["type"] = "string", ["pattern"] = "^[1-9][0-9]{0,9}$" }
            },
            ["required"] = new JsonArray("originReportId", "proposalKey", "ticketId")
        }));

    public ToolValidationResult Validate(JsonElement arguments)
    {
        if (!options.IsConfigured || arguments.ValueKind != JsonValueKind.Object ||
            arguments.EnumerateObject().Any(static property =>
                property.Name is not ("originReportId" or "proposalKey" or "ticketId")) ||
            !arguments.TryGetProperty("originReportId", out var report) ||
            report.ValueKind != JsonValueKind.String ||
            !Guid.TryParseExact(report.GetString(), "N", out var reportId) ||
            !arguments.TryGetProperty("proposalKey", out var key) || key.ValueKind != JsonValueKind.String ||
            !string.Equals(key.GetString(),
                $"post-report:v1:{reportId:N}:{TicketUpdatePostReportActionWorkflow.UpdateToolId}",
                StringComparison.Ordinal) ||
            !arguments.TryGetProperty("ticketId", out var ticket) || ticket.ValueKind != JsonValueKind.String ||
            !int.TryParse(ticket.GetString(), NumberStyles.None, CultureInfo.InvariantCulture, out var issueNumber) ||
            issueNumber <= 0 || ticket.GetString() != issueNumber.ToString(CultureInfo.InvariantCulture))
        {
            return ToolValidationResult.Invalid(
                "invalid_arguments", "Ticket update arguments are invalid.");
        }

        return ToolValidationResult.Valid(JsonSerializer.SerializeToElement(new
        {
            originReportId = reportId.ToString("N"),
            proposalKey = key.GetString(),
            ticketId = issueNumber.ToString(CultureInfo.InvariantCulture)
        }));
    }

    public ExternalActionPreparation Prepare(JsonElement sanitizedArguments)
    {
        var reportId = Guid.ParseExact(
            sanitizedArguments.GetProperty("originReportId").GetString()!, "N");
        var proposalKey = sanitizedArguments.GetProperty("proposalKey").GetString()!;
        var ticketId = sanitizedArguments.GetProperty("ticketId").GetString()!;
        var marker = GitHubIssueCommentMarker.Create(proposalKey, reportId, ticketId);
        var body = $"Governed incident report update: {reportId:N}.\n\n" +
            "Review the report and its cited evidence before acting.\n\n" +
            GitHubIssueCommentMarker.Comment(marker);
        var payload = new JsonObject
        {
            ["body"] = body,
            ["marker"] = marker,
            ["originReportId"] = reportId.ToString("N"),
            ["schemaVersion"] = 1,
            ["ticketId"] = ticketId
        };
        return new ExternalActionPreparation(
            Encoding.UTF8.GetBytes(CanonicalJsonSerializer.Canonicalize(payload)),
            $"Add one evidence comment to ticket {ticketId} for incident report {reportId:N}.");
    }

    public async Task<ExternalActionExecutionResult> ExecuteAsync(
        Guid actionId,
        ReadOnlyMemory<byte> canonicalPayload,
        CancellationToken cancellationToken)
    {
        if (!options.IsConfigured)
        {
            return Failure("github_issue_comment_binding_unavailable");
        }

        if (!GitHubIssueCommentMarker.TryReadPayload(canonicalPayload, out var payload))
        {
            return Failure("github_issue_comment_payload_invalid");
        }

        var preflight = await GitHubIssueCommentPreflight.SafeCheckAsync(
            client, options, payload.IssueNumber, payload.Marker, cancellationToken);
        if (preflight.FailureCode is not null)
        {
            return Failure(preflight.FailureCode);
        }
        if (preflight.Existing is not null)
        {
            return preflight.Existing;
        }
        if (cancellationToken.IsCancellationRequested)
        {
            return Failure("github_issue_comment_cancelled_before_write");
        }

        using var request = CreateRequest(payload.IssueNumber, payload.Body);
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(TimeSpan.FromSeconds(options.TimeoutSeconds));
            using var response = await client.SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
            return await GitHubIssueCommentResponseParser.ParseCreateAsync(
                response, options, payload.IssueNumber, payload.Marker, deadline.Token);
        }
        catch (Exception exception) when (
            exception is OperationCanceledException or HttpRequestException or IOException or JsonException)
        {
            return Failure("dispatch_outcome_unknown");
        }
    }

    public void Dispose() => client.Dispose();

    private HttpRequestMessage CreateRequest(int issueNumber, string body)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/repos/{options.Owner}/{options.Repository}/issues/{issueNumber}/comments")
        {
            Content = new ByteArrayContent(Encoding.UTF8.GetBytes(
                CanonicalJsonSerializer.Canonicalize(new JsonObject { ["body"] = body })))
        };
        GitHubIssueCreateRequestFactory.AddGitHubHeaders(request, options.Token!);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json")
        {
            CharSet = "utf-8"
        };
        return request;
    }

    private static ExternalActionExecutionResult Failure(string code) =>
        GitHubIssueCommentResponseParser.Failure(code);
}
