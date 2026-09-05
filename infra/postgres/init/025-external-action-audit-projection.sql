-- Compact terminal projection for correlating governed actions with safe external identities.
-- Detailed canonical results remain in ActionResult artifacts.

SELECT pg_advisory_lock(hashtext('incidentcompass:025-external-action-audit-projection')::bigint);

ALTER TABLE incidentcompass.action_approvals
    ADD COLUMN IF NOT EXISTS external_resource_kind text NULL,
    ADD COLUMN IF NOT EXISTS external_resource_id text NULL,
    ADD COLUMN IF NOT EXISTS external_before_state text NULL,
    ADD COLUMN IF NOT EXISTS external_after_state text NULL;

DO $migration$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conrelid = 'incidentcompass.action_approvals'::regclass
          AND conname = 'ck_action_approvals_external_resource_kind') THEN
        ALTER TABLE incidentcompass.action_approvals
            ADD CONSTRAINT ck_action_approvals_external_resource_kind CHECK (
                external_resource_kind IS NULL OR
                external_resource_kind IN ('telegram_message', 'github_issue'));
    END IF;
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conrelid = 'incidentcompass.action_approvals'::regclass
          AND conname = 'ck_action_approvals_external_resource_id') THEN
        ALTER TABLE incidentcompass.action_approvals
            ADD CONSTRAINT ck_action_approvals_external_resource_id CHECK (
                external_resource_id IS NULL OR
                external_resource_id ~ '^[1-9][0-9]{0,19}$');
    END IF;
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conrelid = 'incidentcompass.action_approvals'::regclass
          AND conname = 'ck_action_approvals_external_before_state_bound') THEN
        ALTER TABLE incidentcompass.action_approvals
            ADD CONSTRAINT ck_action_approvals_external_before_state_bound CHECK (
                external_before_state IS NULL OR
                octet_length(external_before_state) BETWEEN 1 AND 2048);
    END IF;
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conrelid = 'incidentcompass.action_approvals'::regclass
          AND conname = 'ck_action_approvals_external_after_state_bound') THEN
        ALTER TABLE incidentcompass.action_approvals
            ADD CONSTRAINT ck_action_approvals_external_after_state_bound CHECK (
                external_after_state IS NULL OR
                octet_length(external_after_state) BETWEEN 1 AND 2048);
    END IF;
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conrelid = 'incidentcompass.action_approvals'::regclass
          AND conname = 'ck_action_approvals_external_projection_shape') THEN
        ALTER TABLE incidentcompass.action_approvals
            ADD CONSTRAINT ck_action_approvals_external_projection_shape CHECK (
                (external_resource_kind IS NULL AND external_resource_id IS NULL AND
                 external_before_state IS NULL AND external_after_state IS NULL) OR
                (external_resource_kind IS NOT NULL AND external_resource_id IS NOT NULL AND
                 external_before_state IS NOT NULL AND external_after_state IS NOT NULL));
    END IF;
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conrelid = 'incidentcompass.action_approvals'::regclass
          AND conname = 'ck_action_approvals_external_projection_transition') THEN
        ALTER TABLE incidentcompass.action_approvals
            ADD CONSTRAINT ck_action_approvals_external_projection_transition CHECK (
                external_resource_kind IS NULL OR
                (external_resource_kind = 'telegram_message' AND
                 external_before_state = 'not_sent' AND external_after_state = 'sent') OR
                (external_resource_kind = 'github_issue' AND
                 ((external_before_state = 'absent' AND external_after_state = 'open') OR
                  (external_before_state = 'open' AND external_after_state = 'comment_added'))));
    END IF;
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conrelid = 'incidentcompass.action_approvals'::regclass
          AND conname = 'ck_action_approvals_external_projection_category') THEN
        ALTER TABLE incidentcompass.action_approvals
            ADD CONSTRAINT ck_action_approvals_external_projection_category CHECK (
                external_resource_kind IS NULL OR
                (category = 'notification' AND external_resource_kind = 'telegram_message' AND
                 external_before_state = 'not_sent' AND external_after_state = 'sent') OR
                (category = 'ticket_create' AND external_resource_kind = 'github_issue' AND
                 external_before_state = 'absent' AND external_after_state = 'open') OR
                (category = 'ticket_update' AND external_resource_kind = 'github_issue' AND
                 external_before_state = 'open' AND external_after_state = 'comment_added'));
    END IF;
