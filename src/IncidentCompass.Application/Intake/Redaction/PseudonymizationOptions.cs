namespace IncidentCompass.Application.Intake.Redaction;

public sealed class PseudonymizationOptions
{
    public const string SectionName = "IncidentCompass:Pseudonymization";

    public string? Salt { get; init; }
}
