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

## API-key boundary

The API host supports a minimal shared-key boundary through host-only
`IncidentCompass:ApiKeyAuth` settings. When `Enabled` is true, a fallback authorization policy
protects all current and future endpoints unless they are explicitly anonymous. The complete
anonymous allowlist is `/health`, `/api/v1/health`, `/api/v1/health/memory-sync` and the
Development-only OpenAPI document. Manual intake, incident-data reads, `users/me` and native OTLP
trace/log ingestion all use the same boundary.

Clients send exactly one `X-IncidentCompass-Key` value. It must be 32-128 ASCII base64url
characters with no padding, commas or whitespace. The host stores only its SHA-256 hex digest and
compares the digest in fixed time. A credential also has a non-secret stable key id and exactly one
tenant id. Successful authentication supplies both `IUserContext` and `IIncidentTenantContext`
from that server-owned mapping, so request bodies, OTLP attributes and demo headers cannot choose
the tenant. Missing, malformed and invalid credentials return `401` before endpoint binding or
Application dispatch.

`Enabled`, `PermitLimit` and `WindowSeconds` are startup-static. Protected requests share a
queue-free fixed-window limiter partitioned only by authenticated key id; anonymous health and
OpenAPI requests are not limited. A valid configuration reload atomically rotates the immutable
credential map. An invalid reload, or an attempted live change to a startup-static field, installs
a deny-all map until a fully valid configuration with the original static fields arrives or the
process restarts. This avoids retaining a potentially revoked credential during a broken reload.

Rejects increment `incidentcompass.api.authentication.rejections` with only the bounded outcome
`missing`, `malformed` or `invalid`. Raw keys, configured digests and request bodies are excluded
from auth logs, metrics, errors, the triage ledger and configuration snapshots. Host transport may
return `431` before application code for a header block above its own size limit.

Credentials are injected through host configuration or environment variables. They are not public
triage configuration and no raw key belongs in tracked files. For example, credential fields use
`IncidentCompass__ApiKeyAuth__Credentials__0__KeyId`, `__TenantId` and `__Sha256Digest` suffixes.
This is minimal authentication, not RBAC, key distribution, a secret store, OAuth or a production
identity platform.

## Incident Data Tenant Scope

`IIncidentTenantContext` is separate from `IUserContext`. With API-key authentication enabled it
reads the tenant mapped to the authenticated key. In explicitly auth-disabled local/demo mode it
reads `Ingestion.DefaultTenant` from the server-loaded triage configuration. `X-Demo-Tenant-Id`,
incident-envelope fields, OTLP resource attributes and other sender-controlled data never select
the incident-data tenant.

Fault, ledger and report read paths resolve this server-owned scope before querying. An object
outside the scope is indistinguishable from a missing object and returns `404`; compact report
lists only return scoped rows. API-key authentication changes only the API composition adapter,
not the intake or read use cases. Worker/system jobs continue to use their background identity and
the server-loaded job/configuration tenant context.

## Logging

- Full rendered prompt logging is disabled by default.
- Metadata logging is allowed: request ID, user ID, model, tokens, cost, status.
- If full prompt logging is ever enabled, it must require opt-in, redaction, encryption, retention policy and restricted access.
- Tool execution is controlled by backend policy. The model may propose tool calls, but it cannot execute tools directly and never receives infrastructure credentials.

## Local source read boundary

The source worker never receives a filesystem root, release selector or arbitrary read argument.
Host options allowlist exact service/release roots and optional build-path prefixes; the job's
snapshotted `CurrentReleases` entry is the only release selector. Candidate paths are canonicalized
and revalidated below the selected root before opening, reparse/symlink traversal is rejected, and
only configured text extensions within byte, frame, candidate and excerpt limits are read. Source
bodies and absolute host paths are not logged or persisted. Durable artifacts contain only a
repository-relative path, bounded excerpt, line range, release and `heuristic` mapping label.

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

The GitHub Issues token is bound only from Worker host configuration, normally the
`IncidentCompass__Tickets__GitHub__Token` environment variable. It is absent from public triage
configuration, config snapshots, tool definitions, prompts, artifacts and report payloads. The
repository is also host-owned; model arguments, incident fields and tenants cannot select another
repository or API authority. The adapter does not log authorization headers, response bodies or
issue bodies. Authentication, rate-limit, timeout and malformed-response failures are reduced to a
closed sanitized code before they reach durable tool outcomes or report limitations. Caller/job
cancellation propagates instead of being misreported as a connector timeout.
