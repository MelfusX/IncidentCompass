# Phase 2 Implementation Report

This report records the Phase 2 implementation state for the MVP plan. It is evidence for the four-unit split, the governed runtime shape, and the requested invariant checklist items. It is not a production-readiness claim and does not claim the Phase 5 grounded report pipeline.

## Runtime Shape

The Worker host polls PostgreSQL for claimable `triage_jobs`, bounded by `IncidentCompass:Worker:MaxConcurrentJobs`. `WorkerJobPump` fills available slots, and each claimed job is processed through `ITriageJobRunner`.

`TriageJobRunner` rehydrates the triage configuration through `ITriageConfigurationRepository.GetByHashAsync(job.ConfigHash)`. The Worker therefore uses the persisted `triage_config_snapshots` row attached to the job, not the Worker's startup config file.

The infrastructure host replaces the application placeholder processor with `GovernedTriageInvestigationProcessor`. That processor builds one orchestrator session using the snapshot's `Orchestrator.Instructions`, the grounded job context, and exactly two tool definitions: `delegate` and `publish_report`.

Delegation is sequential. On a `delegate` tool call, `AnalysisDelegateExecutor` validates the requested role against `configuration.Roles`, rejects unknown roles with `unknown_role`, rejects tool-bearing roles for Phase 2, calls the no-tool worker model session, validates the worker JSON against the role `OutputSchema`, parses the typed analysis output, stores an attempt-level `WorkerOutput` artifact, and returns a bounded summary to the orchestrator. The orchestrator then continues the same session until it receives `publish_report` or exhausts the Phase 2 turn bound.

`publish_report` is minimal in Phase 2. `MinimalTriageReportPublisher` parses the model's report payload, calls `IMinimalTriageReportRepository`, and appends a `ReportPublished` ledger event. `PostgresMinimalTriageReportRepository` writes or replaces one `triage_reports` row, clears the job lock, marks the job `Succeeded`, and marks the fault `Completed` or `InsufficientEvidence`. Full report evidence rows, grounding validation, backend-stamped report `is_mass_issue`, and ReportPublished-inside-final-tx are intentionally Phase 5.

## Delegate Enum

`OrchestratorToolDefinitions.Create(configuration)` generates the `delegate.role` JSON-schema enum from `configuration.Roles.Keys`. That enum is planning help for the model only. Backend validation still checks `configuration.Roles.TryGetValue(roleName, out role)` before executing delegation. Unknown role values fail closed and are returned as a tool result.

## Ledger Write Points

Ledger writes go through `PostgresTriageLedgerWriter`, which opens its own connection and transaction per append. It does not use `PostgresIntakeTransactionContext`, so ledger rows survive ambient intake rollbacks and mid-run failures.

Phase 2 writes these event types in the scripted analysis path:

- `Delegated`: after backend role validation accepts the `delegate` call and before the worker model call.
- `WorkerCompleted`: after worker output schema/typed validation and after the attempt-level `WorkerOutput` artifact is stored.
- `ReportPublished`: after the minimal report repository closes the job and fault.

The analysis role has no tools, so `ToolProposed`, `PolicyDecision`, and `ToolResult` are not emitted in this Phase 2 path. Those events remain reserved for later tool-bearing worker roles.

## Smoke Measurement

The deterministic mock path reaches `publish_report` in the integration test `TriageInvestigationLoopTests.ProcessClaimedAsync_ScriptedMockDelegatesAnalysisAndPublishesMinimalReport`.

A non-gated real-local-LLM smoke harness exists in `scripts/phase2-real-local-llm-smoke.ps1` and `TriageInvestigationRealLlmSmokeTests`. It is opt-in through `INCIDENTCOMPASS_LLM_SMOKE_ENABLED=true` and records a reach-rate result to `docs/phase-2-real-llm-smoke-result.md`.

