namespace IncidentCompass.Application.Core.Observability;

public interface IRuntimeTelemetry
{
    IDisposable StartJobClaim();

    IDisposable StartJobAttempt();

    IDisposable StartModelCall();

    IDisposable StartToolCall();

    IDisposable StartMigration();

    IDisposable StartMemorySync();

    void RecordJobClaim();

    void RecordJobAttempt(RuntimeTelemetryOutcome outcome);

    void RecordModelCall(RuntimeTelemetryOutcome outcome, double durationMilliseconds);

    void RecordToolCall(RuntimeTelemetryOutcome outcome);

    void RecordMigration(RuntimeTelemetryOutcome outcome);

    void RecordMemorySync(RuntimeTelemetryOutcome outcome);
}