CREATE TABLE IF NOT EXISTS incidentcompass.recurrence_states (
    tenant_id text NOT NULL,
    service_name text NOT NULL,
    environment text NOT NULL,
    fingerprint text NOT NULL,
    fingerprint_version integer NOT NULL,
    grouping_rule_id text NOT NULL,
    grouping_rule_version integer NOT NULL,
    recurrence_count integer NOT NULL CHECK (recurrence_count > 0),
    first_recurrence_at_utc timestamptz NOT NULL,
    last_recurrence_at_utc timestamptz NOT NULL,
    escalation_intent_job_id uuid NULL REFERENCES incidentcompass.triage_jobs (id),
    escalation_intent_fault_id uuid NULL REFERENCES incidentcompass.faults (id),
    PRIMARY KEY (tenant_id, service_name, environment, fingerprint, fingerprint_version, grouping_rule_id, grouping_rule_version),
    CHECK ((escalation_intent_job_id IS NULL) = (escalation_intent_fault_id IS NULL)),
    CHECK (last_recurrence_at_utc >= first_recurrence_at_utc)
);

CREATE INDEX IF NOT EXISTS ix_recurrence_states_escalation_intent
    ON incidentcompass.recurrence_states (escalation_intent_job_id)
    WHERE escalation_intent_job_id IS NOT NULL;

DO $$
DECLARE
    constraint_name text;
BEGIN
    FOR constraint_name IN
        SELECT conname
        FROM pg_constraint
        WHERE conrelid = 'incidentcompass.triage_artifacts'::regclass
          AND contype = 'c'
          AND (conname IN ('triage_artifacts_kind_check', 'ck_triage_artifacts_kind')
               OR pg_get_constraintdef(oid) LIKE '%kind%')
    LOOP
        EXECUTE format('ALTER TABLE incidentcompass.triage_artifacts DROP CONSTRAINT %I', constraint_name);
    END LOOP;

    ALTER TABLE incidentcompass.triage_artifacts
        ADD CONSTRAINT ck_triage_artifacts_kind
        CHECK (kind IN ('TriggerSignal', 'NeighborSet', 'PriorReport', 'RecurrenceState', 'RetrievedItem', 'ToolResult', 'WorkerOutput'));
END $$;
DO $$
DECLARE
    constraint_name text;
BEGIN
    FOR constraint_name IN
        SELECT conname
        FROM pg_constraint
        WHERE conrelid = 'incidentcompass.triage_evidence'::regclass
          AND contype = 'c'
          AND conname IN ('triage_evidence_kind_check', 'ck_triage_evidence_kind')
    LOOP
        EXECUTE format('ALTER TABLE incidentcompass.triage_evidence DROP CONSTRAINT %I', constraint_name);
    END LOOP;

    ALTER TABLE incidentcompass.triage_evidence
        ADD CONSTRAINT ck_triage_evidence_kind
        CHECK (kind IN ('TriggerSignal', 'NeighborSet', 'PriorReport', 'RecurrenceState', 'RetrievedItem', 'Runbook', 'KnownIncident', 'ToolResult'));
END $$;