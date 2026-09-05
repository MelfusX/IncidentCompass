namespace IncidentCompass.Infrastructure.Postgres;

internal sealed class PostgresMigrationReadiness : IPostgresMigrationReadiness
{
    private int isReady;

    public bool IsReady => Volatile.Read(ref isReady) == 1;

    public void MarkReady() => Volatile.Write(ref isReady, 1);

    public void MarkFailed() => Volatile.Write(ref isReady, 0);
}
