-- Durable, tenant-scoped post-report action approval and outbox state.
-- dispatch_started_at is an immutable claim marker on approved rows, not a public lifecycle state.

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
        ADD CONSTRAINT ck_triage_artifacts_kind CHECK (kind IN (
            'TriggerSignal', 'NeighborSet', 'PriorReport', 'RecurrenceState', 'RetrievedItem',
            'ToolResult', 'WorkerOutput', 'ProposedAction', 'ActionResult'));
END $$;

ALTER TABLE incidentcompass.triage_ledger
    DROP CONSTRAINT IF EXISTS triage_ledger_event_type_check,
    DROP CONSTRAINT IF EXISTS triage_ledger_decision_check,
    DROP CONSTRAINT IF EXISTS triage_ledger_tool_status_check,
    DROP CONSTRAINT IF EXISTS triage_ledger_check,
    DROP CONSTRAINT IF EXISTS triage_ledger_check1,
    DROP CONSTRAINT IF EXISTS triage_ledger_status_shape_check,
    DROP CONSTRAINT IF EXISTS triage_ledger_budget_shape_check,
    DROP CONSTRAINT IF EXISTS triage_ledger_action_ref_check,
    DROP CONSTRAINT IF EXISTS triage_ledger_action_summary_bound_check;

ALTER TABLE incidentcompass.triage_ledger
    ADD CONSTRAINT triage_ledger_event_type_check CHECK (event_type IN (
        'Delegated', 'ToolProposed', 'PolicyDecision', 'ToolResult', 'WorkerCompleted',
        'BudgetEvent', 'ReportPublished', 'ModelCall', 'ActionProposed', 'ApprovalDecision',
        'ActionDispatchStarted', 'ActionCompleted')),
    ADD CONSTRAINT triage_ledger_decision_check CHECK (
        (event_type = 'PolicyDecision' AND decision IN ('Allowed', 'Denied', 'ApprovalRequired')) OR
        (event_type = 'ApprovalDecision' AND decision IN ('AutoApproved', 'Approved', 'Rejected', 'Expired')) OR
        (event_type NOT IN ('PolicyDecision', 'ApprovalDecision') AND decision IS NULL)),
    ADD CONSTRAINT triage_ledger_tool_status_check CHECK (
        tool_status IS NULL OR tool_status IN ('Succeeded', 'Failed')),
    ADD CONSTRAINT triage_ledger_status_shape_check CHECK (
        (event_type IN ('ToolResult', 'ActionCompleted') AND tool_status IS NOT NULL) OR
        (event_type NOT IN ('ToolResult', 'ActionCompleted') AND tool_status IS NULL)),
    ADD CONSTRAINT triage_ledger_budget_shape_check CHECK (
        event_type = 'BudgetEvent' OR (tokens_delta IS NULL AND workers_delta IS NULL)),
    ADD CONSTRAINT triage_ledger_action_ref_check CHECK (
        event_type NOT IN ('ActionProposed', 'ApprovalDecision', 'ActionDispatchStarted', 'ActionCompleted') OR
        (payload_ref LIKE 'action:%' OR payload_ref LIKE 'artifact:%')),
    ADD CONSTRAINT triage_ledger_action_summary_bound_check CHECK (
        payload_ref NOT LIKE 'action:%' OR rationale IS NULL OR octet_length(rationale) <= 2048);

