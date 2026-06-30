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
when the Docker volume is first created, including observability/cost tracking
and tool audit logging. If you are reusing an older local Docker volume,
recreate it with `docker compose down -v` or apply the missing numbered SQL
scripts manually.

Run the API:

```powershell
$env:ConnectionStrings__IncidentCompass = "Host=localhost;Port=5432;Database=incidentcompass;Username=incidentcompass;Password=incidentcompass_dev_password"
dotnet run --project src/IncidentCompass.Api --launch-profile http
```

In a second terminal, run the background worker host. It currently runs a
startup health check and idles on a fixed poll interval; a future phase
replaces the idle loop with the triage job-claim loop. Set the connection
string again in this terminal; PowerShell process environment variables do not
carry into a new window:

```powershell
$env:ConnectionStrings__IncidentCompass = "Host=localhost;Port=5432;Database=incidentcompass;Username=incidentcompass;Password=incidentcompass_dev_password"
dotnet run --project src/IncidentCompass.Worker
```

Useful local endpoints:

- `GET http://localhost:5198/api/v1/health`
- `GET http://localhost:5198/api/v1/users/me`

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

The model and embedding gateways are currently exercised through Infrastructure
and integration tests; there is no chat or document-ingestion endpoint in this
phase that calls them end-to-end from the API.

## Demo Flow Checklist

1. `docker compose up -d postgres`
2. Set `ConnectionStrings__IncidentCompass` in the API terminal.
3. `dotnet run --project src/IncidentCompass.Api --launch-profile http`
4. Set `ConnectionStrings__IncidentCompass` in the Worker terminal.
5. `dotnet run --project src/IncidentCompass.Worker`
6. `GET /api/v1/health`
7. `GET /api/v1/users/me`
