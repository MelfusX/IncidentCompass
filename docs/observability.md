# Observability

Production-minded model-backed systems need visibility into model calls, latency, token usage, budget decisions and failures without storing sensitive prompt material.

## Minimum Signals

- structured application logs;
- correlation ID;
- request duration;
- model latency;
- token usage;
- budget ledger entries;
- error logs.

## ModelCall Ledger Events

The live model telemetry mechanism is the append-only triage ledger. Each investigation model call writes a compact `ModelCall` event to `incidentcompass.triage_ledger`.

`ModelCall` rationale stores redacted metadata only:

- call kind;
- route ID;
- provider;
- model;
- token usage;
- usage source (`provider` or `estimate`);
- duration in milliseconds;
- proposed tool-call count.

The ledger does not store rendered prompts, full provider responses, document text, provider credentials, API keys or embedding vectors. Token budget accounting is recorded separately as first-class `BudgetEvent` rows with `tokens_delta` and `workers_delta` columns.

## Hourly Cost Rollups

`GET /api/v1/observability/cost-rollups` reads these durable `ModelCall` rows for the authenticated
tenant over required `fromUtc` and `toUtc` values. Both boundaries must use a UTC offset. Start is
inclusive, end is exclusive, and the non-empty window is limited to 31 days. Results contain only UTC
hour, call count, input/output/total token totals, priced/unpriced call counts and exact spend totals
separated by currency.

Each call is priced only when its metadata is valid and exactly one case-sensitive provider/model
pricing interval contains the call timestamp. Valid calls without a price still contribute token
totals and increment `unpricedCallCount`. Invalid JSON or types, blank identities, unsafe token
values and overlapping or tied prices increment the call and unpriced counts but contribute no token
or spend value. A real configured zero price remains a priced call; missing or ambiguous pricing never
becomes false zero spend.

The pricing table is effective-dated operator-maintained database configuration. This release adds no
price-management API, configuration reload, currency conversion, threshold, alert job or notification.
The rollup response and dispatch logs do not return ModelCall provider, model, route ID, prompt,
response, credential, endpoint or embedding data. Existing ModelCall and BudgetEvent writes and the
fault-ledger response are unchanged.

Post-report approval state uses the exact `ActionProposed`, `ApprovalDecision`,
`ActionDispatchStarted` and `ActionCompleted` ledger events. These rows carry bounded summaries,
closed decisions/statuses and `action:<id>` or `artifact:<id>` references. They do not copy canonical
payload bodies, provenance bodies, adapter routes, credentials, prompts or transcripts into the
ledger or application logs.

Denied post-report proposals use `PolicyDecision(Denied)` with a closed bounded reason and a safe
`report:<id>` reference only after same-tenant current origin resolution. They do not create action,
artifact or provenance rows. Rejections before that origin boundary write no ledger row, avoiding a
foreign-report oracle. Accepted proposal rate caps count `ActionProposed`, not only allowed policy
decisions, so requested and auto-approved proposals consume the same cap.

Post-report evaluation intents deliberately add no new triage-ledger event kind. Their immutable
identity, fenced processing state, database-clock lease, attempt count, next retry time and bounded
closed error code remain inspectable in `incidentcompass.post_report_action_intents`; any accepted
proposal then uses the existing action events above. Intent input contains only identifiers, exact
tool/workflow version and an optional bounded route id. Logs must not copy its bytes, report or
evidence bodies, prompts, provider responses, credentials or adapter routes.

