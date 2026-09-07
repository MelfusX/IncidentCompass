namespace IncidentCompass.Application.Investigation.Jobs;

internal static class WorkerOutputDiagnosticLabels
{
    public static string Output(string roleName)
    {
        var normalized = Normalize(roleName);
        return normalized.Length == 0 ? "worker output" : normalized + " worker output";
    }

    public static string Schema(string roleName)
    {
        var normalized = Normalize(roleName);
        return normalized.Length == 0 ? "output schema" : normalized + " output schema";
    }

    private static string Normalize(string roleName)
    {
        return string.IsNullOrWhiteSpace(roleName)
            ? string.Empty
            : roleName.Trim().ToLowerInvariant();
    }
}
