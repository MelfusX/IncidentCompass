namespace IncidentCompass.Application.SourceContext;

public sealed record SourceLookupMatch(
    string RelativePath,
    int LineStart,
    int LineEnd,
    string Excerpt,
    string Release,
    string MappingMethod);