Current recorded result: executed against LM Studio at `http://localhost:1234` using `qwen2.5-14b-instruct`. Reach-rate was `0/3`: all three runs reached the analysis worker but failed validation before `publish_report` because the model returned non-string `keyFacts` array items. This is the plan's expected bad-smoke case: `qwen2.5-14b-instruct` is below the reference-model floor for the current multi-turn trajectory. The pre-agreed fallback remains to either raise the documented reference-model floor, collapse weak local models to a single-shot report path, or add a bounded delegation-retry/repair path in a later phase.

## Commit Units

Unit 1 - typed triage configuration and snapshot rehydration:

- `src/IncidentCompass.Application/Intake/Configuration/ITriageConfigurationRepository.cs`
- `src/IncidentCompass.Application/Intake/Configuration/TriageConfiguration.cs`
- `src/IncidentCompass.Application/Intake/Configuration/OrchestratorBudgetSettings.cs`
- `src/IncidentCompass.Application/Intake/Configuration/OrchestratorSettings.cs`
- `src/IncidentCompass.Application/Intake/Configuration/TriageProviderSettings.cs`
- `src/IncidentCompass.Application/Intake/Configuration/TriageRoleSettings.cs`
- `src/IncidentCompass.Application/Intake/Configuration/TriageRouteSettings.cs`
- `src/IncidentCompass.Application/Intake/Configuration/TriageRuleSettings.cs`
- `src/IncidentCompass.Application/Intake/Configuration/TriageToolSettings.cs`
- `src/IncidentCompass.Infrastructure/Intake/FileTriageConfigurationRepository.cs`
- `src/IncidentCompass.Infrastructure/Intake/SerializedTriageConfiguration.cs`
- `src/IncidentCompass.Infrastructure/Intake/TriageConfigurationLoadException.cs`
- `src/IncidentCompass.Infrastructure/Intake/TriageConfigurationLoadValidator.cs`
- `src/IncidentCompass.Infrastructure/Intake/TriageConfigurationMaterializer.cs`
- `src/IncidentCompass.Infrastructure/Intake/TriageConfigurationSnapshotDocument.cs`
- `src/IncidentCompass.Infrastructure/Intake/TriageConfigurationSnapshotStore.cs`
- `src/IncidentCompass.Infrastructure/Intake/TriageConfigurationValidationGuards.cs`
- `tests/IncidentCompass.UnitTests/TriageConfigurationMaterializerTests.cs`
- `tests/IncidentCompass.UnitTests/TestTriageConfiguration.cs`
- `tests/IncidentCompass.IntegrationTests/IncidentIngestionTests.cs`

Unit 2 - durable triage ledger:

- `infra/postgres/init/008-triage-ledger.sql`
- `src/IncidentCompass.Application/Governance/Ledger/ITriageLedgerWriter.cs`
- `src/IncidentCompass.Application/Governance/Ledger/TriageLedgerAppendRequest.cs`
- `src/IncidentCompass.Domain/Incidents/TriageLedgerEntry.cs`
- `src/IncidentCompass.Domain/Incidents/Statuses/TriageLedgerDecision.cs`
- `src/IncidentCompass.Domain/Incidents/Statuses/TriageLedgerEventType.cs`
- `src/IncidentCompass.Infrastructure/Governance/PostgresTriageLedgerWriter.cs`
- `tests/IncidentCompass.IntegrationTests/PostgresTriageLedgerWriterTests.cs`

Unit 3 - Worker claim loop, lease, attempts and concurrency:

