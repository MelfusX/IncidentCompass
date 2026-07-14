-- v0.2 retry budget: provider outages may opt into one zero-cost retry.

ALTER TABLE incidentcompass.triage_jobs
    ADD COLUMN IF NOT EXISTS retry_without_consuming_attempt boolean NOT NULL DEFAULT false;