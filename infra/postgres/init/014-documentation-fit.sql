ALTER TABLE incidentcompass.triage_reports
    ADD COLUMN IF NOT EXISTS documentation_fit text NOT NULL DEFAULT 'Missing'
        CHECK (documentation_fit IN (
            'Current',
            'CurrentWithHistorical',
            'StaleOnly',
            'Missing',
            'MultipleCurrentDocuments'));