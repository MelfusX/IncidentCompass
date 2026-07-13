ALTER TABLE incidentcompass.triage_reports
    DROP CONSTRAINT IF EXISTS triage_reports_fault_id_key;

ALTER TABLE incidentcompass.triage_reports
    ADD COLUMN IF NOT EXISTS job_id uuid NULL REFERENCES incidentcompass.triage_jobs (id),
    ADD COLUMN IF NOT EXISTS supersedes_report_id uuid NULL REFERENCES incidentcompass.triage_reports (id);

ALTER TABLE incidentcompass.triage_reports
    DROP CONSTRAINT IF EXISTS fk_triage_reports_superseded_same_fault,
    DROP CONSTRAINT IF EXISTS uq_triage_reports_id_fault;

ALTER TABLE incidentcompass.triage_reports
    ADD CONSTRAINT uq_triage_reports_id_fault UNIQUE (id, fault_id);

ALTER TABLE incidentcompass.triage_reports
    ADD CONSTRAINT fk_triage_reports_superseded_same_fault
        FOREIGN KEY (supersedes_report_id, fault_id)
        REFERENCES incidentcompass.triage_reports (id, fault_id);
ALTER TABLE incidentcompass.triage_reports
    DROP CONSTRAINT IF EXISTS ck_triage_reports_no_self_supersede;

ALTER TABLE incidentcompass.triage_reports
    ADD CONSTRAINT ck_triage_reports_no_self_supersede
        CHECK (supersedes_report_id IS NULL OR supersedes_report_id <> id);

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM pg_trigger
        WHERE tgrelid = 'incidentcompass.triage_reports'::regclass
          AND tgname = 'trg_triage_reports_immutable') THEN
        UPDATE incidentcompass.triage_reports AS report
        SET job_id = (
            SELECT job.id
            FROM incidentcompass.triage_jobs AS job
            WHERE job.fault_id = report.fault_id
            ORDER BY job.created_at_utc DESC, job.id DESC
            LIMIT 1
        )
        WHERE report.job_id IS NULL
          AND EXISTS (
              SELECT 1
              FROM incidentcompass.triage_jobs AS job
              WHERE job.fault_id = report.fault_id);
    END IF;
END;
$$;

CREATE UNIQUE INDEX IF NOT EXISTS ux_triage_reports_job
    ON incidentcompass.triage_reports (job_id)
    WHERE job_id IS NOT NULL;

CREATE UNIQUE INDEX IF NOT EXISTS ux_triage_reports_supersedes
    ON incidentcompass.triage_reports (supersedes_report_id)
    WHERE supersedes_report_id IS NOT NULL;

CREATE INDEX IF NOT EXISTS ix_triage_reports_fault_history
    ON incidentcompass.triage_reports (fault_id, created_at_utc DESC, id DESC);

CREATE OR REPLACE FUNCTION incidentcompass.reject_triage_report_mutation()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    RAISE EXCEPTION 'triage reports are immutable after publication';
END;
$$;

DROP TRIGGER IF EXISTS trg_triage_reports_immutable ON incidentcompass.triage_reports;

CREATE TRIGGER trg_triage_reports_immutable
    BEFORE UPDATE OR DELETE ON incidentcompass.triage_reports
    FOR EACH ROW
    EXECUTE FUNCTION incidentcompass.reject_triage_report_mutation();
