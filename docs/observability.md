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

Provider failures are normalized at the Application port boundary and recorded through job failure state and application logs rather than through a separate AI request-log table.

## Later Audit Events

Additional sensitive actions should use durable audit records when implemented:

- quota exceeded;
- governed standalone tool execution once a caller is wired into IC-BL-010;
- cost rollups once IC-BL-014 consumes `ModelCall` rows and pricing records.

## Later Options

- OpenTelemetry traces;
- metrics endpoint;
- Prometheus/Grafana example;
- Azure Application Insights adapter.
