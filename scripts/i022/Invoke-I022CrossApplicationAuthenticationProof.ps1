param(
    [string] $CanonicalDatabaseRepository = "D:\SourceCodes\exitpassdb_v1.2",
    [string] $ManagementPlatformRepository = "D:\SourceCodes\ExitPass-ManagementPlatform",
    [string] $AptRepository = "D:\SourceCodes\ExitPass-AssistedPaymentTerminal",
    [string] $PostgresContainer = "exitpass-i022-postgres",
    [int] $PostgresPort = 55439,
    [switch] $SkipConsumerBuilds
)

$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$integrationProject = Join-Path $repoRoot "src\Services\CentralPms\tests\ExitPass.CentralPms.IntegrationTests\ExitPass.CentralPms.IntegrationTests.csproj"
$unitProject = Join-Path $repoRoot "src\Services\CentralPms\tests\ExitPass.CentralPms.UnitTests\ExitPass.CentralPms.UnitTests.csproj"
$apiProject = Join-Path $repoRoot "src\Services\CentralPms\src\ExitPass.CentralPms.Api\ExitPass.CentralPms.Api.csproj"
$operatorConsole = Join-Path $repoRoot "src\Services\OperatorConsoleUi"
$tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) "exitpass-i022-proof-$PID"

function Invoke-Checked([scriptblock] $Action, [string] $Failure) {
    & $Action
    if ($LASTEXITCODE -ne 0) { throw $Failure }
}

function Copy-ProofRepository([string] $Source, [string] $Destination) {
    New-Item -ItemType Directory -Path $Destination -Force | Out-Null
    & robocopy $Source $Destination /E /NFL /NDL /NJH /NJS /NP /XD .git node_modules bin obj dist test-results playwright-report | Out-Null
    if ($LASTEXITCODE -gt 7) { throw "Proof copy failed from $Source." }
    $global:LASTEXITCODE = 0
}

function Assert-NoAuthorityMaterial([string] $Path, [string[]] $Patterns) {
    foreach ($pattern in $Patterns) {
        $matches = rg -n --glob '!**/*.test.*' --glob '!**/e2e/**' --glob '!**/docs/**' --glob '!**/scripts/**' -- $pattern $Path
        if ($LASTEXITCODE -eq 0) { throw "Prohibited client authority pattern '$pattern' found:`n$matches" }
        if ($LASTEXITCODE -ne 1) { throw "Source scan failed for '$pattern'." }
    }
    $global:LASTEXITCODE = 0
}

