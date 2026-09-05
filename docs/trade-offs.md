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

## Local Incident Tenant Partition Is Not Authentication

v0.2.0 keeps one server-configured incident-data tenant through `IIncidentTenantContext`. It prevents accidental cross-tenant reads and makes the future auth boundary explicit, but it does not authenticate callers or make demo headers trustworthy. Production multi-tenant use requires IC-BL-024 to authenticate an API key and map it server-side to exactly one tenant before replacing the config-backed context.

## Sequential Ledger-Backed Governance

Phase 3 evaluates `rate_cap`, `precondition` and budget state by reading the append-only ledger. This is simple and inspectable for the MVP because worker delegation is sequential. It is not a parallel-safe counter mechanism; future parallel fan-out would need serialized policy evaluation or atomic counters to avoid two workers passing a cap at the same time.

## Token Budget Overshoot

`MaxTokens` means the backend will not start a new model call once the current-attempt budget is already reached. A single in-flight call can still overshoot the limit because final usage is known only after the provider responds. The overshoot is recorded as a `BudgetEvent` instead of hidden.

## File-Backed Memory Is The Write Path

Memory content stays in reviewed files instead of an unauthenticated admin endpoint. Source path is
the stable database identity; a content change updates and re-embeds that item, and a removed file is
deactivated from retrieval. This keeps provenance simple and prevents an edited file from leaving a
second stale live item. Runtime resync is opt-in and bounded; operators may enable it for reviewed file changes without adding a memory write API. The default remains startup-only synchronization.

## Documentation Fit Is Evidence Classification

`CurrentReleases` is a manually maintained per-service marker in the snapshotted triage configuration.
Retrieved memory is labeled from its service and release metadata before report publication. The backend
can show current, stale-only, mixed historical, missing and multiple-current-document states, but it
cannot prove that two documents agree semantically or that a runbook is operationally correct. Multiple
current matches therefore add an explicit review limitation rather than being silently resolved by the
model.
## Memory Embedding Model Changes Require Re-Embedding

Phase 4 memory retrieval filters by tenant, embedding provider, embedding model and embedding dimensions. This avoids mixing incompatible corpora, but it also means changing the embedding provider or model makes existing memory chunks silently unretrievable until they are re-embedded. Changing the configured embedding provider or model should be paired with a full memory re-seed or migration.

## Bounded Memory Reranking Instead of Database Full-Text Search

Memory search overfetches at most four times the configured result count, capped at 100 candidates,
then reranks in Application with deterministic lexical and trusted metadata features. This recovers
relevant chunks that vector-only `TopK` can hide while keeping tenant, embedding-route, dimension and
active-item isolation inside the PostgreSQL query. Current same-service documentation has priority;
stale-only evidence remains eligible and keeps its stale label. Component and evidence-kind boosts use
only exact normalized query matches against stored metadata and code-owned aliases.

This is a bounded reference implementation, not a general hybrid-search engine. Its fixed lexical rules
may need revision for multilingual or much larger corpora, and overfetch adds query and application work.
PostgreSQL full-text search, reciprocal-rank fusion, adaptive retries and caller-configurable ranking
weights remain deferred. Each execution makes exactly one embedding request and one repository search.

## Deterministic Grouping Is Not Incident Correlation

Delivery deduplication, open-fault grouping, suppression and recurrence are deliberately separate.
A duplicate delivery is ignored after its first accepted signal. A distinct matching delivery can attach
to one open fault, be stored as suppressed against a recently closed fault, or advance a recurrence state
once the silence window has elapsed. The state is keyed by the effective fingerprint-rule generation and
uses a PostgreSQL upsert, so concurrent accepted recurrence deliveries count once each and create at most
one threshold-crossing escalation intent. This keeps the behavior auditable, but the selected fingerprint
and suppression policy can still be wrong for the operator's real incident boundary. Cross-fault incident
correlation remains a later capability rather than an implicit effect of grouping.
## Re-triage Reuses Untrusted History

