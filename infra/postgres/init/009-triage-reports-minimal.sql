-- Phase 2 minimal report persistence. Full report contract validation, evidence rows,
-- grounding and the fenced final transaction are Phase 5.

CREATE TABLE IF NOT EXISTS incidentcompass.triage_reports (
    id uuid PRIMARY KEY,
    fault_id uuid NOT NULL REFERENCES incidentcompass.faults (id),
    status text NOT NULL CHECK (status IN ('Completed', 'InsufficientEvidence', 'Failed')),
    summary text NOT NULL CHECK (length(btrim(summary)) > 0),
    classification text NOT NULL CHECK (classification IN ('KnownIncident', 'LikelyRegression', 'SimpleKnownError', 'Unknown', 'Noise')),
    confidence text NOT NULL CHECK (confidence IN ('Low', 'Medium', 'High')),
    is_mass_issue boolean NULL,
    recommended_next_action text NULL,
    limitations text[] NOT NULL DEFAULT ARRAY[]::text[],
    config_hash text NOT NULL REFERENCES incidentcompass.triage_config_snapshots (config_hash),
    created_at_utc timestamptz NOT NULL,
    UNIQUE (fault_id)
);
