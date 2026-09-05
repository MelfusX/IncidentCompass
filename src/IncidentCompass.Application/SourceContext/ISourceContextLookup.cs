namespace IncidentCompass.Application.SourceContext;

public interface ISourceContextLookup
{
    Task<SourceLookupResult> LookupAsync(
        SourceLookupRequest request,
        CancellationToken cancellationToken);
}
