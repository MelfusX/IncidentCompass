using System.Security.Cryptography;

namespace IncidentCompass.Infrastructure.Memory;

internal static class MemorySeedFileLoader
{
    private static readonly IReadOnlyList<(string Directory, string Kind)> SeedDirectories =
    [
        ("runbooks", "runbook"),
        ("incidents", "known_incident"),
        ("operational-notes", "operational_note"),
        ("documents", "operational_note"),
        ("release-notes", "release_note"),
        ("postmortems", "postmortem")
    ];

    private static readonly IReadOnlyDictionary<string, string> KindAliases =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["runbook"] = "runbook",
            ["knownincident"] = "known_incident",
            ["known_incident"] = "known_incident",
            ["operationalnote"] = "operational_note",
            ["operational_note"] = "operational_note",
            ["releasenote"] = "release_note",
            ["release_note"] = "release_note",
            ["postmortem"] = "postmortem"
        };

    public static IReadOnlyList<MemorySeedFile> Load(string rootDirectory)
    {
        if (!Directory.Exists(rootDirectory))
        {
            return [];
        }

        return SeedDirectories
            .SelectMany(mapping => LoadKind(rootDirectory, mapping.Directory, mapping.Kind))
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
        string defaultKind)
    {
        var directory = Path.Combine(rootDirectory, directoryName);
        if (!Directory.Exists(directory))
        {
            yield break;
        }

        foreach (var path in Directory.EnumerateFiles(directory, "*.md").Order(StringComparer.Ordinal))
        {
            var source = Path.GetRelativePath(rootDirectory, path).Replace('\\', '/');
            var rawContent = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(rawContent))
            {
                continue;
            }

            var (metadata, content) = MemorySeedFrontmatterParser.Parse(source, rawContent);
            yield return new MemorySeedFile(
                NormalizeKind(metadata.Kind ?? defaultKind, source),
                source,
                ReadTitle(content, Path.GetFileNameWithoutExtension(path)),
                content,
                metadata.Tags,
                metadata.ServiceName,
                metadata.Component,
                metadata.ReleaseName);
        }
    }

    private static string NormalizeKind(string value, string source)
    {
        var normalized = value.Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal);
        if (KindAliases.TryGetValue(normalized, out var kind) ||
            KindAliases.TryGetValue(value, out kind))
        {
            return kind;
        }

        throw new InvalidOperationException($"Memory seed '{source}' has unsupported kind '{value}'.");
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