Recurrence escalation is deterministic database state, but the prior report copied into a new investigation is model output and incident-derived context, not authority. The prompt labels it as an untrusted hypothesis; the worker must independently ground its result and cite the new `RecurrenceState` artifact before publishing a successor. This prevents historical text from becoming sticky fact, but it does not make model reasoning a security boundary. There is no manual re-triage endpoint or mass-issue-flip trigger in v0.2.0; the latter is an explicit scope cut.

## Provider Backpressure Is Process-Local

Provider-outage backpressure is deliberately held in each Worker process. It prevents a local outage from rapidly consuming retries and clears after a successful model call, but multiple Worker hosts do not share breaker state. A future distributed deployment needs coordinated provider health if a global circuit is required; v0.2.0 remains a local/reference deployment and does not claim that property.
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
## One Live Tool Policy Path

`ToolRuleEngine` is the single tool-policy mechanism. Immediate Worker reads feed it role grants;
backend-owned post-report proposals feed it the exact snapshotted `Actions.AllowedTools` grant.
External proposal facts use a transaction-bound ledger reader only after fault-first current-origin
and job locking; this preserves one evaluator while serializing preconditions and accepted-use caps.
Capability registration prevents configuration from turning a read into an external action, and
external actions never enter the investigation model surface. The earlier standalone executor and
audit repository were removed instead of retaining a parallel policy interpretation. The released
`infra/postgres/init/006-tool-audit.sql` migration stays byte-identical and its legacy table remains
unused so fresh and upgraded databases preserve migration integrity.

## Dormant Pricing Components Kept for the Roadmap

IC-BL-014 keeps cost-pricing primitives dormant: `AiCostEstimator`, `PricingRecord`, `IPricingRepository`, `PostgresObservabilityRepository` and the `incidentcompass.ai_model_pricing` half of `infra/postgres/init/004-observability-cost.sql`. Live model usage is recorded as `ModelCall` and `BudgetEvent` ledger rows; cost rollup is deferred until a reporting workflow consumes those rows.

## Durable Evaluation Queue Is Not A Second Action Outbox

Report publication and evaluation cannot share one long transaction across arbitrary workflow code.
IncidentCompass instead commits a minimal immutable intent with the report, then evaluates it through
a separate bounded Worker pump. The queue uses database-clock renewable leases, random fences,
bounded deterministic retries and a maximum attempt count. A crash or lost lease can therefore replay
evaluation, so workflows must be deterministic and proposal creation uses a stable proposal key plus
the existing proposal transaction's idempotency boundary.

The queue stops at proposal creation. It has no adapter port, approval decision, dispatch state or
new ledger vocabulary; `action_approvals` remains the only approval and external-dispatch outbox.
This adds durable scheduling and recovery without creating a competing policy system. Production
composition intentionally registers no evaluation workflow in this slice, so only synthetic Docker
tests exercise the handoff.

## At-Most-Once Action Dispatch Prefers Visible Uncertainty

The post-report action path separates immutable proposal/approval from a bounded Worker dispatcher.
The dispatcher locks the fault before the action, rechecks current report and policy, freezes one
owner/fence/deadline claim and calls the exact registered adapter with the stored bytes and action id.
It never automatically invokes that action again after claim. Dry-run terminates without a call, and
binding or policy drift fails closed.

This is deliberately at-most-once backend invocation, not exactly-once delivery. If a provider accepts
the request but the response or terminal database commit is lost, IncidentCompass cannot prove the
external outcome. Recovery records `dispatch_outcome_unknown` after the database deadline and fences
late completion instead of risking a duplicate side effect. A later adapter may use the action id as
its own idempotency key, but IncidentCompass does not rely on provider idempotency for correctness.
Synthetic tools prove the workflow without granting production side-effect authority; production
callers and real notification/ticket adapters remain separate work.
