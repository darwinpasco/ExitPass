param(
    [ValidateSet("SelfTest", "Start", "SetScenario", "State", "Stop", "Cleanup")]
    [string] $Action = "SelfTest",
    [ValidateSet(
        "validation-pending", "validation-failed", "scan-pending", "scan-retryable", "reviewable", "malware",
        "review-pending", "approved", "rejected", "applied", "not-required", "replacement-denied",
        "provider-unavailable", "expired-session", "upload-delayed", "service-unavailable", "access-denied",
        "malformed-response"
    )]
    [string] $Scenario = "validation-pending",
    [ValidateRange(1024, 65535)]
    [int] $Port = 5196
)

$ErrorActionPreference = "Stop"
$uiRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$localRoot = [System.IO.Path]::GetFullPath((Join-Path $uiRoot ".local"))
$manualRoot = [System.IO.Path]::GetFullPath((Join-Path $localRoot "g006-manual"))
$statePath = Join-Path $manualRoot "state.json"
$outLog = Join-Path $manualRoot "fixture.out.log"
$errLog = Join-Path $manualRoot "fixture.err.log"
$fixturePath = Join-Path $uiRoot "e2e\fixtures\webpay-browser-smoke-server.mjs"
$playwrightConfigPath = Join-Path $uiRoot "playwright.config.ts"
$evidenceSpecPath = Join-Path $uiRoot "e2e\webpay-statutory-evidence.spec.ts"
$packagePath = Join-Path $uiRoot "package.json"
$baseUrl = "http://127.0.0.1:$Port"
$paymentOrchestratorFixtureUrl = $baseUrl
$ticketReference = "WEBPAY-EVIDENCE-G006"
$siteGroupId = "40000000-0000-4000-8000-000000000001"
$siteId = "50000000-0000-4000-8000-000000000001"
$vendorSystemId = "60000000-0000-4000-8000-000000000001"

function Assert-SafeManualRoot {
    $expectedPrefix = $localRoot.TrimEnd('\') + '\'
    if (-not $manualRoot.StartsWith($expectedPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to use manual state outside the WebPay .local directory: $manualRoot"
    }
}

function Assert-LoopbackFixtureUrl {
    param(
        [Parameter(Mandatory = $true)][string] $Url,
        [Parameter(Mandatory = $true)][string] $Name
    )

    $parsed = $null
    if (-not [Uri]::TryCreate($Url, [UriKind]::Absolute, [ref] $parsed)) {
        throw "$Name is not a valid absolute URL."
    }
    if ($parsed.Scheme -ne "http" -or $parsed.Host -ne "127.0.0.1" -or $parsed.Port -ne $Port) {
        throw "$Name must use deterministic loopback port $Port."
    }
}

function Assert-DeterministicConfiguration {
    param(
        [AllowEmptyString()][string] $ConfiguredVendorSystemId = $vendorSystemId,
        [AllowEmptyString()][string] $ConfiguredWebPayUrl = $baseUrl,
        [AllowEmptyString()][string] $ConfiguredPaymentOrchestratorUrl = $paymentOrchestratorFixtureUrl
    )

    if ([string]::IsNullOrWhiteSpace($ConfiguredVendorSystemId)) {
        throw "VITE_WEBPAY_DEFAULT_VENDOR_SYSTEM_ID is required by the G-006 manual fixture."
    }
    if ($ConfiguredVendorSystemId -ne $vendorSystemId) {
        throw "VITE_WEBPAY_DEFAULT_VENDOR_SYSTEM_ID does not match the deterministic G-006 vendor fixture."
    }
    $parsedVendor = [Guid]::Empty
    if (-not [Guid]::TryParse($ConfiguredVendorSystemId, [ref] $parsedVendor) -or $parsedVendor -eq [Guid]::Empty) {
        throw "VITE_WEBPAY_DEFAULT_VENDOR_SYSTEM_ID must be a non-empty deterministic fixture GUID."
    }
    if ([string]::IsNullOrWhiteSpace($ticketReference)) {
        throw "The synthetic G-006 ticket mapping is missing."
    }

    Assert-LoopbackFixtureUrl -Url $ConfiguredWebPayUrl -Name "WebPay fixture URL"
    Assert-LoopbackFixtureUrl -Url $ConfiguredPaymentOrchestratorUrl -Name "Payment Orchestrator fixture URL"

    foreach ($requiredPath in @($fixturePath, $playwrightConfigPath, $evidenceSpecPath, $packagePath)) {
        if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf)) {
            throw "Required G-006 fixture file is missing: $requiredPath"
        }
    }
}

