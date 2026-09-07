namespace IncidentCompass.Application.Core.Observability;

internal sealed class RuntimeTelemetryNoopScope : IDisposable
{
    public static RuntimeTelemetryNoopScope Instance { get; } = new();

    public void Dispose()
    {
    }
}
