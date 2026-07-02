namespace IncidentCompass.Infrastructure.Configuration;

public sealed class TriageConfigSourceOptions
{
    public const string SectionName = "IncidentCompass:ConfigSource";

    public string Kind { get; init; } = "File";

    public string Path { get; init; } = "../../config/incidentcompass.config.json";
}
