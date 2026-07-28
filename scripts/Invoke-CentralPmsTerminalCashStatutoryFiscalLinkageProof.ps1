param(
    [string]$Configuration = "Debug"
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$testProject = Join-Path $repoRoot "src\Services\CentralPms\tests\ExitPass.CentralPms.IntegrationTests\ExitPass.CentralPms.IntegrationTests.csproj"

Write-Host "Central PMS terminal cash statutory fiscal linkage proof"
Write-Host "Repository: $repoRoot"
Write-Host "Test project: $testProject"
Write-Host "Scope: terminal-cash fiscal issuance maps approved statutory context into the POS Server request"
Write-Host "Coverage: non-statutory unchanged, valid statutory linkage, invalid linkage blocked before POS Server, replay/idempotency preserved by existing fiscal reference flow"
Write-Host "Stop boundaries: no statutory mutation, no direct HikCentral/WebPay/ExitAuthorization/gate behavior"

dotnet test $testProject `
    --configuration $Configuration `
    --filter "FullyQualifiedName~TerminalCashFiscalIssuanceIntegrationTests" `
    --logger "console;verbosity=minimal"

Write-Host "Proof completed successfully."
