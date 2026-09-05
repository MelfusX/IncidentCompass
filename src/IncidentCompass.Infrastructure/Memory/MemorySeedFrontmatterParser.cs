namespace IncidentCompass.Infrastructure.Memory;

internal static class MemorySeedFrontmatterParser
{
    public static (MemorySeedFrontmatter Metadata, string Content) Parse(string source, string content)
    {
        var normalized = content.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal);
        var lines = normalized.Split('\n');
        if (lines.Length == 0 || !string.Equals(lines[0].Trim(), "---", StringComparison.Ordinal))
        {
            return (new MemorySeedFrontmatter(null, null, null, null, []), normalized.Trim());
        }

        var closingIndex = Array.FindIndex(lines, 1, static line =>
            string.Equals(line.Trim(), "---", StringComparison.Ordinal));
        if (closingIndex < 0)
        {
            throw new InvalidOperationException($"Memory seed '{source}' has an unterminated frontmatter block.");
        }

        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 1; index < closingIndex; index++)
        {
            var line = lines[index].Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            var separator = line.IndexOf(':');
            if (separator <= 0)
            {
                throw new InvalidOperationException($"Memory seed '{source}' has invalid frontmatter line '{line}'.");
            }

            var key = line[..separator].Trim();
            if (!KnownKeys.Contains(key))
            {
                throw new InvalidOperationException($"Memory seed '{source}' has unknown frontmatter key '{key}'.");
            }

            values[key] = Unquote(line[(separator + 1)..].Trim());
        }

        var body = string.Join('\n', lines[(closingIndex + 1)..]).Trim();
        if (body.Length == 0)
        {
            throw new InvalidOperationException($"Memory seed '{source}' has no content after frontmatter.");
        }

        return (new MemorySeedFrontmatter(
            ReadOptional(values, "kind"),
            ReadOptional(values, "service"),
            ReadOptional(values, "component"),
            ReadOptional(values, "release"),
            ParseTags(values.GetValueOrDefault("tags"))), body);
    }

    private static readonly HashSet<string> KnownKeys = new(
        ["kind", "service", "component", "release", "tags"],
        StringComparer.OrdinalIgnoreCase);

    private static string? ReadOptional(Dictionary<string, string> values, string key) =>
        values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;

    private static string[] ParseTags(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        var normalized = value.Trim();
        if (normalized.StartsWith('[') && normalized.EndsWith(']'))
        {
            normalized = normalized[1..^1];
        }

        return normalized.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(Unquote)
            .Where(static tag => tag.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string Unquote(string value)
    {
        if (value.Length >= 2 &&
            ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\'')))
        {
            return value[1..^1].Trim();
        }

        return value.Trim();
    }
}
