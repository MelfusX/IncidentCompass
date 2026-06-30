# Changelog

## Unreleased

Initial bootstrap of IncidentCompass, a governed incident-triage agent backend.
**Not a release** — `v0.1.0` is reserved for the first public reference release after the MVP.

This is Phase 0: repository bootstrap. IncidentCompass was extracted and
specialized from an upstream starter-kit reference repo (see `README.md`), keeping the
model/embedding gateway, the governed tool-execution/policy/audit primitive (currently
uncalled library code), sanitized AI-request logging, generic dispatch/health/security/user
scaffolding, and Postgres/observability infrastructure. Chat, RAG document ingestion,
evaluations, usage tracking and MCP (host and client) were removed; they are not part of
this product.

Layered monolith (Clean Architecture): `Domain`, a single `Application` project (`Core` / `Governance`
populated; `Intake` / `Investigation` / `Memory` reserved for later phases), `Infrastructure`,
and host projects (`Api` / `Worker`). Deterministic mock providers by default; OpenAI-compatible
adapters behind ports.

Not a production system.
