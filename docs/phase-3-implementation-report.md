# Phase 3 Implementation Report

Phase 3 adds the governance rails around the Phase 2 investigation loop: bounded reprompts, ledger-backed worker tool policy, per-attempt budget accounting, deeper config validation and config-snapshot regression coverage. This remains reference-quality software; the shipped config still has no live worker tool implementation until Phase 4 memory search lands.

## Review Fixes Folded In

- Rule scope validation now rejects every configured `Rules[]` scope except `attempt` and `job`. A configured `fault` scope and a bogus scope both fail load-time validation with an error on `Rules.<type>.Scope` that names the allowed values. `PostgresTriageLedgerReader` still contains the post-MVP `fault` predicate mechanics, but they are unreachable from config.
- `ToolResult` success is first-class ledger state. `infra/postgres/init/008-triage-ledger.sql` was edited in place because this is pre-release, has no deployments, and has no migration framework. The ledger has nullable `tool_status text` constrained to `Succeeded` or `Failed`, with NULL for non-`ToolResult` events. Successful tool commits and failed/non-executed tool results set the column, and `HasSuccessfulToolResultAsync` queries it directly.
- `ToolResult.rationale` is human-readable justification again. It no longer carries status JSON on the write path, and the read path no longer parses `ToolResult` rationale for success.
- `BudgetEvent` now mirrors the `tool_status` pattern. `tokens_delta` and `workers_delta` are nullable first-class ledger columns, only `BudgetEvent` rows may carry them, budget emitters write them directly, and `ReadBudgetUsageAsync` sums those columns instead of parsing rationale. `BudgetEvent.rationale` is only an optional human note.
- `CHANGELOG.md` covers Phase 2 and Phase 3, including the non-gated local smoke turnaround from the Phase 2 `qwen2.5-14b-instruct` baseline to Phase 3 `3/3` completions with `MaxReprompts: 2`.

## Final Commit-Unit Split

Unit 1 - bounded reprompt and smoke hardening:

- `config/incidentcompass.config.json`
- `config/instructions/analysis.md`
- `scripts/phase2-real-local-llm-smoke.ps1`
- `src/IncidentCompass.Application/Intake/Configuration/OrchestratorBudgetSettings.cs`
- `src/IncidentCompass.Application/Investigation/Jobs/GovernedTriageInvestigationProcessor.cs` (hunk-stage shared file)
- `src/IncidentCompass.Application/Investigation/Jobs/WorkerRoleRunner.cs` (hunk-stage shared file)
- `tests/IncidentCompass.IntegrationTests/Fixtures/test-triage-config/incidentcompass.config.json`
- `tests/IncidentCompass.IntegrationTests/Fixtures/test-triage-config/instructions/analysis.md`
- `tests/IncidentCompass.IntegrationTests/TriageInvestigationLoopTests.cs` (hunk-stage shared file)
- `tests/IncidentCompass.IntegrationTests/TriageInvestigationRealLlmSmokeTests.cs`
- `docs/phase-3-real-llm-smoke-result.md`
- `docs/phase-3-real-llm-smoke-result-qwen36-27b.md`

Unit 2 - governed worker tool path and first-class `ToolResult` status:

- `infra/postgres/init/008-triage-ledger.sql` (hunk-stage shared file)
- `src/IncidentCompass.Application/AssemblyInfo.cs`
- `src/IncidentCompass.Application/Governance/Ledger/ITriageLedgerReader.cs`
- `src/IncidentCompass.Application/Governance/Ledger/TriageLedgerAppendRequest.cs` (hunk-stage shared file)
- `src/IncidentCompass.Application/Investigation/InvestigationSetup.cs`
- `src/IncidentCompass.Application/Investigation/Jobs/AnalysisDelegateExecutor.cs` (hunk-stage shared file)
- `src/IncidentCompass.Application/Investigation/Jobs/GovernedTriageInvestigationProcessor.cs` (hunk-stage shared file)
- `src/IncidentCompass.Application/Investigation/Jobs/ITriageToolResultCommitFaultInjector.cs`
- `src/IncidentCompass.Application/Investigation/Jobs/ITriageToolResultCommitter.cs`
- `src/IncidentCompass.Application/Investigation/Jobs/NoopTriageToolResultCommitFaultInjector.cs`
- `src/IncidentCompass.Application/Investigation/Jobs/TriageLedgerAppender.cs` (hunk-stage shared file)
- `src/IncidentCompass.Application/Investigation/Jobs/TriageToolResultCommitRequest.cs`
- `src/IncidentCompass.Application/Investigation/Jobs/WorkerRoleRunner.cs` (hunk-stage shared file)
- `src/IncidentCompass.Application/Investigation/Jobs/WorkerToolCallExecutor.cs`
- `src/IncidentCompass.Application/Investigation/Jobs/WorkerToolPolicyResult.cs`
- `src/IncidentCompass.Application/Governance/Tools/ToolRuleEngine.cs` (the Phase 3 Worker engine was
  later extracted here for shared immediate-read and post-report use)
