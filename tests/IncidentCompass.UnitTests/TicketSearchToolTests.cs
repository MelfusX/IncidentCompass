using System.Text.Json;
using IncidentCompass.Application.Governance.Tools;
using IncidentCompass.Application.Tickets;
using IncidentCompass.Domain.Incidents;
using IncidentCompass.Domain.Incidents.Statuses;

namespace IncidentCompass.UnitTests;

public sealed class TicketSearchToolTests
{
    [Fact]
    public async Task Execute_UsesOnlyBoundedBackendContextAndCreatesBodyFreeArtifact()
    {
        var adapter = new CapturingTicketSearch(new TicketSearchResult(
            TicketSearchOutcome.Matched,
            "ticket_search_matches",
            [new TicketSearchMatch("github", "owner/repo", "42", "Checkout failure", "open", "octocat",
                DateTimeOffset.Parse("2026-08-28T00:00:00Z"), "https://github.com/owner/repo/issues/42", 0.85)],
            "github",
            "owner/repo"));
        var tool = new TicketSearchTool(adapter, TimeProvider.System);

        var result = await tool.ExecuteAsync(CreateContext(), Json("{}"), TestContext.Current.CancellationToken);

        Assert.Equal("fault-fingerprint", adapter.Request!.Fingerprint);
        Assert.Equal("checkout", adapter.Request.ServiceName);
        Assert.Equal("checkout-api", adapter.Request.Component);
        Assert.Equal("TimeoutException", adapter.Request.ErrorType);
        Assert.Equal(["sev1", "payments"], adapter.Request.KnownLabels);
        Assert.True(result.Output.GetProperty("matched").GetBoolean());
        Assert.Equal("github", result.Output.GetProperty("provider").GetString());
        Assert.Equal("owner/repo", result.Output.GetProperty("repository").GetString());
        Assert.DoesNotContain("body", result.Output.GetRawText(), StringComparison.OrdinalIgnoreCase);
        var artifact = Assert.Single(result.Artifacts!);
        Assert.Equal(ArtifactKind.RetrievedItem, artifact.Kind);
        Assert.Equal("ticket:github:owner/repo:42", artifact.DomainRef);
        Assert.Equal("ExistingTicket", artifact.RedactedPayload.GetProperty("evidenceKind").GetString());
        Assert.False(artifact.RedactedPayload.TryGetProperty("body", out _));
    }

    [Fact]
    public async Task Execute_JiraShapedMockPassesTheNeutralPortContract()
    {
        var adapter = new CapturingTicketSearch(new TicketSearchResult(
            TicketSearchOutcome.Matched,
            "ticket_search_matches",
            [new TicketSearchMatch("jira", "INC", "INC-42", "Checkout failure", "Open", null,
                DateTimeOffset.Parse("2026-08-28T00:00:00Z"), "https://jira.example/browse/INC-42", 0.4)],
            "jira",
            "INC"));
        var tool = new TicketSearchTool(adapter, TimeProvider.System);

        var result = await tool.ExecuteAsync(CreateContext(), Json("{}"), TestContext.Current.CancellationToken);

        Assert.Equal("jira", result.Output.GetProperty("items")[0].GetProperty("provider").GetString());
        Assert.Equal("INC-42", result.Output.GetProperty("items")[0].GetProperty("externalId").GetString());
    }

    [Fact]
    public void Validate_RejectsModelSelectedQueryOrRepository()
    {
        var tool = new TicketSearchTool(
            new CapturingTicketSearch(TicketSearchResult.NoMatch("github", "owner/repo")),
            TimeProvider.System);

        Assert.False(tool.Validate(Json("{\"query\":\"secret\"}")).IsValid);
        Assert.False(tool.Validate(Json("{\"repository\":\"other/repo\"}")).IsValid);
        Assert.True(tool.Validate(Json("{}")).IsValid);
    }

    private static AgentToolExecutionContext CreateContext()
    {
        var now = DateTimeOffset.Parse("2026-08-28T00:00:00Z");
        var job = new TriageJob(Guid.NewGuid(), Guid.NewGuid(), TriageJobStatus.Processing, 1,
            "worker", now.AddMinutes(1), null, null, null, "hash", now, now);
        return new AgentToolExecutionContext(
            job, TestTriageConfiguration.Create(), "tickets", "ticket_search", "tenant", "checkout")
        {
            FaultFingerprint = "fault-fingerprint",
            TriggerSignal = new Signal(
                Guid.NewGuid(), "tenant", "otel", null, null, null, FingerprintStrength.Strong, true,
                null, false, null, null, null, null, null, "checkout", "prod", null, "Error",
                "TimeoutException", "Payment timed out", "summary", null, null, null, null, null,
                Json("{\"service.component\":\"checkout-api\",\"incident.labels\":[\"sev1\",\"payments\"]}"),
                Json("{}"), now, now, null)
        };
    }

    private static JsonElement Json(string value)
    {
        using var document = JsonDocument.Parse(value);
        return document.RootElement.Clone();
    }

    private sealed class CapturingTicketSearch(TicketSearchResult result) : ITicketSearch
    {
        public TicketSearchRequest? Request { get; private set; }

        public Task<TicketSearchResult> SearchAsync(TicketSearchRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Request = request;
            return Task.FromResult(result);
        }
    }
}
