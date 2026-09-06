namespace IncidentCompass.Infrastructure.Postgres.Testing;

/// <summary>
/// Production binding for <see cref="IPostgresMigrationFailureInjector"/>. It never faults; the
/// migration runner needs something to call at the seam.
/// </summary>
internal sealed class NoPostgresMigrationFailureInjector : IPostgresMigrationFailureInjector
{
    public void ThrowIfRequested(int version)
    {
    }
}
