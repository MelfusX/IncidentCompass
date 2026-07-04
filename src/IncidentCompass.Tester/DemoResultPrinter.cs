namespace IncidentCompass.Tester;

internal static class DemoResultPrinter
{
    public static void Print(IReadOnlyList<DemoResult> results)
    {
        Console.WriteLine("Scenario | FaultId | ReportId | is_mass_issue | Classification | LedgerUrl | ReportUrl | Check");
        Console.WriteLine("--- | --- | --- | --- | --- | --- | --- | ---");
        foreach (var result in results)
        {
            Console.WriteLine(string.Join(" | ",
                result.Scenario.Id + " " + result.Scenario.Name,
                result.FaultId?.ToString() ?? "missing",
                result.ReportId?.ToString() ?? "missing",
                FormatNullableBool(result.IsMassIssue),
                result.Classification ?? "missing",
                result.LedgerUrl ?? "missing",
                result.ReportUrl ?? "missing",
                result.Passed ? "ok" : result.Detail));
        }
    }

    private static string FormatNullableBool(bool? value) => value.HasValue ? value.Value.ToString().ToLowerInvariant() : "null";
}