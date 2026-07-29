<#
.SYNOPSIS
    Runs the local manual-validation harness for WebPay statutory service authentication.

.DESCRIPTION
    Starts a harness-owned Central PMS statutory contract stub, Payment Orchestrator,
    and WebPay UI for deterministic service-authentication and safe-error validation.
    The stub is local-only and validates the Payment Orchestrator server-to-server
    headers for statutory routes. It is not controlled UAT and does not replace
    Central PMS RBAC integration tests.
#>

[CmdletBinding(DefaultParameterSetName = "Run")]
param(
    [ValidateSet(
        "Valid",
        "MissingConfiguration",
        "RejectedIdentity",
        "PermissionDenied",
        "Timeout",
        "Unavailable",
        "ValidationFailure",
        "Conflict",
        "IdempotentReplay",
        "All")]
    [string] $Scenario = "Valid",

    [switch] $Start,
    [switch] $ProbeOnly,
    [switch] $DryRun,
    [switch] $Stop,
    [switch] $Cleanup,
    [switch] $SelfTest,
    [switch] $RunCentralPmsStub,
    [switch] $NoWebPay,
    [switch] $BrowserRouteProbe,
    [switch] $ResetBrowserRecovery,

    [int] $CentralPmsPort = 5080,
    [int] $PaymentOrchestratorPort = 5081,
    [int] $WebPayPort = 5173,
    [int] $TimeoutDelaySeconds = 45,

    [string] $LogRoot = ".local/webpay-statutory-service-auth"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..\..")
$repoRootText = [string] $repoRoot
$logRootPath = Join-Path $repoRoot $LogRoot
$pidRoot = Join-Path $logRootPath "pids"
$logsRoot = Join-Path $logRootPath "logs"
$stateRoot = Join-Path $logRootPath "state"
$scriptPath = $PSCommandPath

$validServiceIdentityId = "9b000000-0000-0000-0000-000000000005"
$rejectedServiceIdentityId = "9b000000-0000-0000-0000-000000000007"
$permissionDeniedServiceIdentityId = "9b000000-0000-0000-0000-000000000008"
$siteGroupId = "40000000-0000-4000-8000-000000000001"
$siteId = "50000000-0000-4000-8000-000000000001"
$vendorSystemId = "60000000-0000-4000-8000-000000000001"
$parkingSessionId = "20000000-0000-4000-8000-000000000001"
$originalTariffSnapshotId = "30000000-0000-4000-8000-000000000001"
$decisionCommandId = "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa"
$requestReference = "bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb"
$ticketReference = "WEBPAY-STAT-SERVICE-AUTH-001"
$plateNumber = "SVC 0001"
$submitPermission = "statutory-discounts.decision.submit.webpay"
$readPermission = "statutory-discounts.decision.read"
$serviceIdentityHeader = "X-ExitPass-Service-Identity-Id"
$permissionsHeader = "X-ExitPass-Permissions"

$scenarioList = @(
    "Valid",
    "MissingConfiguration",
    "RejectedIdentity",
    "PermissionDenied",
    "Timeout",
    "Unavailable",
    "ValidationFailure",
    "Conflict",
    "IdempotentReplay"
)

function Get-ScenariosToRun {
    if ($Scenario -eq "All") {
        return $scenarioList
    }

    return @($Scenario)
}

function Ensure-HarnessDirectories {
    New-Item -ItemType Directory -Force -Path $pidRoot, $logsRoot, $stateRoot | Out-Null
}

function Write-Info {
    param([string] $Message)
    Write-Host "[webpay-service-auth] $Message"
}

function Assert-CommandExists {
    param([string] $Command)
    if (-not (Get-Command $Command -ErrorAction SilentlyContinue)) {
        throw "Required command '$Command' was not found on PATH."
    }
}

function Test-PortInUse {
    param([int] $Port)
    return [System.Net.NetworkInformation.IPGlobalProperties]::GetIPGlobalProperties().
        GetActiveTcpListeners().
        Where({ $_.Port -eq $Port }).
        Count -gt 0
}

function Assert-PortAvailable {
    param([int] $Port, [string] $Name)
    if (Test-PortInUse -Port $Port) {
        throw "$Name port $Port is already in use. Stop the occupying process or pass an explicit free port."
    }
}

function Invoke-JsonRequest {
    param(
        [string] $Method,
        [string] $Uri,
        [hashtable] $Headers,
        [object] $Body,
        [int] $TimeoutSec = 10
    )

    $parameters = @{
        Method = $Method
        Uri = $Uri
        Headers = $Headers
        UseBasicParsing = $true
        TimeoutSec = $TimeoutSec
        ErrorAction = "Stop"
    }

    if ($null -ne $Body) {
        $parameters.ContentType = "application/json"
        $parameters.Body = ($Body | ConvertTo-Json -Depth 12)
    }

    try {
        $response = Invoke-WebRequest @parameters
        return [pscustomobject]@{
            StatusCode = [int] $response.StatusCode
            Headers = $response.Headers
            Body = $response.Content
        }
    }
    catch {
        $webResponse = $_.Exception.Response
        if ($null -eq $webResponse) {
            throw
        }

        $reader = New-Object System.IO.StreamReader($webResponse.GetResponseStream())
        $content = $reader.ReadToEnd()
        return [pscustomobject]@{
            StatusCode = [int] $webResponse.StatusCode
            Headers = $webResponse.Headers
            Body = $content
        }
    }
}

function ConvertTo-PowerShellSingleQuotedLiteral {
    param([string] $Value)
    return "'" + ($Value -replace "'", "''") + "'"
}

function ConvertTo-CommandLineArgument {
    param([string] $Value)
    return '"' + ($Value -replace '\\', '\\' -replace '"', '\"') + '"'
}

function Wait-ForHttp {
    param([string] $Url, [int] $Seconds = 60)
    $deadline = (Get-Date).AddSeconds($Seconds)
    while ((Get-Date) -lt $deadline) {
        try {
            $response = Invoke-WebRequest -Uri $Url -UseBasicParsing -TimeoutSec 2
            if ($response.StatusCode -ge 200 -and $response.StatusCode -lt 500) {
                return
            }
        }
        catch {
            Start-Sleep -Milliseconds 500
        }
    }

    throw "Timed out waiting for $Url."
}

function Get-ScenarioServiceIdentity {
    param([string] $Name)
    switch ($Name) {
        "MissingConfiguration" { return "" }
        "RejectedIdentity" { return $rejectedServiceIdentityId }
        "PermissionDenied" { return $permissionDeniedServiceIdentityId }
        default { return $validServiceIdentityId }
    }
}

function Get-ScenarioExpectedCode {
    param([string] $Name)
    switch ($Name) {
        "Valid" { return $null }
        "IdempotentReplay" { return $null }
        "ValidationFailure" { return "WEBPAY_STATUTORY_DISCOUNT_REQUEST_INVALID" }
        "Conflict" { return "STATUTORY_DISCOUNT_DECISION_SEMANTIC_CONFLICT" }
        "Timeout" { return "WEBPAY_STATUTORY_REQUEST_TEMPORARILY_UNAVAILABLE" }
        "Unavailable" { return "WEBPAY_STATUTORY_REQUEST_TEMPORARILY_UNAVAILABLE" }
        default { return "WEBPAY_STATUTORY_SERVICE_UNAVAILABLE" }
    }
}

function New-ScenarioRequestBody {
    param([string] $Name)
    $masked = if ($Name -eq "ValidationFailure") { "123456789012" } else { "SC-****-1234" }

    return [ordered]@{
        requestReference = $requestReference
        parkingSessionId = $parkingSessionId
        siteId = $siteId
        siteGroupId = $siteGroupId
        ticketReference = $ticketReference
        plateNumber = $plateNumber
        entitlementType = "SENIOR_CITIZEN"
        idDocumentType = "OSCA ID"
        issuingAuthority = "Local validation fixture"
        expiryDate = "2027-12-31"
        maskedIdReference = $masked
        evidenceCaptureRequested = $false
        requesterAttestation = $true
        originalTariffSnapshotId = $originalTariffSnapshotId
    }
}

function New-CentralPmsStubNodeScript {
    param([string] $Name)

    Ensure-HarnessDirectories
    $stubScript = Join-Path $stateRoot "central-pms-statutory-stub-$Name.cjs"
    $requestLogPathJson = (Join-Path $logsRoot "central-pms-statutory-stub.requests.jsonl") | ConvertTo-Json
    $statePathJson = (Join-Path $stateRoot "central-pms-stub-$Name.txt") | ConvertTo-Json
    $decisionBodyJson = New-DecisionBody -CorrelationId "00000000-0000-4000-8000-000000000000" | ConvertTo-Json -Depth 20
    $parkingBodyJson = ([ordered]@{
        parkingSessionId = $parkingSessionId
        tariffSnapshotId = $originalTariffSnapshotId
        siteGroupId = $siteGroupId
        siteId = $siteId
        lookupOutcome = "FOUND"
        plateNumber = $plateNumber
        ticketReference = $ticketReference
        netPayableMinorUnits = 13750
        currency = "PHP"
        tariffExpiresAt = "2026-07-29T10:15:00+08:00"
        feeValidUntil = "2026-07-29T10:15:00+08:00"
        vendorSystemId = $vendorSystemId
        correlationId = "00000000-0000-4000-8000-000000000000"
        siteGroupName = "Service Auth Local Site Group"
        siteName = "Service Auth Local Site"
        entryTime = "2026-07-29T08:00:00+08:00"
        currentFeeCalculationTime = "2026-07-29T09:00:00+08:00"
        tariffName = "Local Service Auth Tariff"
        parkingStatus = "PaymentRequired"
        paymentStatus = "Not Started"
    }) | ConvertTo-Json -Depth 20

    $script = @"
const http = require('http');
const fs = require('fs');
const crypto = require('crypto');

const scenario = '$Name';
const port = $CentralPmsPort;
const validServiceIdentityId = '$validServiceIdentityId';
const serviceIdentityHeader = '$($serviceIdentityHeader.ToLowerInvariant())';
const permissionsHeader = '$($permissionsHeader.ToLowerInvariant())';
const submitPermission = '$submitPermission';
const readPermission = '$readPermission';
const decisionCommandId = '$decisionCommandId';
const timeoutDelayMilliseconds = $($TimeoutDelaySeconds * 1000);
const requestLogPath = $requestLogPathJson;
const statePath = $statePathJson;
const decisionTemplate = $decisionBodyJson;
const parkingTemplate = $parkingBodyJson;

function writeJson(response, statusCode, body) {
  const json = JSON.stringify(body);
  response.writeHead(statusCode, {
    'Content-Type': 'application/json',
    'Content-Length': Buffer.byteLength(json)
  });
  response.end(json);
}

function errorBody(code, message, retryable, correlationId) {
  return {
    code,
    safeErrorCode: code,
    message,
    retryable,
    correlationId
  };
}

function decisionBody(correlationId) {
  return { ...decisionTemplate, correlationId };
}

function parkingBody(correlationId) {
  return { ...parkingTemplate, correlationId };
}

function handleRequest(request, response) {
  const path = new URL(request.url, 'http://127.0.0.1:' + port).pathname;
  const correlationId = request.headers['x-correlation-id'] || crypto.randomUUID();

  if (path === '/__health') {
    writeJson(response, 200, { status: 'ready', scenario });
    return;
  }

  const record = {
    at: new Date().toISOString(),
    method: request.method,
    path,
    scenario,
    correlationId,
    serviceIdentityHeaderPresent: Boolean(request.headers[serviceIdentityHeader]),
    permissionHeaderPresent: Boolean(request.headers[permissionsHeader]),
    serviceIdentity: request.headers[serviceIdentityHeader] || null,
    permission: request.headers[permissionsHeader] || null
  };
  fs.appendFileSync(requestLogPath, JSON.stringify(record) + '\n');

  if (path === '/v1/vendor-parking/resolve' && request.method === 'POST') {
    writeJson(response, 200, parkingBody(correlationId));
    return;
  }

  const isStatutoryPost = path === '/v1/statutory-discounts/decisions' && request.method === 'POST';
  const isStatutoryGet = path === '/v1/statutory-discounts/decisions/' + decisionCommandId && request.method === 'GET';
  if (!isStatutoryPost && !isStatutoryGet) {
    writeJson(response, 404, errorBody('NOT_FOUND', 'Not found.', false, correlationId));
    return;
  }

  const finishStatutory = () => {
    if (scenario === 'Unavailable') {
      writeJson(response, 503, errorBody('CENTRAL_PMS_UNAVAILABLE', 'Central PMS statutory operation is temporarily unavailable.', true, correlationId));
      return;
    }

    if (scenario === 'RejectedIdentity') {
      writeJson(response, 401, errorBody('CENTRAL_PMS_AUTHENTICATED_ACTOR_REQUIRED', 'Authenticated user or service identity is required for statutory-discount decision submission.', false, correlationId));
      return;
    }

    if (scenario === 'PermissionDenied') {
      writeJson(response, 403, errorBody('CENTRAL_PMS_POLICY_DENIED', 'CentralPmsStatutoryDiscountDecisionSubmit denied permission statutory-discounts.decision.submit.webpay.', false, correlationId));
      return;
    }

    if (scenario === 'ValidationFailure') {
      writeJson(response, 400, errorBody('STATUTORY_DISCOUNT_REQUEST_INVALID', 'Enter a valid masked ID reference.', false, correlationId));
      return;
    }

    if (scenario === 'Conflict') {
      writeJson(response, 409, errorBody('STATUTORY_DISCOUNT_DECISION_SEMANTIC_CONFLICT', 'A statutory discount request already exists with different submitted details.', false, correlationId));
      return;
    }

    const expectedPermission = isStatutoryGet ? readPermission : submitPermission;
    if (request.headers[serviceIdentityHeader] !== validServiceIdentityId || request.headers[permissionsHeader] !== expectedPermission) {
      writeJson(response, 403, errorBody('CENTRAL_PMS_POLICY_DENIED', 'CentralPmsStatutoryDiscountDecisionSubmit denied internal service auth headers.', false, correlationId));
      return;
    }

    writeJson(response, 200, decisionBody(correlationId));
  };

  if (scenario === 'Timeout') {
    setTimeout(finishStatutory, timeoutDelayMilliseconds);
    return;
  }

  finishStatutory();
}

fs.writeFileSync(statePath, 'Listening http://127.0.0.1:' + port + '/ scenario ' + scenario);
http.createServer(handleRequest).listen(port, '127.0.0.1');
"@

    Set-Content -Path $stubScript -Value $script -Encoding ASCII
    return $stubScript
}

function Get-PaymentOrchestratorEnvironment {
    param([string] $Name)
    $envMap = @{
        "ASPNETCORE_ENVIRONMENT" = "Development"
        "ConnectionStrings__MainDatabase" = "Host=127.0.0.1;Port=59999;Database=exitpass_statutory_service_auth_unused;Username=exitpass;Include Error Detail=false"
        "Integrations__CentralPms__BaseUrl" = "http://127.0.0.1:$CentralPmsPort"
        "Payments__Providers__PayMongo__SecretKey" = "sk_test_local_service_auth_placeholder"
        "Payments__Providers__PayMongo__PublicKey" = "pk_test_local_service_auth_placeholder"
        "Payments__Providers__PayMongo__WebhookSecretKey" = "whsec_test_local_service_auth_placeholder"
        "Payments__Providers__PayMongo__BaseUrl" = "http://127.0.0.1:59998"
        "PAYMONGO_SECRET_KEY" = "sk_test_local_service_auth_placeholder"
        "PAYMONGO_PUBLIC_KEY" = "pk_test_local_service_auth_placeholder"
        "PAYMONGO_WEBHOOK_SECRET_KEY" = "whsec_test_local_service_auth_placeholder"
        "PAYMONGO_BASE_URL" = "http://127.0.0.1:59998"
        "WEBPAY_PUBLIC_BASE_URL" = "http://127.0.0.1:$WebPayPort"
        "WebPay__PublicBaseUrl" = "http://127.0.0.1:$WebPayPort"
    }

    $serviceIdentity = Get-ScenarioServiceIdentity -Name $Name
    if (-not [string]::IsNullOrWhiteSpace($serviceIdentity)) {
        $envMap["Integrations__CentralPms__StatutoryDiscounts__WebPayServiceIdentityId"] = $serviceIdentity
    }

    return $envMap
}

function Start-HarnessProcess {
    param(
        [string] $Name,
        [string] $FileName,
        [string[]] $Arguments,
        [string] $WorkingDirectory,
        [hashtable] $Environment = @{}
    )

    Ensure-HarnessDirectories
    $stdout = Join-Path $logsRoot "$Name.out.log"
    $stderr = Join-Path $logsRoot "$Name.err.log"
    $launcher = Join-Path $stateRoot "$Name.launch.ps1"
    $launcherLines = New-Object System.Collections.Generic.List[string]
    $launcherLines.Add('$ErrorActionPreference = "Stop"')
    foreach ($entry in $Environment.GetEnumerator()) {
        $key = $entry.Key
        $value = ConvertTo-PowerShellSingleQuotedLiteral -Value ([string] $entry.Value)
        $launcherLines.Add("`$env:$key = $value")
    }

    $fileNameLiteral = ConvertTo-PowerShellSingleQuotedLiteral -Value $FileName
    $argumentLiterals = @($Arguments | ForEach-Object { ConvertTo-PowerShellSingleQuotedLiteral -Value $_ })
    $stdoutLiteral = ConvertTo-PowerShellSingleQuotedLiteral -Value $stdout
    $stderrLiteral = ConvertTo-PowerShellSingleQuotedLiteral -Value $stderr
    $launcherLines.Add("& $fileNameLiteral @(")
    foreach ($argumentLiteral in $argumentLiterals) {
        $launcherLines.Add("    $argumentLiteral")
    }
    $launcherLines.Add(") 1> $stdoutLiteral 2> $stderrLiteral")
    Set-Content -Path $launcher -Value $launcherLines -Encoding ASCII

    $processStartInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $processStartInfo.FileName = "powershell"
    $processStartInfo.WorkingDirectory = $WorkingDirectory
    $processStartInfo.UseShellExecute = $false
    $processStartInfo.CreateNoWindow = $true
    $processStartInfo.Arguments = "-NoProfile -ExecutionPolicy Bypass -File " + (ConvertTo-CommandLineArgument -Value $launcher)

    $process = [System.Diagnostics.Process]::Start($processStartInfo)

    Set-Content -Path (Join-Path $pidRoot "$Name.pid") -Value $process.Id -Encoding ASCII
    Write-Info "Started $Name with PID $($process.Id). Logs: $stdout, $stderr"
}

function Test-PortDetectionSelfCheck {
    $listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Parse("127.0.0.1"), 0)
    try {
        $listener.Start()
        $port = $listener.LocalEndpoint.Port
        if (-not (Test-PortInUse -Port $port)) {
            throw "Occupied-port detection did not detect listener on port $port."
        }
    }
    finally {
        $listener.Stop()
    }
}

