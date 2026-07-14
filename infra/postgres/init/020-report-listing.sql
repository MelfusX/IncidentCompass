CREATE INDEX IF NOT EXISTS ix_triage_reports_created_desc
    ON incidentcompass.triage_reports (created_at_utc DESC, id DESC);

CREATE INDEX IF NOT EXISTS ix_faults_tenant_id
    ON incidentcompass.faults (tenant_id, id);