- `src/IncidentCompass.Application/Investigation/InvestigationSetup.cs`
- `src/IncidentCompass.Application/Investigation/Jobs/ITriageJobRunner.cs`
- `src/IncidentCompass.Application/Investigation/Jobs/ITriageJobRuntimeRepository.cs`
- `src/IncidentCompass.Application/Investigation/Jobs/IClaimedTriageJobProcessor.cs`
- `src/IncidentCompass.Application/Investigation/Jobs/DeferredClaimedTriageJobProcessor.cs`
- `src/IncidentCompass.Application/Investigation/Jobs/TriageJobAttemptFailure.cs`
- `src/IncidentCompass.Application/Investigation/Jobs/TriageJobProcessingSettings.cs`
- `src/IncidentCompass.Application/Investigation/Jobs/TriageJobRunner.cs`
- `src/IncidentCompass.Infrastructure/Intake/PostgresTriageJobMapper.cs`
- `src/IncidentCompass.Infrastructure/Intake/PostgresTriageJobRuntimeRepository.cs`
- `src/IncidentCompass.Worker/Worker.cs`
- `src/IncidentCompass.Worker/WorkerJobPump.cs`
- `src/IncidentCompass.Worker/WorkerOptions.cs`
- `src/IncidentCompass.Worker/WorkerOptionsValidator.cs`
- `src/IncidentCompass.Worker/appsettings.json`
- `src/IncidentCompass.Worker/appsettings.Development.json`
- `tests/IncidentCompass.IntegrationTests/PostgresTriageJobRuntimeRepositoryTests.cs`
- `tests/IncidentCompass.IntegrationTests/WorkerJobPumpTests.cs`

Unit 4 - orchestrator, analysis worker, minimal publish_report, scripted mock, smoke harness:

- `infra/postgres/init/009-triage-reports-minimal.sql`
- `src/IncidentCompass.Application/Investigation/Jobs/AnalysisDelegateExecutor.cs`
- `src/IncidentCompass.Application/Investigation/Jobs/AnalysisWorkerOutput.cs`
- `src/IncidentCompass.Application/Investigation/Jobs/AnalysisWorkerOutputParser.cs`
- `src/IncidentCompass.Application/Investigation/Jobs/AnalysisWorkerOutputSchemaValidator.cs`
- `src/IncidentCompass.Application/Investigation/Jobs/GovernedTriageInvestigationProcessor.cs`
- `src/IncidentCompass.Application/Investigation/Jobs/ITriageJobInvestigationContextRepository.cs`
- `src/IncidentCompass.Application/Investigation/Jobs/MinimalTriageReportParser.cs`
- `src/IncidentCompass.Application/Investigation/Jobs/MinimalTriageReportPublisher.cs`
- `src/IncidentCompass.Application/Investigation/Jobs/OrchestratorToolDefinitions.cs`
- `src/IncidentCompass.Application/Investigation/Jobs/TriageInvestigationPromptBuilder.cs`
- `src/IncidentCompass.Application/Investigation/Jobs/TriageJobInvestigationContext.cs`
- `src/IncidentCompass.Application/Investigation/Jobs/TriageLedgerAppender.cs`
- `src/IncidentCompass.Application/Investigation/Reports/IMinimalTriageReportRepository.cs`
- `src/IncidentCompass.Application/Investigation/Reports/MinimalTriageReport.cs`
- `src/IncidentCompass.Application/Investigation/Reports/MinimalTriageReportStatus.cs`
- `src/IncidentCompass.Infrastructure/Investigation/PostgresMinimalTriageReportRepository.cs`
- `src/IncidentCompass.Infrastructure/Investigation/PostgresTriageJobInvestigationContextRepository.cs`
- `src/IncidentCompass.Infrastructure/ModelGateway/Mock/MockAiModelClient.cs`
- `scripts/phase2-real-local-llm-smoke.ps1`
- `tests/IncidentCompass.IntegrationTests/TriageInvestigationLoopTests.cs`
- `tests/IncidentCompass.IntegrationTests/TriageInvestigationRealLlmSmokeTests.cs`
- `docs/phase-2-real-llm-smoke-result.md`

Shared wiring files that need hunk staging:

- `src/IncidentCompass.Application/Setup.cs`
- `src/IncidentCompass.Application/Intake/IntakeSetup.cs`
- `src/IncidentCompass.Infrastructure/Setup.cs`
- `src/IncidentCompass.Infrastructure/Intake/IntakeSetup.cs`
- `src/IncidentCompass.Worker/Program.cs`
- `src/IncidentCompass.Worker/Setup.cs`
- `tests/IncidentCompass.IntegrationTests/HostCompositionTests.cs`
- `tests/IncidentCompass.IntegrationTests/MemoryOnlyCompositionTests.cs`
- `tests/IncidentCompass.UnitTests/ArchitectureTests.cs`
- public docs touched by multiple units: `README.md`, `docs/architecture.md`, `docs/code-organization.md`, `docs/local-demo.md`, `docs/quickstart.md`, `docs/security-model.md`

