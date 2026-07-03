# Changelog

## Unreleased

Initial bootstrap of IncidentCompass, a governed incident-triage agent backend.
**Not a release** - `v0.1.0` is reserved for the first public reference release after the MVP.

This snapshot includes:

- Phase 0 repository bootstrap from the upstream starter-kit reference: layered .NET structure, model/embedding gateway, governed tool-execution/policy/audit primitive, sanitized AI-request logging, generic dispatch/health/security/user scaffolding, and PostgreSQL/observability infrastructure.
- Phase 1 intake: `POST /api/v1/incidents`, `GET /api/v1/faults/{id}`, source allow-list validation, tester/OTel/user/manual normalizers, redaction, deterministic fingerprinting, strong/weak grouping semantics, silence-window suppression, recurrence linking, triage config snapshots, pending triage jobs, and job-level intake artifacts.
- Phase 2 governed investigation loop: Worker claim loop with `MaxConcurrentJobs`, config-snapshot rehydration, a backend-owned orchestrator surface limited to `delegate` and minimal `publish_report`, sequential worker execution, worker output artifacts, durable own-commit triage ledger events, and minimal report persistence that closes the job/fault.
- Phase 3 governance rails: bounded orchestrator and worker reprompts, worker-tool rule evaluation over the durable ledger, role-scoped backend tool grants, audit-visible `ToolProposed`/`PolicyDecision`/`ToolResult` events, per-attempt budget accounting with `ModelCall` and first-class `BudgetEvent` delta columns, context-window guardrails, and full load validation for routes, roles, tools, rules, schemas and budget knobs.
- Phase 4 memory worker: PostgreSQL `memory_items`/`memory_chunks`, deterministic sample seeding, pinned mock embedding model, governed `memory_search` execution for the memory role only, exact tenant/provider/model/dimension retrieval filters, attempt-level `RetrievedItem` artifacts, honest no-match output and live `rate_cap` coverage against the real tool.
- Phase 5 grounded reports: backend-validated `publish_report` payloads, exact citable artifact references, quote substring validation, backend-derived `is_mass_issue` and evidence kind, same-transaction report/evidence/job/fault/`ReportPublished` commit, prior-report readback and `GET /api/v1/triage-reports/{id}`.
- PostgreSQL schemas in `infra/postgres/init/007-intake.sql`, `infra/postgres/init/008-triage-ledger.sql` `infra/postgres/init/009-triage-reports-minimal.sql` and `infra/postgres/init/010-memory.sql`: intake rows, artifacts, DB-ordered ledger events, first-class `ToolResult` status, first-class `BudgetEvent` deltas, grounded triage reports/evidence, and memory rows/chunks with write-time embedding dimension checks.

Notable defaults and invariants:

- Deterministic mock model and embedding providers remain the default for automated tests and local bootstrap.
- Strong fingerprints require both a real non-`unknown` service name and structured `errorType`; user/manual reports with only a service name remain weak.
- Full rendered prompt/body logging remains disabled by default, and redaction preserves null optional signal text fields.
- The Worker now wires the governed investigation loop and the shipped config has one live worker tool: `memory_search`, granted only to the `memory` role.
- Non-gated local smoke improved from the Phase 2 baseline (`qwen2.5-14b-instruct`: 0/N useful completions) to Phase 3 `3/3` `publish_report` completions with `MaxReprompts: 2`; Phase 4 reached memory-backed `publish_report` 5/5 in the recorded local smoke. Phase 5 smoke now requires grounded evidence persistence; the current result file records whether a local OpenAI-compatible endpoint was available. These are opt-in smoke evidence only, not CI release gates.

Chat, RAG document ingestion, evaluations, usage dashboards and MCP host/client product surfaces remain out of scope for IncidentCompass.

Not a production system.