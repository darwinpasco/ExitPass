param(
    [string] $DatabaseName = "exitpass_v12_dev",
    [string] $ContainerName = "exitpass-postgres",
    [string] $DbUser = "exitpass",
    [string] $DdlPath = ".\ExitPass_Full_Database_Creation_DDL_v1.2.sql",
    [string] $SeedPath = ".\infra\db\seed\ExitPass_Reference_Data_v1.2.sql",
    [string] $LogDirectory = ".\logs\db",
    [switch] $ForceRecreate,
    [switch] $SkipSeedReplay,
    [switch] $SkipValidation
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..\..")

function Resolve-RepoPath {
    param([string] $Path)

    if ([System.IO.Path]::IsPathRooted($Path)) {
        return $Path
    }

    return Join-Path $repoRoot $Path
}

function Assert-SafeIdentifier {
    param(
        [string] $Value,
        [string] $Name
    )

    if ($Value -notmatch "^[A-Za-z0-9_][A-Za-z0-9_-]*$") {
        throw "$Name '$Value' contains unsupported characters. Use letters, numbers, underscore, or hyphen."
    }
}

function Invoke-LoggedNative {
    param(
        [string] $StepName,
        [string] $LogPath,
        [scriptblock] $Command
    )

    Write-Host "==> $StepName"
    "[$(Get-Date -Format o)] $StepName" | Tee-Object -FilePath $LogPath
    $Error.Clear()
    $previousErrorActionPreference = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    try {
        $output = & $Command 2>&1
        $exitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previousErrorActionPreference
    }
    $output | Tee-Object -FilePath $LogPath -Append

    if ($exitCode -ne 0) {
        throw "$StepName failed. See $LogPath."
    }
}

function Invoke-PsqlFile {
    param(
        [string] $StepName,
        [string] $Database,
        [string] $Path,
        [string] $LogPath
    )

    Invoke-LoggedNative -StepName $StepName -LogPath $LogPath -Command {
        Get-Content $Path -Raw |
            docker exec -i $ContainerName psql -v ON_ERROR_STOP=1 -U $DbUser -d $Database
    }
}

Push-Location $repoRoot
try {
    if (-not $ForceRecreate) {
        Write-Host "Refusing to drop/recreate '$DatabaseName' without -ForceRecreate."
        Write-Host "Usage:"
        Write-Host "  .\infra\db\scripts\Reset-ExitPassV12Database.ps1 -ForceRecreate"
        Write-Host "  .\infra\db\scripts\Reset-ExitPassV12Database.ps1 -DatabaseName exitpass_v12_rebuild_test -ForceRecreate"
        exit 2
    }

    Assert-SafeIdentifier -Value $DatabaseName -Name "DatabaseName"
    Assert-SafeIdentifier -Value $ContainerName -Name "ContainerName"
    Assert-SafeIdentifier -Value $DbUser -Name "DbUser"

    $resolvedDdlPath = Resolve-RepoPath $DdlPath
    $resolvedSeedPath = Resolve-RepoPath $SeedPath
    $resolvedLogDirectory = Resolve-RepoPath $LogDirectory

    if (-not (Test-Path $resolvedDdlPath -PathType Leaf)) {
        throw "DDL file not found: $resolvedDdlPath"
    }

    if (-not (Test-Path $resolvedSeedPath -PathType Leaf)) {
        throw "Seed file not found: $resolvedSeedPath"
    }

    New-Item -ItemType Directory -Force -Path $resolvedLogDirectory | Out-Null

    $timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
    $logPrefix = Join-Path $resolvedLogDirectory "$DatabaseName-$timestamp"
    $recreateLog = "$logPrefix-recreate.log"
    $ddlLog = "$logPrefix-ddl.log"
    $seedRun1Log = "$logPrefix-seed-run-1.log"
    $seedRun2Log = "$logPrefix-seed-run-2.log"
    $validationLog = "$logPrefix-validation.log"

    Write-Host "ExitPass v1.2 database bootstrap"
    Write-Host "Database: $DatabaseName"
    Write-Host "Container: $ContainerName"
    Write-Host "DDL: $resolvedDdlPath"
    Write-Host "Seed: $resolvedSeedPath"
    Write-Host "Logs: $resolvedLogDirectory"

    Invoke-LoggedNative -StepName "Verify PostgreSQL container is running" -LogPath $recreateLog -Command {
        docker inspect -f "{{.State.Running}}" $ContainerName
    }

    $containerState = (docker inspect -f "{{.State.Running}}" $ContainerName 2>$null)
    if ($LASTEXITCODE -ne 0 -or $containerState -ne "true") {
        throw "Docker container '$ContainerName' is not running."
    }

    Invoke-LoggedNative -StepName "Drop and recreate database '$DatabaseName'" -LogPath $recreateLog -Command {
        docker exec $ContainerName psql -v ON_ERROR_STOP=1 -U $DbUser -d postgres -c "DROP DATABASE IF EXISTS $DatabaseName WITH (FORCE);"
        $dropExitCode = $LASTEXITCODE
        if ($LASTEXITCODE -ne 0) {
            $global:LASTEXITCODE = $dropExitCode
            return
        }

        docker exec $ContainerName psql -v ON_ERROR_STOP=1 -U $DbUser -d postgres -c "CREATE DATABASE $DatabaseName OWNER $DbUser;"
        $global:LASTEXITCODE = $LASTEXITCODE
    }

    Invoke-PsqlFile -StepName "Apply ExitPass v1.2 full DDL" -Database $DatabaseName -Path $resolvedDdlPath -LogPath $ddlLog
    Invoke-PsqlFile -StepName "Apply ExitPass v1.2 reference seed run 1" -Database $DatabaseName -Path $resolvedSeedPath -LogPath $seedRun1Log

    if ($SkipSeedReplay) {
        Write-Host "Skipping seed replay because -SkipSeedReplay was supplied."
        "[$(Get-Date -Format o)] Skipped seed replay by request." | Tee-Object -FilePath $seedRun2Log
    }
    else {
        Invoke-PsqlFile -StepName "Apply ExitPass v1.2 reference seed run 2" -Database $DatabaseName -Path $resolvedSeedPath -LogPath $seedRun2Log
    }

    $validationScript = Join-Path $repoRoot "infra\db\Validate-FullDdlPaymentChain.ps1"
    if ($SkipValidation) {
        Write-Host "Skipping validation because -SkipValidation was supplied."
        "[$(Get-Date -Format o)] Skipped validation by request." | Tee-Object -FilePath $validationLog
    }
    elseif (Test-Path $validationScript -PathType Leaf) {
        $validationDatabaseName = "${DatabaseName}_validation"
        Assert-SafeIdentifier -Value $validationDatabaseName -Name "Validation database name"

        Invoke-LoggedNative -StepName "Prepare validation database '$validationDatabaseName'" -LogPath $validationLog -Command {
            docker exec $ContainerName psql -v ON_ERROR_STOP=1 -U $DbUser -d postgres -c "DROP DATABASE IF EXISTS $validationDatabaseName WITH (FORCE);"
            $dropExitCode = $LASTEXITCODE
            if ($dropExitCode -ne 0) {
                $global:LASTEXITCODE = $dropExitCode
                return
            }

            docker exec $ContainerName psql -v ON_ERROR_STOP=1 -U $DbUser -d postgres -c "CREATE DATABASE $validationDatabaseName OWNER $DbUser;"
            $global:LASTEXITCODE = $LASTEXITCODE
        }

        Invoke-LoggedNative -StepName "Run ExitPass v1.2 full DDL validation/preflight script" -LogPath $validationLog -Command {
            & $validationScript -DatabaseName $validationDatabaseName -PostgresContainer $ContainerName -PostgresUser $DbUser -SkipTests
        }
    }
    else {
        Write-Host "No validation/preflight script found; skipping validation."
        "[$(Get-Date -Format o)] No validation/preflight script found." | Tee-Object -FilePath $validationLog
    }

    Write-Host "ExitPass v1.2 database bootstrap completed."
    Write-Host "Log files:"
    Write-Host "  $recreateLog"
    Write-Host "  $ddlLog"
    Write-Host "  $seedRun1Log"
    Write-Host "  $seedRun2Log"
    Write-Host "  $validationLog"
}
finally {
    Pop-Location
}
