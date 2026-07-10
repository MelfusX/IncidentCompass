# IncidentCompass

A governed incident-triage agent backend. It ingests a signal, an orchestrator delegates to scoped
workers under policy/audit/budget rails, and the system produces a grounded report.

This is reference-quality software, not a production system. This snapshot includes the Phase 0
repository bootstrap, Phase 1 intake, the Phase 2 governed investigation loop, Phase 3 governance
rails, Phase 4 memory worker, Phase 5 grounded report closeout and Phase 6 demo packaging: typed
triage configuration rehydration, a durable triage ledger, a bounded Worker claim loop,
deterministic analysis and memory delegation, governed memory_search, backend-grounded
publish_report persistence, ledger/report readback, Docker Compose packaging and a deterministic
Tester-driven local demo.

## What This Is

- A layered-monolith .NET 10 skeleton using Clean Architecture and a lightweight internal
  application pipeline.
- A model/embedding gateway abstraction with deterministic mock providers (default) and
  OpenAI-compatible adapters behind the same ports.
- A Phase 1 intake pipeline (Intake) with POST /api/v1/incidents, GET /api/v1/faults/{id}, source
  normalizers, redaction, fingerprinting, fault grouping, triage-job creation and grounded intake
  artifacts.
- Governance rails for the investigation loop: typed tool schemas, role-scoped backend tool grants,
  backend policy decisions, a durable triage ledger writer and GET /api/v1/faults/{id}/ledger for
  readback. The Worker path records Delegated, WorkerCompleted, ToolProposed, PolicyDecision,
  ToolResult and same-transaction ReportPublished events.
- ModelCall ledger telemetry, grounded triage report readback, deterministic Docker demo packaging,
  and generic dispatch/health/security/user scaffolding over PostgreSQL.

## What This Is Not

- Not a chat backend, a RAG/document-ingestion system, an evaluation harness, a usage dashboard, or
  an MCP host/client. All of that existed in the upstream starter kit and was deliberately removed
  here.
- Not a full framework with stable public extension contracts.
- Not production-ready as-is: demo auth and mock providers are intentionally replaceable adapters
  with no battle-testing and no SLA.
- Not proof that a local LLM can reliably complete the multi-turn investigation loop. The gated demo
  uses scripted mock providers to prove the backend rails and packaging; optional real-LLM smoke runs
  are separate evidence.

## Architecture

~~~mermaid
flowchart LR
    Client["Client / API consumer"] --> Api["IncidentCompass.Api"]
    Tester["IncidentCompass.Tester"] --> Api
    Api --> App["IncidentCompass.Application"]
    Worker["IncidentCompass.Worker"] --> App
    App --> Domain["IncidentCompass.Domain"]
    Infrastructure["IncidentCompass.Infrastructure"] --> App
    Infrastructure --> Postgres["PostgreSQL"]
    Infrastructure --> Providers["Mock or OpenAI-compatible providers"]
~~~

IncidentCompass.Application is a single project organized by feature folder: Core/ (dispatcher,
identity/correlation, model/embedding gateway abstractions, options), Governance/ (triage
ledger and worker policy helpers), Intake/ (signal normalization, redaction, fingerprinting, fault
grouping and triage-job orchestration), Investigation/ (Worker claim/runtime orchestration, config
rehydration and the governed processor) and Memory/ (memory search contracts, seed records and
memory_search) are populated today. Infrastructure implements persistence and provider adapters. Api
maps HTTP input/output only. Worker polls PostgreSQL, claims bounded triage jobs and runs the
configured orchestrator with only delegate and publish_report available. Tester is a console HTTP
client for the deterministic local demo; it has no dependency on the application assemblies. The
memory role can call only the governed memory_search worker tool. publish_report can cite only
backend-grounded artifacts; the backend derives mass-issue state and evidence kind.

Start here:

- [Architecture](docs/architecture.md)
- [Application pipeline](docs/application-pipeline.md)
- [Versioning](docs/versioning.md)
- [Trade-offs](docs/trade-offs.md)

## Documentation

- [Quickstart](docs/quickstart.md)
- [Local demo walkthrough](docs/local-demo.md)
- [Security model](docs/security-model.md)
- [Model gateway](docs/model-gateway.md)
- [Cost tracking](docs/cost-tracking.md)
- [Observability](docs/observability.md)
- [Code organization](docs/code-organization.md)
- [Phase 2 implementation report](docs/phase-2-implementation-report.md)
- [Phase 3 implementation report](docs/phase-3-implementation-report.md)

## Target Stack

- .NET 10 LTS.
- ASP.NET Core.
- PostgreSQL with pgvector.
- Docker Compose.
- OpenAI-compatible model and embedding clients.
- Mock model and embedding clients for tests and the deterministic demo.
- xUnit, Testcontainers for integration tests.

## Project Structure

~~~text
src/
  IncidentCompass.Api
  IncidentCompass.Application
  IncidentCompass.Domain
  IncidentCompass.Infrastructure
  IncidentCompass.Tester
  IncidentCompass.Worker
tests/
  IncidentCompass.UnitTests
  IncidentCompass.IntegrationTests
~~~

## Quickstart

Recommended deterministic demo path:

~~~powershell
# Optional collision overrides: IC_API_PORT=5298 and IC_POSTGRES_PORT=55432
powershell -ExecutionPolicy Bypass -File scripts/demo.ps1
~~~

That command builds and starts PostgreSQL, API and Worker containers, runs the HTTP-only Tester
against all four local scenarios, and prints host URLs for the fault ledger and triage report. See
[docs/local-demo.md](docs/local-demo.md) for -NoBuild, -RealLlm, service layout and the honesty
caveats around scripted mocks versus optional real-LLM runs.

See [docs/quickstart.md](docs/quickstart.md) for manual local setup and provider override examples.

Minimal manual path:

~~~powershell
Copy-Item .env.example .env
dotnet restore IncidentCompass.slnx
dotnet build IncidentCompass.slnx
dotnet test IncidentCompass.slnx
docker compose up -d postgres
~~~

Run the API and Worker in separate terminals. Set the connection string in each terminal because
PowerShell process environment variables are not shared across new windows:

~~~powershell
$env:ConnectionStrings__IncidentCompass = "Host=localhost;Port=5432;Database=incidentcompass;Username=incidentcompass;Password=incidentcompass_dev_password"
dotnet run --project src/IncidentCompass.Api --launch-profile http
~~~

~~~powershell
$env:ConnectionStrings__IncidentCompass = "Host=localhost;Port=5432;Database=incidentcompass;Username=incidentcompass;Password=incidentcompass_dev_password"
dotnet run --project src/IncidentCompass.Worker
~~~

Sample HTTP requests (health check, current-user lookup, incident ingestion, source rejection and
fault lookup) are available in
[src/IncidentCompass.Api/IncidentCompass.Api.http](src/IncidentCompass.Api/IncidentCompass.Api.http)
and [samples/http/local-demo.http](samples/http/local-demo.http).

## Relationship to dotnet-genai-starter

IncidentCompass was bootstrapped from the dotnet-genai-starter repo's patterns: its layered .NET
structure and its model/embedding gateway abstractions. It was then specialized into a single-purpose incident-triage agent. Everything else from
that starter kit - chat (direct, RAG, and agentic), RAG document ingestion, evaluations, usage
tracking, and MCP (both the local host and the external-MCP client) - was stripped out because it
doesn't belong to this product's scope.

This is reference-quality, not production, software: no battle-testing, no SLA, and the demo adapters
(header-based demo auth, mock providers) are intentionally replaceable rather than deployment-ready.
