[CmdletBinding(DefaultParameterSetName = 'Preflight')]
param(
    [Parameter(ParameterSetName = 'Preflight')]
    [switch]$PreflightOnly,

    [Parameter(Mandatory, ParameterSetName = 'Apply')]
    [switch]$Apply,

    [string]$ContainerName = 'exitpass-ist-persistent-db',
    [string]$DatabaseName = 'exitpass_ist',
    [string]$DatabaseUser = 'exitpass_ist',
    [string]$Username = 'JuanDC01',
    [string]$DeviceBindingCode = 'PITX-L3-OC-IST-01',
    [string]$PrivateRoot = 'D:\SourceCodes\ExitPass.local\persistent-ist',
    [string]$BackupDirectory = 'D:\SourceCodes\ExitPass.local\persistent-ist\backups'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$sqlPath = Join-Path $PSScriptRoot 'sql\Initialize-OperatorConsoleControlledWorkstation.sql'
$proofDirectory = Join-Path $PrivateRoot 'operator-console\device-bindings'
$proofPath = Join-Path $proofDirectory "$DeviceBindingCode.proof"

function Invoke-Docker {
    param([Parameter(Mandatory)][string[]]$Arguments)

    & docker.exe @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Docker command failed with exit code ${LASTEXITCODE}: docker $($Arguments -join ' ')"
    }
}

function Protect-PrivateFile {
    param([Parameter(Mandatory)][string]$Path)

    $currentIdentity = [Security.Principal.WindowsIdentity]::GetCurrent().Name
    & icacls.exe $Path /inheritance:r /grant:r "${currentIdentity}:(F)" '*S-1-5-18:(F)' | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "Unable to restrict the private Operator Console proof ACL: $Path"
    }
}

function New-OpaqueProof {
    $bytes = [byte[]]::new(48)
    $generator = [Security.Cryptography.RandomNumberGenerator]::Create()
    try {
        $generator.GetBytes($bytes)
    }
    finally {
        $generator.Dispose()
    }
    return [Convert]::ToBase64String($bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_')
}

function Invoke-ProvisioningSql {
    param(
        [Parameter(Mandatory)][bool]$ApplyChanges,
        [string]$ProofHash = ''
    )

    $arguments = @(
        'exec', '-i', $ContainerName,
        'psql', '-X', '-v', 'ON_ERROR_STOP=1', '--single-transaction',
        '-v', "apply=$($ApplyChanges.ToString().ToLowerInvariant())",
        '-v', "username=$Username",
        '-v', "device_binding_code=$DeviceBindingCode",
        '-v', "proof_hash=$ProofHash",
        '-U', $DatabaseUser, '-d', $DatabaseName, '-P', 'pager=off'
    )
    Get-Content -LiteralPath $sqlPath -Raw | & docker.exe @arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Controlled Operator Console workstation provisioning failed with exit code $LASTEXITCODE."
    }
}

if (-not (Test-Path -LiteralPath $sqlPath -PathType Leaf)) {
    throw "Required provisioning SQL is missing: $sqlPath"
}
$running = & docker.exe inspect -f '{{.State.Running}}' $ContainerName 2>$null
if ($LASTEXITCODE -ne 0 -or $running.Trim() -cne 'true') {
    throw "The expected persistent IST database container is not running: $ContainerName"
}
$actualDatabase = & docker.exe exec $ContainerName psql -X -Atq -U $DatabaseUser -d $DatabaseName -c 'SELECT current_database();'
if ($LASTEXITCODE -ne 0 -or $actualDatabase.Trim() -cne $DatabaseName) {
    throw "Database identity check failed. Expected exactly $DatabaseName."
}

Write-Host 'Controlled Operator Console workstation preflight'
Write-Host "Container: $ContainerName"
Write-Host "Database:  $DatabaseName"
Write-Host "User:      $Username"
Write-Host "Device:    $DeviceBindingCode"
Write-Host "Proof file present: $(Test-Path -LiteralPath $proofPath -PathType Leaf)"
Invoke-ProvisioningSql -ApplyChanges:$false

if (-not $Apply) {
    Write-Host 'Preflight completed read-only. No database or private-file mutation was performed.'
    exit 0
}

[void](New-Item -ItemType Directory -Path $proofDirectory -Force)
if (-not (Test-Path -LiteralPath $proofPath -PathType Leaf)) {
    [IO.File]::WriteAllText($proofPath, (New-OpaqueProof), [Text.UTF8Encoding]::new($false))
}
Protect-PrivateFile -Path $proofPath
$proof = [IO.File]::ReadAllText($proofPath, [Text.Encoding]::UTF8).Trim()
if ($proof.Length -lt 32) {
    throw "The private Operator Console proof is invalid: $proofPath"
}
$sha256 = [Security.Cryptography.SHA256]::Create()
try {
    $proofHashBytes = $sha256.ComputeHash([Text.Encoding]::UTF8.GetBytes($proof))
}
finally {
    $sha256.Dispose()
}
$proofHash = [BitConverter]::ToString($proofHashBytes).Replace('-', '').ToLowerInvariant()

[void](New-Item -ItemType Directory -Path $BackupDirectory -Force)
$timestamp = [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssZ')
$backupName = "exitpass-ist-pre-operator-console-workstation-$timestamp.dump"
$containerBackupPath = "/tmp/$backupName"
$hostBackupPath = Join-Path $BackupDirectory $backupName
try {
    Invoke-Docker -Arguments @('exec', $ContainerName, 'pg_dump', '-U', $DatabaseUser, '-d', $DatabaseName, '-Fc', '-f', $containerBackupPath)
    Invoke-Docker -Arguments @('cp', "${ContainerName}:$containerBackupPath", $hostBackupPath)
}
finally {
    & docker.exe exec $ContainerName rm -f $containerBackupPath 2>$null
}
$backup = Get-Item -LiteralPath $hostBackupPath
if ($backup.Length -le 0) {
    throw "The pre-provisioning backup is empty: $hostBackupPath"
}
Write-Host "Backup: $hostBackupPath"
Write-Host "Backup SHA-256: $((Get-FileHash -LiteralPath $hostBackupPath -Algorithm SHA256).Hash)"

Invoke-ProvisioningSql -ApplyChanges:$true -ProofHash $proofHash
Write-Host "Private workstation proof: $proofPath"
Write-Host 'The proof value was not printed. Enter it only in the Operator Console workstation provisioning control.'
Write-Host 'Controlled Operator Console workstation provisioning completed.'