END;
$migration$;

CREATE INDEX IF NOT EXISTS ix_action_approvals_external_resource
    ON incidentcompass.action_approvals (
        tenant_id, external_resource_kind, external_resource_id, completed_at_utc DESC, id DESC)
    WHERE external_resource_kind IS NOT NULL AND external_resource_id IS NOT NULL;

CREATE OR REPLACE FUNCTION incidentcompass.reject_action_approval_identity_mutation()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    IF NEW.state IS DISTINCT FROM OLD.state AND NOT (
       (OLD.state = 'requested' AND NEW.state IN ('approved', 'rejected', 'expired', 'failed')) OR
       (OLD.state = 'approved' AND NEW.state IN ('executed', 'failed'))) THEN
        RAISE EXCEPTION 'invalid action approval lifecycle transition';
    END IF;

    IF OLD.state = 'requested' AND NEW.state = 'failed' AND
       NEW.failure_code IS DISTINCT FROM 'origin_report_superseded' THEN
        RAISE EXCEPTION 'requested action may fail only when its origin report is superseded';
    END IF;

    IF NEW.id IS DISTINCT FROM OLD.id OR
       NEW.tenant_id IS DISTINCT FROM OLD.tenant_id OR
       NEW.origin_report_id IS DISTINCT FROM OLD.origin_report_id OR
       NEW.fault_id IS DISTINCT FROM OLD.fault_id OR
       NEW.job_id IS DISTINCT FROM OLD.job_id OR
       NEW.attempt IS DISTINCT FROM OLD.attempt OR
       NEW.tool_id IS DISTINCT FROM OLD.tool_id OR
       NEW.proposal_key IS DISTINCT FROM OLD.proposal_key OR
       NEW.category IS DISTINCT FROM OLD.category OR
       NEW.mode IS DISTINCT FROM OLD.mode OR
       NEW.logical_target_id IS DISTINCT FROM OLD.logical_target_id OR
       NEW.adapter_binding_fingerprint IS DISTINCT FROM OLD.adapter_binding_fingerprint OR
       NEW.approval_contract_version IS DISTINCT FROM OLD.approval_contract_version OR
       NEW.provenance_sha256 IS DISTINCT FROM OLD.provenance_sha256 OR
       NEW.canonical_payload IS DISTINCT FROM OLD.canonical_payload OR
       NEW.payload_sha256 IS DISTINCT FROM OLD.payload_sha256 OR
       NEW.approval_sha256 IS DISTINCT FROM OLD.approval_sha256 OR
       NEW.proposal_artifact_id IS DISTINCT FROM OLD.proposal_artifact_id OR
       NEW.review_summary IS DISTINCT FROM OLD.review_summary OR
       NEW.created_at_utc IS DISTINCT FROM OLD.created_at_utc OR
       NEW.expires_at_utc IS DISTINCT FROM OLD.expires_at_utc THEN
        RAISE EXCEPTION 'action approval identity and review tuple are immutable';
    END IF;

    IF NEW.external_resource_kind IS DISTINCT FROM OLD.external_resource_kind OR
       NEW.external_resource_id IS DISTINCT FROM OLD.external_resource_id OR
       NEW.external_before_state IS DISTINCT FROM OLD.external_before_state OR
       NEW.external_after_state IS DISTINCT FROM OLD.external_after_state THEN
        IF OLD.state <> 'approved' OR NEW.state <> 'executed' OR
           OLD.external_resource_kind IS NOT NULL OR OLD.external_resource_id IS NOT NULL OR
           OLD.external_before_state IS NOT NULL OR OLD.external_after_state IS NOT NULL OR
           NEW.external_resource_kind IS NULL OR NEW.external_resource_id IS NULL OR
           NEW.external_before_state IS NULL OR NEW.external_after_state IS NULL THEN
            RAISE EXCEPTION 'external action audit projection may be set only during successful terminal transition';
        END IF;
    END IF;

    IF NEW.state IS NOT DISTINCT FROM OLD.state AND NEW IS DISTINCT FROM OLD THEN
        IF OLD.state <> 'approved' OR
           OLD.dispatch_started_at IS NOT NULL OR NEW.dispatch_started_at IS NULL OR
           NEW.decision_actor IS DISTINCT FROM OLD.decision_actor OR
           NEW.decision_at_utc IS DISTINCT FROM OLD.decision_at_utc OR
           NEW.rejection_reason IS DISTINCT FROM OLD.rejection_reason OR
           NEW.result_payload IS DISTINCT FROM OLD.result_payload OR
           NEW.result_summary IS DISTINCT FROM OLD.result_summary OR
           NEW.failure_code IS DISTINCT FROM OLD.failure_code OR
           NEW.completed_at_utc IS DISTINCT FROM OLD.completed_at_utc THEN
            RAISE EXCEPTION 'action approval lifecycle fields may change only through a valid transition or first claim';
        END IF;
    END IF;

    IF OLD.state = 'requested' AND NEW.state = 'approved' AND (
       NEW.result_payload IS NOT NULL OR NEW.result_summary IS NOT NULL OR
       NEW.failure_code IS NOT NULL OR NEW.completed_at_utc IS NOT NULL OR
       NEW.dispatch_started_at IS NOT NULL) THEN
        RAISE EXCEPTION 'approval transition contains invalid lifecycle fields';
    END IF;

    IF OLD.state = 'requested' AND NEW.state IN ('rejected', 'expired') AND (
       NEW.result_payload IS NOT NULL OR NEW.result_summary IS NOT NULL OR
       NEW.failure_code IS NOT NULL OR NEW.dispatch_started_at IS NOT NULL) THEN
        RAISE EXCEPTION 'closed decision contains invalid lifecycle fields';
    END IF;

    IF OLD.state = 'requested' AND NEW.state = 'failed' AND (
       NEW.decision_actor IS NOT NULL OR NEW.dispatch_started_at IS NOT NULL OR
       NEW.result_payload IS NULL OR NEW.result_summary IS NULL OR NEW.completed_at_utc IS NULL) THEN
        RAISE EXCEPTION 'superseded requested action has invalid terminal fields';
    END IF;

    IF OLD.state = 'approved' AND NEW.state IN ('executed', 'failed') AND (
       NEW.decision_actor IS DISTINCT FROM OLD.decision_actor OR
       NEW.decision_at_utc IS DISTINCT FROM OLD.decision_at_utc OR
       NEW.rejection_reason IS DISTINCT FROM OLD.rejection_reason OR
       NEW.dispatch_started_at IS DISTINCT FROM OLD.dispatch_started_at OR
       NEW.dispatch_owner IS DISTINCT FROM OLD.dispatch_owner OR
       NEW.dispatch_fence IS DISTINCT FROM OLD.dispatch_fence OR
       NEW.dispatch_deadline_at IS DISTINCT FROM OLD.dispatch_deadline_at OR
       NEW.result_payload IS NULL OR NEW.result_summary IS NULL OR NEW.completed_at_utc IS NULL) THEN
        RAISE EXCEPTION 'action terminal transition contains invalid lifecycle fields';
    END IF;

    IF OLD.state IN ('rejected', 'expired', 'executed', 'failed') THEN
        RAISE EXCEPTION 'terminal action approvals are immutable';
    END IF;

    IF OLD.dispatch_started_at IS NOT NULL AND (
       NEW.dispatch_started_at IS DISTINCT FROM OLD.dispatch_started_at OR
       NEW.dispatch_owner IS DISTINCT FROM OLD.dispatch_owner OR
       NEW.dispatch_fence IS DISTINCT FROM OLD.dispatch_fence OR
       NEW.dispatch_deadline_at IS DISTINCT FROM OLD.dispatch_deadline_at) THEN
        RAISE EXCEPTION 'action dispatch claim is immutable';
    END IF;

    RETURN NEW;
END;
$$;

SELECT pg_advisory_unlock(hashtext('incidentcompass:025-external-action-audit-projection')::bigint);