function Stop-HarnessProcesses {
    Ensure-HarnessDirectories
    Get-ChildItem -Path $pidRoot -Filter "*.pid" -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -notlike "*.stdout-job.pid" -and $_.Name -notlike "*.stderr-job.pid" } |
        ForEach-Object {
            $name = $_.BaseName
            $pidValue = (Get-Content -Path $_.FullName -ErrorAction SilentlyContinue | Select-Object -First 1)
            if ($pidValue -match "^\d+$") {
                $ownedPids = @(Get-DescendantProcessIds -ParentProcessId ([int] $pidValue))
                [array]::Reverse($ownedPids)
                foreach ($ownedPid in $ownedPids) {
                    $childProcess = Get-Process -Id $ownedPid -ErrorAction SilentlyContinue
                    if ($null -ne $childProcess) {
                        Write-Info "Stopping harness-owned child process $name PID $ownedPid."
                        Stop-Process -Id $ownedPid -Force -ErrorAction SilentlyContinue
                    }
                }

                $process = Get-Process -Id ([int] $pidValue) -ErrorAction SilentlyContinue
                if ($null -ne $process) {
                    Write-Info "Stopping harness-owned process $name PID $pidValue."
                    Stop-Process -Id ([int] $pidValue) -Force -ErrorAction SilentlyContinue
                }
            }
            Remove-Item -LiteralPath $_.FullName -Force -ErrorAction SilentlyContinue
        }

    Get-ChildItem -Path $pidRoot -Filter "*.job.pid" -ErrorAction SilentlyContinue |
        ForEach-Object { Remove-Item -LiteralPath $_.FullName -Force -ErrorAction SilentlyContinue }

    Stop-WorktreeListenersOnHarnessPorts
    Stop-DetachedWorktreeHarnessProcesses
}

