using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using IncidentCompass.Application.Core.ModelClients;
using IncidentCompass.Application.Investigation.Jobs;
using IncidentCompass.Application.Investigation.Reports;
using Microsoft.Extensions.DependencyInjection;
using static IncidentCompass.IntegrationTests.TriageReportGroundingTestSupport;

namespace IncidentCompass.IntegrationTests;

[Collection(PostgresRepositoryCollection.CollectionName)]
public sealed class TriageReportPublicationTests(PostgresRepositoryFixture postgres)
{
    [DockerAvailableFact]
    public async Task PublishAsync_StaleAttemptIsRejectedByFence()
    {
        using var scope = await CreateScopeAsync(postgres);
        var ingested = await PostIngestAsync(scope.Client, "stale-fence");
        var claimed = await ClaimAsync(scope, ingested.JobId!.Value, "worker-stale-1");
        var triggerArtifactId = await ReadArtifactIdAsync(scope.ConnectionString, claimed.Id, "TriggerSignal");
        await ExecuteAsync(scope.ConnectionString, """
            UPDATE incidentcompass.triage_jobs
            SET attempt = 2, locked_by = 'worker-stale-2', locked_until_utc = now() + interval '5 minutes'
            WHERE id = @job_id;
            """, ("job_id", claimed.Id));

        using var serviceScope = scope.Factory.Services.CreateScope();
        var repository = serviceScope.ServiceProvider.GetRequiredService<ITriageReportRepository>();
        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.PublishAsync(
            claimed,
            "worker-stale-1",
            CreateReport(triggerArtifactId),
            TestContext.Current.CancellationToken));