CREATE TABLE IF NOT EXISTS incidentcompass.action_approvals (
    id uuid PRIMARY KEY,
    tenant_id text NOT NULL CHECK (length(btrim(tenant_id)) > 0),
    origin_report_id uuid NOT NULL REFERENCES incidentcompass.triage_reports (id),
    fault_id uuid NOT NULL REFERENCES incidentcompass.faults (id),
    job_id uuid NOT NULL REFERENCES incidentcompass.triage_jobs (id),
    attempt integer NOT NULL CHECK (attempt > 0),
    tool_id text NOT NULL CHECK (length(btrim(tool_id)) BETWEEN 1 AND 128),
    proposal_key text NOT NULL CHECK (length(btrim(proposal_key)) BETWEEN 1 AND 256),
    category text NOT NULL CHECK (category IN (
        'notification', 'ticket_create', 'ticket_update', 'code_write', 'branch_push', 'pr_create')),
    mode text NOT NULL CHECK (mode IN ('live', 'dry_run', 'disabled')),
    logical_target_id text NOT NULL CHECK (length(logical_target_id) BETWEEN 1 AND 128),
    adapter_binding_fingerprint text NOT NULL CHECK (adapter_binding_fingerprint ~ '^[0-9a-f]{64}$'),
    approval_contract_version integer NOT NULL CHECK (approval_contract_version = 1),
    provenance_sha256 text NOT NULL CHECK (provenance_sha256 ~ '^[0-9a-f]{64}$'),
    state text NOT NULL CHECK (state IN ('requested', 'approved', 'rejected', 'expired', 'executed', 'failed')),
    canonical_payload bytea NOT NULL CHECK (octet_length(canonical_payload) BETWEEN 1 AND 65536),
    payload_sha256 text NOT NULL CHECK (payload_sha256 ~ '^[0-9a-f]{64}$'),
    approval_sha256 text NOT NULL CHECK (approval_sha256 ~ '^[0-9a-f]{64}$'),
    proposal_artifact_id uuid NOT NULL REFERENCES incidentcompass.triage_artifacts (id) DEFERRABLE INITIALLY DEFERRED,
    review_summary text NOT NULL CHECK (octet_length(review_summary) BETWEEN 1 AND 2048),
    created_at_utc timestamptz NOT NULL,
    expires_at_utc timestamptz NOT NULL CHECK (expires_at_utc > created_at_utc),
    decision_actor text NULL CHECK (decision_actor IS NULL OR length(decision_actor) BETWEEN 1 AND 256),
    decision_at_utc timestamptz NULL,
    rejection_reason text NULL CHECK (rejection_reason IS NULL OR length(rejection_reason) BETWEEN 1 AND 500),
    dispatch_owner text NULL CHECK (dispatch_owner IS NULL OR length(dispatch_owner) BETWEEN 1 AND 128),
    dispatch_fence uuid NULL,
    dispatch_started_at timestamptz NULL,
    dispatch_deadline_at timestamptz NULL,
    result_payload bytea NULL CHECK (result_payload IS NULL OR octet_length(result_payload) BETWEEN 1 AND 65536),
    result_summary text NULL CHECK (result_summary IS NULL OR octet_length(result_summary) BETWEEN 1 AND 2048),
    failure_code text NULL CHECK (failure_code IS NULL OR length(failure_code) BETWEEN 1 AND 128),
    completed_at_utc timestamptz NULL,
    UNIQUE (tenant_id, origin_report_id, tool_id, proposal_key),
    CHECK ((decision_actor IS NULL) = (decision_at_utc IS NULL)),
    CHECK ((dispatch_started_at IS NULL AND dispatch_owner IS NULL AND dispatch_fence IS NULL AND dispatch_deadline_at IS NULL) OR
           (dispatch_started_at IS NOT NULL AND dispatch_owner IS NOT NULL AND dispatch_fence IS NOT NULL AND dispatch_deadline_at > dispatch_started_at)),
    CHECK (state <> 'requested' OR (decision_actor IS NULL AND dispatch_started_at IS NULL AND completed_at_utc IS NULL)),
    CHECK (state <> 'approved' OR (decision_actor IS NOT NULL AND completed_at_utc IS NULL)),
    CHECK (state <> 'rejected' OR (decision_actor IS NOT NULL AND dispatch_started_at IS NULL AND completed_at_utc IS NOT NULL)),
    CHECK (state <> 'expired' OR (decision_actor IS NOT NULL AND dispatch_started_at IS NULL AND completed_at_utc IS NOT NULL)),
    CHECK (state NOT IN ('executed', 'failed') OR
           (completed_at_utc IS NOT NULL AND result_payload IS NOT NULL AND result_summary IS NOT NULL)),
    CHECK (state <> 'executed' OR (dispatch_started_at IS NOT NULL AND result_payload IS NOT NULL AND failure_code IS NULL)),
    CHECK (state <> 'failed' OR failure_code IS NOT NULL)
);

CREATE INDEX IF NOT EXISTS ix_action_approvals_tenant_created
    ON incidentcompass.action_approvals (tenant_id, created_at_utc DESC, id DESC);

CREATE INDEX IF NOT EXISTS ix_action_approvals_dispatch_candidates
    ON incidentcompass.action_approvals (created_at_utc, id)
    WHERE state IN ('requested', 'approved');

