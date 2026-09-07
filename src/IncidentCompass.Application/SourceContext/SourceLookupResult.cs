namespace IncidentCompass.Application.SourceContext;

public sealed record SourceLookupResult(
    SourceLookupOutcome Outcome,
    string Code,
    IReadOnlyList<SourceLookupMatch> Matches,
    IReadOnlyList<SourceLookupLimitation> Limitations)
{
    public static SourceLookupResult Unavailable(string code) =>
        new(SourceLookupOutcome.ConnectorUnavailable, code, [], []);

    public static SourceLookupResult NoMatch(
        string code,
        IReadOnlyList<SourceLookupLimitation>? limitations = null) =>
        new(SourceLookupOutcome.NoMatch, code, [], limitations ?? []);
}
