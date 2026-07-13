-- Versioned grouping identity prevents faults from merging across independently configured rules.

ALTER TABLE incidentcompass.signals
    ADD COLUMN IF NOT EXISTS grouping_rule_id text NOT NULL DEFAULT 'legacy-default',
    ADD COLUMN IF NOT EXISTS grouping_rule_version integer NOT NULL DEFAULT 1;

ALTER TABLE incidentcompass.faults
    ADD COLUMN IF NOT EXISTS grouping_rule_id text NOT NULL DEFAULT 'legacy-default',
    ADD COLUMN IF NOT EXISTS grouping_rule_version integer NOT NULL DEFAULT 1;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'ck_signals_grouping_rule_id_nonblank'
          AND conrelid = 'incidentcompass.signals'::regclass
    ) THEN
        ALTER TABLE incidentcompass.signals
            ADD CONSTRAINT ck_signals_grouping_rule_id_nonblank
            CHECK (length(btrim(grouping_rule_id)) > 0);
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'ck_signals_grouping_rule_version_positive'
          AND conrelid = 'incidentcompass.signals'::regclass
    ) THEN
        ALTER TABLE incidentcompass.signals
            ADD CONSTRAINT ck_signals_grouping_rule_version_positive
            CHECK (grouping_rule_version > 0);
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'ck_faults_grouping_rule_id_nonblank'
          AND conrelid = 'incidentcompass.faults'::regclass
    ) THEN
        ALTER TABLE incidentcompass.faults
            ADD CONSTRAINT ck_faults_grouping_rule_id_nonblank
            CHECK (length(btrim(grouping_rule_id)) > 0);
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'ck_faults_grouping_rule_version_positive'
          AND conrelid = 'incidentcompass.faults'::regclass
    ) THEN
        ALTER TABLE incidentcompass.faults
            ADD CONSTRAINT ck_faults_grouping_rule_version_positive
            CHECK (grouping_rule_version > 0);
    END IF;
END;
$$;

DROP INDEX IF EXISTS incidentcompass.ux_faults_open_group;

CREATE UNIQUE INDEX IF NOT EXISTS ux_faults_open_group
    ON incidentcompass.faults (
        tenant_id, service_name, environment, fingerprint, fingerprint_version,
        grouping_rule_id, grouping_rule_version)
    WHERE status IN ('Queued', 'Analyzing') AND can_group;

CREATE INDEX IF NOT EXISTS ix_faults_versioned_group_lookup
    ON incidentcompass.faults (
        tenant_id, service_name, environment, fingerprint, fingerprint_version,
        grouping_rule_id, grouping_rule_version, created_at_utc DESC);

CREATE INDEX IF NOT EXISTS ix_signals_versioned_neighbor_lookup
    ON incidentcompass.signals (
        tenant_id, service_name, environment, fingerprint, fingerprint_version,
        grouping_rule_id, grouping_rule_version, observed_at_utc);