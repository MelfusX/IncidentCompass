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
        new(3, "v0.2-signal-delivery-idempotency", ["012-signal-delivery-idempotency.sql"]),
        new(4, "v0.2-memory-seed-generations", ["013-memory-seed-generations.sql"]),
        new(5, "v0.2-documentation-fit", ["014-documentation-fit.sql"]),
        new(6, "v0.2-versioned-fault-grouping", ["015-versioned-fault-grouping.sql"]),
        new(7, "v0.2-scoped-suppression", ["016-scoped-suppression.sql"]),
        new(8, "v0.2-recurrence-state", ["017-recurrence-state.sql"]),
        new(9, "v0.2-report-lifecycle", ["018-report-lifecycle.sql"]),
        new(10, "v0.2-retriage-jobs", ["019-retriage-jobs.sql"]),
        new(11, "v0.2-report-listing", ["020-report-listing.sql"]),
        new(12, "v0.2-memory-seed-sync-status", ["021-memory-seed-sync-status.sql"]),
        new(13, "v0.2-triage-job-retry-budget", ["022-triage-job-retry-budget.sql"])
    ];
}