- `src/IncidentCompass.Domain/Incidents/Statuses/TriageLedgerToolStatus.cs`
- `src/IncidentCompass.Domain/Incidents/TriageLedgerEntry.cs` (hunk-stage shared file)
- `src/IncidentCompass.Infrastructure/Governance/PostgresTriageLedgerReader.cs` (hunk-stage shared file)
- `src/IncidentCompass.Infrastructure/Governance/PostgresTriageLedgerWriter.cs` (hunk-stage shared file)
- `src/IncidentCompass.Infrastructure/Investigation/PostgresTriageToolResultCommitter.cs`
- `src/IncidentCompass.Infrastructure/Setup.cs` (hunk-stage shared file)
- `tests/IncidentCompass.IntegrationTests/GovernedWorkerToolPathTests.cs`
- `tests/IncidentCompass.IntegrationTests/PostgresGovernanceLedgerReaderTests.cs` (hunk-stage shared file)
- `tests/IncidentCompass.IntegrationTests/PostgresTriageLedgerWriterTests.cs` (hunk-stage shared file)

Suggested commit-message note for this unit: `008-triage-ledger.sql` is edited in place because the project is pre-release, has no deployments, and has no migration framework.

Unit 3 - per-attempt budget, termination and context-window guard:

- `infra/postgres/init/008-triage-ledger.sql` (hunk-stage shared file)
- `src/IncidentCompass.Application/Governance/Ledger/TriageBudgetLedgerUsage.cs`
- `src/IncidentCompass.Application/Governance/Ledger/TriageLedgerAppendRequest.cs` (hunk-stage shared file)
- `src/IncidentCompass.Application/Investigation/Jobs/AnalysisDelegateExecutor.cs` (hunk-stage shared file)
- `src/IncidentCompass.Application/Investigation/Jobs/GovernedTriageInvestigationProcessor.cs` (hunk-stage shared file)
- `src/IncidentCompass.Application/Investigation/Jobs/InvestigationModelCaller.cs`
- `src/IncidentCompass.Application/Investigation/Jobs/TriageJobCallContext.cs`
- `src/IncidentCompass.Application/Investigation/Jobs/TriageLedgerAppender.cs` (hunk-stage shared file)
- `src/IncidentCompass.Application/Investigation/Jobs/TriageTokenEstimator.cs`
- `src/IncidentCompass.Application/Investigation/Jobs/WorkerRoleRunner.cs` (hunk-stage shared file)
- `src/IncidentCompass.Domain/Incidents/TriageLedgerEntry.cs` (hunk-stage shared file)
- `src/IncidentCompass.Infrastructure/Governance/PostgresTriageLedgerReader.cs` (hunk-stage shared file)
- `src/IncidentCompass.Infrastructure/Governance/PostgresTriageLedgerWriter.cs` (hunk-stage shared file)
- `src/IncidentCompass.Infrastructure/Setup.cs` (hunk-stage shared file)
- `tests/IncidentCompass.IntegrationTests/BoundedRepromptAndBudgetTests.cs`
- `tests/IncidentCompass.IntegrationTests/PostgresGovernanceLedgerReaderTests.cs` (hunk-stage shared file)
- `tests/IncidentCompass.IntegrationTests/PostgresTriageLedgerWriterTests.cs` (hunk-stage shared file)
- `tests/IncidentCompass.IntegrationTests/TriageInvestigationLoopTests.cs` (hunk-stage shared file)

Unit 4 - load validation depth, snapshot tests and docs:

