namespace IncidentCompass.Application.SourceContext;

internal sealed class UnavailableSourceContextLookup : ISourceContextLookup
{
    public Task<SourceLookupResult> LookupAsync(
        SourceLookupRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(SourceLookupResult.Unavailable("source_lookup_unavailable"));
    }
}
