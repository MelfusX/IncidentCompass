namespace IncidentCompass.Application.SourceContext;

public sealed record SourceLookupRequest(
    string ServiceName,
    string Release,
    IReadOnlyList<SourceFrameCandidate> Frames);
