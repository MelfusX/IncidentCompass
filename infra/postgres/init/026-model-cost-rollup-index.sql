-- Supports bounded tenant/fault ModelCall window reads for the read-only cost rollup.
CREATE INDEX IF NOT EXISTS ix_triage_ledger_model_call_fault_created_at
    ON incidentcompass.triage_ledger (fault_id, created_at_utc)
    WHERE event_type = 'ModelCall';
