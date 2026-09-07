using System.Text.Json;
using System.Text.RegularExpressions;
using IncidentCompass.Domain.Incidents;

namespace IncidentCompass.Application.SourceContext;

internal static partial class SourceStackTraceExtractor
{
    internal const int MaxFieldCharacters = 32 * 1024;
    internal const int MaxFrames = 8;

    public static IReadOnlyList<SourceFrameCandidate> Extract(Signal signal)
    {
        foreach (var candidate in EnumerateCandidateFields(signal))
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                continue;
            }

            var bounded = candidate.Length <= MaxFieldCharacters
                ? candidate
                : candidate[..MaxFieldCharacters];
            var frames = ParseFrames(bounded);
            if (frames.Count > 0)
            {
                return frames;
            }
        }

        return [];
    }

    private static List<SourceFrameCandidate> ParseFrames(string value)
    {
        var frames = new List<SourceFrameCandidate>();
        foreach (Match match in DotNetFrameRegex().Matches(value))
        {
            if (int.TryParse(match.Groups["line"].Value, out var line) && line > 0)
            {
                frames.Add(new SourceFrameCandidate(match.Groups["path"].Value.Trim(), line));
                if (frames.Count == MaxFrames)
                {
                    break;
                }
            }
        }

        return frames;
    }

    private static IEnumerable<string?> EnumerateCandidateFields(Signal signal)
    {
        yield return ReadStringProperty(signal.Attributes, "exception.stacktrace");
        yield return ReadStringProperty(signal.Attributes, "exception.stack_trace");
        yield return ReadStringProperty(signal.Body, "stackTrace");
        yield return ReadStringProperty(signal.Body, "exception.stacktrace");
        yield return signal.Description;
        yield return signal.ErrorMessage;
    }

    private static string? ReadStringProperty(JsonElement element, string name)
    {
        return element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty(name, out var value) &&
            value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
    }

    [GeneratedRegex(@"(?m)^\s*at\s+.+?\s+in\s+(?<path>.+):line\s+(?<line>\d+)\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex DotNetFrameRegex();
}
