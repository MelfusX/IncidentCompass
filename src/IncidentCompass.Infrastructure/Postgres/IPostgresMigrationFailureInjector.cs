namespace IncidentCompass.Infrastructure.Postgres;

public interface IPostgresMigrationFailureInjector
{
    void ThrowIfRequested(int version);
}
