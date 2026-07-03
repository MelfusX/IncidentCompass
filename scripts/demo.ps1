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
        exit $LASTEXITCODE
    }
}

if (-not $NoBuild) {
    Invoke-DemoCompose @("build", "api", "worker", "tester")
}

Invoke-DemoCompose @("up", "-d", "--force-recreate", "postgres", "api", "worker")

$healthUrl = "http://localhost:5198/api/v1/health"
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
    Write-Error "API health endpoint did not become ready at $healthUrl."
    exit 1
}

Invoke-DemoCompose @("run", "--rm", "tester")
Write-Host "Demo services remain running. Stop them with: docker compose --profile demo down"