function Get-DescendantProcessIds {
    param([int] $ParentProcessId)
    $children = @(Get-CimInstance Win32_Process -Filter "ParentProcessId = $ParentProcessId" -ErrorAction SilentlyContinue)
    $ids = New-Object System.Collections.Generic.List[int]
    foreach ($child in $children) {
        $ids.Add([int] $child.ProcessId)
        foreach ($descendant in (Get-DescendantProcessIds -ParentProcessId ([int] $child.ProcessId))) {
            $ids.Add([int] $descendant)
        }
    }

    return $ids.ToArray()
}

function Stop-WorktreeListenersOnHarnessPorts {
    $ports = @($CentralPmsPort, $PaymentOrchestratorPort, $WebPayPort)
    $listeners = @(Get-NetTCPConnection -LocalPort $ports -State Listen -ErrorAction SilentlyContinue)
    foreach ($listener in $listeners) {
        $processId = [int] $listener.OwningProcess
        $commandLine = $null
        try {
            $processInfo = Get-CimInstance Win32_Process -Filter "ProcessId = $processId" -ErrorAction Stop
            $commandLine = $processInfo.CommandLine
        }
        catch {
            $commandLine = $null
        }

        if ($commandLine -and $commandLine.Contains($repoRoot)) {
            foreach ($childId in (Get-DescendantProcessIds -ParentProcessId $processId)) {
                $child = Get-Process -Id $childId -ErrorAction SilentlyContinue
                if ($null -ne $child) {
                    Write-Info "Stopping harness-owned listener child PID $childId on port $($listener.LocalPort)."
                    Stop-Process -Id $childId -Force -ErrorAction SilentlyContinue
                }
            }

            Write-Info "Stopping harness-owned listener PID $processId on port $($listener.LocalPort)."
            Stop-Process -Id $processId -Force -ErrorAction SilentlyContinue
        }
    }

    foreach ($port in $ports) {
        $netstatLines = @(netstat -ano -p tcp | Select-String -Pattern ":$port\s+.*LISTENING\s+(\d+)" -AllMatches)
        foreach ($line in $netstatLines) {
            foreach ($match in $line.Matches) {
                $processId = [int] $match.Groups[1].Value
                if ($processId -eq 0) {
                    continue
                }

                $process = Get-Process -Id $processId -ErrorAction SilentlyContinue
                if ($null -ne $process) {
                    Write-Info "Stopping harness-port listener PID $processId on port $port."
                    Stop-Process -Id $processId -Force -ErrorAction SilentlyContinue
                }
            }
        }
    }
}

