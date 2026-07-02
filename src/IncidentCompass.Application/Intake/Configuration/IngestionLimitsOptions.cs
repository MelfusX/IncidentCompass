namespace IncidentCompass.Application.Intake.Configuration;

public sealed class IngestionLimitsOptions
{
    public const string SectionName = "IncidentCompass:IngestionLimits";

    public int MaxPayloadBytes { get; init; } = 65536;
    public int MaxAttributesBytes { get; init; } = 16384;
}
