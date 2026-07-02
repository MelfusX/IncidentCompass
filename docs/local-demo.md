# Local Demo Walkthrough

This walkthrough is the recommended path for reviewing the project locally.

## What To Run First

- Start PostgreSQL with Docker Compose.
- Run the API host.
- Call the health endpoint.
- Call `/api/v1/users/me` with demo identity headers.
- Ingest a structured tester signal with `POST /api/v1/incidents`.
- Read the created fault with `GET /api/v1/faults/{id}`.

See `docs/quickstart.md` for the full step-by-step commands and `samples/http/local-demo.http` for copy-ready requests.

## Why This Path Uses Mocks

The default local demo runs with deterministic mock model and embedding providers. That keeps the flow repeatable without real LLM credentials, network access or provider cost.

OpenAI-compatible model and embedding adapters are included behind Application ports. Enable them through local configuration when you want to test real provider behavior. Phase 1 intake does not call a model provider; it prepares fault/job/artifact state for later worker phases.

## Intake Behavior To Observe

- Tester and OTel-shaped envelopes use structured `attributes` such as `errorType`, `errorMessage`, `operationName` and HTTP fields.
- User/manual reports require `summary` or `description` and produce weak fingerprints when they lack structured error data.
- Strong fingerprints require both a real service name and structured `errorType`; a user report with only `serviceName` still opens its own fault.
- Duplicate strong signals attach to one open fault. A recently closed strong fault suppresses matching signals during the silence window.
- Each new fault creates a pending triage job and job-level intake artifacts (`TriggerSignal`, `NeighborSet`, optional `PriorReport`).

## Summary

The .NET-native IncidentCompass backend now demonstrates the Phase 1 intake pipeline, model/embedding gateway abstraction, sanitized AI request logging and cost tracking, and a governed tool-execution/policy/audit subsystem (`Governance`) that a future phase will wire into an incident-triage agent loop.