# Checkout Timeout Runbook

Use this runbook when checkout requests time out while waiting on the inventory service.

Symptoms:
- Payment or checkout API requests fail with `TimeoutException`.
- The failing route is usually `/checkout`.
- Logs mention waiting for inventory reservation or stock confirmation.

Immediate checks:
1. Confirm inventory service health and request latency.
2. Check the checkout API outbound timeout and retry settings.
3. Compare timeout spikes with inventory deployment or database saturation events.

Recommended action:
Reduce checkout retry fan-out, verify inventory capacity, and fail gracefully with a retryable user-facing message until inventory latency returns to baseline.
