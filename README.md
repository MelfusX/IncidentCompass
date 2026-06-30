# IncidentCompass

A governed incident-triage agent backend. The intended shape: ingest a signal, an
orchestrator delegates to scoped workers under policy/audit/budget rails, and the
system produces a grounded report.

This is reference-quality software, not a production system. Phase 0 (this snapshot)
is a repository bootstrap: it carries over the layered .NET structure, the model/embedding
gateway, and a governed tool-execution/policy/audit primitive from its upstream starter
kit, with all chat/RAG/evaluation/usage/MCP product surface stripped out. No intake
endpoint, agent loop, or ledger exists yet — those are later-phase work.

## What This Is

- A layered-monolith .NET 10 skeleton using Clean Architecture and a lightweight internal
  application pipeline.
- A model/embedding gateway abstraction with deterministic mock providers (default) and
  OpenAI-compatible adapters behind the same ports.
- A governed tool-execution/policy/audit subsystem (`Governance`): typed tool schemas,
  backend policy decisions (allow/require-approval/forbid), and an audit log writer. It is
  currently uncalled library code — its previous caller (a chat loop) was removed along
  with the rest of the chat feature, and a future phase wires a new caller into it.
- Sanitized AI-request logging, cost estimation, and generic dispatch/health/security/user
  scaffolding over PostgreSQL.

## What This Is Not

- Not a chat backend, a RAG/document-ingestion system, an evaluation harness, a usage
  dashboard, or an MCP host/client. All of that existed in the upstream starter kit and was
  deliberately removed here.
- Not a full framework with stable public extension contracts.
- Not production-ready as-is: demo auth and mock providers are intentionally replaceable
  adapters with no battle-testing and no SLA.

## Architecture

```mermaid
flowchart LR
    Client["Client / API consumer"] --> Api["IncidentCompass.Api"]
    Api --> App["IncidentCompass.Application"]
    Worker["IncidentCompass.Worker"] --> App
    App --> Domain["IncidentCompass.Domain"]
    Infrastructure["IncidentCompass.Infrastructure"] --> App
    Infrastructure --> Postgres["PostgreSQL"]
    Infrastructure --> Providers["Mock or OpenAI-compatible providers"]
```

`IncidentCompass.Application` is a single project organized by feature folder: `Core/`
(dispatcher, identity/correlation, model/embedding gateway abstractions, options) and
`Governance/` (the kept tool-execution/policy/audit primitive) are populated today;
`Intake/`, `Investigation/`, and `Memory/` are reserved for later phases and intentionally
empty. `Infrastructure` implements persistence and provider adapters. `Api` maps HTTP
input/output only. `Worker` runs a placeholder background host pending Phase 2's job-claim
loop.

Start here:

- [Architecture](docs/architecture.md)
- [Application pipeline](docs/application-pipeline.md)
- [Versioning](docs/versioning.md)
- [Trade-offs](docs/trade-offs.md)

## Documentation

- [Quickstart](docs/quickstart.md)
- [Security model](docs/security-model.md)
- [Model gateway](docs/model-gateway.md)
- [Cost tracking](docs/cost-tracking.md)
- [Observability](docs/observability.md)
- [Code organization](docs/code-organization.md)

## Target Stack

- .NET 10 LTS.
- ASP.NET Core.
- PostgreSQL.
- Docker Compose.
- OpenAI-compatible model and embedding clients.
- Mock model and embedding clients for tests.
- xUnit, Testcontainers for integration tests.

## Project Structure

```text
src/
  IncidentCompass.Api
  IncidentCompass.Application
  IncidentCompass.Domain
  IncidentCompass.Infrastructure
  IncidentCompass.Worker
tests/
  IncidentCompass.UnitTests
  IncidentCompass.IntegrationTests
```

## Quickstart

See [docs/quickstart.md](docs/quickstart.md) for the full local setup and provider
override examples.

Minimal local path:

```powershell
Copy-Item .env.example .env
dotnet restore IncidentCompass.slnx
dotnet build IncidentCompass.slnx
dotnet test IncidentCompass.slnx
docker compose up -d postgres
```

Run the API and Worker in separate terminals. Set the connection string in each
terminal because PowerShell process environment variables are not shared across
new windows:

```powershell
$env:ConnectionStrings__IncidentCompass = "Host=localhost;Port=5432;Database=incidentcompass;Username=incidentcompass;Password=incidentcompass_dev_password"
dotnet run --project src/IncidentCompass.Api --launch-profile http
```

```powershell
$env:ConnectionStrings__IncidentCompass = "Host=localhost;Port=5432;Database=incidentcompass;Username=incidentcompass;Password=incidentcompass_dev_password"
dotnet run --project src/IncidentCompass.Worker
```

Sample HTTP requests (health check and current-user lookup) are available in
[src/IncidentCompass.Api/IncidentCompass.Api.http](src/IncidentCompass.Api/IncidentCompass.Api.http)
and [samples/http/local-demo.http](samples/http/local-demo.http).

## Relationship to dotnet-genai-starter

IncidentCompass was bootstrapped from the `dotnet-genai-starter` repo's patterns: its
layered .NET structure, its model/embedding gateway abstractions, and its governed
tool-execution/policy/audit primitive. It was then specialized into a single-purpose
incident-triage agent. Everything else from that starter kit — chat (direct, RAG, and
agentic), RAG document ingestion, evaluations, usage tracking, and MCP (both the local host
and the external-MCP client) — was stripped out because it doesn't belong to this product's
scope.

This is reference-quality, not production, software: no battle-testing, no SLA, and the
demo adapters (header-based demo auth, mock providers) are intentionally replaceable rather
than deployment-ready.
