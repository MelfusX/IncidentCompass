namespace IncidentCompass.Infrastructure.Postgres;

internal sealed class NoPostgresMigrationFailureInjector : IPostgresMigrationFailureInjector
{
    public void ThrowIfRequested(int version)
    {
    }
}