# Phase 5 Real Local LLM Grounded Report Smoke Result

- GeneratedUtc: 2026-07-03T16:39:58.3377381+00:00
- Status: executed
- Endpoint: http://localhost:1234
- ModelsEndpoint: http://localhost:1234/v1/models
- ChatCompletionsPath: /v1/chat/completions
- Model: qwen2.5-14b-instruct
- Runs: 5
- Scenario: delegate -> memory -> memory_search -> publish_report -> grounded evidence
- full trajectory reach-rate: 3/5

## Outcomes
- Run 1: reached - delegate_memory_memory_search_publish_report_with_evidence_reached
- Run 2: reached - delegate_memory_memory_search_publish_report_with_evidence_reached
- Run 3: reached - delegate_memory_memory_search_publish_report_with_evidence_reached
- Run 4: missed - DeadLettered (triage_job_attempt_failed: The triage attempt token budget was reached before the next model call.) trajectory(report=False, evidence=False, memory_worker=True, memory_search=True)
- Run 5: missed - DeadLettered (triage_job_attempt_failed: The triage attempt token budget was reached before the next model call.) trajectory(report=False, evidence=False, memory_worker=True, memory_search=True)