- `CHANGELOG.md`
- `README.md`
- `docs/architecture.md`
- `docs/model-gateway.md`
- `docs/phase-3-implementation-report.md`
- `docs/security-model.md`
- `docs/trade-offs.md`
- `src/IncidentCompass.Infrastructure/Intake/TriageConfigurationLoadValidator.cs`
- `src/IncidentCompass.Infrastructure/Intake/TriageRuleLoadValidator.cs`
- `tests/IncidentCompass.IntegrationTests/ConfigSnapshotHashTests.cs`
- `tests/IncidentCompass.UnitTests/TriageConfigurationMaterializerTests.cs`

Review fixes 1 and 3 belong in unit 4. Review fix 2 belongs in unit 2. The BudgetEvent delta-column mirror belongs in unit 3. No fifth commit is needed.

Shared files that need hunk-staging rather than whole-file staging:

- `infra/postgres/init/008-triage-ledger.sql`
- `src/IncidentCompass.Application/Governance/Ledger/TriageLedgerAppendRequest.cs`
- `src/IncidentCompass.Application/Investigation/Jobs/GovernedTriageInvestigationProcessor.cs`
- `src/IncidentCompass.Application/Investigation/Jobs/AnalysisDelegateExecutor.cs`
- `src/IncidentCompass.Application/Investigation/Jobs/TriageLedgerAppender.cs`
- `src/IncidentCompass.Application/Investigation/Jobs/WorkerRoleRunner.cs`
- `src/IncidentCompass.Domain/Incidents/TriageLedgerEntry.cs`
- `src/IncidentCompass.Infrastructure/Governance/PostgresTriageLedgerReader.cs`
- `src/IncidentCompass.Infrastructure/Governance/PostgresTriageLedgerWriter.cs`
- `src/IncidentCompass.Infrastructure/Setup.cs`
- `tests/IncidentCompass.IntegrationTests/PostgresGovernanceLedgerReaderTests.cs`
- `tests/IncidentCompass.IntegrationTests/PostgresTriageLedgerWriterTests.cs`
- `tests/IncidentCompass.IntegrationTests/TriageInvestigationLoopTests.cs`

## Decision Reads

The rule engine reads durable ledger state through `ITriageLedgerReader`; it does not inspect chat history and does not consume `ToolProposed` or `ModelCall` for decisions.

- Scope `attempt`: `job_id = @job_id AND attempt = @attempt`.
- Scope `job`: `job_id = @job_id`.
- Scope `fault`: `fault_id = @fault_id` remains implemented in the reader for post-MVP work but is rejected by config load validation in MVP.

`rate_cap` counts prior `PolicyDecision` rows for the target worker tool and `decision = Allowed` in the configured scope. `precondition` checks for a prior `ToolResult` for the required tool whose `tool_status = Succeeded` in the configured scope. Budget reads only current-attempt `BudgetEvent` rows and sums `tokens_delta` and `workers_delta`; it does not parse `BudgetEvent.rationale`.

Rules apply only to worker tools. `delegate` and `publish_report` remain control-plane tools and are governed by the orchestrator budget, not by `Rules` or `Tool: "*"`.

## Load Validation

Config load validation fails fast for invalid Phase 3 rule scope. `attempt` and `job` are the only supported configured scopes; `fault` is intentionally deferred to post-MVP per the MVP plan. The validation error names the rule path and the allowed values.

Phase 3 load validation also verifies route references, provider kind/route kind combinations, role instructions and schemas, configured worker tools, rule tool references, rule type-specific required fields and non-negative `MaxReprompts`. Snapshot-hash tests prove instruction/schema/config text contributes to the persisted config hash.

## Reprompt And Budget

`Orchestrator.Budget.MaxReprompts` is the triage-config knob. It is part of the config snapshot and defaults to 1 for older snapshots. A schema-invalid worker output and an orchestrator turn with no tool call both receive a validation-error reprompt at most `MaxReprompts` times. Each reprompt is a normal model call through `InvestigationModelCaller`, so it writes `ModelCall` plus `BudgetEvent` and charges the current attempt budget.

