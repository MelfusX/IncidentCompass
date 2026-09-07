namespace IncidentCompass.Infrastructure.SourceContext;

public sealed class SourceContextRootOptions
{
    public string ServiceName { get; set; } = string.Empty;

    public string Release { get; set; } = string.Empty;

    public string RootPath { get; set; } = string.Empty;

    public string[] BuildPathPrefixes { get; set; } = [];
}