function Stop-DetachedWorktreeHarnessProcesses {
    $currentProcessId = $PID
    try {
        $processes = @(Get-CimInstance Win32_Process -ErrorAction Stop | Where-Object {
            $_.ProcessId -ne $currentProcessId -and
            $_.CommandLine -and
            $_.CommandLine.Contains($repoRootText) -and
            (
                $_.CommandLine.Contains("ExitPass.PaymentOrchestrator.Api.csproj") -or
                $_.CommandLine.Contains("Invoke-WebPayStatutoryServiceAuthManualValidation.ps1") -or
                $_.CommandLine.Contains("src\Services\WebPayUi\node_modules") -or
                $_.CommandLine.Contains("vite")
            )
        })
    }
    catch {
        return
    }

    foreach ($processInfo in $processes) {
        $processId = [int] $processInfo.ProcessId
        foreach ($childId in (Get-DescendantProcessIds -ParentProcessId $processId)) {
            $child = Get-Process -Id $childId -ErrorAction SilentlyContinue
            if ($null -ne $child) {
                Write-Info "Stopping detached harness child PID $childId."
                Stop-Process -Id $childId -Force -ErrorAction SilentlyContinue
            }
        }

        $process = Get-Process -Id $processId -ErrorAction SilentlyContinue
        if ($null -ne $process) {
            Write-Info "Stopping detached harness process PID $processId."
            Stop-Process -Id $processId -Force -ErrorAction SilentlyContinue
        }
    }
}

function Start-Scenario {
    param([string] $Name)
    Ensure-HarnessDirectories
    Assert-CommandExists "dotnet"
    Assert-CommandExists "npm.cmd"
    Assert-CommandExists "powershell"

    Assert-PortAvailable -Port $CentralPmsPort -Name "Central PMS statutory stub"
    Assert-PortAvailable -Port $PaymentOrchestratorPort -Name "Payment Orchestrator"
    if (-not $NoWebPay) {
        Assert-PortAvailable -Port $WebPayPort -Name "WebPay"
    }

    $centralPmsStubScript = New-CentralPmsStubNodeScript -Name $Name
    Start-HarnessProcess `
        -Name "central-pms-statutory-stub" `
        -FileName "node" `
        -Arguments @($centralPmsStubScript) `
        -WorkingDirectory $repoRoot
    Wait-ForHttp -Url "http://127.0.0.1:$CentralPmsPort/__health" -Seconds 20

    Start-HarnessProcess `
        -Name "payment-orchestrator" `
        -FileName "dotnet" `
        -Arguments @(
            "run",
            "--project",
            (Join-Path $repoRoot "src\Services\PaymentOrchestrator\src\ExitPass.PaymentOrchestrator.Api\ExitPass.PaymentOrchestrator.Api.csproj"),
            "--urls",
            "http://127.0.0.1:$PaymentOrchestratorPort"
        ) `
        -WorkingDirectory $repoRoot `
        -Environment (Get-PaymentOrchestratorEnvironment -Name $Name)
    Wait-ForHttp -Url "http://127.0.0.1:$PaymentOrchestratorPort/health/ready" -Seconds 90

    if (-not $NoWebPay) {
        Start-HarnessProcess `
            -Name "webpay-ui" `
            -FileName "npm.cmd" `
            -Arguments @("run", "dev", "--", "--host", "127.0.0.1", "--port", [string] $WebPayPort) `
            -WorkingDirectory (Join-Path $repoRoot "src\Services\WebPayUi") `
            -Environment @{
                "VITE_WEBPAY_API_BASE_URL" = ""
                "VITE_WEBPAY_API_PROXY_TARGET" = "http://127.0.0.1:$PaymentOrchestratorPort"
                "VITE_WEBPAY_DEFAULT_VENDOR_SYSTEM_ID" = $vendorSystemId
                "VITE_WEBPAY_DEFAULT_SITE_GROUP_ID" = $siteGroupId
                "VITE_WEBPAY_DEFAULT_SITE_ID" = $siteId
            }
        Wait-ForHttp -Url "http://127.0.0.1:$WebPayPort/" -Seconds 90
    }

    Write-ScenarioInstructions -Name $Name
}

