# Changelog

## Unreleased

Initial bootstrap of IncidentCompass, a governed incident-triage agent backend.
**Not a release** - `v0.1.0` is reserved for the first public reference release after the MVP.

This snapshot includes:

- Phase 0 repository bootstrap from the upstream starter-kit reference: layered .NET structure, model/embedding gateway, governed tool-execution/policy/audit primitive, sanitized AI-request logging, generic dispatch/health/security/user scaffolding, and PostgreSQL/observability infrastructure.
- Phase 1 intake: `POST /api/v1/incidents`, `GET /api/v1/faults/{id}`, source allow-list validation, tester/OTel/user/manual normalizers, redaction, deterministic fingerprinting, strong/weak grouping semantics, silence-window suppression, recurrence linking, triage config snapshots, pending triage jobs, and job-level intake artifacts.
- Phase 2 governed investigation loop: Worker claim loop with `MaxConcurrentJobs`, config-snapshot rehydration, a backend-owned orchestrator surface limited to `delegate` and minimal `publish_report`, sequential worker execution, worker output artifacts, durable own-commit triage ledger events, and minimal report persistence that closes the job/fault.
- Phase 3 governance rails: bounded orchestrator and worker reprompts, worker-tool rule evaluation over the durable ledger, role-scoped backend tool grants, audit-visible `ToolProposed`/`PolicyDecision`/`ToolResult` events, per-attempt budget accounting with `ModelCall` and first-class `BudgetEvent` delta columns, context-window guardrails, and full load validation for routes, roles, tools, rules, schemas and budget knobs.
- PostgreSQL schemas in `infra/postgres/init/007-intake.sql`, `infra/postgres/init/008-triage-ledger.sql` and `infra/postgres/init/009-triage-reports-minimal.sql`: intake rows, artifacts, DB-ordered ledger events, first-class `ToolResult` status, first-class `BudgetEvent` deltas, and minimal triage reports.

Notable defaults and invariants:

- Deterministic mock model and embedding providers remain the default for automated tests and local bootstrap.
- Strong fingerprints require both a real non-`unknown` service name and structured `errorType`; user/manual reports with only a service name remain weak.
- Full rendered prompt/body logging remains disabled by default, and redaction preserves null optional signal text fields.
- The Worker now wires the governed investigation loop, but the shipped config still has no live worker tool implementation until a later memory/search phase.
- Non-gated local smoke improved from the Phase 2 baseline (`qwen2.5-14b-instruct`: 0/N useful completions) to Phase 3 `3/3` `publish_report` completions with `MaxReprompts: 2`. This is recorded as opt-in smoke evidence only, not a CI release gate.

Chat, RAG document ingestion, evaluations, usage dashboards and MCP host/client product surfaces remain out of scope for IncidentCompass.

Not a production system.