function Invoke-WithDeterministicWebPayEnvironment {
    param([Parameter(Mandatory = $true)][scriptblock] $Operation)

    $settings = [ordered]@{
        VITE_WEBPAY_DEFAULT_SITE_GROUP_ID = $siteGroupId
        VITE_WEBPAY_DEFAULT_SITE_ID = $siteId
        VITE_WEBPAY_DEFAULT_VENDOR_SYSTEM_ID = $vendorSystemId
        VITE_WEBPAY_API_BASE_URL = ""
    }
    $previous = @{}
    try {
        foreach ($entry in $settings.GetEnumerator()) {
            $previous[$entry.Key] = [Environment]::GetEnvironmentVariable($entry.Key, "Process")
            [Environment]::SetEnvironmentVariable($entry.Key, $entry.Value, "Process")
        }
        & $Operation
    }
    finally {
        foreach ($entry in $settings.GetEnumerator()) {
            [Environment]::SetEnvironmentVariable($entry.Key, $previous[$entry.Key], "Process")
        }
    }
}

function Invoke-WebPayBuild {
    Invoke-WithDeterministicWebPayEnvironment {
        & npm.cmd run build
        if ($LASTEXITCODE -ne 0) {
            throw "WebPay production build failed with exit code $LASTEXITCODE."
        }
    }
}

function Read-HarnessState {
    if (-not (Test-Path -LiteralPath $statePath -PathType Leaf)) {
        return $null
    }
    return Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json
}

function Get-Health {
    try {
        return Invoke-RestMethod -Uri "$baseUrl/__fixture/health" -Method Get -TimeoutSec 2
    }
    catch {
        return $null
    }
}

function Assert-HealthContract {
    $health = Get-Health
    if ($null -eq $health -or -not $health.ok) {
        throw "The G-006 fixture health contract is unavailable at $baseUrl."
    }
    $contract = $health.contract
    if ($null -eq $contract) {
        throw "The G-006 fixture health response is missing its runtime contract."
    }
    $expected = [ordered]@{
        webPayBaseUrl = $baseUrl
        paymentOrchestratorBaseUrl = $paymentOrchestratorFixtureUrl
        ticketReference = $ticketReference
        siteGroupId = $siteGroupId
        siteId = $siteId
        vendorSystemId = $vendorSystemId
    }
    foreach ($entry in $expected.GetEnumerator()) {
        if ([string] $contract.($entry.Key) -ne [string] $entry.Value) {
            throw "Fixture runtime contract mismatch for $($entry.Key)."
        }
    }
}

function Start-FixtureServer {
    New-Item -ItemType Directory -Path $manualRoot -Force | Out-Null
    $previousPort = [Environment]::GetEnvironmentVariable("WEBPAY_BROWSER_SMOKE_PORT", "Process")
    try {
        [Environment]::SetEnvironmentVariable("WEBPAY_BROWSER_SMOKE_PORT", [string] $Port, "Process")
        $start = @{
            FilePath = "node"
            ArgumentList = @($fixturePath)
            WorkingDirectory = $uiRoot
            WindowStyle = "Hidden"
            PassThru = $true
            RedirectStandardOutput = $outLog
            RedirectStandardError = $errLog
        }
        $server = Start-Process @start
    }
    finally {
        [Environment]::SetEnvironmentVariable("WEBPAY_BROWSER_SMOKE_PORT", $previousPort, "Process")
    }

    $ready = $false
    for ($attempt = 1; $attempt -le 60; $attempt += 1) {
        if ($server.HasExited) {
            break
        }
        if ($null -ne (Get-Health)) {
            $ready = $true
            break
        }
        Start-Sleep -Milliseconds 250
    }
    if (-not $ready) {
        if (-not $server.HasExited) {
            Stop-Process -Id $server.Id -Force
        }
        throw "G-006 fixture did not become healthy. Inspect $errLog."
    }

    Assert-HealthContract
    return $server
}

