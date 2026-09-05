using Microsoft.Extensions.Options;

namespace IncidentCompass.Infrastructure.SourceContext;

internal sealed class SourceContextOptionsValidator : IValidateOptions<SourceContextOptions>
{
    public ValidateOptionsResult Validate(string? name, SourceContextOptions options)
    {
        var failures = new List<string>();
        RequireRange(options.MaxFrames, 1, 8, nameof(options.MaxFrames), failures);
        RequireRange(options.MaxCandidateFiles, 1, 1000, nameof(options.MaxCandidateFiles), failures);
        RequireRange(options.MaxSourceBytes, 1024, 1024 * 1024, nameof(options.MaxSourceBytes), failures);
        RequireRange(options.MaxExcerptLines, 1, 100, nameof(options.MaxExcerptLines), failures);
        ValidateExtensions(options.AllowedExtensions, failures);
        ValidateRoots(options.Roots, failures);
        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private static void ValidateExtensions(IReadOnlyCollection<string>? extensions, ICollection<string> failures)
    {
        if (extensions is null || extensions.Count == 0)
        {
            failures.Add("AllowedExtensions must contain at least one extension.");
            return;
        }

        foreach (var extension in extensions)
        {
            if (string.IsNullOrWhiteSpace(extension) || !extension.StartsWith(".", StringComparison.Ordinal) ||
                extension.IndexOfAny(['/', '\\']) >= 0)
            {
                failures.Add("AllowedExtensions entries must be dot-prefixed file extensions.");
            }
        }
    }

    private static void ValidateRoots(IReadOnlyCollection<SourceContextRootOptions>? roots, ICollection<string> failures)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var root in roots ?? [])
        {
            if (string.IsNullOrWhiteSpace(root.ServiceName) || root.ServiceName.Length > 128 ||
                string.IsNullOrWhiteSpace(root.Release) || root.Release.Length > 128)
            {
                failures.Add("Each source root requires bounded nonblank ServiceName and Release values.");
            }

            if (!IsAbsolutePath(root.RootPath))
            {
                failures.Add("Each source RootPath must be absolute.");
            }

            if (!keys.Add(root.ServiceName + "\n" + root.Release))
            {
                failures.Add("Source root ServiceName and Release mappings must be unique.");
            }

            if ((root.BuildPathPrefixes ?? []).Any(prefix => !IsAbsolutePath(prefix)))
            {
                failures.Add("BuildPathPrefixes entries must be absolute paths.");
            }
        }
    }

    internal static bool IsAbsolutePath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return Path.IsPathRooted(value) ||
            (value.Length >= 3 && char.IsAsciiLetter(value[0]) && value[1] == ':' && value[2] is '/' or '\\');
    }

    private static void RequireRange(int value, int minimum, int maximum, string name, ICollection<string> failures)
    {
        if (value < minimum || value > maximum)
        {
            failures.Add($"{name} must be between {minimum} and {maximum}.");
        }
    }
}
