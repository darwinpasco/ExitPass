param(
    [string]$Configuration = "Debug"
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$testProject = Join-Path $repoRoot "src\Services\CentralPms\tests\ExitPass.CentralPms.IntegrationTests\ExitPass.CentralPms.IntegrationTests.csproj"
$validationDatabase = "exitpass_apt_terminal_cash_receipt_readback_validation_$([guid]::NewGuid().ToString("N"))"

Write-Host "Central PMS terminal cash receipt-presentation readback proof"
Write-Host "Repository: $repoRoot"
Write-Host "Test project: $testProject"
Write-Host "Validation database marker: $validationDatabase"
Write-Host "Scope: recorded terminal cash fiscal document -> POS Server-owned Digital Sales Invoice presentation readback"
Write-Host "Authority boundary: Central PMS delegates presentation content to POS Server and does not render, print, issue exit authorization, or command gates"

dotnet test $testProject `
    --configuration $Configuration `
    --no-restore `
    --filter "FullyQualifiedName~TerminalCashReceiptReadback" `
    --logger "console;verbosity=minimal"

if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

Write-Host "Proof completed successfully."
