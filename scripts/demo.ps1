param(
    [switch] $NoBuild,
    [switch] $RealLlm
)

$ErrorActionPreference = "Stop"

$composeArgs = @("compose", "--profile", "demo")
if ($RealLlm) {
    $composeArgs = @("compose", "-f", "docker-compose.yml", "-f", "compose.real-llm.yml", "--profile", "demo")
}

function Invoke-DemoCompose {
    param([string[]] $Arguments)

    & docker @composeArgs @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "docker compose $($Arguments -join ' ') failed with exit code $LASTEXITCODE."
    }
}

function Write-DemoDiagnostics {
    Write-Host ""
    Write-Host "Docker Compose status:"
    & docker @composeArgs ps

    Write-Host ""
    Write-Host "Last 20 api log lines:"
    & docker @composeArgs logs --tail 20 api

    Write-Host ""
    Write-Host "Last 20 worker log lines:"
    & docker @composeArgs logs --tail 20 worker
}

try {
    if (-not $NoBuild) {
        Invoke-DemoCompose @("build", "api", "worker", "tester")
    }

    Invoke-DemoCompose @("up", "-d", "--force-recreate", "postgres", "api", "worker")

    $apiPortBinding = & docker @composeArgs port api 8080
    if ($LASTEXITCODE -ne 0) {
        throw "docker compose port api 8080 failed with exit code $LASTEXITCODE."
    }

    $apiPortMatch = [regex]::Match(($apiPortBinding | Select-Object -First 1), ':(?<port>[0-9]+)$')
    if (-not $apiPortMatch.Success) {
        throw "Could not resolve the API host port from '$apiPortBinding'."
    }

    $healthUrl = "http://localhost:$($apiPortMatch.Groups['port'].Value)/api/v1/health"
    $deadline = (Get-Date).AddMinutes(3)
    $healthy = $false
    while ((Get-Date) -lt $deadline) {
        try {
            $response = Invoke-WebRequest -Uri $healthUrl -UseBasicParsing -TimeoutSec 2
            if ($response.StatusCode -ge 200 -and $response.StatusCode -lt 300) {
                $healthy = $true
                break
            }
        }
        catch {
            Start-Sleep -Seconds 2
        }
    }

    if (-not $healthy) {
        throw "API health endpoint did not become ready at $healthUrl."
    }

    Invoke-DemoCompose @("run", "--rm", "tester")
    Write-Host "Demo services remain running. Stop them with: docker compose --profile demo down"
}
catch {
    Write-Error -ErrorAction Continue $_
    Write-DemoDiagnostics
    exit 1
}
