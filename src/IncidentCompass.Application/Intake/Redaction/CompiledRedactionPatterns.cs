using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using IncidentCompass.Application.Intake.Configuration;

namespace IncidentCompass.Application.Intake.Redaction;

/// <summary>
/// The configured redaction patterns of one <see cref="RedactionSettings"/> instance, compiled once
/// instead of once per field. The cache is keyed on the settings instance itself through a
/// <see cref="ConditionalWeakTable{TKey,TValue}"/>, which compares keys by reference: two snapshots
/// never share compiled state even when their pattern text is identical, and a snapshot's patterns
/// become collectable together with the snapshot.
/// </summary>
internal sealed class CompiledRedactionPatterns
{
    /// <summary>Per-pattern match timeout for operator-configured expressions.</summary>
    public static readonly TimeSpan ConfiguredPatternTimeout = TimeSpan.FromMilliseconds(200);

    private static readonly ConditionalWeakTable<RedactionSettings, CompiledRedactionPatterns> Cache = new();

    private CompiledRedactionPatterns(IReadOnlyList<CompiledRedactionPattern> patterns) => Patterns = patterns;

    public IReadOnlyList<CompiledRedactionPattern> Patterns { get; }

    public static CompiledRedactionPatterns ForSettings(RedactionSettings settings) =>
        Cache.GetValue(settings, Compile);

    /// <summary>
    /// Returns the already-compiled patterns for <paramref name="settings"/> without compiling them,
    /// so callers can observe whether a settings instance has been materialized yet.
    /// </summary>
    public static bool TryGetExisting(
        RedactionSettings settings,
        [MaybeNullWhen(false)] out CompiledRedactionPatterns patterns) =>
        Cache.TryGetValue(settings, out patterns);

    private static CompiledRedactionPatterns Compile(RedactionSettings settings)
    {
        var compiled = new List<CompiledRedactionPattern>(settings.Patterns.Count);
        foreach (var pattern in settings.Patterns)
        {
            // RegexOptions.Compiled is deliberately not used: a configuration snapshot is rehydrated
            // per triage job, so paying IL emission for every snapshot would cost more than the
            // interpreted engine saves. The fix that matters is compiling once per snapshot instead
            // of once per field.
            var options = pattern.IgnoreCase ? RegexOptions.IgnoreCase : RegexOptions.None;
            compiled.Add(new CompiledRedactionPattern(
                pattern.Name,
                new Regex(pattern.Pattern, options, ConfiguredPatternTimeout),
                pattern.Replacement));
        }

        return new CompiledRedactionPatterns(compiled);
    }
}
