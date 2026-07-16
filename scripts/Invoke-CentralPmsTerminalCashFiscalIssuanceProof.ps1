param(
    [string]$Configuration = "Debug"
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$testProject = Join-Path $repoRoot "src\Services\CentralPms\tests\ExitPass.CentralPms.IntegrationTests\ExitPass.CentralPms.IntegrationTests.csproj"

Write-Host "Central PMS terminal cash fiscal issuance proof"
Write-Host "Repository: $repoRoot"
Write-Host "Test project: $testProject"
Write-Host "Scope: confirmed terminal cash payment -> existing fiscal issuance path -> durable readback"
Write-Host "Stop boundaries: no ExitAuthorization and no gate behavior"

dotnet test $testProject `
    --configuration $Configuration `
    --filter "FullyQualifiedName~TerminalCashFiscalIssuance" `
    --logger "console;verbosity=minimal"

Write-Host "Proof completed successfully."
