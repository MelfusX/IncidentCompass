ALTER TABLE incidentcompass.triage_jobs
    ADD COLUMN IF NOT EXISTS retriage_trigger_job_id uuid NULL REFERENCES incidentcompass.triage_jobs (id),
    ADD COLUMN IF NOT EXISTS supersedes_report_id uuid NULL;

ALTER TABLE incidentcompass.triage_jobs
    DROP CONSTRAINT IF EXISTS ck_triage_jobs_retriage_provenance,
    DROP CONSTRAINT IF EXISTS fk_triage_jobs_superseded_same_fault;

ALTER TABLE incidentcompass.triage_jobs
    ADD CONSTRAINT ck_triage_jobs_retriage_provenance
        CHECK ((retriage_trigger_job_id IS NULL) = (supersedes_report_id IS NULL));

ALTER TABLE incidentcompass.triage_jobs
    ADD CONSTRAINT fk_triage_jobs_superseded_same_fault
        FOREIGN KEY (supersedes_report_id, fault_id)
        REFERENCES incidentcompass.triage_reports (id, fault_id);

CREATE UNIQUE INDEX IF NOT EXISTS ux_triage_jobs_retriage_trigger
    ON incidentcompass.triage_jobs (retriage_trigger_job_id)
    WHERE retriage_trigger_job_id IS NOT NULL;