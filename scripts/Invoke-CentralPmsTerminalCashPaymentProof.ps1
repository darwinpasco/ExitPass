[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
$testProject = Join-Path $repoRoot 'src\Services\CentralPms\tests\ExitPass.CentralPms.IntegrationTests\ExitPass.CentralPms.IntegrationTests.csproj'
$filter = 'FullyQualifiedName~TerminalCashPayment'

Write-Host 'Central PMS terminal cash-payment proof'
Write-Host "Repository: $repoRoot"
Write-Host "Test project: $testProject"
Write-Host "Filter: $filter"
Write-Host ''
Write-Host 'Proof coverage:'
Write-Host '- newly accepted cash-payment command'
Write-Host '- one canonical cash payment confirmation'
Write-Host '- same-key/same-payload idempotent replay'
Write-Host '- same-key/different-payload HTTP 409'
Write-Host '- status readback returning the same confirmation after a new application factory'
Write-Host '- no fiscal issuance'
Write-Host '- no exit authorization'
Write-Host '- no Payment Orchestrator provider session or outcome'
Write-Host ''

dotnet test $testProject --no-build --filter $filter
