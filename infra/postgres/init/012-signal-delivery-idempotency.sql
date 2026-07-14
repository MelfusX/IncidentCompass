ALTER TABLE incidentcompass.signals
    ADD COLUMN IF NOT EXISTS delivery_key text NULL;

CREATE UNIQUE INDEX IF NOT EXISTS ux_signals_delivery_key
    ON incidentcompass.signals (tenant_id, source, delivery_key)
    WHERE delivery_key IS NOT NULL;