function Get-WebPayBrowserUrl {
    return "http://127.0.0.1:$WebPayPort/?ticketReference=$ticketReference&webpayStatutoryRecoveryReset=1"
}

function Write-ScenarioInstructions {
    param([string] $Name)
    $browserUrl = Get-WebPayBrowserUrl
    $browserOrigin = "http://127.0.0.1:$WebPayPort"
    $browserFacingRequestUrl = "$browserOrigin/v1/webpay/parking-session"
    $paymentOrchestratorUrl = "http://127.0.0.1:$PaymentOrchestratorPort"
    $probeCommand = ".\scripts\v1.3\webpay\Invoke-WebPayStatutoryServiceAuthManualValidation.ps1 -Scenario $Name -ProbeOnly"
    $browserProbeCommand = ".\scripts\v1.3\webpay\Invoke-WebPayStatutoryServiceAuthManualValidation.ps1 -Scenario $Name -BrowserRouteProbe"
    Write-Host ""
    Write-Host "Scenario: $Name"
    Write-Host "WebPay URL: $browserUrl"
    Write-Host "Browser recovery reset key: exitpass:webpay:statutory-discount-recovery:v1"
    Write-Host "Browser recovery reset mode: local-only URL parameter webpayStatutoryRecoveryReset=1"
    Write-Host "Browser origin: $browserOrigin"
    Write-Host "Browser-facing request URL: $browserFacingRequestUrl"
    Write-Host "Payment Orchestrator internal URL: $paymentOrchestratorUrl"
    Write-Host "Browser routing: same-origin Vite proxy (/v1 -> $paymentOrchestratorUrl)"
    Write-Host "Expected preflight behavior: none; same-origin browser requests do not require CORS preflight."
    Write-Host "Expected POST behavior: POST $browserFacingRequestUrl reaches Payment Orchestrator through Vite."
    Write-Host "Payment Orchestrator URL: $paymentOrchestratorUrl"
    Write-Host "Central PMS statutory stub URL: http://127.0.0.1:$CentralPmsPort"
    Write-Host "Ticket: $ticketReference"
    Write-Host "Plate: $plateNumber"
    Write-Host "Parking session: $parkingSessionId"
    Write-Host "Site Group: $siteGroupId"
    Write-Host "Site: $siteId"
    Write-Host "Valid local WebPay service identity: $validServiceIdentityId"
    Write-Host "Probe command: $probeCommand"
    Write-Host "Browser route probe command: $browserProbeCommand"
    Write-Host "Stop command: .\scripts\v1.3\webpay\Invoke-WebPayStatutoryServiceAuthManualValidation.ps1 -Stop"
    Write-Host "Cleanup command: .\scripts\v1.3\webpay\Invoke-WebPayStatutoryServiceAuthManualValidation.ps1 -Cleanup"
}

function Invoke-Probe {
    param([string] $Name)
    $correlationId = "11111111-1111-4111-8111-111111111111"
    $idempotencyKey = "webpay-statutory-service-auth:${Name}:$parkingSessionId:SENIOR_CITIZEN"
    $uri = "http://127.0.0.1:$PaymentOrchestratorPort/v1/webpay/statutory-discounts/decisions"
    $headers = @{
        "X-Correlation-Id" = $correlationId
        "Idempotency-Key" = $idempotencyKey
    }
    $body = New-ScenarioRequestBody -Name $Name

    $timeout = if ($Name -eq "Timeout") { $TimeoutDelaySeconds + 10 } else { 15 }
    $response = Invoke-JsonRequest -Method "POST" -Uri $uri -Headers $headers -Body $body -TimeoutSec $timeout
    Write-Host "HTTP $($response.StatusCode)"
    Write-Host $response.Body

    $expectedCode = Get-ScenarioExpectedCode -Name $Name
    if ($null -ne $expectedCode -and $response.Body -notmatch [regex]::Escape($expectedCode)) {
        throw "Expected safe error code '$expectedCode' was not present."
    }

    if ($Name -eq "Unavailable" -and $response.StatusCode -ne 503) {
        throw "Unavailable scenario must return HTTP 503 from the WebPay statutory proxy."
    }

    if ($Name -eq "Unavailable" -and $response.Body -notmatch '"retryable"\s*:\s*true') {
        throw "Unavailable scenario must preserve retryable true."
    }

    if ($response.Body -match "Authenticated user|service identity is required|CentralPmsStatutoryDiscountDecisionSubmit|X-ExitPass|stack trace|connection string|127\.0\.0\.1:$CentralPmsPort|127\.0\.0\.1:59997|connection refused|actively refused|SocketException|HttpRequestException|HttpClient") {
        throw "Probe response leaked internal authentication, policy, header, stack, or backend URL detail."
    }

    if ($Name -eq "Unavailable") {
        Assert-StubRequestLogged -CorrelationId $correlationId -Path "/v1/statutory-discounts/decisions" | Out-Null
        Write-Host "PASS: Unavailable statutory operation reached Payment Orchestrator and mapped to a safe retryable HTTP 503."
    }

    if ($Name -eq "IdempotentReplay" -or $Name -eq "Valid") {
        $response2 = Invoke-JsonRequest -Method "POST" -Uri $uri -Headers $headers -Body $body -TimeoutSec 15
        Write-Host "Replay HTTP $($response2.StatusCode)"
        Write-Host $response2.Body
        if ($response2.StatusCode -ne $response.StatusCode) {
            throw "Replay status did not converge."
        }
    }

    if ($Name -eq "Valid") {
        $readbackUri = "http://127.0.0.1:$PaymentOrchestratorPort/v1/webpay/statutory-discounts/decisions/$decisionCommandId"
        $readbackResponse = Invoke-JsonRequest -Method "GET" -Uri $readbackUri -Headers @{ "X-Correlation-Id" = $correlationId } -Body $null -TimeoutSec 15
        Write-Host "Readback HTTP $($readbackResponse.StatusCode)"
        Write-Host $readbackResponse.Body
        if ($readbackResponse.StatusCode -ne 200) {
            throw "Valid readback did not return HTTP 200."
        }

        if ($readbackResponse.Body -notmatch '"decisionCommandStatus"\s*:\s*"AWAITING_REVIEW"') {
            throw "Valid readback did not return decisionCommandStatus AWAITING_REVIEW."
        }

        if ($readbackResponse.Body -notmatch '"overallResultClassification"\s*:\s*"PENDING_REVIEW"') {
            throw "Valid readback did not return overallResultClassification PENDING_REVIEW."
        }

        if ($readbackResponse.Body -match "Status temporarily unavailable|service identity is required|CentralPmsStatutoryDiscountDecisionSubmit|statutory-discounts\.decision\.submit\.webpay|stack trace") {
            throw "Valid readback leaked internal or incorrect customer-facing failure text."
        }

        Write-Host "PASS: Valid statutory submission and authoritative pending-review readback succeeded."
        Write-Host "Expected Valid UI title: Awaiting review"
        Write-Host "Unexpected Valid UI title: Status temporarily unavailable"
    }
}

