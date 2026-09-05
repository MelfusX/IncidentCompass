-- Durable post-report evaluation intents. This queue prepares governed proposals only;
-- action_approvals remains the sole approval and external-dispatch outbox.

CREATE TABLE IF NOT EXISTS incidentcompass.post_report_action_intents (
    id uuid PRIMARY KEY,
    tenant_id text NOT NULL CHECK (length(btrim(tenant_id)) > 0),
    origin_report_id uuid NOT NULL REFERENCES incidentcompass.triage_reports (id),
    fault_id uuid NOT NULL REFERENCES incidentcompass.faults (id),
    job_id uuid NOT NULL REFERENCES incidentcompass.triage_jobs (id),
    attempt integer NOT NULL CHECK (attempt > 0),
    tool_id text NOT NULL CHECK (tool_id ~ '^[A-Za-z0-9_.-]{1,128}$'),
    workflow_version integer NOT NULL CHECK (workflow_version = 1),
    route_id text NULL CHECK (route_id IS NULL OR route_id ~ '^[A-Za-z0-9_.-]{1,128}$'),
    config_hash text NOT NULL REFERENCES incidentcompass.triage_config_snapshots (config_hash),
    proposal_key text NOT NULL CHECK (length(btrim(proposal_key)) BETWEEN 1 AND 256),
    workflow_input bytea NOT NULL CHECK (octet_length(workflow_input) BETWEEN 1 AND 8192),
    state text NOT NULL CHECK (state IN (
        'pending', 'processing', 'retry_pending', 'completed', 'dead_lettered')),
    claim_owner text NULL CHECK (claim_owner IS NULL OR length(claim_owner) BETWEEN 1 AND 128),
    claim_fence uuid NULL,
    claim_until_utc timestamptz NULL,
    attempt_count integer NOT NULL DEFAULT 0 CHECK (attempt_count BETWEEN 0 AND 100),
    next_attempt_at_utc timestamptz NULL,
    last_error_code text NULL CHECK (last_error_code IS NULL OR length(last_error_code) BETWEEN 1 AND 128),
    created_at_utc timestamptz NOT NULL,
    completed_at_utc timestamptz NULL,
    UNIQUE (tenant_id, origin_report_id, tool_id),
    CHECK ((claim_owner IS NULL) = (claim_fence IS NULL)),
    CHECK ((claim_owner IS NULL) = (claim_until_utc IS NULL)),
    CHECK (state <> 'pending' OR
           (attempt_count = 0 AND claim_owner IS NULL AND next_attempt_at_utc IS NULL AND
            last_error_code IS NULL AND completed_at_utc IS NULL)),
    CHECK (state <> 'processing' OR
           (attempt_count > 0 AND claim_owner IS NOT NULL AND next_attempt_at_utc IS NULL AND
            completed_at_utc IS NULL)),
    CHECK (state <> 'retry_pending' OR
           (attempt_count > 0 AND claim_owner IS NULL AND next_attempt_at_utc IS NOT NULL AND
            last_error_code IS NOT NULL AND completed_at_utc IS NULL)),
    CHECK (state NOT IN ('completed', 'dead_lettered') OR
           (attempt_count > 0 AND claim_owner IS NULL AND next_attempt_at_utc IS NULL AND
            completed_at_utc IS NOT NULL)),
    CHECK (state <> 'dead_lettered' OR last_error_code IS NOT NULL)
);

CREATE INDEX IF NOT EXISTS ix_post_report_action_intents_candidates
    ON incidentcompass.post_report_action_intents (
        state, next_attempt_at_utc, claim_until_utc, created_at_utc, id)
    WHERE state IN ('pending', 'retry_pending', 'processing');

