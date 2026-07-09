-- v0.2 file-backed memory sync: one active seed per source, metadata and deactivation.

ALTER TABLE incidentcompass.memory_items
    ADD COLUMN IF NOT EXISTS service_name text,
    ADD COLUMN IF NOT EXISTS component text,
    ADD COLUMN IF NOT EXISTS release_name text,
    ADD COLUMN IF NOT EXISTS is_active boolean NOT NULL DEFAULT true,
    ADD COLUMN IF NOT EXISTS seed_managed boolean NOT NULL DEFAULT false,
    ADD COLUMN IF NOT EXISTS updated_at_utc timestamptz,
    ADD COLUMN IF NOT EXISTS superseded_at_utc timestamptz;

UPDATE incidentcompass.memory_items
SET updated_at_utc = created_at_utc
WHERE updated_at_utc IS NULL;

ALTER TABLE incidentcompass.memory_items
    ALTER COLUMN updated_at_utc SET NOT NULL,
    ALTER COLUMN updated_at_utc SET DEFAULT now();

UPDATE incidentcompass.memory_items
SET seed_managed = true
WHERE source LIKE 'runbooks/%'
   OR source LIKE 'incidents/%'
   OR source LIKE 'operational-notes/%'
   OR source LIKE 'documents/%'
   OR source LIKE 'release-notes/%'
   OR source LIKE 'postmortems/%';

DO $$
BEGIN
    IF EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conrelid = 'incidentcompass.memory_items'::regclass
          AND conname = 'memory_items_kind_check') THEN
        ALTER TABLE incidentcompass.memory_items DROP CONSTRAINT memory_items_kind_check;
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conrelid = 'incidentcompass.memory_items'::regclass
          AND conname = 'memory_items_kind_v2_check') THEN
        ALTER TABLE incidentcompass.memory_items
            ADD CONSTRAINT memory_items_kind_v2_check
            CHECK (kind IN ('runbook', 'known_incident', 'operational_note', 'release_note', 'postmortem'));
    END IF;
END
$$;

WITH ranked AS (
    SELECT id,
           row_number() OVER (
               PARTITION BY tenant_id, source
               ORDER BY created_at_utc DESC, id DESC) AS rank
    FROM incidentcompass.memory_items
    WHERE seed_managed = true
      AND is_active = true
)
UPDATE incidentcompass.memory_items AS item
SET is_active = false,
    superseded_at_utc = COALESCE(item.superseded_at_utc, now()),
    updated_at_utc = now()
FROM ranked
WHERE item.id = ranked.id
  AND ranked.rank > 1;

CREATE UNIQUE INDEX IF NOT EXISTS ux_memory_items_active_seed_source
    ON incidentcompass.memory_items (tenant_id, source)
    WHERE seed_managed = true AND is_active = true;

CREATE INDEX IF NOT EXISTS ix_memory_items_active_lookup
    ON incidentcompass.memory_items (tenant_id, is_active, kind);
