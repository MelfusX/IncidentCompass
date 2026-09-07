namespace IncidentCompass.Infrastructure.SourceContext;

internal sealed record SourceFileReadResult(
    string? Excerpt,
    int LineStart,
    int LineEnd,
    string Code)
{
    public bool IsReadable => Excerpt is not null;
}
