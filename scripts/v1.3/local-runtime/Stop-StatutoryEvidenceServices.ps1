[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$ownershipLabelName = 'com.exitpass.local-runtime'
$ownershipLabelValue = 'persistent-pitx-statutory-evidence'
$containerNames = @(
    'exitpass-pitx-statutory-evidence-clamav',
    'exitpass-pitx-statutory-evidence-minio'
)

if (-not (Get-Command docker.exe -ErrorAction SilentlyContinue)) {
    throw 'Docker Desktop is required to stop persistent PITX statutory evidence services.'
}

foreach ($name in $containerNames) {
    $containerName = (& docker.exe ps -a --filter "name=^/$name$" --format '{{.Names}}')
    if ($LASTEXITCODE -ne 0) {
        throw "Unable to inspect Docker container '$name'."
    }
    if ([string]::IsNullOrWhiteSpace($containerName)) {
        continue
    }

    $inspection = ((& docker.exe inspect $name) | ConvertFrom-Json)[0]
    if ($inspection.Name.TrimStart('/') -cne $name -or
        $inspection.Config.Labels.$ownershipLabelName -cne $ownershipLabelValue) {
        throw "Container '$name' is not owned by the persistent PITX statutory-evidence runtime. It will not be stopped."
    }

    if ($inspection.State.Running) {
        & docker.exe stop --time 20 $inspection.Id | Out-Null
        if ($LASTEXITCODE -ne 0) {
            throw "Could not stop '$name'."
        }
    }
}

Write-Host 'Persistent PITX statutory evidence containers are stopped.'
Write-Host 'Private credentials, MinIO evidence objects, and PostgreSQL state were preserved.'