try {
    foreach ($path in @($CanonicalDatabaseRepository, $ManagementPlatformRepository, $AptRepository)) {
        if (-not (Test-Path -LiteralPath $path)) { throw "Required repository not found: $path" }
    }
    $running = docker inspect -f '{{.State.Running}}' $PostgresContainer 2>$null
    if ($LASTEXITCODE -ne 0 -or $running.Trim() -ne "true") {
        throw "Disposable PostgreSQL container '$PostgresContainer' is not running."
    }

    $env:EXITPASS_STATUTORY_CANONICAL_DB_REPO = $CanonicalDatabaseRepository
    $env:EXITPASS_STATUTORY_DB_FIXTURE_POSTGRES_CONTAINER = $PostgresContainer
    $env:EXITPASS_STATUTORY_DB_FIXTURE_POSTGRES_USER = "postgres"
    $env:EXITPASS_STATUTORY_DB_FIXTURE_ADMIN_CONNECTION = "Host=127.0.0.1;Port=$PostgresPort;Database=postgres;Username=postgres;Pooling=false"
    $env:EXITPASS_STATUTORY_DB_FIXTURE_PREFIX = "exitpass_i022_"

    Invoke-Checked { dotnet build $apiProject --configuration Release -m:1 /p:UseSharedCompilation=false --verbosity minimal } "Central PMS Release build failed."
    Invoke-Checked {
        dotnet test $unitProject --configuration Release --filter "FullyQualifiedName~HumanAuthentication|FullyQualifiedName~IdentityAdministration|FullyQualifiedName~AptPayableBasis|FullyQualifiedName~OperatorConsoleStatutory" --logger "console;verbosity=minimal" -m:1 /p:UseSharedCompilation=false
    } "Central PMS focused unit proof failed."
    Invoke-Checked {
        dotnet test $integrationProject --configuration Release --filter "FullyQualifiedName~CrossApplicationHumanAuthenticationIntegrationTests|FullyQualifiedName~HumanAuthenticationApiIntegrationTests|FullyQualifiedName~ProductionHostedIdentityAdministrationIntegrationTests|FullyQualifiedName~IdentityAdministrationSecurityBoundaryTests|FullyQualifiedName~HumanAuthenticationRepositoryIntegrationTests|FullyQualifiedName~ManagementPlatformIdentityAdministrationRepositoryIntegrationTests" --logger "console;verbosity=minimal" -m:1 /p:UseSharedCompilation=false
    } "Central PMS cross-application integration proof failed."

    Assert-NoAuthorityMaterial (Join-Path $ManagementPlatformRepository "src") @(
        "localStorage\.setItem\(.*(token|session|permission|scope)",
        "sessionStorage\.setItem\(.*(token|session|permission|scope)",
        "X-ExitPass-User-Id.*set|X-ExitPass-Permissions.*set"
    )
    Assert-NoAuthorityMaterial (Join-Path $operatorConsole "src") @(
        "localStorage\.setItem\(.*(token|session|permission|scope)",
        "sessionStorage\.setItem\(.*(token|session|permission|scope)",
        "X-ExitPass-User-Id.*set|X-ExitPass-Permissions.*set"
    )
    Assert-NoAuthorityMaterial (Join-Path $AptRepository "src\AssistedPaymentTerminal.App\src") @(
        "localStorage\.setItem\(.*(token|session|permission|scope|identity|authority)",
        "sessionStorage\.setItem\(.*(token|session|permission|scope|identity|authority)",
        "X-ExitPass-Permissions",
        "Authorization\s*:\s*Bearer"
    )

    if (-not $SkipConsumerBuilds) {
        Copy-ProofRepository $ManagementPlatformRepository (Join-Path $tempRoot "management-platform")
        Copy-ProofRepository $AptRepository (Join-Path $tempRoot "apt")
        Copy-ProofRepository $operatorConsole (Join-Path $tempRoot "operator-console")

        Push-Location (Join-Path $tempRoot "management-platform")
        try {
            Invoke-Checked { npm.cmd ci --ignore-scripts } "Management Platform dependency restore failed."
            Invoke-Checked { npm.cmd test -- --run src/humanAuthentication.test.ts src/HumanAuthenticationShell.test.tsx src/identityAdministration.test.ts src/apiClient.test.ts } "Management Platform authentication tests failed."
            Invoke-Checked { npm.cmd run build } "Management Platform Production build failed."
        }
        finally { Pop-Location }

        Push-Location (Join-Path $tempRoot "operator-console")
        try {
            Invoke-Checked { npm.cmd ci --ignore-scripts } "Operator Console dependency restore failed."
            Invoke-Checked { npm.cmd run proof:authentication } "Operator Console authentication proof failed."
            Invoke-Checked { npm.cmd run build } "Operator Console Production build failed."
        }
        finally { Pop-Location }

        Push-Location (Join-Path $tempRoot "apt")
        try {
            Invoke-Checked { npm.cmd ci --ignore-scripts } "APT dependency restore failed."
            Invoke-Checked { powershell -ExecutionPolicy Bypass -File .\scripts\Invoke-AptHumanSessionProof.ps1 } "APT Windows host proof failed."
        }
        finally { Pop-Location }
    }

    Invoke-Checked { git -C $repoRoot diff --check } "git diff --check failed."
    Write-Host "I-022 cross-application authentication proof passed."
}
finally {
    if (Test-Path -LiteralPath $tempRoot) {
        $resolvedTemp = (Resolve-Path -LiteralPath $tempRoot).Path
        $systemTemp = ([System.IO.Path]::GetTempPath()).TrimEnd('\')
        if (-not $resolvedTemp.StartsWith($systemTemp, [StringComparison]::OrdinalIgnoreCase) -or
            -not (Split-Path -Leaf $resolvedTemp).StartsWith("exitpass-i022-proof-", [StringComparison]::Ordinal)) {
            throw "Refusing to remove unexpected proof path: $resolvedTemp"
        }
        Remove-Item -LiteralPath $resolvedTemp -Recurse -Force
    }
}
