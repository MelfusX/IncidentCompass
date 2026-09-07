using IncidentCompass.Infrastructure.Postgres.Testing;

namespace IncidentCompass.IntegrationTests;

internal sealed class FailingMigrationInjector(int failedVersion) : IPostgresMigrationFailureInjector
{
    public void ThrowIfRequested(int version)
    {
        if (version == failedVersion)
        {
            throw new InvalidOperationException($"Injected migration failure for version {version}.");
        }
    }
}
