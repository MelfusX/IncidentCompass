namespace IncidentCompass.Infrastructure.SourceContext;

public sealed class SourceContextOptions
{
    public const string SectionName = "IncidentCompass:SourceContext";

    public int MaxFrames { get; set; } = 8;

    public int MaxCandidateFiles { get; set; } = 256;

    public int MaxSourceBytes { get; set; } = 256 * 1024;

    public int MaxExcerptLines { get; set; } = 21;

    public string[] AllowedExtensions { get; set; } = [".cs"];

    public SourceContextRootOptions[] Roots { get; set; } = [];
}