function Invoke-BrowserRouteProbe {
    param([string] $Name)
    $browserOrigin = "http://127.0.0.1:$WebPayPort"
    $browserFacingRequestUrl = "$browserOrigin/v1/webpay/parking-session"
    $paymentOrchestratorUrl = "http://127.0.0.1:$PaymentOrchestratorPort"
    $correlationId = "22222222-2222-4222-8222-222222222222"
    $body = [ordered]@{
        ticketReference = $ticketReference
        siteGroupId = $siteGroupId
        siteId = $siteId
        vendorSystemId = $vendorSystemId
        correlationId = $correlationId
    }
    $headers = @{
        "X-Correlation-Id" = $correlationId
    }

    Write-Host "Browser origin: $browserOrigin"
    Write-Host "Browser-facing request URL: $browserFacingRequestUrl"
    Write-Host "Payment Orchestrator internal URL: $paymentOrchestratorUrl"
    Write-Host "Routing mode: Vite same-origin proxy (/v1 -> $paymentOrchestratorUrl)"
    Write-Host "Expected preflight behavior: none; the browser request remains same-origin."
    Write-Host "Expected POST behavior: Vite forwards POST /v1/webpay/parking-session to Payment Orchestrator."

    $response = Invoke-JsonRequest -Method "POST" -Uri $browserFacingRequestUrl -Headers $headers -Body $body -TimeoutSec 15
    Write-Host "HTTP $($response.StatusCode)"
    Write-Host $response.Body

    if ($response.StatusCode -ne 200) {
        throw "Browser-facing parking-session POST failed."
    }

    if ($response.Body -notmatch [regex]::Escape($parkingSessionId)) {
        throw "Parking-session POST did not return the deterministic parking session."
    }

    if ($response.Body -notmatch '"amountMinorUnits"\s*:\s*13750') {
        throw "Parking-session POST did not return the authoritative PHP 137.50 payable basis."
    }

    if ($response.Body -notmatch [regex]::Escape($correlationId)) {
        throw "Correlation ID was not preserved through the browser-facing route."
    }

    $matchingLog = Assert-StubRequestLogged -CorrelationId $correlationId -Path "/v1/vendor-parking/resolve"

    if ($matchingLog -match '"serviceIdentityHeaderPresent":true' -or $matchingLog -match '"permissionHeaderPresent":true') {
        throw "Browser parking-session route unexpectedly used statutory service-auth headers."
    }

    Write-Host "Correlation ID: $correlationId"
    Write-Host "PASS: same-origin browser route reached Payment Orchestrator and Central PMS without CORS preflight."
}

function Assert-StubRequestLogged {
    param([string] $CorrelationId, [string] $Path)
    $requestLogPath = Join-Path $logsRoot "central-pms-statutory-stub.requests.jsonl"
    if (-not (Test-Path -LiteralPath $requestLogPath)) {
        throw "Central PMS stub request log was not found."
    }

    $matchingLog = Get-Content -LiteralPath $requestLogPath |
        Where-Object { $_ -match [regex]::Escape($CorrelationId) -and $_ -match [regex]::Escape($Path) } |
        Select-Object -Last 1

    if (-not $matchingLog) {
        throw "Payment Orchestrator did not forward request '$Path' to Central PMS."
    }

    return $matchingLog
}

function Invoke-BrowserRecoveryReset {
    $url = Get-WebPayBrowserUrl
    Write-Host "Browser recovery reset key: exitpass:webpay:statutory-discount-recovery:v1"
    Write-Host "Reset URL: $url"
    Write-Host "Open the reset URL in the same browser profile used for manual validation. WebPay clears only its statutory recovery record on localhost before loading the fixture."
}

