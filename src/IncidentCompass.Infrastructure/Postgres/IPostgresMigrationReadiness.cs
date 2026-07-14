namespace IncidentCompass.Infrastructure.Postgres;

public interface IPostgresMigrationReadiness
{
    bool IsReady { get; }
}