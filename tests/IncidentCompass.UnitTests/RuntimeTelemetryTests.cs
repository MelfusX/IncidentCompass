using System.Diagnostics;
using System.Diagnostics.Metrics;
using IncidentCompass.Application.Core.Observability;

namespace IncidentCompass.UnitTests;

[Collection("Runtime telemetry")]
public sealed class RuntimeTelemetryTests
{
    [Fact]
    public void RecordsOnlyFixedOperationNamesAndNoSensitiveActivityTags()
    {
        var activities = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "IncidentCompass.Runtime",
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activities.Add
        };
        ActivitySource.AddActivityListener(listener);
        var telemetry = new RuntimeTelemetry();

        using (telemetry.StartJobClaim()) { }
        using (telemetry.StartJobAttempt()) { }
        using (telemetry.StartModelCall()) { }
        using (telemetry.StartToolCall()) { }
        using (telemetry.StartMigration()) { }
        using (telemetry.StartMemorySync()) { }

        Assert.Equal(
            ["memory.sync", "persistence.migration", "triage.job.attempt", "triage.job.claim", "triage.model.call", "triage.tool.call"],
            activities.Select(static activity => activity.OperationName).OrderBy(static name => name, StringComparer.Ordinal));
        Assert.All(activities, static activity => Assert.Empty(activity.TagObjects));
    }

    [Fact]
    public void ListenerFailuresDoNotEscapeTelemetryCalls()
    {
        using var activityListener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "IncidentCompass.Runtime",
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStarted = static _ => throw new InvalidOperationException("export unavailable")
        };
        ActivitySource.AddActivityListener(activityListener);
        using var meterListener = new MeterListener();
        meterListener.InstrumentPublished = static (instrument, listener) =>
        {
            if (instrument.Meter.Name == "IncidentCompass.Runtime")
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };
        meterListener.SetMeasurementEventCallback<long>(static (_, _, _, _) => throw new InvalidOperationException("export unavailable"));
        meterListener.Start();
        var telemetry = new RuntimeTelemetry();

        var exception = Record.Exception(() =>
        {
            using (telemetry.StartModelCall()) { }
            telemetry.RecordJobClaim();
            telemetry.RecordJobAttempt((RuntimeTelemetryOutcome)999);
            telemetry.RecordModelCall(RuntimeTelemetryOutcome.Succeeded, 7);
            telemetry.RecordToolCall(RuntimeTelemetryOutcome.Denied);
            telemetry.RecordMigration(RuntimeTelemetryOutcome.Failed);
            telemetry.RecordMemorySync(RuntimeTelemetryOutcome.Cancelled);
        });

        Assert.Null(exception);
    }
}
