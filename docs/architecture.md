# Architecture

This project is a .NET-native governed incident-triage agent backend. It demonstrates production-aware patterns, but it is not a framework with stable public extension contracts.

The implementation is a layered monolith. The application layer is a single project organized into folders so the codebase stays easy to navigate as new capabilities are added (the layering is by convention and `ArchitectureTests`, not enforced module assemblies):

```mermaid
flowchart LR
    Client["Client / API consumer"] --> Api["IncidentCompass.Api"]
    Api --> Application["IncidentCompass.Application"]
    Worker["IncidentCompass.Worker"] --> Application
    Application --> Domain["IncidentCompass.Domain"]
    Infrastructure["IncidentCompass.Infrastructure"] --> Application
    Infrastructure --> Postgres["PostgreSQL"]
    Infrastructure --> Providers["Mock or OpenAI-compatible providers"]
```

## Projects

- `IncidentCompass.Api`: HTTP endpoints, OpenAPI, demo auth adapter, request/response mapping.
- `IncidentCompass.Application`: single application project with two top-level folders:
  - `Core/`: dispatcher, pipeline behaviors, identity/correlation contracts, shared configuration, base errors, health echo, current-user use case, and model/embedding gateway abstractions.
  - `Governance/`: backend-governed tool-execution contracts, tool policy/audit orchestration and validation primitives. This subsystem is currently dead code with no caller; a future phase wires a new caller into it.
- `IncidentCompass.Domain`: simple domain records, enums and workflow state types shared by Application use cases.
- `IncidentCompass.Infrastructure`: PostgreSQL persistence adapters, model clients, embedding clients, sanitized AI request logging, pricing/cost estimation and other adapters.
- `IncidentCompass.Worker`: DB-backed background job host.

## Rules

- Domain must not depend on Application, Infrastructure, Api, Worker, provider SDKs or persistence libraries.
- Application owns use-case contracts, ports, orchestration, validation policies and pipeline behavior.
- Infrastructure implements application ports and owns the observability mechanism, including sanitized request logging and pricing/cost estimation.
- API and Worker hosts should call application use cases instead of duplicating orchestration.
- Provider SDKs must not appear in controllers or use-case handlers.
- Keep the system a layered monolith for this project's scope.

## Style

Use Clean Architecture with Domain-owned records and enums for shared workflow concepts, and Application-owned orchestration, validation policies and pipeline behavior. FluentValidation is the request-validation framework, and the internal dispatcher runs pipeline behaviors for cross-cutting concerns such as request logging and validation before handlers execute. Use CQRS-lite where it improves clarity, but avoid separate read/write stores, event sourcing and ceremony that does not serve the project.

Follow `docs/code-organization.md` for maintainability guardrails. In short: keep classes small, keep one entity per file, split unrelated responsibilities, and keep application handlers focused on use-case orchestration.
