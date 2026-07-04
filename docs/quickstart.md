# Quickstart

This guide runs the local mock-provider demo path. It does not call real model or embedding
providers unless you explicitly configure OpenAI-compatible adapters.

## Prerequisites

- .NET 10 SDK.
- Docker, for local PostgreSQL and the one-command demo.
- PowerShell examples below assume Windows, but the same dotnet and docker compose commands work
  cross-platform with shell syntax changes.

## One-Command Demo

The fastest review path is the Phase 6 compose demo:

~~~powershell
powershell -ExecutionPolicy Bypass -File scripts/demo.ps1
~~~

The script builds the API, Worker and Tester images, starts PostgreSQL/API/Worker, waits for
GET http://localhost:5198/api/v1/health, then runs the Tester against four deterministic scenarios.
Compose health checks also gate API readiness and Worker process startup before the Tester runs.
The printed table includes FaultId, ReportId, is_mass_issue, Classification, ledger URL and report
URL.

Use -NoBuild to reuse images or -RealLlm to add compose.real-llm.yml and point the chat gateway at
an OpenAI-compatible local endpoint. The real-LLM path is optional and non-gated; the deterministic
path uses mock model and embedding providers.

~~~powershell
powershell -ExecutionPolicy Bypass -File scripts/demo.ps1 -NoBuild
powershell -ExecutionPolicy Bypass -File scripts/demo.ps1 -RealLlm
~~~

Stop demo services with:

~~~powershell
docker compose --profile demo down
~~~

The one-command path is described in more detail in [local-demo.md](local-demo.md), including the
mock-vs-real-LLM caveat: the gated demo proves the backend rails and packaging around a scripted mock
trajectory, not autonomous local-model quality.

## Local Configuration

~~~powershell
Copy-Item .env.example .env
$env:ConnectionStrings__IncidentCompass = "Host=localhost;Port=5432;Database=incidentcompass;Username=incidentcompass;Password=incidentcompass_dev_password"
~~~

The application reads normal .NET configuration sources. Runtime appsettings.json files
intentionally omit database credentials; supply ConnectionStrings__IncidentCompass through
environment variables, user secrets, command-line configuration or dotenv-aware tooling. The .env
file is ignored by Git and should stay machine-local; copying .env.example is useful for Docker
Compose, but plain dotnet run does not load .env automatically unless your shell/tooling does that
for you. Values in .env.example are local-only Docker/demo placeholders.

The triage runtime also loads config/incidentcompass.config.json, including instruction file
references used to compute the persisted config_hash. API and Worker appsettings point at that
checked-in file for the local demo path. The Docker images copy config/ into /app/config and set
IncidentCompass__ConfigSource__Path=/app/config/incidentcompass.config.json so published/container
layouts do not depend on repository-relative paths. The Worker rehydrates claimed jobs from
persisted config snapshots by config_hash; its own concurrency/lease settings live in appsettings,
not in the hashed triage snapshot.

## Build And Test

~~~powershell
dotnet restore IncidentCompass.slnx
dotnet build IncidentCompass.slnx
dotnet test IncidentCompass.slnx
~~~

PostgreSQL repository tests use Testcontainers. Outside CI they skip when Docker is unavailable; in
CI, or when INCIDENTCOMPASS_REQUIRE_DOCKER_TESTS=true is set, Docker-backed tests are required and
will fail instead of silently skipping.

## Run Manually

Start local infrastructure:

~~~powershell
docker compose up -d postgres
~~~

The local PostgreSQL image applies the init scripts under infra/postgres/init when the Docker volume
is first created, including observability/cost tracking, tool audit logging, intake tables (signals,
faults, triage_jobs, triage_config_snapshots, triage_artifacts) and triage ledger/report/memory
tables (triage_ledger, triage_reports, triage_evidence, memory_items, memory_chunks). If you are
reusing an older local Docker volume, recreate it with docker compose down -v or apply the missing
numbered SQL scripts manually.

### Optional Memory Seed

Sample memory lives under samples/runbooks and samples/incidents. To seed it into local PostgreSQL,
start either host once with memory seeding enabled; the seed path uses the configured memory_search
embedding route and the pinned mock model mock-memory-embedding-v1 by default.

~~~powershell
$env:IncidentCompass__Memory__Seed__Enabled = "true"
$env:IncidentCompass__Memory__Seed__TenantId = "local"
$env:IncidentCompass__Memory__Seed__SourceDirectory = "../../samples"
$env:ConnectionStrings__IncidentCompass = "Host=localhost;Port=5432;Database=incidentcompass;Username=incidentcompass;Password=incidentcompass_dev_password"
dotnet run --project src/IncidentCompass.Api --launch-profile http
~~~

After the seed completes, stop the host and unset IncidentCompass__Memory__Seed__Enabled for normal
runs. Seeding is idempotent for the same tenant/source/content hash/version.

Run the API:

~~~powershell
$env:ConnectionStrings__IncidentCompass = "Host=localhost;Port=5432;Database=incidentcompass;Username=incidentcompass;Password=incidentcompass_dev_password"
dotnet run --project src/IncidentCompass.Api --launch-profile http
~~~

In a second terminal, run the background worker host. It performs a startup health check, then polls
PostgreSQL for pending triage jobs, claiming at most the configured
IncidentCompass:Worker:MaxConcurrentJobs per process. Each claimed job rehydrates its persisted
triage config by config_hash, runs the governed orchestrator with only delegate and publish_report,
records live ledger events, stores worker output and retrieved memory as attempt-level artifacts, and
writes a grounded report plus evidence rows before marking the job terminal. Set the connection
string again in this terminal; PowerShell process environment variables do not carry into a new
window:

