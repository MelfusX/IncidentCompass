using Microsoft.Extensions.Logging;

namespace IncidentCompass.Application.Intake.Redaction;

/// <summary>
/// Diagnostics for configured redaction patterns that exceed their match timeout. The event carries
/// the pattern name and the field path only: the field value is exactly the text the redactor failed
/// to clean, so it must never reach a log.
/// </summary>
internal static partial class RedactionTimeoutLog
{
    [LoggerMessage(
        EventId = 3601,
        Level = LogLevel.Error,
        Message = "Redaction pattern {PatternName} exceeded its match timeout at field {FieldPath}; the whole field was replaced with {Marker}. The field value is never logged.")]
    public static partial void PatternTimedOut(
        ILogger logger,
        string patternName,
        string fieldPath,
        string marker);
}
