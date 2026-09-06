using System.Text;

namespace IncidentCompass.Application.Intake.Redaction;

/// <summary>
/// Decides whether a JSON property name looks like a secret holder.
/// <para>
/// The rule: a property name is lowercased and split into segments on every non-alphanumeric
/// character and on camel-case and letter/digit boundaries, and the property is redacted when any
/// run of consecutive segments joins to one of <see cref="SegmentTerms"/>. A property is also
/// redacted when the whole name, with its separators removed, equals one of
/// <see cref="WholeNameTerms"/>.
/// </para>
/// <para>
/// Whole-name terms exist so that broad words match only when they are the entire name:
/// <c>session</c> is redacted but <c>session_id</c>, <c>sessionCount</c> and
/// <c>session_start_time</c> are not. Terms that are never a legitimate non-secret word, such as
/// <c>password</c> or <c>apikey</c>, are segment terms and therefore match anywhere, which is what
/// catches <c>x-api-key</c>, <c>set-cookie</c>, <c>user_password_hash</c> and <c>db_password_2</c>.
/// This is best-effort matching, not a proof that every secret-bearing name is known.
/// </para>
/// </summary>
internal static class SecretPropertyNameMatcher
{
    private static readonly HashSet<string> SegmentTerms = new(StringComparer.Ordinal)
    {
        "accesskey",
        "apikey",
        "authorization",
        "clientsecret",
        "connectionstring",
        "cookie",
        "jwt",
        "passwd",
        "password",
        "privatekey",
        "secret",
        "token",
    };

    private static readonly HashSet<string> WholeNameTerms = new(StringComparer.Ordinal)
    {
        "session",
    };

    private static readonly int MaxSegmentTermLength = SegmentTerms.Max(term => term.Length);

    public static bool IsSensitive(string propertyName)
    {
        if (string.IsNullOrEmpty(propertyName))
        {
            return false;
        }

        var starts = new List<int>();
        var normalized = Normalize(propertyName, starts);
        if (starts.Count == 0)
        {
            return false;
        }

        return WholeNameTerms.Contains(normalized) || ContainsSegmentTerm(normalized, starts);
    }

    private static bool ContainsSegmentTerm(string normalized, List<int> starts)
    {
        var lookup = SegmentTerms.GetAlternateLookup<ReadOnlySpan<char>>();
        for (var first = 0; first < starts.Count; first++)
        {
            for (var last = first; last < starts.Count; last++)
            {
                var from = starts[first];
                var to = last + 1 < starts.Count ? starts[last + 1] : normalized.Length;
                var length = to - from;
                if (length > MaxSegmentTermLength)
                {
                    break;
                }

                if (lookup.Contains(normalized.AsSpan(from, length)))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Builds the lowercase separator-free form of <paramref name="propertyName"/> and records the
    /// start index of every segment inside it.
    /// </summary>
    private static string Normalize(string propertyName, List<int> starts)
    {
        var builder = new StringBuilder(propertyName.Length);
        var previous = '\0';
        for (var index = 0; index < propertyName.Length; index++)
        {
            var current = propertyName[index];
            if (!char.IsLetterOrDigit(current))
            {
                previous = '\0';
                continue;
            }

            var next = index + 1 < propertyName.Length ? propertyName[index + 1] : '\0';
            if (previous == '\0' || IsSegmentBoundary(previous, current, next))
            {
                starts.Add(builder.Length);
            }

            builder.Append(char.ToLowerInvariant(current));
            previous = current;
        }

        return builder.ToString();
    }

    /// <summary>
    /// A new segment starts at a lower-to-upper transition, at the last capital of a capital run
    /// that is followed by a lowercase letter (so <c>APIKey</c> splits into <c>api</c> and
    /// <c>key</c>), and at any letter-to-digit or digit-to-letter transition.
    /// </summary>
    private static bool IsSegmentBoundary(char previous, char current, char next) =>
        (char.IsUpper(current) && (!char.IsUpper(previous) || char.IsLower(next))) ||
        (char.IsDigit(current) != char.IsDigit(previous));
}
