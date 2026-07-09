---
kind: KnownIncident
service: checkout-api
component: payments
release: 0.1
tags: [checkout, timeout, known-incident]
---

# Known Incident: Checkout Inventory Timeout

Known incident pattern for checkout failures caused by inventory latency.

Matching signals usually contain:
- service name: payments-api or checkout-api.
- error type: `TimeoutException`.
- error message mentioning checkout timeout, inventory timeout, stock reservation, or inventory dependency latency.

Prior resolution:
Inventory request latency increased after a connection-pool configuration change. Restoring the pool limit and restarting saturated inventory workers stopped checkout timeouts.

Operator note:
If this pattern appears again, start with inventory dependency latency before changing payment processing.
