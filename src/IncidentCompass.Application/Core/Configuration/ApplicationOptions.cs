namespace IncidentCompass.Application.Core.Configuration;

public sealed class ApplicationOptions
{
    public const string SectionName = "IncidentCompass:Application";

    [RequiredNonBlank]
    public string ApiVersion { get; init; } = "v1";
}
