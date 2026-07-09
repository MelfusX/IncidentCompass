# IncidentCompass

A governed incident-triage agent backend. It ingests a signal, an orchestrator delegates to scoped
workers under policy/audit/budget rails, and the system produces a grounded report.

This is reference-quality software, not a production system. This snapshot includes the Phase 0
repository bootstrap, Phase 1 intake, the Phase 2 governed investigation loop, Phase 3 governance
rails, Phase 4 memory worker, Phase 5 grounded report closeout and Phase 6 demo packaging: typed
triage configuration rehydration, a durable triage ledger, bounded Worker claims, analysis and
memory delegation, governed memory_search, backend-grounded publish_report persistence,
ledger/report readback, Docker Compose packaging and an HTTP-only Tester.

## What This Is

- A layered-monolith .NET 10 application using Clean Architecture and a lightweight internal
  application pipeline.
- A model/embedding gateway abstraction with OpenAI-compatible providers as the normal local/demo
  runtime path.
- Mock model and embedding adapters for automated tests and explicit mock-only local checks.
- A Phase 1 intake pipeline with POST /api/v1/incidents, GET /api/v1/faults/{id}, source normalizers,
  redaction, fingerprinting, fault grouping, triage-job creation and grounded intake artifacts.
- Governance rails for the investigation loop: typed tool schemas, role-scoped backend tool grants,
  backend policy decisions, a durable triage ledger writer and GET /api/v1/faults/{id}/ledger for
  readback.
- ModelCall ledger telemetry, grounded triage report readback, Docker Compose packaging, and generic
  dispatch/health/security/user scaffolding over PostgreSQL.

## What This Is Not

- Not a chat backend, a RAG/document-ingestion system, an evaluation harness, a usage dashboard, or
  an MCP host/client.
- Not a full framework with stable public extension contracts.
- Not production-ready as-is: demo auth, local credentials and local compose defaults are for local
  review only.
- Not a guarantee that any arbitrary local model will complete the multi-turn investigation well.
  The backend enforces grounding and governance; model quality still depends on the configured model.

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
    Infrastructure --> Providers["OpenAI-compatible providers"]
    Infrastructure --> TestProviders["Mock providers (tests only)"]
~~~

IncidentCompass.Application is organized by feature folder: Core/ (dispatcher, identity/correlation,
model/embedding gateway abstractions, options), Governance/ (triage ledger and worker policy helpers),
Intake/ (signal normalization, redaction, fingerprinting, fault grouping and triage-job orchestration),
Investigation/ (Worker claim/runtime orchestration, config rehydration and governed processing) and
Memory/ (memory search contracts, seed records and memory_search). Infrastructure implements
persistence and provider adapters. Api maps HTTP input/output only. Worker polls PostgreSQL, claims
bounded triage jobs and runs the configured orchestrator with only delegate and publish_report
available. Tester is a console HTTP client; it has no dependency on the application assemblies.

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
- Mock model and embedding clients for tests only.
- xUnit and Testcontainers for integration tests.

## Quickstart

Default local/demo runs expect an OpenAI-compatible chat endpoint and embeddings endpoint. For Docker
Compose on Windows/macOS, `host.docker.internal:1234` is the default host-side model server address.
Override model settings in `.env` when your local server uses different names.

~~~powershell
Copy-Item .env.example .env
# Edit INCIDENTCOMPASS_LLM_MODEL and INCIDENTCOMPASS_EMBEDDINGS_MODEL if your provider requires exact model ids.
# Optional collision overrides: IC_API_PORT=5298 and IC_POSTGRES_PORT=55432
powershell -ExecutionPolicy Bypass -File scripts/demo.ps1
~~~

That command builds and starts PostgreSQL, API and Worker containers, runs the HTTP-only Tester, and
prints host URLs for the fault ledger and triage report. `scripts/demo.ps1 -Mock` exists for automated
or deterministic checks only; it is not the normal product demo path.

See [docs/quickstart.md](docs/quickstart.md) for manual local setup and provider configuration.

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

Sample HTTP requests are available in
[src/IncidentCompass.Api/IncidentCompass.Api.http](src/IncidentCompass.Api/IncidentCompass.Api.http)
and [samples/http/local-demo.http](samples/http/local-demo.http).

## Relationship to dotnet-genai-starter

IncidentCompass was bootstrapped from the dotnet-genai-starter repo's patterns: its layered .NET
structure and model/embedding gateway abstractions. It was then specialized into a single-purpose
incident-triage agent. Chat, RAG document ingestion, evaluations, usage tracking and MCP product
surfaces from the starter kit were stripped out because they do not belong to this product's scope.
