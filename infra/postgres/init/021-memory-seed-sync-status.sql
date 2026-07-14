-- v0.2 memory sync health: Worker-owned status must be visible to the API process.

CREATE TABLE IF NOT EXISTS incidentcompass.memory_seed_sync_status (
    tenant_id text NOT NULL CHECK (length(btrim(tenant_id)) > 0),
    seed_owner text NOT NULL CHECK (length(btrim(seed_owner)) > 0),
    enabled boolean NOT NULL,
    runtime_resync_enabled boolean NOT NULL,
    last_attempt_at_utc timestamptz,
    last_success_at_utc timestamptz,
    active_generation uuid,
    last_error_code text,
    updated_at_utc timestamptz NOT NULL,
    PRIMARY KEY (tenant_id, seed_owner)
);