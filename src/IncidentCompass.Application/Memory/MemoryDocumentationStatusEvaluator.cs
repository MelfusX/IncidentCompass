using IncidentCompass.Application.Intake.Configuration;

namespace IncidentCompass.Application.Memory;

internal static class MemoryDocumentationStatusEvaluator
{
    private const string UnknownServiceName = "unknown";

    public static MemoryDocumentationAssessment Assess(
        TriageConfiguration configuration,
        string faultServiceName,
        MemorySearchMatch match)
    {
        var targetCurrentRelease = FindTargetCurrentRelease(configuration, faultServiceName);
        if (!IsKnownServiceName(match.ServiceName) ||
            !string.Equals(match.ServiceName, faultServiceName, StringComparison.Ordinal))
        {
            return new MemoryDocumentationAssessment(targetCurrentRelease, MemoryDocumentationStatus.ServiceMismatch);
        }

        if (targetCurrentRelease is null || string.IsNullOrWhiteSpace(match.ReleaseName))
        {
            return new MemoryDocumentationAssessment(targetCurrentRelease, MemoryDocumentationStatus.Unversioned);
        }

        var status = string.Equals(match.ReleaseName, targetCurrentRelease, StringComparison.Ordinal)
            ? MemoryDocumentationStatus.Current
            : MemoryDocumentationStatus.Stale;
        return new MemoryDocumentationAssessment(targetCurrentRelease, status);
    }

    private static string? FindTargetCurrentRelease(TriageConfiguration configuration, string faultServiceName)
    {
        return IsKnownServiceName(faultServiceName) &&
            configuration.CurrentReleases.TryGetValue(faultServiceName, out var targetCurrentRelease) &&
            !string.IsNullOrWhiteSpace(targetCurrentRelease)
            ? targetCurrentRelease
            : null;
    }

    private static bool IsKnownServiceName(string? serviceName)
    {
        return !string.IsNullOrWhiteSpace(serviceName) &&
            !string.Equals(serviceName, UnknownServiceName, StringComparison.OrdinalIgnoreCase);
    }
}