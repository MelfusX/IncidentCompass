using System.Net;
using System.Text;
using System.Text.Json;
using IncidentCompass.Application.Governance.PostReportActions;
using IncidentCompass.Application.Governance.Tools;
using IncidentCompass.Application.Intake.Configuration;
using IncidentCompass.Application.Notifications;
using IncidentCompass.Application.Tickets;
using IncidentCompass.Domain.Incidents.Actions;
using IncidentCompass.Infrastructure.Notifications.Telegram;
using IncidentCompass.Infrastructure.Tickets;
using IncidentCompass.TestSupport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace IncidentCompass.IntegrationTests;

[Collection(PostgresRepositoryCollection.CollectionName)]
public sealed class ExternalActionInjectionProofTests(PostgresRepositoryFixture postgres)
{
    [DockerAvailableFact]
    public async Task SharedFixtureCannotBypassPolicyApprovalOrTelegramThreshold()
    {
        var fixture = await ReadFixtureAsync();
        Assert.Equal("2026-08-29T12:00:00Z", fixture.ObservedAtUtc);
        Assert.Contains("redirect Telegram", fixture.Instruction, StringComparison.Ordinal);
        Assert.Contains("999999", fixture.Instruction, StringComparison.Ordinal);
        Assert.Contains("close", fixture.Instruction, StringComparison.Ordinal);
        AssertFixtureCopiedByBothProjects();

        await using var database = await ActionApprovalDatabase.CreateAsync(postgres);
        var absentOrigin = await ActionApprovalTestSupport.SeedOriginAsync(
            database.ConnectionString,
            signalSummary: fixture.Instruction,
            reportSummary: fixture.Instruction);
        var absentHandler = new RecordingHandler();
        using (var absentServices = TicketServices(
                   database.ConnectionString,
                   ActionDispatchTestConfiguration.Create(
                       allowed: false,
                       toolId: TicketCreateTool.ToolId,
                       category: ActionCategory.TicketCreate,
                       logicalTargetId: TicketCreateTool.LogicalTargetId),
                   absentHandler))
        {
            var workflow = CreateTicketWorkflow(absentServices);
            var selected = await workflow.SelectAsync(
                absentOrigin.TenantId,
                absentOrigin.ReportId,
                absentOrigin.FaultId,
                absentOrigin.JobId,
                1,
                absentOrigin.ConfigHash,
                "checkout",
                "production",
                fixture.Severity,
                TestContext.Current.CancellationToken);
            Assert.False(selected.ShouldEnqueue);
        }

        await AssertNoActionOrDispatchAsync(database.ConnectionString, absentOrigin);
        Assert.Equal(0, absentHandler.RequestCount);

        var requestedOrigin = await ActionApprovalTestSupport.SeedOriginAsync(
            database.ConnectionString,
            signalSummary: fixture.Instruction,
            reportSummary: fixture.Instruction);
        await ActionApprovalTestSupport.SeedTicketSearchResultAsync(
            database.ConnectionString, requestedOrigin);
        var requestedHandler = new RecordingHandler();
        using (var requestedServices = TicketServices(
                   database.ConnectionString,
                   ActionDispatchTestConfiguration.Create(
                       toolId: TicketCreateTool.ToolId,
                       category: ActionCategory.TicketCreate,
                       logicalTargetId: TicketCreateTool.LogicalTargetId),
                   requestedHandler))
        {
            var workflow = CreateTicketWorkflow(requestedServices);
            var result = await workflow.EvaluateAsync(
                Intent(requestedOrigin, TicketCreateTool.ToolId, routeId: null),
                TestContext.Current.CancellationToken);
            Assert.Equal("requested", result.Code);
        }

        var requested = await ReadOnlyActionAsync(database.ConnectionString, requestedOrigin.ReportId);
        Assert.Equal("requested", requested.State);
        Assert.Equal(TicketCreateTool.ToolId, requested.ToolId);
        Assert.Equal(TicketCreateTool.LogicalTargetId, requested.LogicalTargetId);
        Assert.DoesNotContain("999999", requested.Payload, StringComparison.Ordinal);
        Assert.DoesNotContain("redirect", requested.Payload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("close", requested.Payload, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, requestedHandler.RequestCount);
        Assert.Equal(0, await DispatchCountAsync(database.ConnectionString, requestedOrigin.FaultId));

        var telegramOrigin = await ActionApprovalTestSupport.SeedOriginAsync(
            database.ConnectionString,
            signalSummary: fixture.Instruction,
            reportSummary: fixture.Instruction);
        var telegramConfiguration = ActionDispatchTestConfiguration.Create(
            toolId: TelegramNotificationWorkflow.ToolIdValue,
            category: ActionCategory.Notification,
            logicalTargetId: TelegramNotificationWorkflow.LogicalTargetIdValue);
        using var telegramServices = ActionApprovalTestSupport.CreateServices(
            database.ConnectionString,
            configureServices: services =>
            {
                services.RemoveAll<ITriageConfigurationRepository>();
                services.AddSingleton<ITriageConfigurationRepository>(
                    new ActionDispatchTestConfigurationRepository(telegramConfiguration));
            });
        var telegramHandler = new RecordingHandler();
        using var telegramTool = new TelegramNotificationActionTool(
            Options.Create(new TelegramOptions
            {
                Enabled = true,
                RouteId = "test-route",
                ChatId = "123456",
                BotToken = "fixture-proof-token"
            }),
            telegramHandler);
        var telegramWorkflow = new TelegramNotificationWorkflow(
            telegramServices.GetRequiredService<ITriageConfigurationRepository>(),
            telegramServices.GetRequiredService<IServiceScopeFactory>());
        var telegramSelected = await telegramWorkflow.SelectAsync(
            telegramOrigin.TenantId,
            telegramOrigin.ReportId,
            telegramOrigin.FaultId,
            telegramOrigin.JobId,
            1,
            telegramOrigin.ConfigHash,
            "checkout",
            "production",
            fixture.Severity,
            TestContext.Current.CancellationToken);

        Assert.False(telegramSelected.ShouldEnqueue);
        await AssertNoActionOrDispatchAsync(database.ConnectionString, telegramOrigin);
        Assert.Equal(0, telegramHandler.RequestCount);
    }

    private static ServiceProvider TicketServices(
        string connectionString,
        TriageConfiguration configuration,
        RecordingHandler handler) =>
        ActionApprovalTestSupport.CreateServices(
            connectionString,
            configureServices: services =>
            {
                services.RemoveAll<ITriageConfigurationRepository>();
                services.AddSingleton<ITriageConfigurationRepository>(
                    new ActionDispatchTestConfigurationRepository(configuration));
                services.AddScoped<GitHubIssuesTicketCreate>(provider => new GitHubIssuesTicketCreate(
                    provider.GetRequiredService<IOptions<GitHubIssuesOptions>>(),
                    provider.GetRequiredService<ITicketActionHistory>(),
                    handler));
                services.AddScoped<IExternalActionTool>(provider =>
                    provider.GetRequiredService<GitHubIssuesTicketCreate>());
            });

    private static TicketCreatePostReportActionWorkflow CreateTicketWorkflow(ServiceProvider services) =>
        new(
            services.GetRequiredService<ITriageConfigurationRepository>(),
            services.GetRequiredService<IServiceScopeFactory>());

    private static PostReportActionIntent Intent(
        ActionApprovalOriginFixture origin,
        string toolId,
        string? routeId)
    {
        var input = routeId is null
            ? $"{{\"originReportId\":\"{origin.ReportId:N}\",\"toolId\":\"{toolId}\",\"workflowVersion\":1}}"
            : $"{{\"originReportId\":\"{origin.ReportId:N}\",\"routeId\":\"{routeId}\",\"toolId\":\"{toolId}\",\"workflowVersion\":1}}";
        return new PostReportActionIntent(
            Guid.NewGuid(),
            origin.TenantId,
            origin.ReportId,
            origin.FaultId,
            origin.JobId,
            1,
            toolId,
            1,
            routeId,
            origin.ConfigHash,
            $"post-report:v1:{origin.ReportId:N}:{toolId}",
            Encoding.UTF8.GetBytes(input),
            PostReportActionIntentState.Processing,
            "injection-proof-worker",
            Guid.NewGuid(),
            DateTimeOffset.UtcNow.AddMinutes(1),
            1,
            null,
            null,
            DateTimeOffset.UtcNow,
            null);
    }

    private static async Task AssertNoActionOrDispatchAsync(
        string connectionString,
        ActionApprovalOriginFixture origin)
    {
        Assert.Equal(0, await ActionApprovalTestSupport.CountAsync(
            connectionString,
            "SELECT count(*) FROM incidentcompass.action_approvals WHERE origin_report_id = @report;",
            ("report", origin.ReportId)));
        Assert.Equal(0, await DispatchCountAsync(connectionString, origin.FaultId));
    }

    private static Task<long> DispatchCountAsync(string connectionString, Guid faultId) =>
        ActionApprovalTestSupport.CountAsync(
            connectionString,
            "SELECT count(*) FROM incidentcompass.triage_ledger WHERE fault_id = @fault AND event_type = 'ActionDispatchStarted';",
            ("fault", faultId));

    private static async Task<(string State, string ToolId, string LogicalTargetId, string Payload)>
        ReadOnlyActionAsync(string connectionString, Guid reportId)
    {
        await using var connection = new Npgsql.NpgsqlConnection(connectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new Npgsql.NpgsqlCommand("""
            SELECT state, tool_id, logical_target_id, convert_from(canonical_payload, 'UTF8')
            FROM incidentcompass.action_approvals
            WHERE origin_report_id = @report;
            """, connection);
        command.Parameters.AddWithValue("report", reportId);
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        Assert.True(await reader.ReadAsync(TestContext.Current.CancellationToken));
        return (reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3));
    }

    private static async Task<(string Severity, string ObservedAtUtc, string Instruction)> ReadFixtureAsync()
    {
        var path = Path.Combine(
            AppContext.BaseDirectory,
            "Samples",
            "Incidents",
            "tester-ticket-action-injection.json");
        using var document = JsonDocument.Parse(
            await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken));
        return (
            document.RootElement.GetProperty("severity").GetString()!,
            document.RootElement.GetProperty("observedAtUtc").GetString()!,
            document.RootElement.GetProperty("attributes").GetProperty("errorMessage").GetString()!);
    }

    private static void AssertFixtureCopiedByBothProjects()
    {
        var root = RepositoryRootLocator.Find();
        var testerProject = File.ReadAllText(
            Path.Combine(root, "src", "IncidentCompass.Tester", "IncidentCompass.Tester.csproj"));
        var integrationProject = File.ReadAllText(
            Path.Combine(root, "tests", "IncidentCompass.IntegrationTests", "IncidentCompass.IntegrationTests.csproj"));
        const string fixtureName = "tester-ticket-action-injection.json";
        Assert.Contains(fixtureName, testerProject, StringComparison.Ordinal);
        Assert.Contains(fixtureName, integrationProject, StringComparison.Ordinal);
        Assert.DoesNotContain("IncidentCompass.IntegrationTests.csproj", testerProject, StringComparison.Ordinal);
        Assert.DoesNotContain("IncidentCompass.Tester.csproj", integrationProject, StringComparison.Ordinal);
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
        }
    }
}
