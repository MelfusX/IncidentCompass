# Quickstart

This guide runs the local mock-provider demo path. It does not call real model
or embedding providers unless you explicitly configure OpenAI-compatible
adapters.

## Prerequisites

- .NET 10 SDK.
- Docker, for local PostgreSQL.
- PowerShell examples below assume Windows, but the same `dotnet` and
  `docker compose` commands work cross-platform with shell syntax changes.

## Local Configuration

```powershell
Copy-Item .env.example .env
$env:ConnectionStrings__IncidentCompass = "Host=localhost;Port=5432;Database=incidentcompass;Username=incidentcompass;Password=incidentcompass_dev_password"
```

The application reads normal .NET configuration sources. Runtime
`appsettings.json` files intentionally omit database credentials; supply
`ConnectionStrings__IncidentCompass` through environment variables, user secrets,
command-line configuration or dotenv-aware tooling. The `.env` file is ignored
by Git and should stay machine-local; copying `.env.example` is useful for
Docker Compose, but plain `dotnet run` does not load `.env` automatically unless
your shell/tooling does that for you. Values in `.env.example` are local-only
Docker/demo placeholders.

The triage runtime also loads `config/incidentcompass.config.json`, including instruction
file references used to compute the persisted `config_hash`. API and Worker appsettings
point at that checked-in file for the local demo path. The Worker rehydrates claimed jobs
from persisted config snapshots by `config_hash`; its own concurrency/lease settings live
in appsettings, not in the hashed triage snapshot.

## Build And Test

```powershell
dotnet restore IncidentCompass.slnx
dotnet build IncidentCompass.slnx
dotnet test IncidentCompass.slnx
```

PostgreSQL repository tests use Testcontainers. Outside CI they skip when Docker
is unavailable; in CI, or when `INCIDENTCOMPASS_REQUIRE_DOCKER_TESTS=true` is set,
Docker-backed tests are required and will fail instead of silently skipping.

## Run Locally

Start local infrastructure:

```powershell
docker compose up -d postgres
```

The local PostgreSQL image applies the init scripts under `infra/postgres/init`
when the Docker volume is first created, including observability/cost tracking,
tool audit logging, Phase 1 intake tables (`signals`, `faults`, `triage_jobs`,
`triage_config_snapshots`, `triage_artifacts`) and Phase 2 `triage_ledger` plus
`triage_reports`. If you are reusing an older local
Docker volume, recreate it with `docker compose down -v` or apply the missing
numbered SQL scripts manually.


### Optional Memory Seed

Phase 4 includes sample memory under `samples/runbooks` and `samples/incidents`. To seed it into local PostgreSQL, start either host once with memory seeding enabled; the seed path uses the configured `memory_search` embedding route and the pinned mock model `mock-memory-embedding-v1` by default.

```powershell
$env:IncidentCompass__Memory__Seed__Enabled = "true"
$env:IncidentCompass__Memory__Seed__TenantId = "local"
$env:IncidentCompass__Memory__Seed__SourceDirectory = "../../samples"
$env:ConnectionStrings__IncidentCompass = "Host=localhost;Port=5432;Database=incidentcompass;Username=incidentcompass;Password=incidentcompass_dev_password"
dotnet run --project src/IncidentCompass.Api --launch-profile http
```

After the seed completes, stop the host and unset `IncidentCompass__Memory__Seed__Enabled` for normal runs. Seeding is idempotent for the same tenant/source/content hash/version.
Run the API:

```powershell
$env:ConnectionStrings__IncidentCompass = "Host=localhost;Port=5432;Database=incidentcompass;Username=incidentcompass;Password=incidentcompass_dev_password"
dotnet run --project src/IncidentCompass.Api --launch-profile http
```

In a second terminal, run the background worker host. It performs a startup health check,
then polls PostgreSQL for pending triage jobs, claiming at most the configured
`IncidentCompass:Worker:MaxConcurrentJobs` per process. Each claimed job rehydrates its
persisted triage config by `config_hash`, runs the governed orchestrator with only
`delegate` and `publish_report`, records live ledger events, stores the analysis worker
output as an attempt-level artifact and writes a minimal report row before marking the job
terminal. Set the connection string again in this terminal; PowerShell process environment
variables do not carry into a new window:

```powershell
$env:ConnectionStrings__IncidentCompass = "Host=localhost;Port=5432;Database=incidentcompass;Username=incidentcompass;Password=incidentcompass_dev_password"
dotnet run --project src/IncidentCompass.Worker
```

Useful local endpoints:

- `GET http://localhost:5198/api/v1/health`
- `GET http://localhost:5198/api/v1/users/me`
- `POST http://localhost:5198/api/v1/incidents`
- `GET http://localhost:5198/api/v1/faults/{id}`

Sample HTTP requests are available in
[src/IncidentCompass.Api/IncidentCompass.Api.http](../src/IncidentCompass.Api/IncidentCompass.Api.http)
and [samples/http/local-demo.http](../samples/http/local-demo.http).

## Demo Identity

Demo identity can be supplied with headers:

```http
X-Demo-User-Id: alice
X-Demo-Tenant-Id: local
X-Demo-Roles: developer,admin
X-Demo-Groups: demo,engineering
```

Demo header auth is enabled by default only when the API runs in `Development`,
where missing headers fall back to the local `demo-user` defaults for the
quickstart. A `Production` API host always requires a real `IUserContext`
authentication adapter in the API composition root. Non-production demo
environments can explicitly opt in to demo headers; in that opt-in mode, default
identity, tenant, role and group values are disabled: a request without
`X-Demo-User-Id` remains unauthenticated and receives no default claims. Do not
use `X-Demo-*` headers as deployed authentication.

## Demo Requests

Check API health:

```powershell
Invoke-RestMethod -Method Get -Uri http://localhost:5198/api/v1/health
```

Fetch the current demo user:

```powershell
Invoke-RestMethod `
  -Method Get `
  -Headers @{ "X-Demo-User-Id" = "alice"; "X-Demo-Tenant-Id" = "local"; "X-Demo-Roles" = "developer,admin"; "X-Demo-Groups" = "demo,engineering" } `
  -Uri http://localhost:5198/api/v1/users/me
```

Ingest a structured tester signal:

```powershell
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
```

Fetch the created fault:

```powershell
Invoke-RestMethod -Method Get -Uri "http://localhost:5198/api/v1/faults/$($ingested.faultId)"
```

## Provider Overrides

To use an OpenAI-compatible chat completions endpoint, override configuration
locally:

```powershell
$env:IncidentCompass__ModelGateway__Provider = "OpenAiCompatible"
$env:IncidentCompass__ModelGateway__DefaultModel = "<model-name>"
$env:IncidentCompass__ModelGateway__OpenAiCompatible__ApiKey = "<api-key>"
```

To use an OpenAI-compatible embeddings endpoint, override configuration locally:

```powershell
$env:IncidentCompass__Embeddings__Provider = "OpenAiCompatible"
$env:IncidentCompass__Embeddings__DefaultModel = "<embedding-model-name>"
$env:IncidentCompass__Embeddings__OpenAiCompatible__ApiKey = "<api-key>"
```

OpenAI-compatible provider URLs must use HTTPS by default. For a local loopback
test server only, set
`IncidentCompass__ModelGateway__OpenAiCompatible__AllowInsecureHttpForLoopback=true`.

The model gateway is exercised by the Worker investigation loop after an incident is ingested. The embedding gateway is exercised by the governed `memory_search` worker tool. There is still no chat or document-ingestion endpoint.

## Demo Flow Checklist

1. `docker compose up -d postgres`
2. Set `ConnectionStrings__IncidentCompass` in the API terminal.
3. `dotnet run --project src/IncidentCompass.Api --launch-profile http`
4. Set `ConnectionStrings__IncidentCompass` in the Worker terminal.
5. Optional once: enable `IncidentCompass__Memory__Seed__Enabled=true` to seed sample memory, then disable it.
6. `dotnet run --project src/IncidentCompass.Worker`
7. `GET /api/v1/health`
8. `GET /api/v1/users/me`
9. `POST /api/v1/incidents`
10. `GET /api/v1/faults/{id}`