## Invariant Checklist Status

| Item | Status | Evidence |
| --- | --- | --- |
| 1. Orchestrator surface | PASS | `OrchestratorToolDefinitions` exposes only `delegate` and `publish_report`; `AnalysisDelegateExecutor` backend-validates the role and returns `unknown_role` for unknown roles. |
| 2. Sequential delegation | PASS | `GovernedTriageInvestigationProcessor` handles one proposed tool call per turn, awaits delegate completion, appends the tool result, and then continues the orchestrator loop. There is no parallel fan-out. |
| 3. Ledger durability and DB order | PASS for Phase 2 scope | `008-triage-ledger.sql` uses an identity `id` and `(job_id, id)` ordering. `PostgresTriageLedgerWriter` commits each append through its own connection/transaction. The Phase 5 subclause that places `ReportPublished` inside the final report transaction is intentionally not claimed here; Phase 2 uses a plain `ReportPublished` ledger event after minimal closeout, matching the Phase 2 goal text. |
| 7. Backend-stamped mass issue and report model boundaries | PASS for touched Phase 2 behavior | Intake keeps `isMassIssue` in artifacts/backend state. `MinimalTriageReportParser` accepts only minimal model fields and rejects invalid `Completed`/`Unknown` and `InsufficientEvidence` classification combinations. The report row leaves `is_mass_issue` null in Phase 2. |
| 10. Typed routes and worker output validation | PASS | Config load validation enforces role chat routes and tool embedding routes. `AnalysisWorkerOutputSchemaValidator` validates worker JSON against the role `OutputSchema`, and `AnalysisWorkerOutputParser` validates the typed Phase 2 shape. |
| 11. Consumer-only | PASS for Phase 2 | The orchestrator has no RAG/MCP/write tools. The only live worker role is no-tool `analysis`; tool-bearing roles return `role_tools_not_supported_in_phase_2`. |
| 12. Determinism | PASS for CI/demo path | `MockAiModelClient` scripts the `delegate` then `publish_report` sequence and returns deterministic analysis JSON. The real-local smoke is non-gated and separately recorded. |
| 18. Post-Phase-1 sync | PASS | Config model now includes Providers/Routes/Roles/Tools/Orchestrator/Rules and `GetByHashAsync`; Worker uses snapshot rehydration by `job.config_hash`; ledger writer bypasses the ambient intake transaction; the real-local-LLM smoke executed and recorded a `0/3` `publish_report` reach-rate. |

## Verification

Latest local verification after adding the opt-in smoke harness:

- `dotnet build IncidentCompass.slnx`
- `dotnet test IncidentCompass.slnx --no-build`
- `dotnet format IncidentCompass.slnx --verify-no-changes --verbosity minimal`
- `powershell -ExecutionPolicy Bypass -File scripts\code-organization-gate.ps1`
- `powershell -ExecutionPolicy Bypass -File scripts\package-vulnerability-gate.ps1`
- `$env:INCIDENTCOMPASS_REQUIRE_DOCKER_TESTS = "true"; dotnet test tests\IncidentCompass.IntegrationTests\IncidentCompass.IntegrationTests.csproj --no-build`
- `powershell -ExecutionPolicy Bypass -File scripts\phase2-real-local-llm-smoke.ps1 -BaseUrl http://localhost:1234 -Model qwen2.5-14b-instruct -Runs 3 -TimeoutSeconds 180`

The opt-in real-local-LLM smoke is skipped by default during normal gates. Re-run it with:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\phase2-real-local-llm-smoke.ps1 -BaseUrl http://localhost:1234 -Model <local-model-name>
```
