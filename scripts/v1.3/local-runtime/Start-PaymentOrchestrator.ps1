[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..\..")).Path
$project = Join-Path $repoRoot "src\Services\PaymentOrchestrator\src\ExitPass.PaymentOrchestrator.Api\ExitPass.PaymentOrchestrator.Api.csproj"

Write-Host "Starting Payment Orchestrator at https://localhost:56062 and http://127.0.0.1:56063"
& dotnet run --project $project --launch-profile ExitPass.PaymentOrchestrator.Api
exit $LASTEXITCODE
