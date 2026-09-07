using Microsoft.Extensions.Logging;

namespace IncidentCompass.UnitTests;

/// <summary>
/// One captured <see cref="ILogger"/> write. Tests assert on the stable event id, the level and
/// the rendered message so that a logging regression is visible without depending on a provider.
/// </summary>
internal sealed record RecordedLogEntry(
    LogLevel Level,
    EventId EventId,
    string Message,
    Exception? Exception);
