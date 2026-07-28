[CmdletBinding()]
param(
    [string]$Configuration = "Debug"
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$testProject = Join-Path $repoRoot "src\Services\CentralPms\tests\ExitPass.CentralPms.IntegrationTests\ExitPass.CentralPms.IntegrationTests.csproj"

Write-Host "Running Central PMS APT payable-basis readiness proof..."
Write-Host "Repository: $repoRoot"
Write-Host "Test project: $testProject"
Write-Host "Coverage: non-statutory readiness, statutory blocked states, applied statutory basis, and applied-basis revalidation."

dotnet test $testProject `
    --configuration $Configuration `
    --no-restore `
    --filter "FullyQualifiedName~AptPayableBasisReadiness" `
    --logger "console;verbosity=minimal"

Write-Host "Central PMS APT payable-basis readiness proof passed."
