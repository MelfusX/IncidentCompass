using IncidentCompass.Application.SourceContext;
using Microsoft.Extensions.Options;

namespace IncidentCompass.Infrastructure.SourceContext;

internal sealed class LocalSourceContextLookup(
    IOptions<SourceContextOptions> optionsAccessor) : ISourceContextLookup
{
    private readonly SourceContextOptions options = optionsAccessor.Value;

    public async Task<SourceLookupResult> LookupAsync(
        SourceLookupRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var mapping = (options.Roots ?? []).FirstOrDefault(candidate =>
            string.Equals(candidate.ServiceName, request.ServiceName, StringComparison.Ordinal) &&
            string.Equals(candidate.Release, request.Release, StringComparison.Ordinal));
        if (mapping is null)
        {
            return SourceLookupResult.Unavailable("source_root_unavailable");
        }

        if (!Directory.Exists(mapping.RootPath))
        {
            return SourceLookupResult.Unavailable("source_root_unavailable");
        }

        try
        {
            return await LookupMappedAsync(mapping, request, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return SourceLookupResult.Unavailable("source_lookup_unavailable");
        }
    }

    private async Task<SourceLookupResult> LookupMappedAsync(
        SourceContextRootOptions mapping,
        SourceLookupRequest request,
        CancellationToken cancellationToken)
    {
        var resolver = new LocalSourcePathResolver(mapping, options);
        var reader = new BoundedSourceFileReader(options);
        var matches = new List<SourceLookupMatch>();
        var limitations = new List<SourceLookupLimitation>();
        foreach (var frame in request.Frames.Take(options.MaxFrames))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var resolution = resolver.Resolve(frame.Path, cancellationToken);
            if (!resolution.IsResolved)
            {
                limitations.Add(new SourceLookupLimitation(resolution.Code));
                continue;
            }

            var read = await reader.ReadAsync(resolution.FullPath!, frame.LineNumber, cancellationToken);
            if (!read.IsReadable)
            {
                limitations.Add(new SourceLookupLimitation(read.Code));
                continue;
            }

            matches.Add(new SourceLookupMatch(
                resolution.RelativePath!,
                read.LineStart,
                read.LineEnd,
                read.Excerpt!,
                request.Release,
                "heuristic"));
        }

        return matches.Count > 0
            ? new SourceLookupResult(SourceLookupOutcome.Matched, "source_match", matches, limitations)
            : SourceLookupResult.NoMatch("source_no_match", limitations);
    }
}
