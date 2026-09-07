namespace IncidentCompass.Infrastructure.SourceContext;

internal static class SourcePathBoundary
{
    public static bool IsUnderRoot(string path, string root, StringComparison comparison)
    {
        var normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var normalizedPath = Path.GetFullPath(path);
        return normalizedPath.StartsWith(normalizedRoot, comparison);
    }

    public static bool HasReparsePoint(string root, string relative)
    {
        var current = root;
        foreach (var segment in NormalizeRelative(relative).Split(Path.DirectorySeparatorChar))
        {
            current = Path.Combine(current, segment);
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            {
                return true;
            }
        }

        return false;
    }

    public static string ResolveRoot(string root) =>
        new DirectoryInfo(root).ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? root;

    public static bool ContainsTraversal(string relative) =>
        NormalizeRelative(relative).Split(Path.DirectorySeparatorChar).Any(segment => segment == "..");

    public static string NormalizeRelative(string value) =>
        value.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar)
            .TrimStart(Path.DirectorySeparatorChar);

    public static string NormalizeForComparison(string value) => value.Replace('\\', '/');

    public static string ToRepositoryPath(string value) => value.Replace('\\', '/');
}
