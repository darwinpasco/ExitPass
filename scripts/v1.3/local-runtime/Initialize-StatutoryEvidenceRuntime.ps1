[CmdletBinding()]
param(
    [string] $DatabaseContainer = 'exitpass-ist-persistent-db',
    [string] $DatabaseName = 'exitpass_ist',
    [string] $DatabaseUser = 'exitpass_ist'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$sqlPath = Join-Path $PSScriptRoot 'Initialize-StatutoryEvidenceRuntime.sql'
if (-not (Test-Path -LiteralPath $sqlPath -PathType Leaf)) {
    throw "Persistent PITX statutory-evidence governance SQL was not found at '$sqlPath'."
}

$runningDatabase = (& docker.exe ps --filter "name=^/$DatabaseContainer$" --format '{{.Names}}')
if ($LASTEXITCODE -ne 0 -or $runningDatabase -ne $DatabaseContainer) {
    throw "Persistent Central PMS database container '$DatabaseContainer' is not running."
}

$databaseIdentity = (& docker.exe exec $DatabaseContainer psql -X -U $DatabaseUser -d $DatabaseName -v ON_ERROR_STOP=1 -Atc 'SELECT current_database();').Trim()
if ($LASTEXITCODE -ne 0 -or $databaseIdentity -cne $DatabaseName) {
    throw "Persistent statutory-evidence governance target is not the expected database '$DatabaseName'."
}

$sql = [IO.File]::ReadAllText($sqlPath)
$output = $sql | & docker.exe exec -i $DatabaseContainer psql -X -U $DatabaseUser -d $DatabaseName -v ON_ERROR_STOP=1 -P pager=off
if ($LASTEXITCODE -ne 0) {
    throw 'Persistent PITX statutory-evidence governance initialization failed closed.'
}

Write-Host 'Persistent PITX statutory-evidence governance is ready.'
Write-Verbose ($output -join [Environment]::NewLine)
