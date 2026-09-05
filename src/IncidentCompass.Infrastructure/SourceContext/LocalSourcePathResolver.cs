namespace IncidentCompass.Infrastructure.SourceContext;

using static SourcePathBoundary;

internal sealed class LocalSourcePathResolver(
    SourceContextRootOptions mapping,
    SourceContextOptions options)
{
    private readonly StringComparison pathComparison = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    public SourcePathResolution Resolve(string framePath, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(framePath) || framePath.IndexOf('\0') >= 0)
        {
            return Rejected("source_path_rejected");
        }

        var configuredRoot = Path.GetFullPath(mapping.RootPath);
        if (!Directory.Exists(configuredRoot))
        {
            return Rejected("source_root_unavailable");
        }

        var canonicalRoot = ResolveRoot(configuredRoot);
        var relativeHint = ResolveRelativeHint(framePath, configuredRoot, canonicalRoot);
        if (relativeHint is null || ContainsTraversal(relativeHint))
        {
            return Rejected("source_path_rejected");
        }

        var direct = ResolveRelativeCandidate(canonicalRoot, relativeHint);
        if (direct.IsResolved || direct.Code != "source_file_missing")
        {
            return direct;
        }

        return ResolveUniqueSuffix(canonicalRoot, relativeHint, cancellationToken);
    }

    private string? ResolveRelativeHint(string framePath, string configuredRoot, string canonicalRoot)
    {
        if (!SourceContextOptionsValidator.IsAbsolutePath(framePath))
        {
            return NormalizeRelative(framePath);
        }

        if (TryRelativeToRoot(framePath, configuredRoot, out var configuredRelative) ||
            TryRelativeToRoot(framePath, canonicalRoot, out configuredRelative))
        {
            return configuredRelative;
        }

        foreach (var prefix in mapping.BuildPathPrefixes ?? [])
        {
            if (TryStripPrefix(framePath, prefix, out var buildRelative))
            {
                return buildRelative;
            }
        }

        return null;
    }

    private SourcePathResolution ResolveRelativeCandidate(string canonicalRoot, string relativeHint)
    {
        var extension = Path.GetExtension(relativeHint);
        if (!options.AllowedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            return Rejected("source_extension_rejected");
        }

        var normalizedRelative = NormalizeRelative(relativeHint);
        var candidate = Path.GetFullPath(Path.Combine(canonicalRoot, normalizedRelative));
        if (!IsUnderRoot(candidate, canonicalRoot, pathComparison))
        {
            return Rejected("source_path_rejected");
        }

        if (!File.Exists(candidate))
        {
            return Rejected("source_file_missing");
        }

        return HasReparsePoint(canonicalRoot, normalizedRelative)
            ? Rejected("source_path_rejected")
            : new SourcePathResolution(candidate, ToRepositoryPath(normalizedRelative), "source_match");
    }

    private SourcePathResolution ResolveUniqueSuffix(
        string canonicalRoot,
        string relativeHint,
        CancellationToken cancellationToken)
    {
        var suffix = ToRepositoryPath(NormalizeRelative(relativeHint));
        var matches = new List<string>(2);
        var scanned = 0;
        foreach (var file in EnumerateFiles(canonicalRoot, cancellationToken))
        {
            if (++scanned > options.MaxCandidateFiles)
            {
                return Rejected("source_candidate_limit");
            }

            var relative = ToRepositoryPath(Path.GetRelativePath(canonicalRoot, file));
            if (relative.Equals(suffix, pathComparison) ||
                relative.EndsWith("/" + suffix, pathComparison))
            {
                matches.Add(file);
                if (matches.Count > 1)
                {
                    return Rejected("source_path_ambiguous");
                }
            }
        }

        return matches.Count == 1
            ? new SourcePathResolution(
                matches[0],
                ToRepositoryPath(Path.GetRelativePath(canonicalRoot, matches[0])),
                "source_match")
            : Rejected("source_file_missing");
    }

    private IEnumerable<string> EnumerateFiles(string root, CancellationToken cancellationToken)
    {
        var pending = new Stack<DirectoryInfo>();
        pending.Push(new DirectoryInfo(root));
        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directory = pending.Pop();
            foreach (var child in directory.EnumerateDirectories())
            {
                if ((child.Attributes & FileAttributes.ReparsePoint) == 0)
                {
                    pending.Push(child);
                }
            }

            foreach (var file in directory.EnumerateFiles())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if ((file.Attributes & FileAttributes.ReparsePoint) == 0 &&
                    options.AllowedExtensions.Contains(file.Extension, StringComparer.OrdinalIgnoreCase))
                {
                    yield return file.FullName;
                }
            }
        }
    }

    private bool TryRelativeToRoot(string path, string root, out string relative)
    {
        try
        {
            var normalizedPath = Path.GetFullPath(path);
            if (IsUnderRoot(normalizedPath, root, pathComparison))
            {
                relative = Path.GetRelativePath(root, normalizedPath);
                return true;
            }
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
        }

        relative = string.Empty;
        return false;
    }

    private bool TryStripPrefix(string path, string prefix, out string relative)
    {
        var normalizedPath = NormalizeForComparison(path);
        var normalizedPrefix = NormalizeForComparison(prefix).TrimEnd('/');
        if (normalizedPath.StartsWith(normalizedPrefix + "/", pathComparison))
        {
            relative = NormalizeRelative(normalizedPath[(normalizedPrefix.Length + 1)..]);
            return true;
        }

        relative = string.Empty;
        return false;
    }

    private static SourcePathResolution Rejected(string code) => new(null, null, code);
}
