param(
    [string] $Configuration = "Debug"
)

# Regenerates tests\IncidentCompass.IntegrationTests\Baselines\openapi-v1.json from the live
# OpenAPI document served by the API host under deterministic mock providers.
#
# OpenApiContractTests.DevelopmentOpenApiDocument_MatchesCommittedBaseline is a pure, read-only
# comparison and never writes this file. Regeneration is a deliberate, explicit action: it is
# implemented as an xUnit "explicit" fact (OpenApiContractTests.RegenerateOpenApiBaseline) that a
# normal `dotnet test` run never executes - this script is the only supported way to run it.
#
# Review the resulting diff before committing a regenerated baseline.

$ErrorActionPreference = "Stop"
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$testProject = Join-Path $repoRoot "tests\IncidentCompass.IntegrationTests\IncidentCompass.IntegrationTests.csproj"

Write-Host "Building IncidentCompass.IntegrationTests ($Configuration)..."
dotnet build $testProject --configuration $Configuration | Out-Host
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

$csprojText = Get-Content -LiteralPath $testProject -Raw
$targetFrameworkMatch = [regex]::Match($csprojText, "<TargetFramework>([^<]+)</TargetFramework>")
if (-not $targetFrameworkMatch.Success) {
    throw "Could not determine the target framework from $testProject."
}
$targetFramework = $targetFrameworkMatch.Groups[1].Value

$testHost = Join-Path $repoRoot "tests\IncidentCompass.IntegrationTests\bin\$Configuration\$targetFramework\IncidentCompass.IntegrationTests.exe"
if (-not (Test-Path $testHost)) {
    throw "Could not find the built test host at $testHost."
}

Write-Host "Regenerating the OpenAPI baseline..."
& $testHost -explicit only -method "IncidentCompass.IntegrationTests.OpenApiContractTests.RegenerateOpenApiBaseline"
if ($LASTEXITCODE -ne 0) {
    throw "OpenAPI baseline regeneration failed. See output above."
}

Write-Host "Baseline updated at tests\IncidentCompass.IntegrationTests\Baselines\openapi-v1.json. Review the diff before committing."
