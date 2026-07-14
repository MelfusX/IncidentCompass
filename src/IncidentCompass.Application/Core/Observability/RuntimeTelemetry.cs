using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace IncidentCompass.Application.Core.Observability;

public sealed class RuntimeTelemetry : IRuntimeTelemetry
{
    private static readonly ActivitySource Source = new("IncidentCompass.Runtime", "0.2.0");
    private static readonly Meter Meter = new("IncidentCompass.Runtime", "0.2.0");
    private static readonly Counter<long> JobClaims = Meter.CreateCounter<long>("incidentcompass.triage.job.claims");
    private static readonly Counter<long> JobAttempts = Meter.CreateCounter<long>("incidentcompass.triage.job.attempts");
    private static readonly Counter<long> ModelCalls = Meter.CreateCounter<long>("incidentcompass.triage.model.calls");
    private static readonly Histogram<double> ModelDuration = Meter.CreateHistogram<double>("incidentcompass.triage.model.duration.ms");
    private static readonly Counter<long> ToolCalls = Meter.CreateCounter<long>("incidentcompass.triage.tool.calls");
    private static readonly Counter<long> Migrations = Meter.CreateCounter<long>("incidentcompass.persistence.migrations");
    private static readonly Counter<long> MemorySyncs = Meter.CreateCounter<long>("incidentcompass.memory.syncs");

    public IDisposable StartJobClaim() => Start("triage.job.claim");

    public IDisposable StartJobAttempt() => Start("triage.job.attempt");

    public IDisposable StartModelCall() => Start("triage.model.call");

    public IDisposable StartToolCall() => Start("triage.tool.call");

    public IDisposable StartMigration() => Start("persistence.migration");

    public IDisposable StartMemorySync() => Start("memory.sync");

    public void RecordJobClaim() => Add(JobClaims, RuntimeTelemetryOutcome.Claimed);

    public void RecordJobAttempt(RuntimeTelemetryOutcome outcome) => Add(JobAttempts, outcome);

    public void RecordModelCall(RuntimeTelemetryOutcome outcome, double durationMilliseconds)
    {
        var tagValue = GetTagValue(outcome);
        Add(ModelCalls, outcome);
        try
        {
            ModelDuration.Record(durationMilliseconds, new KeyValuePair<string, object?>("outcome", tagValue));
        }
        catch
        {
        }
    }

    public void RecordToolCall(RuntimeTelemetryOutcome outcome) => Add(ToolCalls, outcome);

    public void RecordMigration(RuntimeTelemetryOutcome outcome) => Add(Migrations, outcome);

    public void RecordMemorySync(RuntimeTelemetryOutcome outcome) => Add(MemorySyncs, outcome);

    private static IDisposable Start(string name)
    {
        try
        {
            return new RuntimeActivityScope(Source.StartActivity(name));
        }
        catch
        {
            return RuntimeTelemetryNoopScope.Instance;
        }
    }

    private static void Add(Counter<long> counter, RuntimeTelemetryOutcome outcome)
    {
        try
        {
            counter.Add(1, new KeyValuePair<string, object?>("outcome", GetTagValue(outcome)));
        }
        catch
        {
        }
    }

    private static string GetTagValue(RuntimeTelemetryOutcome outcome)
    {
        var normalizedOutcome = Enum.IsDefined(outcome) ? outcome : RuntimeTelemetryOutcome.Failed;
        return normalizedOutcome == RuntimeTelemetryOutcome.ProviderUnavailable
            ? string.Join('_', ["provider", "unavailable"])
            : normalizedOutcome.ToString().ToLowerInvariant();
    }
}