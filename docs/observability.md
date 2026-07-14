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

## Failure Behavior

Model calls are part of the Worker investigation loop. Required durable side effects, including ledger budget/model-call events and final report commit events, are treated as part of the workflow state. The Worker does not expose foreground success to an API caller after a missing required durable write.

Provider failures are normalized at the Application port boundary and recorded through job failure state and application logs rather than through a separate AI request-log table. Provider transport failures use the durable `provider_unavailable` delayed state instead of consuming the ordinary attempt limit. The per-process Worker outage tracker pauses claims after its configured threshold and clears on a successful model call; this state contains no prompt, credential or incident data.

## Later Audit Events

Additional sensitive actions should use durable audit records when implemented:

- quota exceeded;
- governed standalone tool execution once a caller is wired into IC-BL-010;
- cost rollups once IC-BL-014 consumes `ModelCall` rows and pricing records.

## OTLP Ingress

OTLP ingress is separate from IncidentCompass runtime telemetry. The API accepts OTLP/HTTP protobuf
trace and log exports at `/v1/traces` and `/v1/logs`, maps only the signal fields needed for deterministic
intake, and preserves trace, span, parent span, service, operation and error metadata. This makes
IncidentCompass a consumer of an observability pipeline, not an observability backend. It does not expose
an OTLP runtime exporter, a metrics receiver or a profile receiver in this release.

## Later Options

- runtime OpenTelemetry traces;
- metrics endpoint;
- Prometheus/Grafana example;
- Azure Application Insights adapter.
