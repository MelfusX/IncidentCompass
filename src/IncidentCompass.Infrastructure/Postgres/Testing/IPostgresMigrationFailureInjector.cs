namespace IncidentCompass.Infrastructure.Postgres.Testing;

/// <summary>
/// Test-only fault seam for the schema migration runner. This is production code that exists for
/// testability, not dead code: the hook must fire at one chosen migration version in the middle of a
/// sequential catalog run, so that earlier migrations stay applied, the failing one is recorded as
/// <c>Failed</c> in <c>incidentcompass.schema_migrations</c> and later ones never run. A decorator
/// around the migration runner cannot express that, because a single <c>MigrateAsync</c> call applies
/// every pending migration and can only be failed as a whole. Production composition always binds
/// <see cref="NoPostgresMigrationFailureInjector"/>; only the integration tests replace it.
/// </summary>
internal interface IPostgresMigrationFailureInjector
{
    /// <summary>
    /// Invoked inside the per-migration transaction, before that migration applies any statement.
    /// Throwing here must roll that migration back without touching the ones already applied.
    /// </summary>
    void ThrowIfRequested(int version);
}
