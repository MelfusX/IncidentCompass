using Microsoft.Extensions.Logging;

namespace IncidentCompass.UnitTests;

/// <summary>
/// In-memory <see cref="ILogger{TCategoryName}"/> that keeps every write in order so tests can
/// assert both the content of a log event and the sequence in which events were written.
/// </summary>
internal sealed class RecordingLogger<T> : ILogger<T>
{
    private readonly List<RecordedLogEntry> entries = [];

    public IReadOnlyList<RecordedLogEntry> Entries => entries;

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        entries.Add(new RecordedLogEntry(logLevel, eventId, formatter(state, exception), exception));
    }

    public int IndexOf(int eventId) =>
        entries.FindIndex(entry => entry.EventId.Id == eventId);

    public RecordedLogEntry Single(int eventId) =>
        entries.Single(entry => entry.EventId.Id == eventId);
}
