namespace IncidentCompass.Infrastructure.Postgres;

internal static class PostgresMigrationCatalog
{
    public static readonly IReadOnlyList<PostgresSchemaMigration> All =
    [
        new(1, "v0.1.1-baseline",
        [
            "001-enable-pgvector.sql",
            "004-observability-cost.sql",
            "006-tool-audit.sql",
            "007-intake.sql",
            "008-triage-ledger.sql",
            "009-triage-reports-minimal.sql",
            "010-memory.sql"
        ]),
        new(2, "v0.2-memory-file-sync", ["011-memory-file-sync.sql"]),
        new(3, "v0.2-signal-delivery-idempotency", ["012-signal-delivery-idempotency.sql"])
    ];
}