~~~powershell
$env:ConnectionStrings__IncidentCompass = "Host=localhost;Port=5432;Database=incidentcompass;Username=incidentcompass;Password=incidentcompass_dev_password"
dotnet run --project src/IncidentCompass.Worker
~~~

Useful local endpoints:

- GET http://localhost:5198/api/v1/health
- GET http://localhost:5198/api/v1/users/me
- POST http://localhost:5198/api/v1/incidents
- GET http://localhost:5198/api/v1/faults/{id}
- GET http://localhost:5198/api/v1/faults/{id}/ledger
- GET http://localhost:5198/api/v1/triage-reports/{id}

Sample HTTP requests are available in
[src/IncidentCompass.Api/IncidentCompass.Api.http](../src/IncidentCompass.Api/IncidentCompass.Api.http)
and [samples/http/local-demo.http](../samples/http/local-demo.http).

## Demo Identity

Demo identity can be supplied with headers:

~~~http
X-Demo-User-Id: alice
X-Demo-Tenant-Id: local
X-Demo-Roles: developer,admin
X-Demo-Groups: demo,engineering
~~~

Demo header auth is enabled by default only when the API runs in Development, where missing headers
fall back to the local demo-user defaults for the quickstart. A Production API host always requires a
real IUserContext authentication adapter in the API composition root. Non-production demo
environments can explicitly opt in to demo headers; in that opt-in mode, default identity, tenant,
role and group values are disabled: a request without X-Demo-User-Id remains unauthenticated and
receives no default claims. Do not use X-Demo-* headers as deployed authentication.

## Demo Requests

Check API health:

~~~powershell
Invoke-RestMethod -Method Get -Uri http://localhost:5198/api/v1/health
~~~

Fetch the current demo user:

~~~powershell
$headers = @{ "X-Demo-User-Id" = "alice"; "X-Demo-Tenant-Id" = "local"; "X-Demo-Roles" = "developer,admin"; "X-Demo-Groups" = "demo,engineering" }
Invoke-RestMethod -Method Get -Headers $headers -Uri http://localhost:5198/api/v1/users/me
~~~

Ingest a structured tester signal:

~~~powershell
$body = @{
  sourceKind = "tester"
  serviceName = "payments-api"
  environment = "prod"
  severity = "critical"
  observedAtUtc = "2026-07-01T12:00:00Z"
  correlation = @{ traceId = "trace-0001"; spanId = "span-0001"; externalId = "evt-0001" }
  attributes = @{
    errorType = "TimeoutException"
    errorMessage = "Checkout call timed out after 30000ms"
    operationName = "POST /checkout"
    httpRoute = "/checkout"
    httpStatusCode = 504
  }
  payload = @{ note = "synthetic tester envelope" }
} | ConvertTo-Json -Depth 8

$ingested = Invoke-RestMethod -Method Post -ContentType "application/json" -Body $body -Uri http://localhost:5198/api/v1/incidents
$ingested
~~~

Fetch the created fault and its ledger:

~~~powershell
Invoke-RestMethod -Method Get -Uri "http://localhost:5198/api/v1/faults/$($ingested.faultId)"
Invoke-RestMethod -Method Get -Uri "http://localhost:5198/api/v1/faults/$($ingested.faultId)/ledger"
~~~

## Provider Overrides

To use an OpenAI-compatible chat completions endpoint manually, override configuration locally:

~~~powershell
$env:IncidentCompass__ModelGateway__Provider = "OpenAiCompatible"
$env:IncidentCompass__ModelGateway__DefaultModel = "<model-name>"
$env:IncidentCompass__ModelGateway__OpenAiCompatible__ApiKey = "<api-key>"
~~~

To use an OpenAI-compatible embeddings endpoint, override configuration locally:

~~~powershell
$env:IncidentCompass__Embeddings__Provider = "OpenAiCompatible"
$env:IncidentCompass__Embeddings__DefaultModel = "<embedding-model-name>"
$env:IncidentCompass__Embeddings__OpenAiCompatible__ApiKey = "<api-key>"
~~~

OpenAI-compatible provider URLs must use HTTPS by default. For a local loopback test server only,
set IncidentCompass__ModelGateway__OpenAiCompatible__AllowInsecureHttpForLoopback=true. The compose
-RealLlm demo overlay enables this for host.docker.internal so containers can reach a local
OpenAI-compatible server on the host.

The model gateway is exercised by the Worker investigation loop after an incident is ingested. The
embedding gateway is exercised by the governed memory_search worker tool. There is still no chat or
document-ingestion endpoint.

## Manual Demo Flow Checklist

1. docker compose up -d postgres
2. Set ConnectionStrings__IncidentCompass in the API terminal.
3. Optional once: enable IncidentCompass__Memory__Seed__Enabled=true to seed sample memory, then disable it.
4. dotnet run --project src/IncidentCompass.Api --launch-profile http
5. Set ConnectionStrings__IncidentCompass in the Worker terminal.
6. dotnet run --project src/IncidentCompass.Worker
7. GET /api/v1/health
8. GET /api/v1/users/me
9. POST /api/v1/incidents
10. GET /api/v1/faults/{id}
11. GET /api/v1/faults/{id}/ledger
12. GET /api/v1/triage-reports/{id} after the Worker publishes a report.