CREATE OR REPLACE FUNCTION incidentcompass.validate_post_report_action_intent_transition()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    IF NEW.id IS DISTINCT FROM OLD.id OR
       NEW.tenant_id IS DISTINCT FROM OLD.tenant_id OR
       NEW.origin_report_id IS DISTINCT FROM OLD.origin_report_id OR
       NEW.fault_id IS DISTINCT FROM OLD.fault_id OR
       NEW.job_id IS DISTINCT FROM OLD.job_id OR
       NEW.attempt IS DISTINCT FROM OLD.attempt OR
       NEW.tool_id IS DISTINCT FROM OLD.tool_id OR
       NEW.workflow_version IS DISTINCT FROM OLD.workflow_version OR
       NEW.route_id IS DISTINCT FROM OLD.route_id OR
       NEW.config_hash IS DISTINCT FROM OLD.config_hash OR
       NEW.proposal_key IS DISTINCT FROM OLD.proposal_key OR
       NEW.workflow_input IS DISTINCT FROM OLD.workflow_input OR
       NEW.created_at_utc IS DISTINCT FROM OLD.created_at_utc THEN
        RAISE EXCEPTION 'post-report action intent identity is immutable';
    END IF;

    IF OLD.state IN ('completed', 'dead_lettered') THEN
        RAISE EXCEPTION 'terminal post-report action intents are immutable';
    END IF;

    IF NEW.state IS DISTINCT FROM OLD.state THEN
        IF NOT ((OLD.state IN ('pending', 'retry_pending') AND NEW.state = 'processing') OR
                (OLD.state = 'processing' AND NEW.state IN (
                    'retry_pending', 'completed', 'dead_lettered'))) THEN
            RAISE EXCEPTION 'invalid post-report action intent transition';
        END IF;
    ELSIF OLD.state = 'processing' THEN
        IF NEW.claim_owner IS DISTINCT FROM OLD.claim_owner OR
           NEW.claim_fence IS DISTINCT FROM OLD.claim_fence OR
           NEW.attempt_count IS DISTINCT FROM OLD.attempt_count THEN
            IF OLD.claim_until_utc > clock_timestamp() OR
               NEW.claim_owner IS NULL OR NEW.claim_fence IS NULL OR
               NOT (NEW.attempt_count = OLD.attempt_count + 1 OR
                    (NEW.attempt_count = OLD.attempt_count AND
                     OLD.last_error_code IS DISTINCT FROM 'internal_proposal_recovery' AND
                     NEW.last_error_code = 'internal_proposal_recovery' AND
                     EXISTS (
                         SELECT 1
                         FROM incidentcompass.action_approvals a
                         WHERE a.tenant_id = OLD.tenant_id
                           AND a.origin_report_id = OLD.origin_report_id
                           AND a.tool_id = OLD.tool_id
                           AND a.proposal_key = OLD.proposal_key))) THEN
                RAISE EXCEPTION 'invalid post-report action intent reclaim';
            END IF;
        ELSIF NEW.claim_until_utc <= OLD.claim_until_utc THEN
            RAISE EXCEPTION 'post-report action intent lease must advance';
        END IF;
    ELSE
        RAISE EXCEPTION 'post-report action intent mutation requires a valid transition';
    END IF;

    RETURN NEW;
END;
$$;

DROP TRIGGER IF EXISTS trg_post_report_action_intents_transition
    ON incidentcompass.post_report_action_intents;

CREATE TRIGGER trg_post_report_action_intents_transition
    BEFORE UPDATE ON incidentcompass.post_report_action_intents
    FOR EACH ROW
    EXECUTE FUNCTION incidentcompass.validate_post_report_action_intent_transition();

CREATE OR REPLACE FUNCTION incidentcompass.reject_post_report_action_intent_delete()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    RAISE EXCEPTION 'post-report action intents are durable lifecycle records';
END;
$$;

DROP TRIGGER IF EXISTS trg_post_report_action_intents_no_delete
    ON incidentcompass.post_report_action_intents;

CREATE TRIGGER trg_post_report_action_intents_no_delete
    BEFORE DELETE ON incidentcompass.post_report_action_intents
    FOR EACH ROW
    EXECUTE FUNCTION incidentcompass.reject_post_report_action_intent_delete();
