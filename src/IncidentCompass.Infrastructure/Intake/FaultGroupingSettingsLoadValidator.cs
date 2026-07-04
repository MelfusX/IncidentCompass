using IncidentCompass.Application.Intake.Configuration;
using static IncidentCompass.Infrastructure.Intake.TriageConfigurationValidationGuards;

namespace IncidentCompass.Infrastructure.Intake;

internal static class FaultGroupingSettingsLoadValidator
{
    public static void Validate(FaultGroupingSettings settings)
    {
        if (settings.LookbackMinutes <= 0)
        {
            throw Invalid("FaultGrouping.LookbackMinutes", settings.LookbackMinutes.ToString(), "a positive integer");
        }

        if (settings.SilenceWindowMinutes <= 0)
        {
            throw Invalid("FaultGrouping.SilenceWindowMinutes", settings.SilenceWindowMinutes.ToString(), "a positive integer");
        }

        if (settings.MassIssue.MinNeighborCount <= 0)
        {
            throw Invalid("FaultGrouping.MassIssue.MinNeighborCount", settings.MassIssue.MinNeighborCount.ToString(), "a positive integer");
        }

        if (!settings.MassIssue.TryGetMinimumFingerprintStrength(out _))
        {
            throw Invalid("FaultGrouping.MassIssue.MinFingerprintStrength", settings.MassIssue.MinFingerprintStrength, "one of: weak, strong");
        }
    }
}
