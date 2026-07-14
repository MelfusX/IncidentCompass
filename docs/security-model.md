# Security Model

The core principle is simple: the LLM is not a security boundary.

The backend decides what data and tools are available. The model may summarize, reason and propose actions, but it must not enforce authorization or receive privileged credentials.

## Demo Auth

The starter kit uses:

- `IUserContext` in the application layer;
- demo/fake authentication for local development;
- headers, seeded users or configuration as demo identity sources.

Real auth providers such as Entra ID or ASP.NET Identity are future adapters, not requirements for the local sample path.

The API registers the demo header-based `IUserContext` only for `Development` by default. Development requests may omit headers and use the configured local `demo-user` defaults for the quickstart. Production API startup fails unless the API composition root registers a real foreground `IUserContext` adapter before the app starts. The Infrastructure project registers `IBackgroundUserContext` for Worker/system jobs, not a foreground API `IUserContext`, so the background identity cannot satisfy the API auth requirement by DI ordering. Non-production demo environments can explicitly opt in to demo headers; in that opt-in mode, the configured default user, tenant, roles and groups are ignored, the request must include an explicit `X-Demo-User-Id` to be treated as authenticated, and anonymous requests receive no default claims. Worker hosts explicitly map the background context for job processing and do not use HTTP demo headers.

Demo headers such as `X-Demo-User-Id`, `X-Demo-Tenant-Id` and `X-Demo-Roles` are caller-controlled sample inputs. They are useful for local walkthroughs, but they are not authentication and must not be trusted in deployed environments.

## Incident Data Tenant Scope

`IIncidentTenantContext` is separate from `IUserContext`. In v0.2.0 it reads `Ingestion.DefaultTenant` from the server-loaded triage configuration, which is the only tenant source for both manual API intake and OTLP intake. `X-Demo-Tenant-Id`, incident-envelope fields, OTLP resource attributes and other sender-controlled data never select the incident-data tenant.

Fault, ledger and report read paths resolve this server-owned scope before querying. An object outside the scope is indistinguishable from a missing object and returns `404`; compact report lists only return scoped rows. This is a local/single-tenant partition, not authentication or authorization. IC-BL-024 is expected to replace this context implementation with server-side API-key-to-tenant mapping without changing intake or read use cases.

## Logging

- Full rendered prompt logging is disabled by default.
- Metadata logging is allowed: request ID, user ID, model, tokens, cost, status.
- If full prompt logging is ever enabled, it must require opt-in, redaction, encryption, retention policy and restricted access.
- Tool execution is controlled by backend policy. The model may propose tool calls, but it cannot execute tools directly and never receives infrastructure credentials.

## Intake Redaction And Pseudonymization

Built-in secret patterns remain active for every signal. The triage config can add attribute-key
redaction and bounded .NET regular-expression replacements before persistence and before model calls.
These rules are defense in depth, not a guarantee that every possible secret or PII shape is known.

Configured user-identifier attributes are replaced before redaction with an HMAC-SHA256 pseudonym.
The salt comes only from host secrets or `IncidentCompass__Pseudonymization__Salt`; it is not stored in
the triage config, config snapshot, artifact or ledger. If the salt is absent, identifiers fail safe to
`[REDACTED]`, so distinct-user continuity is unavailable but raw identifiers are not stored. Rotating
the salt changes every pseudonym and breaks counts across the rotation boundary.

## Tools

Tool execution must go through backend policy. Risky tools require approval or must be rejected. The LLM must not receive infrastructure credentials. The investigation loop gives the orchestrator only backend-owned `delegate` and `publish_report` actions; `delegate.role` is generated from configuration and validated again before execution. Worker-tool proposals are recorded as `ToolProposed`, checked against role grants and ledger-backed rules, recorded as `PolicyDecision`, and only allowed backend calls execute. Unknown, unregistered, ungranted and invalid worker tool calls fail closed with audit-visible decisions. `ApprovalRequired` denies the call and records a limitation; there is still no suspend/resume lifecycle in MVP. Report publication is also fail-closed: the model may name evidence references, but the backend accepts only citable artifacts from the same job/current attempt, never `WorkerOutput`, derives evidence kind and `is_mass_issue` itself, and marks prior reports as untrusted hypotheses in the artifact payload.
