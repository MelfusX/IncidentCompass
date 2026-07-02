using IncidentCompass.Domain.Incidents;

namespace IncidentCompass.Application.Intake.Configuration;

public sealed record MassIssueSettings(int MinNeighborCount, string MinFingerprintStrength)
{
    public bool TryGetMinimumFingerprintStrength(out FingerprintStrength strength)
    {
        foreach (var name in Enum.GetNames<FingerprintStrength>())
        {
            if (string.Equals(name, MinFingerprintStrength, StringComparison.OrdinalIgnoreCase))
            {
                strength = Enum.Parse<FingerprintStrength>(name);
                return true;
            }
        }

        strength = default;
        return false;
    }

    public FingerprintStrength GetMinimumFingerprintStrength()
    {
        if (TryGetMinimumFingerprintStrength(out var strength))
        {
            return strength;
        }

        throw new InvalidOperationException(
            $"MassIssue.MinFingerprintStrength '{MinFingerprintStrength}' is not supported. Use 'weak' or 'strong'.");
    }
}