function Invoke-TicketResolutionProbe {
    $correlationId = [Guid]::NewGuid().ToString("D")
    $request = @{
        ticketReference = $ticketReference
        siteGroupId = $siteGroupId
        siteId = $siteId
        vendorSystemId = $vendorSystemId
        correlationId = $correlationId
    } | ConvertTo-Json -Compress
    $session = Invoke-RestMethod -Uri "$baseUrl/v1/webpay/parking-session" -Method Post `
        -ContentType "application/json" -Body $request
    if ($session.ticketReference -ne $ticketReference -or
        $session.siteGroupId -ne $siteGroupId -or
        $session.siteId -ne $siteId -or
        $session.vendorSystemId -ne $vendorSystemId) {
        throw "The actual WebPay parking-session route returned an inconsistent G-006 fixture mapping."
    }
    $fixture = Invoke-RestMethod -Uri "$baseUrl/__fixture/state" -Method Get
    if ($null -eq $fixture.evidence -or $fixture.evidence.scenario -ne "validation-pending") {
        throw "The required G-006 scenario state is missing or inconsistent."
    }
    Write-Host "Ticket resolution probe: PASSED ($ticketReference -> $($session.parkingSessionId))"
}

function Invoke-BrowserReachabilityProbe {
    $previousPort = [Environment]::GetEnvironmentVariable("WEBPAY_BROWSER_SMOKE_PORT", "Process")
    $previousArtifacts = [Environment]::GetEnvironmentVariable("WEBPAY_BROWSER_SMOKE_ARTIFACT_ROOT", "Process")
    try {
        [Environment]::SetEnvironmentVariable("WEBPAY_BROWSER_SMOKE_PORT", [string] $Port, "Process")
        [Environment]::SetEnvironmentVariable(
            "WEBPAY_BROWSER_SMOKE_ARTIFACT_ROOT",
            (Join-Path $manualRoot "selftest-browser-artifacts"),
            "Process"
        )
        & npx.cmd playwright test e2e/webpay-statutory-evidence.spec.ts --config playwright.config.ts `
            --grep "manual harness deterministic configuration resolves the synthetic ticket"
        if ($LASTEXITCODE -ne 0) {
            throw "The G-006 browser reachability probe failed with exit code $LASTEXITCODE."
        }
    }
    finally {
        [Environment]::SetEnvironmentVariable("WEBPAY_BROWSER_SMOKE_PORT", $previousPort, "Process")
        [Environment]::SetEnvironmentVariable("WEBPAY_BROWSER_SMOKE_ARTIFACT_ROOT", $previousArtifacts, "Process")
    }
    Write-Host "Browser evidence-panel probe: PASSED"
}

function Stop-OwnedFixture {
    $state = Read-HarnessState
    if ($null -eq $state) {
        Write-Host "No G-006 manual fixture PID is recorded."
        return
    }

    $process = Get-Process -Id ([int] $state.pid) -ErrorAction SilentlyContinue
    if ($null -ne $process) {
        if ($process.ProcessName -notin @("node", "nodejs")) {
            throw "Recorded PID $($state.pid) is not a Node process; refusing to stop it."
        }
        Stop-Process -Id $process.Id -Force
        $process.WaitForExit(5000) | Out-Null
        Write-Host "Stopped harness-owned fixture PID $($state.pid)."
    }
    else {
        Write-Host "Recorded fixture PID $($state.pid) is no longer running."
    }
}