        var reports = await ScalarAsync<long>(scope.ConnectionString, "SELECT COUNT(*) FROM incidentcompass.triage_reports WHERE fault_id = @fault_id;", ("fault_id", ingested.FaultId));
        Assert.Equal(0, reports);
    }


    [DockerAvailableFact]
    public async Task PublishAsync_DerivesAndPersistsDocumentationFitFromCitedMemoryItems()
    {
        var cases = new (string[] DocumentationStatuses, DocumentationFitStatus ExpectedFit, string? ExpectedLimitation)[]
        {
            (["Current"], DocumentationFitStatus.Current, null),
            (["Current", "Stale"], DocumentationFitStatus.CurrentWithHistorical, null),
            (["Stale"], DocumentationFitStatus.StaleOnly, null),
            ([], DocumentationFitStatus.Missing, null),
            (["Unversioned"], DocumentationFitStatus.Missing, "Cited documentation is unversioned or service-mismatched, so its currentness cannot be assessed."),
            (["Current", "Current"], DocumentationFitStatus.MultipleCurrentDocuments, "Multiple current documents were cited; their compatibility requires operator review.")
        };
        foreach (var testCase in cases)
        {
            using var scope = await CreateScopeAsync(postgres);
            var ingested = await PostIngestAsync(scope.Client, "documentation-fit");
            var claimed = await ClaimAsync(scope, ingested.JobId!.Value, "worker-documentation-fit");
            var artifactIds = new List<Guid>();
            foreach (var documentationStatus in testCase.DocumentationStatuses)
            {
                artifactIds.Add(await InsertRetrievedMemoryArtifactAsync(
                    scope.ConnectionString,
                    claimed.Id,
                    claimed.Attempt,
                    documentationStatus));
            }

            if (artifactIds.Count == 0)
            {
                artifactIds.Add(await ReadArtifactIdAsync(scope.ConnectionString, claimed.Id, "TriggerSignal"));
            }

            using var serviceScope = scope.Factory.Services.CreateScope();
            var repository = serviceScope.ServiceProvider.GetRequiredService<ITriageReportRepository>();
            var reportId = await repository.PublishAsync(
                claimed,
                "worker-documentation-fit",
                CreateReport(artifactIds[0]) with
                {
                    DocumentationFit = testCase.ExpectedFit,
                    Evidence = artifactIds.Select(static id => new TriageReportEvidenceReference(id.ToString(), null)).ToArray()
                },
                TestContext.Current.CancellationToken);

            var persistedFit = await ScalarAsync<string>(
                scope.ConnectionString,
                "SELECT documentation_fit FROM incidentcompass.triage_reports WHERE id = @report_id;",
                ("report_id", reportId));
            Assert.Equal(testCase.ExpectedFit.ToString(), persistedFit);

            var response = await scope.Client.GetAsync(
                "/api/v1/triage-reports/" + reportId,
                TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var details = await response.Content.ReadFromJsonAsync<DocumentationFitDetailsDto>(TestContext.Current.CancellationToken);
            Assert.NotNull(details);
            Assert.Equal(testCase.ExpectedFit.ToString(), details.DocumentationFit);
            if (testCase.ExpectedLimitation is null)
            {
                Assert.Empty(details.Limitations);
            }
            else
            {
                Assert.Contains(testCase.ExpectedLimitation, details.Limitations);
            }
        }
    }

    [DockerAvailableFact]
    public async Task PublishAsync_RejectsModelDocumentationFitThatDisagreesWithCitedMemoryItems()
    {
        using var scope = await CreateScopeAsync(postgres);
        var ingested = await PostIngestAsync(scope.Client, "documentation-fit-rejection");
        var claimed = await ClaimAsync(scope, ingested.JobId!.Value, "worker-documentation-fit-rejection");
        var artifactId = await InsertRetrievedMemoryArtifactAsync(
            scope.ConnectionString,
            claimed.Id,
            claimed.Attempt,
            "Current");

        using var serviceScope = scope.Factory.Services.CreateScope();
        var repository = serviceScope.ServiceProvider.GetRequiredService<ITriageReportRepository>();
        await Assert.ThrowsAsync<TriageReportValidationException>(() => repository.PublishAsync(
            claimed,
            "worker-documentation-fit-rejection",
            CreateReport(artifactId),
            TestContext.Current.CancellationToken));

        var reports = await ScalarAsync<long>(
            scope.ConnectionString,
            "SELECT COUNT(*) FROM incidentcompass.triage_reports WHERE fault_id = @fault_id;",
            ("fault_id", ingested.FaultId));
        Assert.Equal(0, reports);
    }
    [DockerAvailableFact]
    public async Task PublishAsync_RefreshedNeighborSetKeepsStableEvidenceReference()
    {
        using var scope = await CreateScopeAsync(postgres);
        var serviceName = "neighbor-refresh-svc-" + Guid.NewGuid().ToString("N");
        var envelope = new TriageReportTesterEnvelope(
            "tester",
            serviceName,
            "prod",
            DateTimeOffset.UtcNow,
            new TriageReportTesterAttributes("TimeoutException", "Neighbor refresh timeout", "/neighbor-refresh"));
        var ingested = await PostIngestAsync(scope.Client, envelope);
        Assert.NotNull(ingested.JobId);
        var neighborArtifactId = await ReadArtifactIdAsync(scope.ConnectionString, ingested.JobId!.Value, "NeighborSet");

        var attached = await PostIngestAsync(scope.Client, envelope with { ObservedAtUtc = DateTimeOffset.UtcNow.AddSeconds(1) });
        Assert.Equal(ingested.FaultId, attached.FaultId);
        Assert.False(attached.IsNewJob);

        var refreshedNeighborArtifactId = await ReadArtifactIdAsync(scope.ConnectionString, ingested.JobId.Value, "NeighborSet");
        Assert.Equal(neighborArtifactId, refreshedNeighborArtifactId);

        var claimed = await ClaimAsync(scope, ingested.JobId.Value, "worker-neighbor-refresh");
        using var serviceScope = scope.Factory.Services.CreateScope();
        var repository = serviceScope.ServiceProvider.GetRequiredService<ITriageReportRepository>();
        await repository.PublishAsync(
            claimed,
            "worker-neighbor-refresh",
            CreateReport(neighborArtifactId),
            TestContext.Current.CancellationToken);

        var evidence = await ReadSingleEvidenceAsync(scope.ConnectionString, ingested.FaultId);
        Assert.Equal("NeighborSet", evidence.Kind);
    }

    [DockerAvailableFact]
    public async Task PublishAsync_CitesClosedSourceCodePayloadUsingExistingRetrievedItemKind()
    {
        using var scope = await CreateScopeAsync(postgres);
        var serviceName = "source-grounding-" + Guid.NewGuid().ToString("N");
        var ingested = await PostIngestAsync(scope.Client, new TriageReportTesterEnvelope(
            "tester", serviceName, "prod", DateTimeOffset.UtcNow,
            new TriageReportTesterAttributes("ExampleException", "source grounding", "/source")));
        var claimed = await ClaimAsync(scope, ingested.JobId!.Value, "worker-source-grounding");
        await SetCurrentReleaseAsync(scope.ConnectionString, claimed.ConfigHash, serviceName, "r1");
        var artifactId = await InsertSourceArtifactAsync(scope.ConnectionString, claimed.Id, claimed.Attempt, "r1");

        using var serviceScope = scope.Factory.Services.CreateScope();
        var repository = serviceScope.ServiceProvider.GetRequiredService<ITriageReportRepository>();
        await repository.PublishAsync(
            claimed,
            "worker-source-grounding",
            CreateReport(artifactId),
            TestContext.Current.CancellationToken);

        var evidence = await ReadSingleEvidenceAsync(scope.ConnectionString, ingested.FaultId);
        Assert.Equal("RetrievedItem", evidence.Kind);
        var evidenceKind = await ScalarAsync<string>(scope.ConnectionString, """
            SELECT a.redacted_payload->>'evidenceKind'
            FROM incidentcompass.triage_evidence e
            JOIN incidentcompass.triage_artifacts a ON a.id = e.artifact_id
            JOIN incidentcompass.triage_reports r ON r.id = e.report_id
            WHERE r.fault_id = @fault_id;
            """, ("fault_id", ingested.FaultId));
        Assert.Equal("SourceCode", evidenceKind);
    }

    [DockerAvailableFact]
    public async Task PublishAsync_RejectsSourceArtifactForDifferentRelease()
    {
        using var scope = await CreateScopeAsync(postgres);
        var serviceName = "source-stale-" + Guid.NewGuid().ToString("N");
        var ingested = await PostIngestAsync(scope.Client, new TriageReportTesterEnvelope(
            "tester", serviceName, "prod", DateTimeOffset.UtcNow,
            new TriageReportTesterAttributes("ExampleException", "stale source", "/source")));
        var claimed = await ClaimAsync(scope, ingested.JobId!.Value, "worker-source-stale");
        await SetCurrentReleaseAsync(scope.ConnectionString, claimed.ConfigHash, serviceName, "r2");
        var artifactId = await InsertSourceArtifactAsync(scope.ConnectionString, claimed.Id, claimed.Attempt, "r1");

        using var serviceScope = scope.Factory.Services.CreateScope();
        var repository = serviceScope.ServiceProvider.GetRequiredService<ITriageReportRepository>();
        await Assert.ThrowsAsync<TriageReportValidationException>(() => repository.PublishAsync(
            claimed,
            "worker-source-stale",
            CreateReport(artifactId),
            TestContext.Current.CancellationToken));
    }

    [DockerAvailableFact]
    public async Task TriageReportPublisher_AppendsDurableSourceNoMatchLimitation()
    {
        using var scope = await CreateScopeAsync(postgres);
        var ingested = await PostIngestAsync(scope.Client, "source-no-match-policy");
        var claimed = await ClaimAsync(scope, ingested.JobId!.Value, "worker-source-no-match");
        await InsertToolOutcomeAsync(scope.ConnectionString, claimed.Id, claimed.Attempt);
        var triggerId = await ReadArtifactIdAsync(scope.ConnectionString, claimed.Id, "TriggerSignal");
        var arguments = JsonSerializer.SerializeToElement(new
        {
            report_json = new
            {
                status = "Completed",
                summary = "Grounded report.",
                classification = "SimpleKnownError",
                confidence = "Medium",
                documentationFit = "Missing",
                evidence = new[] { new { referenceId = triggerId.ToString() } },
                limitations = Array.Empty<string>(),
                recommendedNextAction = "Review the signal."
            }
        });

        using var serviceScope = scope.Factory.Services.CreateScope();
        var publisher = serviceScope.ServiceProvider.GetRequiredService<TriageReportPublisher>();
        await publisher.PublishAsync(
            claimed,
            "worker-source-no-match",
            new AiToolCall("publish-source-no-match", "publish_report", "v1", arguments),
            TestContext.Current.CancellationToken);

        var limitation = await ScalarAsync<string>(
            scope.ConnectionString,
            "SELECT limitations[1] FROM incidentcompass.triage_reports WHERE fault_id = @fault_id;",
            ("fault_id", ingested.FaultId));
        Assert.Equal("Read-only context source_lookup returned no matches (source_no_match).", limitation);
    }

    private sealed record DocumentationFitDetailsDto(string DocumentationFit, IReadOnlyList<string> Limitations);
}
