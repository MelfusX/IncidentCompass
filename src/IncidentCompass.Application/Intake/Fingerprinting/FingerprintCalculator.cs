using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using IncidentCompass.Application.Intake.Normalization;
using IncidentCompass.Domain.Incidents;

namespace IncidentCompass.Application.Intake.Fingerprinting;

internal static partial class FingerprintCalculator
{
    // fingerprintVersion is accepted (future callers / Wave 2 need the signature to exist)
    // but is NOT mixed into the hash itself: it is tracked as a separate column alongside the
    // fingerprint (plan §8), reserved for FUTURE algorithm changes to invalidate old groupings,
    // not part of today's hash input. Ignoring it here is intentional, not a bug.
    public static FingerprintResult Compute(NormalizedSignal signal, int fingerprintVersion)
    {
        // Strength is Strong only when both service_name is a real (non-"unknown") value AND
        // error_type is present -- service_name alone is not used as the sole discriminator
        // because a free-text user/manual report can carry an operator-typed service name
        // while still having no structured error_type, and treating that as "strong" would
        // wrongly fuzzy-match unrelated prose via the masked error_message.
        var isStrong = !string.IsNullOrWhiteSpace(signal.ServiceName) &&
            !string.Equals(signal.ServiceName, "unknown", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(signal.ErrorType);

        var routeOrOperation = signal.HttpRoute ?? signal.OperationName ?? string.Empty;

        var signature = string.Join(
            '|',
            Normalize(signal.ServiceName),
            Normalize(signal.Environment),
            Normalize(signal.ErrorType ?? string.Empty),
            Normalize(Mask(signal.ErrorMessage ?? string.Empty)),
            Normalize(Mask(routeOrOperation)));

        var value = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(signature)));

        return new FingerprintResult(value, isStrong ? FingerprintStrength.Strong : FingerprintStrength.Weak);
    }

    private static string Normalize(string value) => value.Trim().ToLowerInvariant();

    private static string Mask(string value)
    {
        var masked = TimestampPattern().Replace(value, "<ts>");
        masked = GuidPattern().Replace(masked, "<guid>");
        masked = EmailPattern().Replace(masked, "<email>");
        masked = LongHexPattern().Replace(masked, "<hex>");
        masked = NumberPattern().Replace(masked, "<num>");
        return masked;
    }

    [GeneratedRegex(@"\b\d{4}-\d{2}-\d{2}[T ]\d{2}:\d{2}:\d{2}(?:\.\d+)?(?:Z|[+-]\d{2}:?\d{2})?\b")]
    private static partial Regex TimestampPattern();

    [GeneratedRegex(@"\b[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\b")]
    private static partial Regex GuidPattern();

    [GeneratedRegex(@"[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}")]
    private static partial Regex EmailPattern();

    [GeneratedRegex(@"\b[0-9a-fA-F]{8,}\b")]
    private static partial Regex LongHexPattern();

    // No trailing \b: a digit run directly followed by a unit suffix (e.g. "30000ms") has no
    // word-boundary between the last digit and the letter (both are \w), so a trailing \b would
    // silently skip masking it -- and duration-in-message ("after 30000ms") is a common real
    // error-message shape, so under-masking it would fragment identical timeouts into different
    // fingerprints. Only the leading boundary matters here; over-masking a rare case like
    // "sha256" -> "sha<num>" is the accepted trade-off (prefer slight over-grouping, §9).
    [GeneratedRegex(@"\b\d+")]
    private static partial Regex NumberPattern();
}
