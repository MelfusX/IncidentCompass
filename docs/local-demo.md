# Local Demo Walkthrough

This walkthrough is the recommended review path for the Phase 6 MVP demo. It runs PostgreSQL,
the API, the Worker and the deterministic Tester from Docker Compose, then prints a table for the
four public demo scenarios.

## One Command

~~~powershell
powershell -ExecutionPolicy Bypass -File scripts/demo.ps1
~~~

The script builds the api, worker and tester images, starts postgres, api and worker, waits for
GET http://localhost:5198/api/v1/health, then runs the Tester container from the demo profile. After
the table prints, services remain running so you can inspect the API.

Use these variants when needed:

~~~powershell
powershell -ExecutionPolicy Bypass -File scripts/demo.ps1 -NoBuild
powershell -ExecutionPolicy Bypass -File scripts/demo.ps1 -RealLlm
~~~

-NoBuild reuses existing images. -RealLlm adds compose.real-llm.yml, which points the chat model
gateway at an OpenAI-compatible local endpoint such as http://host.docker.internal:1234. That path is
opt-in and non-gated; the deterministic demo uses mock model and embedding providers.

Stop the demo services with:

~~~powershell
docker compose --profile demo down
~~~

If you need a fresh database volume after schema or seed changes, use:

~~~powershell
docker compose --profile demo down --volumes
~~~

## Service Layout

- postgres: pgvector/pgvector:pg16, initialized from infra/postgres/init.
- api: builds from src/IncidentCompass.Api/Dockerfile, exposes http://localhost:5198, runs as the
  non-root incidentcompass user, copies config/ and samples/, and sets
  IncidentCompass__ConfigSource__Path=/app/config/incidentcompass.config.json plus
  IncidentCompass__Memory__Seed__SourceDirectory=/app/samples.
- worker: builds from src/IncidentCompass.Worker/Dockerfile, runs as the non-root incidentcompass
  user, copies the same config/ and samples/, enables sample memory seeding, and uses the same
  explicit config and sample-source paths inside the image.
- tester: builds from src/IncidentCompass.Tester/Dockerfile under the demo profile and talks to the
  API only over HTTP. The project has no references to Application, Domain or Infrastructure; it
  depends only on the .NET runtime libraries used by HttpClient and JSON serialization.

Compose waits for PostgreSQL health before starting the hosts, checks API readiness with GET
/health, and uses a process-level Worker health check before running the Tester. API and Worker
still use restart-on-failure because config warmup intentionally fails fast if durable storage is
unavailable.

## Demo Scenarios

The Tester runs four scenarios from docs and samples-backed local data:

1. Known timeout error with a matching seeded runbook. Expected classification: KnownIncident.
2. Unknown null-reference error with no matching memory. Expected classification: Unknown with an
   insufficient-evidence report.
3. Repeated provider-unavailable errors crossing the configured mass-issue threshold. Expected
   classification: SimpleKnownError and is_mass_issue=true.
4. Validation/noise input the analysis worker closes quickly. Expected classification: Noise.

The output table includes FaultId, ReportId, is_mass_issue, Classification, a host-reachable ledger
URL and a host-reachable report URL. The Tester accepts --request-timeout-seconds,
--poll-timeout-seconds, --poll-interval-seconds, --scenario-timeout-seconds and
--total-timeout-seconds when you run it directly; the same request and poll knobs can also be set
with INCIDENTCOMPASS_TESTER_REQUEST_TIMEOUT_SECONDS, INCIDENTCOMPASS_TESTER_POLL_TIMEOUT_SECONDS
and INCIDENTCOMPASS_TESTER_POLL_INTERVAL_SECONDS. Useful read endpoints after a run are:

- GET http://localhost:5198/api/v1/faults/{id}
- GET http://localhost:5198/api/v1/faults/{id}/ledger
- GET http://localhost:5198/api/v1/triage-reports/{id}

## What The Demo Proves

The default path is deterministic by design. The mock model scripts the orchestrator's
delegation/tool-call sequence, while the real backend enforces source normalization, config hashing,
ledger writes, role-scoped tool execution, memory_search policy, exact artifact grounding, report
persistence and readback. The demo proves those packaging and governance rails are wired end to end.

It does not prove a local LLM can reliably drive the same multi-turn trajectory. A real model run is
available through -RealLlm, but it is intentionally optional and non-gated. Grounded citations mean
each citation resolves to a stored artifact from this run; they do not prove the model's conclusion is
correct.