Token accounting uses provider `Usage.TotalTokens` when present. If usage is absent, the backend records an estimate from prompt, tool schema and response size, with `usageSource = estimate` in the `ModelCall` metadata. Charge rows write `tokens_delta`; `BudgetEvent.rationale` is a human-readable note. `MaxTokens` is checked before starting each call; one-call overshoot is possible and is recorded with a `max_tokens_overshot_after_call` `BudgetEvent`. `MaxWallClockSeconds` is checked before calls and passed into model calls via a linked cancellation token. A prompt-size estimate is checked against the route's `ContextWindowTokens`; overflow denies the call with a `context_window_exceeded` `BudgetEvent`.

`MaxWorkers` is now a budget counter. Each accepted delegation writes a current-attempt `BudgetEvent` with `workers_delta = 1`; retries start fresh because budget reads are attempt-scoped.

## Tool Path

A worker role receives only registered immediate-tool definitions that are both present in the config `Tools` section and granted by that role. A proposed unknown, unregistered or ungranted tool writes `ToolProposed`, writes a denied `PolicyDecision`, records a failed `ToolResult` with human-readable rationale, and fails closed. The later action-gate extraction reserves `requires_approval` for exact external-action tool ids; external actions never enter this Worker surface.

Successful worker tool execution writes a `ToolResult` artifact and the matching `ToolResult` ledger event in one PostgreSQL transaction through `PostgresTriageToolResultCommitter`. The ledger event records `tool_status = Succeeded`; failed/non-executed tool calls record `tool_status = Failed`. The artifact plus `ToolResult` event are the only intentional exception to the ledger's own-commit path. A test-only fault-injection seam proves a simulated crash after artifact insert and before ledger insert rolls both back.

Synthetic `tool_x` and `tool_y` exist only in integration-test composition. They are never resolvable from the shipped config unless a test-specific config registers them.

## Real-LLM Smoke

Recorded Phase 2 baseline from `plan-review.md` section 0p:

- `qwen2.5-14b-instruct`: 0/3, failed on `keyFacts[0] must be string`.
- `qwen/qwen3.6-27b`: 0/1, emitted no tool call after long thinking.

Phase 3 re-measurement:

- `docs/phase-3-real-llm-smoke-result.md`: `qwen2.5-14b-instruct` reached `publish_report` 3/3 with `MaxReprompts: 2`.
- `docs/phase-3-real-llm-smoke-result-qwen36-27b.md`: `qwen/qwen3.6-27b` reached `publish_report` 1/1.

The smoke fixture now uses explicit plain-string `keyFacts`, `/no_think` prompt guidance and larger route output caps. These are opt-in local smoke results, not CI or release gates.

## Invariant Checklist

- 2: Pass. Worker authority is role-scoped; unconfigured or ungranted worker tools are absent from the offered surface and denied if proposed anyway.
- 6: Pass. Budget reads are current-attempt scoped column sums; retries get fresh token and worker counters.
- 13: Pass. Worker tool policy decisions are audit-visible as `ToolProposed` and `PolicyDecision` ledger events.
- 14: Pass. `precondition` requires `ToolResult.tool_status = Succeeded`, not a proposal, model-call record or rationale JSON; default attempt scope rejects prior failed-attempt results and `Scope: job` opts in.
- 16: Pass. Successful tool result artifact plus `ToolResult` ledger event commit atomically; injected mid-commit failure leaves neither.
- 1-3 regression: Pass. Default providers remain mock/scripted for automated tests; real-LLM smoke is opt-in and non-gated; control-plane tools are still exactly `delegate` and `publish_report`.
- 18 regression: Pass. No new MCP surface or external action path was introduced; `requires_approval` fails closed without durable resume.

## Verification

- `dotnet build IncidentCompass.slnx -m:1`
- `dotnet test IncidentCompass.slnx -m:1`
- `$env:INCIDENTCOMPASS_REQUIRE_DOCKER_TESTS = 'true'; dotnet test tests\IncidentCompass.IntegrationTests\IncidentCompass.IntegrationTests.csproj -m:1`
- `dotnet format IncidentCompass.slnx --verify-no-changes --verbosity minimal`
- `powershell -ExecutionPolicy Bypass -File scripts\package-vulnerability-gate.ps1`
- `powershell -ExecutionPolicy Bypass -File scripts\code-organization-gate.ps1`
- Non-gated real-LLM smoke scripts above.
