# Trade-offs

This document records intentional choices and their costs.

## Mock Model vs Real Model In Tests

Real model calls are expensive and nondeterministic. Automated tests use mock clients by default.

## Full Prompt Logging vs Privacy

Full prompt logs help debugging but may leak sensitive data. Default logging is metadata-only.

## Configurable Redaction Is Best Effort

Built-in and configured redaction rules reduce exposure before persistence and model calls, but a
pattern list cannot prove that all secret and PII formats are covered. New realistic data sources must
add regression fixtures for their known sensitive fields, and operators should keep full prompt/body
logging disabled.

## Pseudonymization Salt Rotation

User identifiers can be replaced with stable HMAC-SHA256 pseudonyms so later blast-radius logic can
count distinct users without storing raw identifiers. The host-only salt is intentionally outside the
snapshotted triage config. Rotating it breaks continuity with older pseudonyms; running without it
fails safe to redaction and therefore loses distinct-user counting.

## Simple Access Control vs Enterprise RBAC

The current implementation relies on a minimal demo `IUserContext` rather than enterprise RBAC. Real auth providers and finer-grained authorization are deferred; the architecture should not block later RBAC or Entra ID integration.

## Domain Records with Application-Owned Behavior

Domain types are intentionally simple records and enums in the starter-kit scope, but domain concepts live in the Domain layer so they can be reused across Application workflows without creating Application-to-Application coupling. Workflow behavior, validation policy and partial-failure handling stay in Application services so the public sample remains easy to inspect without DDD ceremony.

## Starter Kit vs Framework

A starter kit is easier to build and understand. A framework requires stable APIs, compatibility guarantees and long-term support.

## Internal Dispatcher vs MediatR

A lightweight internal dispatcher keeps the starter kit dependency-light. MediatR v12 can be familiar for many .NET developers, but newer MediatR versions may introduce licensing considerations. This project uses an internal dispatcher/pipeline and can document MediatR as an optional alternative later.

The replacement boundary is the dispatcher engine, not the hosts or use-case contracts. A MediatR swap should replace `ApplicationDispatcher`, `RequestValidationBehavior`, `DispatchLoggingBehavior` and the dispatcher delegate shape with MediatR request handling and pipeline behaviors. The stable contracts are `IApplicationDispatcher`, the request marker interfaces and `IRequestHandler<TRequest, TResponse>` handlers. Hosts should continue depending on `IApplicationDispatcher` so API and Worker composition do not learn which dispatcher engine is active.

That swap would still require deliberate adapter work because the current dispatcher signatures are not MediatR signatures. Keeping the seam at `IApplicationDispatcher` avoids spreading a framework dependency across hosts while preserving a clear migration path if a team prefers MediatR in its own application.

## FluentValidation vs Custom Validators

FluentValidation is used for request-shape validation because it is familiar to many .NET teams, has no MediatR dependency and keeps rule composition separate from handler orchestration. Handlers that need normalized value objects use a neighboring `Normalizer.cs` instead of asking validators to both reject invalid input and build workflow state.

The MediatR decision remains separate. This project still uses its internal dispatcher and pipeline behaviors; FluentValidation provides the rule engine only.

## .NET 10 LTS vs Older Targets

.NET 10 LTS is the preferred baseline for a new project started in 2026. Older .NET versions may be familiar to more teams, but they have shorter remaining support windows.

## `v0.1.0` vs `v1.0.0`

`v0.1.0` communicates that the project is useful but evolving. `v1.0.0` should wait until contracts, docs and extension points are stable.

## Raw String Identifiers vs Strongly-Typed Value Objects

Identifiers like `TenantId`, `UserId`, `CorrelationId` are passed as `string` and `Guid` throughout the codebase rather than as strongly-typed value objects (e.g. `readonly record struct TenantId`). Value objects offer compile-time safety against argument-mix-ups and centralized validation, but introduce friction with `System.Text.Json`, `Npgsql` parameter binding, and `IOptions<T>` binding at this project's current scope. The current implementation accepts the small risk of string mix-ups in exchange for transport simplicity. A future scope that grows multi-context handler signatures (tenant + user + correlation + ...) may revisit this.

## Sequential Ledger-Backed Governance

Phase 3 evaluates `rate_cap`, `precondition` and budget state by reading the append-only ledger. This is simple and inspectable for the MVP because worker delegation is sequential. It is not a parallel-safe counter mechanism; future parallel fan-out would need serialized policy evaluation or atomic counters to avoid two workers passing a cap at the same time.

## Token Budget Overshoot

`MaxTokens` means the backend will not start a new model call once the current-attempt budget is already reached. A single in-flight call can still overshoot the limit because final usage is known only after the provider responds. The overshoot is recorded as a `BudgetEvent` instead of hidden.

## File-Backed Memory Is The Write Path

Memory content stays in reviewed files instead of an unauthenticated admin endpoint. Source path is
the stable database identity; a content change updates and re-embeds that item, and a removed file is
deactivated from retrieval. This keeps provenance simple and prevents an edited file from leaving a
second stale live item. It also means operators need a repository change and restart to update memory.

## Memory Embedding Model Changes Require Re-Embedding

Phase 4 memory retrieval filters by tenant, embedding provider, embedding model and embedding dimensions. This avoids mixing incompatible corpora, but it also means changing the embedding provider or model makes existing memory chunks silently unretrievable until they are re-embedded. Changing the configured embedding provider or model should be paired with a full memory re-seed or migration.
## Grounded Evidence vs Correct Conclusions

Phase 5 report grounding proves that each persisted evidence row came from a citable artifact visible to the job and that any stored quote was an exact substring of the redacted artifact payload. It does not prove the model's classification is correct. This is an intentional MVP boundary: durable evidence makes review possible, while evaluation of reasoning quality remains outside the backend transaction.
## Renewable Worker Leases Require Cooperative Calls

The Worker renews an owned lease at roughly one third of its duration while processing an investigation.
Renewal and terminal job updates are fenced by job, attempt, worker and an unexpired lease, so a stale
owner cannot publish a report or overwrite the current owner. When renewal fails, ownership is lost or
host shutdown begins, the Worker cancels the in-flight investigation and observes the renewal loop before
releasing its slot. A job left in `Processing` becomes claimable after its current lease expires.

This protects the durable ownership boundary, but it cannot forcibly interrupt a provider or tool that
ignores its cancellation token. The shipped model and tool paths propagate cancellation; custom adapters
must do the same to avoid work that can no longer publish a result.
## Dormant Components Kept for the Roadmap

Two upstream-derived component groups are intentionally retained but not registered in DI.

IC-BL-010 keeps the standalone governed tool execution/audit stack dormant: `ToolPolicy`, `ToolRisk`, `ToolPolicyDecision`, `ToolPolicyMetadata`, `GovernedAgentToolExecutor`, `AgentToolAuditLogWriter`, `IToolAuditLogRepository`, `PostgresToolAuditLogRepository` and `infra/postgres/init/006-tool-audit.sql`. The live Worker path uses its own role-scoped rule engine and triage-ledger audit events instead.

IC-BL-014 keeps cost-pricing primitives dormant: `AiCostEstimator`, `PricingRecord`, `IPricingRepository`, `PostgresObservabilityRepository` and the `incidentcompass.ai_model_pricing` half of `infra/postgres/init/004-observability-cost.sql`. Live model usage is recorded as `ModelCall` and `BudgetEvent` ledger rows; cost rollup is deferred until a reporting workflow consumes those rows.