function Invoke-SelfTest {
    Ensure-HarnessDirectories
    $failures = New-Object System.Collections.Generic.List[string]
    foreach ($name in $scenarioList) {
        try {
            [void] (Get-ScenarioServiceIdentity -Name $name)
            [void] (Get-ScenarioExpectedCode -Name $name)
            [void] (New-ScenarioRequestBody -Name $name)
            [void] (Get-PaymentOrchestratorEnvironment -Name $name)
        }
        catch {
            $failures.Add("$name scenario definition failed: $($_.Exception.Message)")
        }
    }

    if ((Get-ScenarioServiceIdentity -Name "MissingConfiguration")) {
        $failures.Add("MissingConfiguration must omit the WebPay service identity configuration.")
    }

    $validEnv = Get-PaymentOrchestratorEnvironment -Name "Valid"
    if ($validEnv["Integrations__CentralPms__StatutoryDiscounts__WebPayServiceIdentityId"] -ne $validServiceIdentityId) {
        $failures.Add("Valid scenario does not use the deterministic WebPay service identity.")
    }

    $unavailableEnv = Get-PaymentOrchestratorEnvironment -Name "Unavailable"
    if ($unavailableEnv["Integrations__CentralPms__BaseUrl"] -ne "http://127.0.0.1:$CentralPmsPort") {
        $failures.Add("Unavailable scenario must preserve the shared Central PMS base URL so parking-session lookup remains healthy.")
    }

    if ($validEnv.Keys -contains "VITE_WEBPAY_SERVICE_IDENTITY") {
        $failures.Add("Browser-visible service identity environment variable must not exist.")
    }

    $webPayEnv = @{
        "VITE_WEBPAY_API_BASE_URL" = ""
        "VITE_WEBPAY_API_PROXY_TARGET" = "http://127.0.0.1:$PaymentOrchestratorPort"
    }
    if (-not [string]::IsNullOrWhiteSpace($webPayEnv["VITE_WEBPAY_API_BASE_URL"])) {
        $failures.Add("WebPay manual harness must not use a browser-visible cross-origin API base URL.")
    }
    if ($webPayEnv["VITE_WEBPAY_API_PROXY_TARGET"] -ne "http://127.0.0.1:$PaymentOrchestratorPort") {
        $failures.Add("WebPay manual harness proxy target must point to Payment Orchestrator.")
    }

    Test-PortDetectionSelfCheck

    if ($failures.Count -gt 0) {
        $failures | ForEach-Object { Write-Error $_ }
        throw "Harness self-test failed with $($failures.Count) failure(s)."
    }

    Write-Info "Harness self-test passed for $($scenarioList.Count) scenarios."
}

function Write-DryRun {
    foreach ($name in (Get-ScenariosToRun)) {
        Write-ScenarioInstructions -Name $name
        Write-Host "Start command: .\scripts\v1.3\webpay\Invoke-WebPayStatutoryServiceAuthManualValidation.ps1 -Scenario $name -Start"
        Write-Host "Probe command: .\scripts\v1.3\webpay\Invoke-WebPayStatutoryServiceAuthManualValidation.ps1 -Scenario $name -ProbeOnly"
        Write-Host "Browser route probe command: .\scripts\v1.3\webpay\Invoke-WebPayStatutoryServiceAuthManualValidation.ps1 -Scenario $name -BrowserRouteProbe"
        Write-Host "Expected safe code: $(Get-ScenarioExpectedCode -Name $name)"
    }
}

function Write-Json {
    param($Response, [int] $StatusCode, [object] $Body)
    $payload = $Body | ConvertTo-Json -Depth 20 -Compress
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($payload)
    $Response.StatusCode = $StatusCode
    $Response.ContentType = "application/json; charset=utf-8"
    $Response.ContentLength64 = $bytes.Length
    $Response.OutputStream.Write($bytes, 0, $bytes.Length)
    $Response.OutputStream.Close()
}

function New-ErrorBody {
    param([string] $Code, [string] $Message, [bool] $Retryable, [string] $CorrelationId)
    return [ordered]@{
        errorCode = $Code
        message = $Message
        retryable = $Retryable
        correlationId = $CorrelationId
    }
}

function New-DecisionBody {
    param([string] $CorrelationId)
    return [ordered]@{
        statutoryDiscountDecisionCommandId = $decisionCommandId
        requestReference = $requestReference
        statutoryDiscountValidationId = $null
        parkingSessionId = $parkingSessionId
        sourceChannel = "WEBPAY"
        entitlementType = "SENIOR_CITIZEN"
        decisionStatus = "NOT_DECIDED"
        policyResolutionBasis = "LOCAL_VALIDATION_STUB"
        appliedPolicyReferenceId = $null
        fallbackPolicyReferenceId = $null
        localOrdinanceApplied = $false
        grossAmountMinorUnits = 13750
        statutoryDiscountAmountMinorUnits = $null
        netPayableAmountMinorUnits = $null
        currency = "PHP"
        evidenceRequired = $false
        evidenceRecorded = $false
        reasonCode = $null
        errorCode = $null
        correlationId = $CorrelationId
        createdAt = "2026-07-29T09:00:00+08:00"
        decidedAt = $null
        appliedAt = $null
        originalTariffSnapshotId = $originalTariffSnapshotId
        appliedTariffSnapshotId = $null
        commandStatus = "AWAITING_REVIEW"
        clientResultStatus = "NOT_DECIDED"
        resultClassification = "PENDING_REVIEW"
        semanticHashSourceVersion = "statutory-discount-decision:sha256:v2"
        retryable = $true
        recoveryClassification = "PENDING_REVIEW"
        recoveryAction = "POLL_READBACK"
        safeErrorCode = $null
        decisionCommandStatus = "AWAITING_REVIEW"
        decisionResultStatus = "NOT_DECIDED"
        decisionRetryable = $true
        decisionRecoveryClassification = "PENDING_REVIEW"
        decisionRecoveryAction = "POLL_READBACK"
        statutoryDiscountPayableBasisApplicationCommandId = $null
        applicationRequested = $false
        applicationCommandStatus = "NOT_REQUESTED"
        applicationResultClassification = "NOT_REQUESTED"
        applicationSemanticHashSourceVersion = $null
        applicationRetryable = $false
        applicationRecoveryClassification = "NONE"
        applicationRecoveryAction = $null
        overallResultClassification = "PENDING_REVIEW"
        oneShotComplete = $false
        siteId = $siteId
        siteGroupId = $siteGroupId
        vatExclusiveBasisAmountMinorUnits = $null
        vatAmountMinorUnits = $null
        vatTreatment = $null
        payableBasisReady = $false
        payableBasisReadinessStatus = "AWAITING_REVIEW"
        payableBasisReadinessAction = "POLL_READBACK"
    }
}

