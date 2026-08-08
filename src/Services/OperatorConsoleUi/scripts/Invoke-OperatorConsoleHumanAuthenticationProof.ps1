$ErrorActionPreference = "Stop"

$uiRoot = Split-Path -Parent $PSScriptRoot
$sourceRoot = Join-Path $uiRoot "src"
$productionSources = Get-ChildItem -LiteralPath $sourceRoot -File -Include *.ts,*.tsx -Recurse |
    Where-Object { $_.Name -notlike "*.test.*" }

function Assert-NoMatch {
    param(
        [string] $Description,
        [string] $Pattern
    )

    $matches = $productionSources | Select-String -Pattern $Pattern
    if ($matches) {
        $matches | ForEach-Object { Write-Error "$($_.Path):$($_.LineNumber): $($_.Line.Trim())" }
        throw $Description
    }
}

Push-Location $uiRoot
try {
    Assert-NoMatch "Production Operator Console source emits fixture human-authority headers." `
        'X-Operator-User-Id|X-ExitPass-User-Id|X-ExitPass-Permissions'
    Assert-NoMatch "Production Operator Console source persists authentication authority." `
        'localStorage\.(setItem|\w+\s*=)|sessionStorage\.(setItem|\w+\s*=)|indexedDB\.open'
    Assert-NoMatch "Production Operator Console source contains a browser bearer or refresh-token authority." `
        'Authorization\s*:|Bearer\s+|bearerToken|accessToken|authenticationRefreshToken'
    Assert-NoMatch "Operator Console production UI contains a TOTP entry flow." `
        'totpCode|oneTimeCode|provisioningUri|TOTP seed'
    Assert-NoMatch "Operator Console production source revived a legacy payable-application route." `
        '/v1/ops/operator-console/statutory-discounts/.+/(apply|payable-basis)'

    & npm.cmd run typecheck
    if ($LASTEXITCODE -ne 0) { throw "Operator Console typecheck failed." }

    & npm.cmd run test:authentication
    if ($LASTEXITCODE -ne 0) { throw "Operator Console focused authentication tests failed." }

    Write-Host "Operator Console H-008 human-authentication proof passed."
}
finally {
    Pop-Location
}
