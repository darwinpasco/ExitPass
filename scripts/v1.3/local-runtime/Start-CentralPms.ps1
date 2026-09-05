[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..\..")).Path
$project = Join-Path $repoRoot "src\Services\CentralPms\src\ExitPass.CentralPms.Api\ExitPass.CentralPms.Api.csproj"

Write-Host "Starting Central PMS at https://localhost:56064 and http://127.0.0.1:56065"
& dotnet run --project $project --launch-profile ExitPass.CentralPms.Api
exit $LASTEXITCODE
