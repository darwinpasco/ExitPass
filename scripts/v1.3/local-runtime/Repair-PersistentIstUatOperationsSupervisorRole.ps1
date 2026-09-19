[CmdletBinding(DefaultParameterSetName = 'Preflight')]
param(
    [Parameter(ParameterSetName = 'Preflight')]
    [switch]$PreflightOnly,

    [Parameter(Mandatory, ParameterSetName = 'Apply')]
    [switch]$Apply,

    [string]$ContainerName = 'exitpass-ist-persistent-db',

    [string]$BackupDirectory = 'D:\SourceCodes\ExitPass.local\persistent-ist\backups'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$databaseName = 'exitpass_ist'
$databaseUser = 'exitpass_ist'
$sqlRoot = Join-Path $PSScriptRoot 'sql'
$inspectSqlPath = Join-Path $sqlRoot 'Inspect-PersistentIstUatOperationsSupervisorRole.sql'
$repairSqlPath = Join-Path $sqlRoot 'Repair-PersistentIstUatOperationsSupervisorRole.sql'

function Invoke-Docker {
    param([Parameter(Mandatory)][string[]]$Arguments)

    & docker @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Docker command failed with exit code ${LASTEXITCODE}: docker $($Arguments -join ' ')"
    }
}

function Invoke-IdentitySql {
    param(
        [Parameter(Mandatory)][string]$Path,
        [switch]$SingleTransaction
    )

    $arguments = @('exec', '-i', $ContainerName, 'psql', '-X', '-v', 'ON_ERROR_STOP=1', '-U', $databaseUser, '-d', $databaseName, '-P', 'pager=off')
    if ($SingleTransaction) {
        $arguments += '--single-transaction'
    }

    Get-Content -LiteralPath $Path -Raw | & docker @arguments
    if ($LASTEXITCODE -ne 0) {
        throw "SQL execution failed for $Path with exit code $LASTEXITCODE."
    }
}

foreach ($requiredPath in @($inspectSqlPath, $repairSqlPath)) {
    if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf)) {
        throw "Required reconciliation SQL is missing: $requiredPath"
    }
}

$running = & docker inspect -f '{{.State.Running}}' $ContainerName 2>$null
if ($LASTEXITCODE -ne 0 -or $running.Trim() -cne 'true') {
    throw "The expected persistent IST database container is not running: $ContainerName"
}

$actualDatabase = & docker exec $ContainerName psql -X -Atq -U $databaseUser -d $databaseName -c 'SELECT current_database();'
if ($LASTEXITCODE -ne 0 -or $actualDatabase.Trim() -cne $databaseName) {
    throw "Database identity check failed. Expected exactly $databaseName."
}

Write-Host "Persistent IST canonical-role reconciliation preflight"
Write-Host "Container: $ContainerName"
Write-Host "Database:  $databaseName"
Invoke-IdentitySql -Path $inspectSqlPath

if (-not $Apply) {
    Write-Host 'Preflight completed read-only. No database mutation was performed.'
    exit 0
}

[void](New-Item -ItemType Directory -Path $BackupDirectory -Force)
$timestamp = [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssZ')
$backupName = "exitpass-ist-pre-uat-operations-supervisor-role-reconciliation-$timestamp.dump"
$containerBackupPath = "/tmp/$backupName"
$hostBackupPath = Join-Path $BackupDirectory $backupName

try {
    Invoke-Docker -Arguments @('exec', $ContainerName, 'pg_dump', '-U', $databaseUser, '-d', $databaseName, '-Fc', '-f', $containerBackupPath)
    Invoke-Docker -Arguments @('cp', "${ContainerName}:$containerBackupPath", $hostBackupPath)
}
finally {
    & docker exec $ContainerName rm -f $containerBackupPath 2>$null
}

$backup = Get-Item -LiteralPath $hostBackupPath
if ($backup.Length -le 0) {
    throw "The pre-reconciliation backup is empty: $hostBackupPath"
}
$backupHash = (Get-FileHash -LiteralPath $hostBackupPath -Algorithm SHA256).Hash
Write-Host "Backup: $hostBackupPath"
Write-Host "Backup SHA-256: $backupHash"

Invoke-IdentitySql -Path $repairSqlPath -SingleTransaction

Write-Host 'Post-reconciliation verification'
Invoke-IdentitySql -Path $inspectSqlPath
Write-Host 'Persistent IST canonical-role reconciliation completed.'
