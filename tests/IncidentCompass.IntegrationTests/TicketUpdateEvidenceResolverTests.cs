using System.Text.Json;
using IncidentCompass.Application.Tickets;
using Microsoft.Extensions.DependencyInjection;

namespace IncidentCompass.IntegrationTests;

[Collection(PostgresRepositoryCollection.CollectionName)]
public sealed class TicketUpdateEvidenceResolverTests(PostgresRepositoryFixture postgres)
{
    [DockerAvailableFact]
    public async Task ResolvesOnlyOneExactTenantScopedConfiguredRepositoryTicket()
    {
        await using var database = await ActionApprovalDatabase.CreateAsync(postgres);
        using var services = ActionApprovalTestSupport.CreateServices(database.ConnectionString);
        var origin = await ActionApprovalTestSupport.SeedOriginAsync(database.ConnectionString);
        var artifactId = await SeedTicketAsync(database.ConnectionString, origin, "owner/repo", 42);

        using var scope = services.CreateScope();
        var resolver = scope.ServiceProvider.GetRequiredService<ITicketUpdateEvidenceResolver>();
        var evidence = await resolver.ResolveAsync(
            origin.TenantId, origin.ReportId, TestContext.Current.CancellationToken);
        var foreignTenant = await resolver.ResolveAsync(
            "other-tenant", origin.ReportId, TestContext.Current.CancellationToken);

        Assert.NotNull(evidence);
        Assert.Equal(artifactId, evidence.ArtifactId);
        Assert.Equal("42", evidence.TicketId);
        Assert.Null(foreignTenant);
    }

    [DockerAvailableFact]
    public async Task MissingForeignOrMultipleTicketEvidenceFailsClosed()
    {
        await using var database = await ActionApprovalDatabase.CreateAsync(postgres);
        using var services = ActionApprovalTestSupport.CreateServices(database.ConnectionString);
        var resolver = services.GetRequiredService<ITicketUpdateEvidenceResolver>();

        var missing = await ActionApprovalTestSupport.SeedOriginAsync(database.ConnectionString);
        Assert.Null(await resolver.ResolveAsync(
            missing.TenantId, missing.ReportId, TestContext.Current.CancellationToken));

        var foreign = await ActionApprovalTestSupport.SeedOriginAsync(database.ConnectionString);
        await SeedTicketAsync(database.ConnectionString, foreign, "other/repo", 42);
        Assert.Null(await resolver.ResolveAsync(
            foreign.TenantId, foreign.ReportId, TestContext.Current.CancellationToken));

        var multiple = await ActionApprovalTestSupport.SeedOriginAsync(database.ConnectionString);
        await SeedTicketAsync(database.ConnectionString, multiple, "owner/repo", 42);
        await SeedTicketAsync(database.ConnectionString, multiple, "owner/repo", 43);
        Assert.Null(await resolver.ResolveAsync(
            multiple.TenantId, multiple.ReportId, TestContext.Current.CancellationToken));
    }

    internal static async Task<Guid> SeedTicketAsync(
        string connectionString,
        ActionApprovalOriginFixture origin,
        string repository,
        int issueNumber)
    {
        var artifactId = Guid.NewGuid();
        var payload = JsonSerializer.Serialize(new
        {
            evidenceKind = "ExistingTicket",
            provider = "github",
            repository,
            issueNumber,
            title = "Checkout timeout",
            status = "open",
            assignee = (string?)null,
            createdAtUtc = "2026-08-29T00:00:00.0000000+00:00",
            url = $"https://github.com/{repository}/issues/{issueNumber}",
            score = 0.9
        });
        var domainRef = $"ticket:github:{repository}:{issueNumber}";
        await ActionApprovalTestSupport.ExecuteAsync(connectionString, """
            INSERT INTO incidentcompass.triage_artifacts (
                id, job_id, attempt, kind, domain_ref, redacted_payload, content_hash, created_at_utc)
            VALUES (@artifact_id, @job_id, 1, 'RetrievedItem', @domain_ref,
                    CAST(@payload AS jsonb), @content_hash, clock_timestamp());

            INSERT INTO incidentcompass.triage_evidence (
                id, report_id, kind, artifact_id, reference, created_at_utc)
            VALUES (gen_random_uuid(), @report_id, 'RetrievedItem', @artifact_id,
                    @reference, clock_timestamp());
            """,
            ("artifact_id", artifactId),
            ("job_id", origin.JobId),
            ("domain_ref", domainRef),
            ("payload", payload),
            ("content_hash", "ticket-evidence-" + artifactId.ToString("N")),
            ("report_id", origin.ReportId),
            ("reference", "artifact:" + artifactId));
        return artifactId;
    }
}
