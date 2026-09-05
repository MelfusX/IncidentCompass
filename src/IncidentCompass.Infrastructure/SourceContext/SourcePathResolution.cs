namespace IncidentCompass.Infrastructure.SourceContext;

internal sealed record SourcePathResolution(string? FullPath, string? RelativePath, string Code)
{
    public bool IsResolved => FullPath is not null && RelativePath is not null;
}
