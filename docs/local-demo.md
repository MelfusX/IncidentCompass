# Local Demo Walkthrough

This walkthrough is the recommended path for reviewing the project locally.

## What To Run First

- Start PostgreSQL with Docker Compose.
- Run the API host.
- Call the health endpoint.
- Call `/api/v1/users/me` with demo identity headers.

See `docs/quickstart.md` for the full step-by-step commands.

## Why This Path Uses Mocks

The default local demo runs with deterministic mock model and embedding providers. That keeps the flow repeatable without real LLM credentials, network access or provider cost.

OpenAI-compatible model and embedding adapters are included behind Application ports. Enable them through local configuration when you want to test real provider behavior.

## Summary

The .NET-native IncidentCompass backend demonstrates the model/embedding gateway abstraction, sanitized AI request logging and cost tracking, and a governed tool-execution/policy/audit subsystem (Governance) that a future phase will wire into an incident-triage agent loop.