function Set-EvidenceScenario {
    param([Parameter(Mandatory = $true)][string] $Name)

    if ($null -eq (Get-Health)) {
        throw "The G-006 fixture is not healthy at $baseUrl. Start it first."
    }
    Invoke-RestMethod -Uri "$baseUrl/__fixture/reset" -Method Post -ContentType "application/json" -Body "{}" | Out-Null
    Invoke-RestMethod -Uri "$baseUrl/__fixture/evidence-scenario" -Method Post -ContentType "application/json" `
        -Body (@{ scenario = $Name } | ConvertTo-Json -Compress) | Out-Null
    Write-Host "Scenario: $Name"
    Write-Host "WebPay URL: $baseUrl/"
    Write-Host "Synthetic ticket: $ticketReference"
}

function Show-FixtureState {
    if ($null -eq (Get-Health)) {
        throw "The G-006 fixture is not healthy at $baseUrl."
    }
    $fixture = Invoke-RestMethod -Uri "$baseUrl/__fixture/state" -Method Get
    Write-Host "Evidence scenario: $($fixture.evidence.scenario)"
    Write-Host "Lifecycle: $($fixture.evidence.lifecycleClassification)"
    Write-Host "Bootstrap requests: $($fixture.evidence.bootstrapCount)"
    Write-Host "Status reads: $($fixture.evidence.statusCount)"
    Write-Host "Upload-session requests: $($fixture.evidence.uploadSessionCount)"
    Write-Host "Opaque uploads: $($fixture.evidence.uploadCount)"
    Write-Host "Finalizations: $($fixture.evidence.finalizeCount)"
    Write-Host "Observed byte count (bytes are not retained): $($fixture.evidence.uploadedByteCount)"
    Write-Host "Declared content type: $($fixture.evidence.lastDeclaredContentType)"
    Write-Host "Declared content length: $($fixture.evidence.lastDeclaredContentLength)"
}

Assert-SafeManualRoot

switch ($Action) {
    "SelfTest" {
        Assert-DeterministicConfiguration
        $parseErrors = $null
        [System.Management.Automation.Language.Parser]::ParseFile($PSCommandPath, [ref] $null, [ref] $parseErrors) | Out-Null
        if ($parseErrors.Count -ne 0) {
            $parseErrors | Format-List
            throw "G-006 manual harness contains PowerShell parser errors."
        }
        & node --check $fixturePath
        if ($LASTEXITCODE -ne 0) {
            throw "Fixture Node syntax validation failed."
        }

        $missingVendorDetected = $false
        try {
            Assert-DeterministicConfiguration -ConfiguredVendorSystemId ""
        }
        catch {
            $missingVendorDetected = $_.Exception.Message -like "*VITE_WEBPAY_DEFAULT_VENDOR_SYSTEM_ID is required*"
        }
        if (-not $missingVendorDetected) {
            throw "Harness regression control failed to detect missing vendor configuration."
        }

        $mismatchedVendorDetected = $false
        try {
            Assert-DeterministicConfiguration -ConfiguredVendorSystemId "60000000-0000-4000-8000-000000000099"
        }
        catch {
            $mismatchedVendorDetected = $_.Exception.Message -like "*does not match the deterministic G-006 vendor fixture*"
        }
        if (-not $mismatchedVendorDetected) {
            throw "Harness regression control failed to detect mismatched vendor configuration."
        }

        if (Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue) {
            throw "Port $Port is already occupied. SelfTest did not start or stop any process."
        }

        $selfTestServer = $null
        New-Item -ItemType Directory -Path $manualRoot -Force | Out-Null
        Push-Location $uiRoot
        try {
            Invoke-WebPayBuild
            $selfTestServer = Start-FixtureServer
            Set-EvidenceScenario -Name "validation-pending"
            Invoke-TicketResolutionProbe
            Invoke-BrowserReachabilityProbe
        }
        finally {
            if ($null -ne $selfTestServer -and -not $selfTestServer.HasExited) {
                Stop-Process -Id $selfTestServer.Id -Force
                $selfTestServer.WaitForExit(5000) | Out-Null
            }
            Pop-Location
            if (Test-Path -LiteralPath $manualRoot) {
                Remove-Item -LiteralPath $manualRoot -Recurse -Force
            }
        }
        Write-Host "G-006 manual harness self-test: PASSED"
    }
    "Start" {
        Assert-DeterministicConfiguration
        if ($null -ne (Read-HarnessState)) {
            throw "G-006 manual state already exists. Run -Action Stop and -Action Cleanup first."
        }
        if (Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue) {
            throw "Port $Port is already occupied. No process was stopped."
        }

        New-Item -ItemType Directory -Path $manualRoot -Force | Out-Null
        Push-Location $uiRoot
        try {
            Invoke-WebPayBuild
            $server = Start-FixtureServer
            @{
                pid = $server.Id
                port = $Port
                startedAt = [DateTimeOffset]::UtcNow.ToString("O")
                webPayBaseUrl = $baseUrl
                paymentOrchestratorBaseUrl = $paymentOrchestratorFixtureUrl
                ticketReference = $ticketReference
                siteGroupId = $siteGroupId
                siteId = $siteId
                vendorSystemId = $vendorSystemId
            } |
                ConvertTo-Json | Set-Content -LiteralPath $statePath -Encoding UTF8
            Set-EvidenceScenario -Name $Scenario
            Invoke-TicketResolutionProbe
            Set-EvidenceScenario -Name $Scenario
            Write-Host "Fixture PID: $($server.Id)"
            Write-Host "Fixture logs: $outLog and $errLog"
            Write-Host "WebPay URL: $baseUrl/"
            Write-Host "Payment Orchestrator fixture URL: $paymentOrchestratorFixtureUrl"
            Write-Host "Deterministic vendor system ID: $vendorSystemId"
            Write-Host "First screen: enter ticket $ticketReference, click Continue, then request the statutory discount."
            Write-Host "Leave this fixture running while inspecting Edge and DevTools."
        }
        finally {
            Pop-Location
        }
    }
    "SetScenario" { Set-EvidenceScenario -Name $Scenario }
    "State" { Show-FixtureState }
    "Stop" { Stop-OwnedFixture }
    "Cleanup" {
        Stop-OwnedFixture
        if (Test-Path -LiteralPath $manualRoot) {
            Remove-Item -LiteralPath $manualRoot -Recurse -Force
            Write-Host "Removed only harness-generated state: $manualRoot"
        }
    }
}
