param(
    [string]$Configuration = "Debug"
)

$ErrorActionPreference = "Stop"

$scriptDirectory = Split-Path -Parent $MyInvocation.MyCommand.Path
$repositoryRoot = Resolve-Path (Join-Path $scriptDirectory "..")
$testProject = Join-Path $repositoryRoot "src\Services\CentralPms\tests\ExitPass.CentralPms.UnitTests\ExitPass.CentralPms.UnitTests.csproj"

Write-Host "Management Platform POS Server Sales Invoice profile client proof"
Write-Host "Repository: $repositoryRoot"
Write-Host "Test project: $testProject"
Write-Host "Proof posture: in-process stub POS Server handler; no real POS Server repository or external network."
Write-Host "Secret posture: API key assertions use placeholder test material and do not print the key."

dotnet test $testProject `
    --configuration $Configuration `
    --no-build `
    --filter "FullyQualifiedName~PosServerSalesInvoiceProfile"

Write-Host "Proof completed: disabled posture, safe configuration failure, server-side API key header, correlation propagation, client mappings, safe errors, bounded GET retry, mutation no-retry, and no UI/fiscal/exit/gate behavior were covered by focused tests."
