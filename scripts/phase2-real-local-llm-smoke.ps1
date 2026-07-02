param(
    [string] $BaseUrl = "http://localhost:1234",
    [string] $ChatCompletionsPath = "/v1/chat/completions",
    [string] $Model = "local-model",
    [string] $ApiKey = "local-smoke-key",
    [int] $Runs = 3,
    [int] $TimeoutSeconds = 120,
    [string] $ResultPath = "docs\phase-2-real-llm-smoke-result.md"
)

$ErrorActionPreference = "Stop"
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
Set-Location $repoRoot

function Write-SmokeResultUnavailable {
    param([string] $Reason)

    $absoluteResultPath = [System.IO.Path]::GetFullPath((Join-Path $repoRoot $ResultPath))
    $resultDirectory = Split-Path -Parent $absoluteResultPath
    New-Item -ItemType Directory -Force -Path $resultDirectory | Out-Null
    @(
        "# Phase 3 Real Local LLM Smoke Result",
        "",
        "- GeneratedUtc: $([DateTimeOffset]::UtcNow.ToString('O'))",
        "- Endpoint: $BaseUrl",
        "- ChatCompletionsPath: $ChatCompletionsPath",
        "- Model: $Model",
        "- Runs requested: $Runs",
        "- Status: Not executed",
        "- publish_report reach-rate: not measured",
        "- Reason: $Reason"
    ) | Set-Content -LiteralPath $absoluteResultPath -Encoding UTF8

    Write-Output "Real-local-LLM smoke not executed: $Reason"
    Write-Output "Result written to $absoluteResultPath"
}

$modelsUri = $BaseUrl.TrimEnd('/') + "/v1/models"
try {
    Invoke-WebRequest -Uri $modelsUri -UseBasicParsing -TimeoutSec 5 | Out-Null
} catch {
    Write-SmokeResultUnavailable "OpenAI-compatible models endpoint unavailable at $modelsUri ($($_.Exception.Message))"
    exit 2
}

$env:INCIDENTCOMPASS_REAL_LLM_SMOKE = "true"
$env:INCIDENTCOMPASS_REAL_LLM_SMOKE_BASE_URL = $BaseUrl
$env:INCIDENTCOMPASS_REAL_LLM_SMOKE_CHAT_PATH = $ChatCompletionsPath
$env:INCIDENTCOMPASS_REAL_LLM_SMOKE_MODEL = $Model
$env:INCIDENTCOMPASS_REAL_LLM_SMOKE_API_KEY = $ApiKey
$env:INCIDENTCOMPASS_REAL_LLM_SMOKE_RUNS = $Runs.ToString()
$env:INCIDENTCOMPASS_REAL_LLM_SMOKE_TIMEOUT_SECONDS = $TimeoutSeconds.ToString()
$env:INCIDENTCOMPASS_REAL_LLM_SMOKE_RESULT_PATH = [System.IO.Path]::GetFullPath((Join-Path $repoRoot $ResultPath))
$env:INCIDENTCOMPASS_REQUIRE_DOCKER_TESTS = "true"

dotnet test tests\IncidentCompass.IntegrationTests\IncidentCompass.IntegrationTests.csproj --no-build --filter FullyQualifiedName~TriageInvestigationRealLlmSmokeTests
exit $LASTEXITCODE
