# Changelog

## Unreleased

Initial bootstrap of IncidentCompass, a governed incident-triage agent backend.
**Not a release** — `v0.1.0` is reserved for the first public reference release after the MVP.

This snapshot includes:

- Phase 0 repository bootstrap from the upstream starter-kit reference: layered .NET structure, model/embedding gateway, governed tool-execution/policy/audit primitive, sanitized AI-request logging, generic dispatch/health/security/user scaffolding, and PostgreSQL/observability infrastructure.
- Phase 1 intake: `POST /api/v1/incidents`, `GET /api/v1/faults/{id}`, source allow-list validation, tester/OTel/user/manual normalizers, redaction, deterministic fingerprinting, strong/weak grouping semantics, silence-window suppression, recurrence linking, triage config snapshots, pending triage jobs, and job-level intake artifacts.
- PostgreSQL intake schema in `infra/postgres/init/007-intake.sql`: `signals`, `faults`, `triage_jobs`, `triage_config_snapshots`, and `triage_artifacts`.

Notable defaults and invariants:

- Deterministic mock model and embedding providers remain the default; Phase 1 intake does not call real providers.
- Strong fingerprints require both a real non-`unknown` service name and structured `errorType`; user/manual reports with only a service name remain weak.
- Full rendered prompt/body logging remains disabled by default, and redaction preserves null optional signal text fields.
- The Governance subsystem remains uncalled library code until a later phase wires it into the agent loop.

Chat, RAG document ingestion, evaluations, usage dashboards and MCP host/client product surfaces remain out of scope for IncidentCompass.

Not a production system.
