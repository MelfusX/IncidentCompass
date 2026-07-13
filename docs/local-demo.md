# Local Demo Walkthrough

This walkthrough runs PostgreSQL, the API, the Worker and the HTTP-only Tester from Docker Compose.
The default path uses OpenAI-compatible model and embedding providers. Mock providers are available
only through the explicit `-Mock` switch for tests or deterministic backend checks.

## One Command

Configure model names if your local provider requires exact ids:

~~~powershell
Copy-Item .env.example .env
# Edit INCIDENTCOMPASS_LLM_MODEL and INCIDENTCOMPASS_EMBEDDINGS_MODEL in .env.
~~~

Run the demo:

~~~powershell
powershell -ExecutionPolicy Bypass -File scripts/demo.ps1
~~~

The script builds the api, worker and tester images, starts postgres, api and worker, waits for
GET http://localhost:5198/api/v1/health, then runs the Tester container from the demo profile. After
the table prints, services remain running so you can inspect the API.

Use these variants when needed:

~~~powershell
powershell -ExecutionPolicy Bypass -File scripts/demo.ps1 -NoBuild
powershell -ExecutionPolicy Bypass -File scripts/demo.ps1 -Mock
~~~

`-NoBuild` reuses existing images. `-Mock` adds `compose.mock.yml`; use it when you need a stable
backend packaging check without provider calls.

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
- otel-collector: runs the pinned stock OpenTelemetry Collector Contrib image under the demo profile,
  receives OTLP/HTTP on the internal `otel-collector:4318` address and forwards uncompressed traces and
  logs through its stock `otlphttp` exporter to the API's native OTLP routes. It does not transform or
  synthesize IncidentCompass fields.- tester: builds from src/IncidentCompass.Tester/Dockerfile under the demo profile, uses the official
  OpenTelemetry SDK to export an OTLP/HTTP protobuf error span to the API, and runs its remaining
  scenarios through the product HTTP API.

Host mappings use `IC_API_PORT` and `IC_POSTGRES_PORT`, defaulting to `5198` and `5432`. Internal
Compose URLs stay on `api:8080` and `postgres:5432`, so changing host ports does not change service
configuration. Put overrides in the ignored `.env` file.

Compose waits for PostgreSQL health before starting the hosts, checks API readiness with GET
/health, and uses a process-level Worker health check before running the Tester. API and Worker still
use restart-on-failure because config warmup intentionally fails fast if durable storage is unavailable.

## File-Backed Memory Sync

Memory files under `samples/runbooks`, `samples/incidents`, `samples/operational-notes`,
`samples/documents`, `samples/release-notes` and `samples/postmortems` are the source of truth. Optional
frontmatter supports `kind`, `service`, `component`, `release` and `tags`. The body below the
frontmatter is the content embedded and cited by reports.

API and Worker may start together and sync the same corpus safely. A changed file updates one stable
source record and re-embeds its body. A removed file is deactivated, so its old chunks stay available
for audit history but no longer participate in `memory_search`. Git history remains the provenance and
review path; there is no memory write API.

## Model Configuration

Default Docker Compose values point at a host-side OpenAI-compatible server:

- `INCIDENTCOMPASS_LLM_BASE_URL=http://host.docker.internal:1234`
- `INCIDENTCOMPASS_LLM_MODEL=local-model`
- `INCIDENTCOMPASS_EMBEDDINGS_BASE_URL=http://host.docker.internal:1234`
- `INCIDENTCOMPASS_EMBEDDINGS_MODEL=local-embedding-model`

Set these in `.env` before starting the stack when your provider uses different model ids or paths.

## Demo Scenarios

The Tester first exports a real error span through the OpenTelemetry SDK to the Collector, which forwards it to `/v1/traces`, then runs four scenarios from docs and samples-backed local data:

1. Known timeout error with a matching seeded runbook.
2. Unknown null-reference error with no matching memory.
3. Repeated provider-unavailable errors crossing the configured mass-issue threshold.
4. Validation/noise input the analysis worker should close quickly.

The output table includes FaultId, ReportId, is_mass_issue, Classification, a host-reachable ledger
URL and a host-reachable report URL. With real providers, exact classifications can vary by model;
the backend checks are about durable grounding, policy and readback, not pretending model reasoning is
deterministic.

Useful read endpoints after a run are:

- GET http://localhost:5198/api/v1/faults/{id}
- GET http://localhost:5198/api/v1/faults/{id}/ledger
- GET http://localhost:5198/api/v1/triage-reports/{id}

## What The Demo Proves

The normal path proves the full stack is wired to an actual configured provider route: signal intake,
config hashing, worker orchestration, provider calls, role-scoped tool execution, memory_search,
artifact grounding, report persistence and readback.

It does not prove the configured model is always correct. Grounded citations mean each citation
resolves to a stored artifact from this run; they do not prove the model's conclusion is correct.
