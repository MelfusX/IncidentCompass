using System.Net.Http.Json;

namespace IncidentCompass.Tester;

internal sealed class DemoTester(HttpClient client, TesterOptions options)
{
    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        await EnsureHealthyAsync(cancellationToken);
        var runId = DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmss") + "-" + Guid.NewGuid().ToString("N")[..8];
        var results = new List<DemoResult>();

        foreach (var scenario in DemoScenario.CreateAll(runId))
        {
            results.Add(await RunScenarioAsync(scenario, runId, cancellationToken));
        }

        PrintResults(results);
        return results.All(static result => result.Passed) ? 0 : 1;
    }

    private async Task EnsureHealthyAsync(CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync("api/v1/health", cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private async Task<DemoResult> RunScenarioAsync(
        DemoScenario scenario,
        string runId,
        CancellationToken cancellationToken)
    {
        var ingested = new List<IngestSignalResponse>();
        for (var index = 1; index <= scenario.SignalCount; index++)
        {
            ingested.Add(await PostIncidentAsync(scenario.CreateEnvelope(runId, index), cancellationToken));
        }

        var target = ingested.FirstOrDefault(static item => item.JobId is not null) ?? ingested[^1];
        var ledgerUrl = BuildUrl($"api/v1/faults/{target.FaultId}/ledger");
        var reportId = await WaitForReportIdAsync(target.FaultId, cancellationToken);
        if (reportId is null)
        {
            return new DemoResult(scenario, target.FaultId, null, null, null, ledgerUrl, null, false, "report not published");
        }

        var reportUrl = BuildUrl($"api/v1/triage-reports/{reportId}");
        var report = await GetReportAsync(reportId.Value, cancellationToken);
        var passed = MatchesExpectations(scenario, report, out var detail);
        return new DemoResult(
            scenario,
            target.FaultId,
            report.Id,
            report.Classification,
            report.IsMassIssue,
            ledgerUrl,
            reportUrl,
            passed,
            detail);
    }

    private async Task<IngestSignalResponse> PostIncidentAsync(
        IncidentEnvelope envelope,
        CancellationToken cancellationToken)
    {
        using var response = await client.PostAsJsonAsync(
            "api/v1/incidents",
            envelope,
            TesterJsonContext.Default.IncidentEnvelope,
            cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync(
            TesterJsonContext.Default.IngestSignalResponse,
            cancellationToken) ?? throw new InvalidOperationException("Incident response body was empty.");
    }

    private async Task<Guid?> WaitForReportIdAsync(Guid faultId, CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow.Add(options.PollTimeout);
        while (DateTimeOffset.UtcNow < deadline)
        {
            var ledger = await GetLedgerAsync(faultId, cancellationToken);
            var reportRef = ledger.Events
                .LastOrDefault(static item => string.Equals(item.EventType, "ReportPublished", StringComparison.Ordinal))
                ?.PayloadRef;
            if (TryParseReportId(reportRef, out var reportId))
            {
                return reportId;
            }

            await Task.Delay(options.PollInterval, cancellationToken);
        }

        return null;
    }

    private async Task<FaultLedgerResponse> GetLedgerAsync(Guid faultId, CancellationToken cancellationToken)
    {
        return await client.GetFromJsonAsync(
            $"api/v1/faults/{faultId}/ledger",
            TesterJsonContext.Default.FaultLedgerResponse,
            cancellationToken) ?? throw new InvalidOperationException("Ledger response body was empty.");
    }

    private async Task<TriageReportResponse> GetReportAsync(Guid reportId, CancellationToken cancellationToken)
    {
        return await client.GetFromJsonAsync(
            $"api/v1/triage-reports/{reportId}",
            TesterJsonContext.Default.TriageReportResponse,
            cancellationToken) ?? throw new InvalidOperationException("Report response body was empty.");
    }

    private static bool MatchesExpectations(
        DemoScenario scenario,
        TriageReportResponse report,
        out string detail)
    {
        if (!string.Equals(report.Classification, scenario.ExpectedClassification, StringComparison.Ordinal))
        {
            detail = "classification=" + report.Classification;
            return false;
        }

        if (scenario.ExpectedIsMassIssue != report.IsMassIssue)
        {
            detail = "is_mass_issue=" + FormatNullableBool(report.IsMassIssue);
            return false;
        }

        if (scenario.ExpectedEvidenceKind is not null &&
            !report.Evidence.Any(evidence => string.Equals(evidence.Kind, scenario.ExpectedEvidenceKind, StringComparison.Ordinal)))
        {
            detail = "missing evidence kind " + scenario.ExpectedEvidenceKind;
            return false;
        }

        detail = "ok";
        return true;
    }

    private string BuildUrl(string path) => new Uri(client.BaseAddress!, path).ToString();

    private static bool TryParseReportId(string? payloadRef, out Guid reportId)
    {
        reportId = Guid.Empty;
        const string prefix = "report:";
        return payloadRef is not null &&
            payloadRef.StartsWith(prefix, StringComparison.Ordinal) &&
            Guid.TryParse(payloadRef[prefix.Length..], out reportId);
    }

    private static void PrintResults(IReadOnlyList<DemoResult> results)
    {
        Console.WriteLine("Scenario | FaultId | ReportId | is_mass_issue | Classification | LedgerUrl | ReportUrl | Check");
        Console.WriteLine("--- | --- | --- | --- | --- | --- | --- | ---");
        foreach (var result in results)
        {
            Console.WriteLine(string.Join(" | ",
                result.Scenario.Id + " " + result.Scenario.Name,
                result.FaultId,
                result.ReportId?.ToString() ?? "missing",
                FormatNullableBool(result.IsMassIssue),
                result.Classification ?? "missing",
                result.LedgerUrl,
                result.ReportUrl ?? "missing",
                result.Passed ? "ok" : result.Detail));
        }
    }

    private static string FormatNullableBool(bool? value) => value.HasValue ? value.Value.ToString().ToLowerInvariant() : "null";
}