namespace IncidentCompass.Application.Core.Configuration;

public sealed class ApplicationOptions
{
    public const string SectionName = "IncidentCompass:Application";

    [RequiredNonBlank]
    public string ApiVersion { get; init; } = "v1";

    [RequiredNonBlank]
    public string RunnerVersion { get; init; } = "0.1.0-dev";
}
