-- v0.2 memory sync ownership: independently published corpora cannot deactivate each other.

ALTER TABLE incidentcompass.memory_items
    ADD COLUMN IF NOT EXISTS seed_owner text NOT NULL DEFAULT 'default'
        CHECK (length(btrim(seed_owner)) > 0),
    ADD COLUMN IF NOT EXISTS seed_generation uuid;

ALTER TABLE incidentcompass.memory_items
    DROP CONSTRAINT IF EXISTS memory_items_tenant_id_source_content_hash_version_key;

DROP INDEX IF EXISTS incidentcompass.ux_memory_items_active_seed_source;

CREATE UNIQUE INDEX IF NOT EXISTS ux_memory_items_seed_owner_content
    ON incidentcompass.memory_items (tenant_id, seed_owner, source, content_hash, version);

CREATE UNIQUE INDEX IF NOT EXISTS ux_memory_items_active_seed_owner_source
    ON incidentcompass.memory_items (tenant_id, seed_owner, source)
    WHERE seed_managed = true AND is_active = true;

CREATE INDEX IF NOT EXISTS ix_memory_items_seed_owner_generation
    ON incidentcompass.memory_items (tenant_id, seed_owner, seed_generation)
    WHERE seed_managed = true;