function Start-CentralPmsStub {
    Ensure-HarnessDirectories
    $listener = [System.Net.HttpListener]::new()
    $prefix = "http://127.0.0.1:$CentralPmsPort/"
    $listener.Prefixes.Add($prefix)
    $listener.Start()
    Set-Content -Path (Join-Path $stateRoot "central-pms-stub-$Scenario.txt") -Value "Listening $prefix scenario $Scenario" -Encoding ASCII

    try {
        while ($listener.IsListening) {
            $context = $listener.GetContext()
            $request = $context.Request
            $response = $context.Response
            $path = $request.Url.AbsolutePath
            $correlationId = $request.Headers["X-Correlation-Id"]
            if ([string]::IsNullOrWhiteSpace($correlationId)) {
                $correlationId = [guid]::NewGuid().ToString()
            }

            if ($path -eq "/__health") {
                Write-Json -Response $response -StatusCode 200 -Body ([ordered]@{ status = "ready"; scenario = $Scenario })
                continue
            }

            $record = [ordered]@{
                at = (Get-Date).ToString("o")
                method = $request.HttpMethod
                path = $path
                scenario = $Scenario
                correlationId = $correlationId
                serviceIdentityHeaderPresent = -not [string]::IsNullOrWhiteSpace($request.Headers[$serviceIdentityHeader])
                permissionHeaderPresent = -not [string]::IsNullOrWhiteSpace($request.Headers[$permissionsHeader])
                serviceIdentity = $request.Headers[$serviceIdentityHeader]
                permission = $request.Headers[$permissionsHeader]
            }
            Add-Content -Path (Join-Path $logsRoot "central-pms-statutory-stub.requests.jsonl") -Value ($record | ConvertTo-Json -Compress)

            if ($path -eq "/v1/vendor-parking/resolve" -and $request.HttpMethod -eq "POST") {
                Write-Json -Response $response -StatusCode 200 -Body ([ordered]@{
                    parkingSessionId = $parkingSessionId
                    tariffSnapshotId = $originalTariffSnapshotId
                    siteGroupId = $siteGroupId
                    siteId = $siteId
                    lookupOutcome = "FOUND"
                    plateNumber = $plateNumber
                    ticketReference = $ticketReference
                    netPayableMinorUnits = 13750
                    currency = "PHP"
                    tariffExpiresAt = "2026-07-29T10:15:00+08:00"
                    feeValidUntil = "2026-07-29T10:15:00+08:00"
                    vendorSystemId = $vendorSystemId
                    correlationId = $correlationId
                    siteGroupName = "Service Auth Local Site Group"
                    siteName = "Service Auth Local Site"
                    entryTime = "2026-07-29T08:00:00+08:00"
                    currentFeeCalculationTime = "2026-07-29T09:00:00+08:00"
                    tariffName = "Local Service Auth Tariff"
                    parkingStatus = "PaymentRequired"
                    paymentStatus = "Not Started"
                })
                continue
            }

            $isStatutoryPost = $path -eq "/v1/statutory-discounts/decisions" -and $request.HttpMethod -eq "POST"
            $isStatutoryGet = $path -eq "/v1/statutory-discounts/decisions/$decisionCommandId" -and $request.HttpMethod -eq "GET"
            if (-not ($isStatutoryPost -or $isStatutoryGet)) {
                Write-Json -Response $response -StatusCode 404 -Body (New-ErrorBody -Code "NOT_FOUND" -Message "Not found." -Retryable $false -CorrelationId $correlationId)
                continue
            }

            if ($Scenario -eq "Timeout") {
                Start-Sleep -Seconds $TimeoutDelaySeconds
            }

            if ($Scenario -eq "Unavailable") {
                Write-Json -Response $response -StatusCode 503 -Body (New-ErrorBody -Code "CENTRAL_PMS_UNAVAILABLE" -Message "Central PMS statutory operation is temporarily unavailable." -Retryable $true -CorrelationId $correlationId)
                continue
            }

            if ($Scenario -eq "RejectedIdentity") {
                Write-Json -Response $response -StatusCode 401 -Body (New-ErrorBody -Code "CENTRAL_PMS_AUTHENTICATED_ACTOR_REQUIRED" -Message "Authenticated user or service identity is required for statutory-discount decision submission." -Retryable $false -CorrelationId $correlationId)
                continue
            }

            if ($Scenario -eq "PermissionDenied") {
                Write-Json -Response $response -StatusCode 403 -Body (New-ErrorBody -Code "CENTRAL_PMS_POLICY_DENIED" -Message "CentralPmsStatutoryDiscountDecisionSubmit denied permission statutory-discounts.decision.submit.webpay." -Retryable $false -CorrelationId $correlationId)
                continue
            }

            if ($Scenario -eq "ValidationFailure") {
                Write-Json -Response $response -StatusCode 400 -Body (New-ErrorBody -Code "STATUTORY_DISCOUNT_REQUEST_INVALID" -Message "Enter a valid masked ID reference." -Retryable $false -CorrelationId $correlationId)
                continue
            }

            if ($Scenario -eq "Conflict") {
                Write-Json -Response $response -StatusCode 409 -Body (New-ErrorBody -Code "STATUTORY_DISCOUNT_DECISION_SEMANTIC_CONFLICT" -Message "A statutory discount request already exists with different submitted details." -Retryable $false -CorrelationId $correlationId)
                continue
            }

            $expectedPermission = if ($isStatutoryGet) { $readPermission } else { $submitPermission }
            if ($request.Headers[$serviceIdentityHeader] -ne $validServiceIdentityId -or $request.Headers[$permissionsHeader] -ne $expectedPermission) {
                Write-Json -Response $response -StatusCode 403 -Body (New-ErrorBody -Code "CENTRAL_PMS_POLICY_DENIED" -Message "CentralPmsStatutoryDiscountDecisionSubmit denied internal service auth headers." -Retryable $false -CorrelationId $correlationId)
                continue
            }

            Write-Json -Response $response -StatusCode 200 -Body (New-DecisionBody -CorrelationId $correlationId)
        }
    }
    finally {
        if ($listener.IsListening) {
            $listener.Stop()
        }
        $listener.Close()
    }
}

if ($RunCentralPmsStub) {
    Start-CentralPmsStub
    return
}

if ($SelfTest) {
    Invoke-SelfTest
    return
}

if ($Stop) {
    Stop-HarnessProcesses
    return
}

if ($Cleanup) {
    Stop-HarnessProcesses
    Start-Sleep -Milliseconds 500
    if (Test-Path -LiteralPath $logRootPath) {
        Remove-Item -LiteralPath $logRootPath -Recurse -Force
    }
    Write-Info "Removed harness state at $logRootPath."
    return
}

if ($DryRun) {
    Write-DryRun
    return
}

if ($ResetBrowserRecovery) {
    Invoke-BrowserRecoveryReset
    return
}

if ($Start) {
    $toRun = @(Get-ScenariosToRun)
    if ($toRun.Count -ne 1) {
        throw "Start supports one scenario at a time. Use -DryRun -Scenario All to print all commands."
    }
    Start-Scenario -Name $toRun[0]
    return
}

if ($ProbeOnly) {
    $toRun = @(Get-ScenariosToRun)
    if ($toRun.Count -ne 1) {
        throw "ProbeOnly supports one scenario at a time."
    }
    Invoke-Probe -Name $toRun[0]
    return
}

if ($BrowserRouteProbe) {
    $toRun = @(Get-ScenariosToRun)
    if ($toRun.Count -ne 1) {
        throw "BrowserRouteProbe supports one scenario at a time."
    }
    Invoke-BrowserRouteProbe -Name $toRun[0]
    return
}

Write-DryRun
