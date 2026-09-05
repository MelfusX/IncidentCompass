param(
    [string] $BaseUrl = "http://localhost:1234",
    [string] $ChatCompletionsPath = "/v1/chat/completions",
    [string] $Model = "local-model",
    [string] $ApiKey = "local-smoke-key",
    [int] $Runs = 3,
    [int] $TimeoutSeconds = 120,
    [string] $ResultPath = "docs\phase-5-real-llm-smoke-result.md"
)

$ErrorActionPreference = "Stop"
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$previousLocation = Get-Location
Set-Location $repoRoot

function Write-SmokeResultUnavailable {
    param([string] $Reason)

    $absoluteResultPath = [System.IO.Path]::GetFullPath((Join-Path $repoRoot $ResultPath))
    $resultDirectory = Split-Path -Parent $absoluteResultPath
    New-Item -ItemType Directory -Force -Path $resultDirectory | Out-Null
    @(
        "# Phase 5 Real Local LLM Grounded Report Smoke Result",
        "",
        "- GeneratedUtc: $([DateTimeOffset]::UtcNow.ToString('O'))",
        "- Endpoint: $BaseUrl",
        "- ChatCompletionsPath: $ChatCompletionsPath",
        "- Model: $Model",
        "- Runs requested: $Runs",
        "- Status: Not executed",
        "- full trajectory reach-rate: not measured",
        "- Scenario: delegate -> memory -> memory_search -> publish_report -> grounded evidence",
        "- Reason: $Reason"
    ) | Set-Content -LiteralPath $absoluteResultPath -Encoding UTF8

    Write-Output "Real-local-LLM smoke not executed: $Reason"
    Write-Output "Result written to $absoluteResultPath"
}

function Save-EnvironmentVariables {
    param([string[]] $Names)

    $snapshot = @{}
    foreach ($name in $Names) {
        $value = [Environment]::GetEnvironmentVariable($name, "Process")
        $snapshot[$name] = [pscustomobject]@{
            Exists = $null -ne $value
            Value = $value
        }
    }

    return $snapshot
}

function Restore-EnvironmentVariables {
    param([hashtable] $Snapshot)

    foreach ($entry in $Snapshot.GetEnumerator()) {
        if ($entry.Value.Exists) {
            [Environment]::SetEnvironmentVariable($entry.Key, $entry.Value.Value, "Process")
        } else {
            [Environment]::SetEnvironmentVariable($entry.Key, $null, "Process")
        }
    }
}

try {
    $modelsUri = $BaseUrl.TrimEnd('/') + "/v1/models"
    try {
        Invoke-WebRequest -Uri $modelsUri -UseBasicParsing -TimeoutSec 5 | Out-Null
    } catch {
        Write-SmokeResultUnavailable "OpenAI-compatible models endpoint unavailable at $modelsUri ($($_.Exception.GetType().Name))"
        exit 2
    }

    $envNames = @(
        "INCIDENTCOMPASS_LLM_SMOKE_ENABLED",
        "INCIDENTCOMPASS_LLM_SMOKE_BASE_URL",
        "INCIDENTCOMPASS_LLM_SMOKE_CHAT_PATH",
        "INCIDENTCOMPASS_LLM_SMOKE_MODEL",
        "INCIDENTCOMPASS_LLM_SMOKE_API_KEY",
        "INCIDENTCOMPASS_LLM_SMOKE_RUNS",
        "INCIDENTCOMPASS_LLM_SMOKE_TIMEOUT_SECONDS",
        "INCIDENTCOMPASS_LLM_SMOKE_RESULT_PATH",
        "INCIDENTCOMPASS_REQUIRE_DOCKER_TESTS"
    )
    $savedEnvironment = Save-EnvironmentVariables $envNames
    $exitCode = 0
    try {
        $env:INCIDENTCOMPASS_LLM_SMOKE_ENABLED = "true"
        $env:INCIDENTCOMPASS_LLM_SMOKE_BASE_URL = $BaseUrl
        $env:INCIDENTCOMPASS_LLM_SMOKE_CHAT_PATH = $ChatCompletionsPath
        $env:INCIDENTCOMPASS_LLM_SMOKE_MODEL = $Model
        $env:INCIDENTCOMPASS_LLM_SMOKE_API_KEY = $ApiKey
        $env:INCIDENTCOMPASS_LLM_SMOKE_RUNS = $Runs.ToString()
        $env:INCIDENTCOMPASS_LLM_SMOKE_TIMEOUT_SECONDS = $TimeoutSeconds.ToString()
        $env:INCIDENTCOMPASS_LLM_SMOKE_RESULT_PATH = [System.IO.Path]::GetFullPath((Join-Path $repoRoot $ResultPath))
        $env:INCIDENTCOMPASS_REQUIRE_DOCKER_TESTS = "true"

        dotnet test --project tests\IncidentCompass.IntegrationTests\IncidentCompass.IntegrationTests.csproj --no-build --filter FullyQualifiedName~TriageInvestigationRealLlmSmokeTests
        $exitCode = $LASTEXITCODE
    } finally {
        Restore-EnvironmentVariables $savedEnvironment
    }

    exit $exitCode
} finally {
    Set-Location $previousLocation
}