CREATE TABLE IF NOT EXISTS incidentcompass.action_approval_provenance (
    action_id uuid NOT NULL REFERENCES incidentcompass.action_approvals (id),
    ordinal integer NOT NULL CHECK (ordinal BETWEEN 0 AND 32),
    source_type text NOT NULL CHECK (source_type IN ('report', 'artifact')),
    source_id uuid NOT NULL,
    artifact_kind text NULL CHECK (artifact_kind IS NULL OR artifact_kind IN (
        'TriggerSignal', 'NeighborSet', 'PriorReport', 'RecurrenceState', 'RetrievedItem', 'ToolResult')),
    trust_class text NOT NULL CHECK (trust_class IN (
        'untrusted_signal', 'untrusted_prior', 'untrusted_retrieved', 'backend_fact')),
    PRIMARY KEY (action_id, ordinal),
    UNIQUE (action_id, source_type, source_id),
    CHECK ((source_type = 'report' AND ordinal = 0 AND artifact_kind IS NULL AND trust_class = 'untrusted_prior') OR
           (source_type = 'artifact' AND ordinal > 0 AND artifact_kind IS NOT NULL))
);

CREATE OR REPLACE FUNCTION incidentcompass.reject_action_provenance_insert_after_seal()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    IF EXISTS (
        SELECT 1
        FROM incidentcompass.triage_ledger
        WHERE event_type = 'ActionProposed'
          AND payload_ref = 'action:' || NEW.action_id::text) THEN
        RAISE EXCEPTION 'action approval provenance is sealed';
    END IF;
    RETURN NEW;
END;
$$;

DROP TRIGGER IF EXISTS trg_action_approval_provenance_sealed ON incidentcompass.action_approval_provenance;

CREATE TRIGGER trg_action_approval_provenance_sealed
    BEFORE INSERT ON incidentcompass.action_approval_provenance
    FOR EACH ROW
    EXECUTE FUNCTION incidentcompass.reject_action_provenance_insert_after_seal();

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

DROP TRIGGER IF EXISTS trg_action_approvals_immutable ON incidentcompass.action_approvals;

CREATE TRIGGER trg_action_approvals_immutable
    BEFORE UPDATE ON incidentcompass.action_approvals
    FOR EACH ROW
    EXECUTE FUNCTION incidentcompass.reject_action_approval_identity_mutation();

CREATE OR REPLACE FUNCTION incidentcompass.reject_action_approval_delete()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    RAISE EXCEPTION 'action approvals are append-only lifecycle records';
END;
$$;

DROP TRIGGER IF EXISTS trg_action_approvals_no_delete ON incidentcompass.action_approvals;

CREATE TRIGGER trg_action_approvals_no_delete
    BEFORE DELETE ON incidentcompass.action_approvals
    FOR EACH ROW
    EXECUTE FUNCTION incidentcompass.reject_action_approval_delete();

CREATE OR REPLACE FUNCTION incidentcompass.reject_action_provenance_mutation()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    RAISE EXCEPTION 'action approval provenance is immutable';
END;
$$;

DROP TRIGGER IF EXISTS trg_action_approval_provenance_immutable ON incidentcompass.action_approval_provenance;

CREATE TRIGGER trg_action_approval_provenance_immutable
    BEFORE UPDATE OR DELETE ON incidentcompass.action_approval_provenance
    FOR EACH ROW
    EXECUTE FUNCTION incidentcompass.reject_action_provenance_mutation();

CREATE OR REPLACE FUNCTION incidentcompass.reject_action_review_artifact_mutation()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    IF OLD.kind = 'ProposedAction' OR (TG_OP = 'UPDATE' AND NEW.kind = 'ProposedAction') THEN
        RAISE EXCEPTION 'proposed action review artifacts are immutable';
    END IF;
    IF TG_OP = 'DELETE' THEN
        RETURN OLD;
    END IF;
    RETURN NEW;
END;
$$;

DROP TRIGGER IF EXISTS trg_action_review_artifact_immutable ON incidentcompass.triage_artifacts;

CREATE TRIGGER trg_action_review_artifact_immutable
    BEFORE UPDATE OR DELETE ON incidentcompass.triage_artifacts
    FOR EACH ROW
    EXECUTE FUNCTION incidentcompass.reject_action_review_artifact_mutation();
