using System.Security.Cryptography;

namespace IncidentCompass.Infrastructure.Memory;

internal static class MemorySeedFileLoader
{
    public static IReadOnlyList<MemorySeedFile> Load(string rootDirectory)
    {
        if (!Directory.Exists(rootDirectory))
        {
            return [];
        }

        return LoadKind(rootDirectory, "runbooks", "runbook")
            .Concat(LoadKind(rootDirectory, "incidents", "known_incident"))
            .Concat(LoadKind(rootDirectory, "operational-notes", "operational_note"))
            .OrderBy(static file => file.Source, StringComparer.Ordinal)
            .ToArray();
    }

    public static Guid DeterministicId(string value)
    {
        var bytes = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value));
        return new Guid(bytes[..16]);
    }

    private static IEnumerable<MemorySeedFile> LoadKind(
        string rootDirectory,
        string directoryName,
        string kind)
    {
        var directory = Path.Combine(rootDirectory, directoryName);
        if (!Directory.Exists(directory))
        {
            yield break;
        }

        foreach (var path in Directory.EnumerateFiles(directory, "*.md").Order(StringComparer.Ordinal))
        {
            var content = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(content))
            {
                continue;
            }

            yield return new MemorySeedFile(
                kind,
                Path.GetRelativePath(rootDirectory, path).Replace('\\', '/'),
                ReadTitle(content, Path.GetFileNameWithoutExtension(path)),
                content.Trim(),
                []);
        }
    }

    private static string ReadTitle(string content, string fallback)
    {
        foreach (var line in content.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("# ", StringComparison.Ordinal) && trimmed.Length > 2)
            {
                return trimmed[2..].Trim();
            }
        }

        return fallback;
    }
}