Approved dispatch writes `ActionDispatchStarted` in the same transaction as its durable owner/fence
claim. Definitive success or failure writes one bounded `ActionResult` and `ActionCompleted` atomically
with terminal state. Dry-run uses the same terminal evidence with zero adapter calls. Exceptions,
timeouts, cancellation and expired in-doubt claims use the closed `dispatch_outcome_unknown` failure;
logs and ledger rows do not contain frozen payload bytes, provider bodies, credentials or routes.
Confirmed live Telegram and GitHub success additionally stores a compact typed projection in that
same terminal transaction. It contains only external resource kind/id and one closed state change:
`not_sent` to `sent`, `absent` to `open`, or `open` to `comment_added`. Dry-run, definitive failure and
outcome-unknown leave the projection null and remain observable through their stable `ActionResult`
and `ActionCompleted` outcome. Projection fields are immutable after terminal commit.
For Telegram notifications, an unclaimed predecessor that is replaced records the existing bounded
superseded terminal evidence. A started predecessor denies a successor. Confirmed live success and
outcome-unknown start a 30-minute database-clock cooldown measured from durable dispatch start;
simulated, requested, rejected, expired and definitive pre-mutation failures do not. Telegram success
keeps only the bounded provider kind and
message id in the action result, never the token, chat id, request path or raw response.

GitHub ticket create uses the same action events and stores only a bounded canonical provider kind
and issue number on confirmed success. Repository-bound no-match eligibility is proven from the
durable current-attempt `ToolResult`; proposal denials use closed reason codes. Preflight and create
failures expose only stable codes, and a response that becomes unreadable after the single POST is
`dispatch_outcome_unknown`. Correlation markers may be read from frozen payload/history for bounded
duplicate lookup, but tokens, Authorization headers, repository authority, request paths and raw
provider bodies never enter ledger rows or logs.

GitHub ticket update uses a distinct action category and stores only bounded canonical provider,
issue-number and comment-id metadata on confirmed success. The cited `ExistingTicket` target is
resolved and rechecked from durable report evidence; missing, foreign, malformed or ambiguous targets
produce stable closed failures without a provider write. Target/comment preflight and POST failures
expose only stable codes. The frozen comment body and marker may be used for exact duplicate lookup,
but neither they nor raw provider bodies, credentials, headers, repository authority or request paths
enter ledger rows or logs. An unreadable or interrupted response after comment POST begins is recorded
as `dispatch_outcome_unknown` and is not resent.

## Failure Behavior

Model calls are part of the Worker investigation loop. Required durable side effects, including ledger budget/model-call events and final report commit events, are treated as part of the workflow state. The Worker does not expose foreground success to an API caller after a missing required durable write.

Provider failures are normalized at the Application port boundary and recorded through job failure state and application logs rather than through a separate AI request-log table. Provider transport failures use the durable `provider_unavailable` delayed state instead of consuming the ordinary attempt limit. The per-process Worker outage tracker pauses claims after its configured threshold and clears on a successful model call; this state contains no prompt, credential or incident data.

## Runtime Metadata Telemetry

`IncidentCompass.Runtime` exposes an in-process `ActivitySource` and `Meter` for job claims and attempts, model calls and duration, governed tool calls, PostgreSQL migrations and memory synchronization. It is a source only: v0.2.0 does not configure an OTLP exporter, collector endpoint or metrics endpoint. A host may attach a compatible listener or exporter without changing application workflows.

The source uses fixed operation names and a closed `outcome` vocabulary: `claimed`, `succeeded`, `failed`, `cancelled`, `provider_unavailable` and `denied`. It never attaches incident IDs, fault IDs, tenant IDs, user IDs, service names, prompt text, document text, tool arguments, provider responses, credentials or connection strings as telemetry tags. Listener and exporter callback failures are isolated so triage, migrations and memory synchronization continue according to their normal durable-workflow behavior.

## Later Audit Events

Additional sensitive actions should use durable audit records when implemented:

- quota exceeded;
- additional external-action before/after correlation beyond the existing action lifecycle events;
- cost alert delivery and quota enforcement built on independently governed policy.

## OTLP Ingress

OTLP ingress is separate from IncidentCompass runtime telemetry. The API accepts OTLP/HTTP protobuf
trace and log exports at `/v1/traces` and `/v1/logs`, maps only the signal fields needed for deterministic
intake, and preserves trace, span, parent span, service, operation and error metadata. This makes
IncidentCompass a consumer of an observability pipeline, not an observability backend. It does not expose
an OTLP runtime exporter, a metrics receiver or a profile receiver in this release.

## Later Options

- configurable OTLP exporter wiring and collector examples;
- metrics endpoint;
- Prometheus/Grafana example;
- Azure Application Insights adapter.
