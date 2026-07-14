ALTER TABLE incidentcompass.signals
    ADD COLUMN IF NOT EXISTS suppression_rule_id text NULL,
    ADD COLUMN IF NOT EXISTS effective_suppression_window_minutes integer NULL;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE connamespace = 'incidentcompass'::regnamespace
          AND conname = 'ck_signals_suppression_rule_id_nonblank') THEN
        ALTER TABLE incidentcompass.signals
            ADD CONSTRAINT ck_signals_suppression_rule_id_nonblank
            CHECK (suppression_rule_id IS NULL OR length(btrim(suppression_rule_id)) > 0);
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE connamespace = 'incidentcompass'::regnamespace
          AND conname = 'ck_signals_effective_suppression_window_positive') THEN
        ALTER TABLE incidentcompass.signals
            ADD CONSTRAINT ck_signals_effective_suppression_window_positive
            CHECK (effective_suppression_window_minutes IS NULL OR effective_suppression_window_minutes > 0);
    END IF;
END $$;

CREATE INDEX IF NOT EXISTS ix_signals_suppression_audit
    ON incidentcompass.signals (suppressed_by_fault_id, observed_at_utc)
    WHERE is_suppressed;