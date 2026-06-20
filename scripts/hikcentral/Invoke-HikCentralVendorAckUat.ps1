<#
.SYNOPSIS
    Runs a controlled ExitPass to HikCentral ticket-only vendor acknowledgment UAT path.

.DESCRIPTION
    This script is a smoke/UAT runner for the post-payment-finality vendor acknowledgment flow.
    It resolves a HikCentral ticket by cardNum through Central PMS, creates an ExitPass payment
    attempt, reports controlled verified payment finality, verifies database evidence, and checks
    the read-only ops monitoring endpoint.

    Modes:
      - EnabledConfirm: requires HIKCENTRAL_CONFIRM_PAYMENT_ENABLED=true and explicitly confirms
        an operator-controlled live ticket. HikCentral gate open must remain disabled.
      - DisabledGuard: requires HIKCENTRAL_CONFIRM_PAYMENT_ENABLED=false and validates
        SKIPPED_DISABLED without calling HikCentral confirm. HikCentral gate open must remain
        disabled.

    The script never calls a gate/barrier-open API. The production acknowledgment workflow sends
    HikCentral parkingfee/confirm with immediatelyLeave=0.
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet("EnabledConfirm", "DisabledGuard")]
    [string] $Mode,

    [Parameter(Mandatory = $true)]
    [string] $CardNum,

    [string] $CentralPmsBaseUrl = "http://127.0.0.1:8080",

    [string] $PaymentProvider = "GCASH",

    [string] $RequestedBy = "controlled-hikcentral-vendor-ack-uat",

    [guid] $RequestedByUserId = "12000000-0000-0000-0000-000000000001",

    [guid] $CorrelationId = [guid]::NewGuid(),

    [string] $SiteGroupId,

    [string] $SiteId,

    [switch] $ConfirmControlledTicket,

    [string] $PhysicalValidatorResult = "",

    [switch] $SkipRawHikCentralCalculate,

    [switch] $SkipOpsEndpointCheck,

    [switch] $UseDockerPsql,

    [string] $DockerContainerName = "exitpass-postgres",

    [string] $DockerDatabaseName = "exitpass_v12_dev",

    [string] $DockerDatabaseUser = "exitpass",

    [switch] $OutputJson
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Get-RequiredEnv {
    param([string] $Name)

    $value = [Environment]::GetEnvironmentVariable($Name)
    if ([string]::IsNullOrWhiteSpace($value)) {
        throw "Missing required environment variable: $Name"
    }

    return $value
}

function Get-EnvValue {
    param(
        [string] $Name,
        [string] $DefaultValue = ""
    )

    $value = [Environment]::GetEnvironmentVariable($Name)
    if ([string]::IsNullOrWhiteSpace($value)) {
        return $DefaultValue
    }

    return $value
}

function ConvertTo-Base64Md5 {
    param([string] $Text)

    $bytes = [System.Text.Encoding]::UTF8.GetBytes($Text)
    $md5 = [System.Security.Cryptography.MD5]::Create()
    $hash = $md5.ComputeHash($bytes)

    return [Convert]::ToBase64String($hash)
}

function New-HikCentralSignature {
    param(
        [string] $Method,
        [string] $Path,
        [string] $Body,
        [string] $Mode,
        [string] $Accept,
        [string] $ContentType,
        [string] $AppKey,
        [string] $AppSecret,
        [string] $Timestamp
    )

    $contentMd5 = ConvertTo-Base64Md5 $Body

    if ($Mode -eq "NoMd5NoDateLines-KeyTimestamp") {
        $signatureHeaders = "x-ca-key,x-ca-timestamp"
        $canonicalHeaders = "x-ca-key:$AppKey`n" + "x-ca-timestamp:$Timestamp`n"
        $stringToSign = $Method.ToUpperInvariant() + "`n" + $Accept + "`n" + $ContentType + "`n" + $canonicalHeaders + $Path
    }
    elseif ($Mode -eq "BlankMd5BlankDate-KeyTimestamp") {
        $signatureHeaders = "x-ca-key,x-ca-timestamp"
        $canonicalHeaders = "x-ca-key:$AppKey`n" + "x-ca-timestamp:$Timestamp`n"
        $stringToSign = $Method.ToUpperInvariant() + "`n" + $Accept + "`n`n" + $ContentType + "`n`n" + $canonicalHeaders + $Path
    }
    elseif ($Mode -eq "Md5BlankDate-KeyTimestamp") {
        $signatureHeaders = "x-ca-key,x-ca-timestamp"
        $canonicalHeaders = "x-ca-key:$AppKey`n" + "x-ca-timestamp:$Timestamp`n"
        $stringToSign = $Method.ToUpperInvariant() + "`n" + $Accept + "`n" + $contentMd5 + "`n" + $ContentType + "`n`n" + $canonicalHeaders + $Path
    }
    elseif ($Mode -eq "BlankMd5BlankDate-KeyOnly") {
        $signatureHeaders = "x-ca-key"
        $canonicalHeaders = "x-ca-key:$AppKey`n"
        $stringToSign = $Method.ToUpperInvariant() + "`n" + $Accept + "`n`n" + $ContentType + "`n`n" + $canonicalHeaders + $Path
    }
    else {
        throw "Unknown HIKCENTRAL_AUTH_MODE: $Mode"
    }

    $hmac = New-Object System.Security.Cryptography.HMACSHA256
    $hmac.Key = [System.Text.Encoding]::UTF8.GetBytes($AppSecret)
    $signatureBytes = $hmac.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($stringToSign))

    return @{
        Signature = [Convert]::ToBase64String($signatureBytes)
        SignatureHeaders = $signatureHeaders
        ContentMd5 = $contentMd5
    }
}

function Resolve-HikCentralAuthMode {
    $cached = [Environment]::GetEnvironmentVariable("HIKCENTRAL_RESOLVED_AUTH_MODE")
    if (-not [string]::IsNullOrWhiteSpace($cached)) {
        return $cached
    }

    $configured = Get-EnvValue "HIKCENTRAL_AUTH_MODE" "Auto"
    if ($configured -ne "Auto") {
        return $configured
    }

    $modes = @(
        "NoMd5NoDateLines-KeyTimestamp",
        "BlankMd5BlankDate-KeyTimestamp",
        "Md5BlankDate-KeyTimestamp",
        "BlankMd5BlankDate-KeyOnly"
    )

    foreach ($mode in $modes) {
        try {
            $raw = Invoke-HikCentralPost `
                -Label "Auth probe" `
                -Path "/artemis/api/common/v1/version" `
                -BodyObject @{} `
                -AuthMode $mode
            $json = $raw | ConvertFrom-Json

            if ($json.code -eq "0" -or $json.code -ne "68") {
                [Environment]::SetEnvironmentVariable("HIKCENTRAL_RESOLVED_AUTH_MODE", $mode, "Process")
                return $mode
            }
        }
        catch {
            # Try next mode.
        }
    }

    throw "Could not resolve HikCentral auth mode. Set HIKCENTRAL_AUTH_MODE manually."
}

function Invoke-HikCentralPost {
    param(
        [string] $Label,
        [string] $Path,
        [object] $BodyObject,
        [string] $AuthMode = ""
    )

    $baseUrl = (Get-RequiredEnv "HIKCENTRAL_BASE_URL").TrimEnd("/")
    $appKey = Get-RequiredEnv "HIKCENTRAL_APP_KEY"
    $appSecret = Get-RequiredEnv "HIKCENTRAL_APP_SECRET"

    if ([string]::IsNullOrWhiteSpace($AuthMode)) {
        $AuthMode = Resolve-HikCentralAuthMode
    }

    if ($null -eq $BodyObject) {
        $BodyObject = @{}
    }

    $accept = "*/*"
    $contentType = "application/json"
    $timestamp = [string] [DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds()
    $body = $BodyObject | ConvertTo-Json -Depth 50 -Compress

    $sig = New-HikCentralSignature `
        -Method "POST" `
        -Path $Path `
        -Body $body `
        -Mode $AuthMode `
        -Accept $accept `
        -ContentType $contentType `
        -AppKey $appKey `
        -AppSecret $appSecret `
        -Timestamp $timestamp

    $headers = @{
        "Accept" = $accept
        "X-Ca-Key" = $appKey
        "X-Ca-Timestamp" = $timestamp
        "X-Ca-Signature" = $sig.Signature
        "X-Ca-Signature-Headers" = $sig.SignatureHeaders
    }

    $userId = Get-EnvValue "HIKCENTRAL_USER_ID" ""
    if (-not [string]::IsNullOrWhiteSpace($userId)) {
        $headers["userId"] = $userId
    }

    if ($AuthMode -eq "Md5BlankDate-KeyTimestamp") {
        $headers["Content-MD5"] = $sig.ContentMd5
    }

    $url = "$baseUrl$Path"
    try {
        $response = Invoke-WebRequest `
            -Method POST `
            -Uri $url `
            -Headers $headers `
            -Body ([System.Text.Encoding]::UTF8.GetBytes($body)) `
            -ContentType $contentType `
            -TimeoutSec 30 `
            -UseBasicParsing

        return $response.Content
    }
    catch {
        $errorBody = $null
        if ($_.Exception.Response) {
            $stream = $_.Exception.Response.GetResponseStream()
            if ($stream) {
                $reader = New-Object System.IO.StreamReader($stream)
                $errorBody = $reader.ReadToEnd()
            }
        }

        throw "$Label failed for POST $Path. $($_.Exception.Message) $errorBody"
    }
}

function Assert-Safety {
    $db = Get-RequiredEnv "EXITPASS_INTEGRATION_DB"
    [void] $db
    [void] (Get-RequiredEnv "HIKCENTRAL_BASE_URL")
    [void] (Get-RequiredEnv "HIKCENTRAL_APP_KEY")
    [void] (Get-RequiredEnv "HIKCENTRAL_APP_SECRET")
    [void] (Get-RequiredEnv "HIKCENTRAL_TEST_PARKING_LOT_INDEX_CODE")

    $gateOpen = Get-EnvValue "HIKCENTRAL_GATE_OPEN_ALLOWED" "false"
    if ($gateOpen -ne "false") {
        throw "Stop: HIKCENTRAL_GATE_OPEN_ALLOWED must remain false. Current value: $gateOpen"
    }

    $confirmPayment = Get-EnvValue "HIKCENTRAL_CONFIRM_PAYMENT_ENABLED" ""
    if ($Mode -eq "EnabledConfirm") {
        if ($confirmPayment -ne "true") {
            throw "Stop: EnabledConfirm requires HIKCENTRAL_CONFIRM_PAYMENT_ENABLED=true. Current value: $confirmPayment"
        }

        if (-not $ConfirmControlledTicket) {
            throw "Stop: pass -ConfirmControlledTicket only after the operator confirms this is a controlled live ticket."
        }
    }
    else {
        if ($confirmPayment -ne "false") {
            throw "Stop: DisabledGuard requires HIKCENTRAL_CONFIRM_PAYMENT_ENABLED=false. Current value: $confirmPayment"
        }
    }
}

function ConvertTo-CorrelatedGuidString {
    param(
        [string] $Prefix,
        [guid] $Value
    )

    $suffix = $Value.ToString("N").Substring(20, 12)
    return "$Prefix-0000-0000-0000-$suffix"
}

function Invoke-HttpJson {
    param(
        [ValidateSet("GET", "POST")]
        [string] $Method,

        [string] $Url,

        [object] $Body = $null,

        [hashtable] $Headers = @{}
    )

    $parameters = @{
        Method = $Method
        Uri = $Url
        Headers = $Headers
        TimeoutSec = 60
        UseBasicParsing = $true
    }

    if ($null -ne $Body) {
        $parameters.Body = ($Body | ConvertTo-Json -Depth 20 -Compress)
        $parameters.ContentType = "application/json"
    }

    try {
        $response = Invoke-WebRequest @parameters
        $content = $response.Content
        $json = $null
        if (-not [string]::IsNullOrWhiteSpace($content)) {
            $json = $content | ConvertFrom-Json
        }

        return [pscustomobject]@{
            StatusCode = [int] $response.StatusCode
            Body = $json
            RawBody = $content
        }
    }
    catch {
        $statusCode = $null
        $content = $null
        if ($_.Exception.Response) {
            $statusCode = [int] $_.Exception.Response.StatusCode
            $stream = $_.Exception.Response.GetResponseStream()
            if ($stream) {
                $reader = New-Object System.IO.StreamReader($stream)
                $content = $reader.ReadToEnd()
            }
        }

        $json = $null
        if (-not [string]::IsNullOrWhiteSpace($content)) {
            try {
                $json = $content | ConvertFrom-Json
            }
            catch {
                $json = $null
            }
        }

        return [pscustomobject]@{
            StatusCode = $statusCode
            Body = $json
            RawBody = $content
            Error = $_.Exception.Message
        }
    }
}

function Get-DbSettings {
    $connectionString = Get-RequiredEnv "EXITPASS_INTEGRATION_DB"
    $settings = @{}
    foreach ($part in ($connectionString -split ";")) {
        if ([string]::IsNullOrWhiteSpace($part) -or $part -notmatch "=") {
            continue
        }

        $key, $value = $part -split "=", 2
        $settings[$key.Trim().ToLowerInvariant()] = $value.Trim()
    }

    foreach ($required in @("host", "port", "database", "username", "password")) {
        if (-not $settings.ContainsKey($required) -or [string]::IsNullOrWhiteSpace($settings[$required])) {
            throw "EXITPASS_INTEGRATION_DB is missing '$required'."
        }
    }

    return $settings
}

function Invoke-PsqlRows {
    param([string] $Sql)

    if ($UseDockerPsql) {
        $args = @(
            "exec",
            "-i",
            $DockerContainerName,
            "psql",
            "-U", $DockerDatabaseUser,
            "-d", $DockerDatabaseName,
            "-v", "ON_ERROR_STOP=1",
            "-t",
            "-A",
            "-f", "-"
        )

        $output = $Sql | & docker @args
        if ($LASTEXITCODE -ne 0) {
            throw "docker exec psql failed with exit code $LASTEXITCODE."
        }

        return @($output | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    }

    $settings = Get-DbSettings
    $psql = Get-Command "psql" -ErrorAction SilentlyContinue
    if ($null -eq $psql) {
        throw "psql was not found on PATH. Install PostgreSQL client tools or run this from a shell where psql is available."
    }

    $oldPassword = [Environment]::GetEnvironmentVariable("PGPASSWORD", "Process")
    try {
        [Environment]::SetEnvironmentVariable("PGPASSWORD", $settings["password"], "Process")
        $args = @(
            "-h", $settings["host"],
            "-p", $settings["port"],
            "-U", $settings["username"],
            "-d", $settings["database"],
            "-v", "ON_ERROR_STOP=1",
            "-t",
            "-A",
            "-c", $Sql
        )

        $output = & $psql.Source @args
        if ($LASTEXITCODE -ne 0) {
            throw "psql failed with exit code $LASTEXITCODE."
        }

        return @($output | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    }
    finally {
        [Environment]::SetEnvironmentVariable("PGPASSWORD", $oldPassword, "Process")
    }
}

function Invoke-PsqlJsonRows {
    param([string] $Sql)

    $lines = Invoke-PsqlRows $Sql
    return @($lines | ForEach-Object { $_ | ConvertFrom-Json })
}

function Assert-DatabaseShape {
    $sql = @"
SELECT row_to_json(q)
FROM (
    SELECT
        to_regclass('core.parking_sessions') IS NOT NULL AS parking_sessions_exists,
        to_regclass('core.payment_attempts') IS NOT NULL AS payment_attempts_exists,
        to_regclass('core.payment_confirmations') IS NOT NULL AS payment_confirmations_exists,
        to_regclass('core.exit_authorizations') IS NOT NULL AS exit_authorizations_exists,
        to_regclass('integration.vendor_payment_acknowledgments') IS NOT NULL AS vendor_acknowledgments_exists
) q;
"@
    $shape = @(Invoke-PsqlJsonRows $sql)[0]
    foreach ($property in $shape.PSObject.Properties) {
        if ($property.Value -ne $true) {
            throw "Stop: live database schema is not aligned. Missing relation check failed: $($property.Name)"
        }
    }
}

function Read-Evidence {
    param(
        [guid] $PaymentAttemptId,
        [guid] $PaymentConfirmationId,
        [guid] $ExitAuthorizationId
    )

    $sql = @"
SELECT row_to_json(q)
FROM (
    SELECT
        pa.payment_attempt_id::text,
        pa.parking_session_id::text,
        pa.attempt_status::text AS payment_attempt_status,
        pa.finalized_at,
        pc.payment_confirmation_id::text,
        pc.confirmation_status::text AS payment_confirmation_status,
        pc.provider_reference,
        pc.verified_timestamp,
        ea.exit_authorization_id::text,
        ea.authorization_status::text AS exit_authorization_status,
        ea.issued_at AS exit_authorization_issued_at,
        vpa.vendor_payment_acknowledgment_id::text,
        vpa.vendor_system_code,
        vpa.ticket_number,
        vpa.card_num,
        vpa.acknowledgment_status::text AS vendor_acknowledgment_status,
        vpa.vendor_code,
        vpa.vendor_message,
        vpa.request_fee_minor_units,
        vpa.request_currency_code,
        vpa.confirmed_fee_minor_units,
        vpa.vendor_confirmed_at,
        vpa.attempt_count,
        vpa.last_attempted_at,
        vpa.next_retry_at,
        vpa.created_at AS vendor_acknowledgment_created_at,
        vpa.updated_at AS vendor_acknowledgment_updated_at,
        (pc.payment_confirmation_id IS NOT NULL) AS payment_confirmation_recorded,
        (pa.attempt_status::text = 'CONFIRMED' AND pa.finalized_at IS NOT NULL) AS payment_attempt_finalized_confirmed,
        (ea.exit_authorization_id IS NOT NULL AND ea.authorization_status::text = 'ISSUED') AS exit_authorization_issued,
        (vpa.vendor_payment_acknowledgment_id IS NOT NULL) AS vendor_acknowledgment_created,
        (ea.issued_at IS NOT NULL AND vpa.created_at IS NOT NULL AND ea.issued_at <= vpa.created_at) AS exit_authorization_before_vendor_ack_created,
        (ea.issued_at IS NOT NULL AND vpa.last_attempted_at IS NOT NULL AND ea.issued_at <= vpa.last_attempted_at) AS exit_authorization_before_vendor_ack_attempted
    FROM core.payment_attempts pa
    LEFT JOIN core.payment_confirmations pc
        ON pc.payment_attempt_id = pa.payment_attempt_id
       AND pc.payment_confirmation_id = '$PaymentConfirmationId'::uuid
    LEFT JOIN core.exit_authorizations ea
        ON ea.payment_attempt_id = pa.payment_attempt_id
       AND ea.exit_authorization_id = '$ExitAuthorizationId'::uuid
    LEFT JOIN integration.vendor_payment_acknowledgments vpa
        ON vpa.payment_confirmation_id = pc.payment_confirmation_id
    WHERE pa.payment_attempt_id = '$PaymentAttemptId'::uuid
    LIMIT 1
) q;
"@

    $rows = @(Invoke-PsqlJsonRows $sql)
    if ($rows.Count -ne 1) {
        throw "Expected exactly one evidence row for payment_attempt_id=$PaymentAttemptId."
    }

    return $rows[0]
}

function Read-VendorAckByPaymentAttempt {
    param([guid] $PaymentAttemptId)

    $sql = @"
SELECT row_to_json(q)
FROM (
    SELECT
        vendor_payment_acknowledgment_id::text,
        payment_attempt_id::text,
        payment_confirmation_id::text,
        parking_session_id::text,
        vendor_system_code,
        ticket_number,
        card_num,
        acknowledgment_status::text,
        vendor_code,
        vendor_message,
        request_fee_minor_units,
        request_currency_code,
        confirmed_fee_minor_units,
        vendor_confirmed_at,
        attempt_count,
        last_attempted_at,
        next_retry_at,
        created_at,
        updated_at
    FROM integration.vendor_payment_acknowledgments
    WHERE payment_attempt_id = '$PaymentAttemptId'::uuid
    ORDER BY updated_at DESC, created_at DESC
    LIMIT 5
) q;
"@

    return @(Invoke-PsqlJsonRows $sql)
}

function Assert-ExpectedEvidence {
    param([object] $Evidence)

    if ($Evidence.payment_confirmation_recorded -ne $true) {
        throw "PaymentConfirmation was not recorded."
    }

    if ($Evidence.payment_attempt_finalized_confirmed -ne $true) {
        throw "PaymentAttempt was not finalized as CONFIRMED."
    }

    if ($Evidence.exit_authorization_issued -ne $true) {
        throw "ExitAuthorization was not issued."
    }

    if ($Evidence.vendor_acknowledgment_created -ne $true) {
        throw "Vendor payment acknowledgment was not created."
    }

    if ($Evidence.exit_authorization_before_vendor_ack_created -ne $true) {
        throw "ExitAuthorization was not issued before the Vendor PMS acknowledgment record was created."
    }

    if ($Mode -eq "EnabledConfirm") {
        if ($Evidence.vendor_acknowledgment_status -ne "CONFIRMED") {
            throw "Expected vendor acknowledgment status CONFIRMED, got '$($Evidence.vendor_acknowledgment_status)'."
        }

        if ($Evidence.exit_authorization_before_vendor_ack_attempted -ne $true) {
            throw "ExitAuthorization was not issued before Vendor PMS acknowledgment attempt."
        }
    }
    else {
        if ($Evidence.vendor_acknowledgment_status -ne "SKIPPED_DISABLED") {
            throw "Expected vendor acknowledgment status SKIPPED_DISABLED, got '$($Evidence.vendor_acknowledgment_status)'."
        }

        if ($Evidence.attempt_count -ne 0) {
            throw "Disabled guard should not increment vendor attempt_count. Got '$($Evidence.attempt_count)'."
        }
    }
}

function Write-Step {
    param([string] $Text)

    if (-not $OutputJson) {
        Write-Host ""
        Write-Host "== $Text ==" -ForegroundColor Cyan
    }
}

Assert-Safety
Assert-DatabaseShape

$centralPmsBase = $CentralPmsBaseUrl.TrimEnd("/")
$siteGroup = if ([string]::IsNullOrWhiteSpace($SiteGroupId)) {
    ConvertTo-CorrelatedGuidString "279f0001" $CorrelationId
}
else {
    $SiteGroupId
}
$site = if ([string]::IsNullOrWhiteSpace($SiteId)) {
    ConvertTo-CorrelatedGuidString "279f0002" $CorrelationId
}
else {
    $SiteId
}

$rawCalculate = $null
if (-not $SkipRawHikCentralCalculate) {
    Write-Step "Raw HikCentral calculate by cardNum"
    $rawCalculateText = Invoke-HikCentralPost `
        -Label "Parking Fee Calculate" `
        -Path "/artemis/api/vehicle/v1/parkingfee/calculate" `
        -BodyObject @{ cardNum = $CardNum }
    $rawCalculate = $rawCalculateText | ConvertFrom-Json
}

Write-Step "Central PMS resolve ticket"
$resolve = Invoke-HttpJson `
    -Method POST `
    -Url "$centralPmsBase/v1/vendor-parking/resolve" `
    -Body @{
        siteGroupId = $siteGroup
        siteId = $site
        vendorSystemId = "HIKCENTRAL"
        plateNumber = $null
        ticketReference = $CardNum
        correlationId = $CorrelationId
    } `
    -Headers @{ "X-Correlation-Id" = $CorrelationId.ToString() }

if ($resolve.StatusCode -ne 200) {
    throw "Central PMS resolve failed. HTTP $($resolve.StatusCode): $($resolve.RawBody)"
}

Write-Step "Create payment attempt"
$createPaymentIdempotencyKey = "uat-279f-create-$($CorrelationId.ToString('N'))"
$payment = Invoke-HttpJson `
    -Method POST `
    -Url "$centralPmsBase/v1/public/payment-attempts" `
    -Body @{
        parkingSessionId = $resolve.Body.parkingSessionId
        tariffSnapshotId = $resolve.Body.tariffSnapshotId
        paymentProvider = $PaymentProvider
    } `
    -Headers @{
        "X-Correlation-Id" = $CorrelationId.ToString()
        "Idempotency-Key" = $createPaymentIdempotencyKey
    }

if ($payment.StatusCode -notin @(200, 201)) {
    throw "Create payment attempt failed. HTTP $($payment.StatusCode): $($payment.RawBody)"
}

Write-Step "Report verified payment outcome"
$providerReference = "uat-279f-$Mode-$($CorrelationId.ToString('N'))"
$outcomeIdempotencyKey = "uat-279f-outcome-$($CorrelationId.ToString('N'))"
$outcome = Invoke-HttpJson `
    -Method POST `
    -Url "$centralPmsBase/v1/internal/payments/outcome" `
    -Body @{
        paymentAttemptId = $payment.Body.paymentAttemptId
        parkingSessionId = $resolve.Body.parkingSessionId
        providerReference = $providerReference
        providerStatus = "SUCCESS"
        finalAttemptStatus = "CONFIRMED"
        requestedBy = $RequestedBy
        requestedByUserId = $RequestedByUserId
    } `
    -Headers @{
        "X-Correlation-Id" = $CorrelationId.ToString()
        "Idempotency-Key" = $outcomeIdempotencyKey
    }

if ($outcome.StatusCode -ne 200) {
    throw "Verified payment outcome failed. HTTP $($outcome.StatusCode): $($outcome.RawBody)"
}

$evidence = Read-Evidence `
    -PaymentAttemptId ([guid] $payment.Body.paymentAttemptId) `
    -PaymentConfirmationId ([guid] $outcome.Body.paymentConfirmationId) `
    -ExitAuthorizationId ([guid] $outcome.Body.exitAuthorizationId)
Assert-ExpectedEvidence $evidence

$retryProbe = Read-VendorAckByPaymentAttempt ([guid] $payment.Body.paymentAttemptId)

$opsSearch = $null
$opsDetail = $null
if (-not $SkipOpsEndpointCheck) {
    Write-Step "Read ops monitoring endpoint"
    $opsSearch = Invoke-HttpJson `
        -Method POST `
        -Url "$centralPmsBase/v1/ops/vendor-payment-acknowledgments/search" `
        -Body @{
            paymentAttemptId = $payment.Body.paymentAttemptId
            pageIndex = 0
            pageSize = 10
        } `
        -Headers @{ "X-Correlation-Id" = $CorrelationId.ToString() }

    if ($opsSearch.StatusCode -ne 200) {
        throw "Ops acknowledgment search failed. HTTP $($opsSearch.StatusCode): $($opsSearch.RawBody)"
    }

    if ($opsSearch.Body.items.Count -lt 1) {
        throw "Ops acknowledgment search did not return the vendor acknowledgment."
    }

    $opsDetail = Invoke-HttpJson `
        -Method GET `
        -Url "$centralPmsBase/v1/ops/vendor-payment-acknowledgments/$($evidence.vendor_payment_acknowledgment_id)" `
        -Headers @{ "X-Correlation-Id" = $CorrelationId.ToString() }

    if ($opsDetail.StatusCode -ne 200) {
        throw "Ops acknowledgment detail failed. HTTP $($opsDetail.StatusCode): $($opsDetail.RawBody)"
    }
}

$summary = [ordered]@{
    mode = $Mode
    cardNum = $CardNum
    centralPmsBaseUrl = $centralPmsBase
    correlationId = $CorrelationId
    safety = [ordered]@{
        confirmPaymentEnabled = (Get-EnvValue "HIKCENTRAL_CONFIRM_PAYMENT_ENABLED" "")
        gateOpenAllowed = (Get-EnvValue "HIKCENTRAL_GATE_OPEN_ALLOWED" "")
        immediatelyLeave = 0
    }
    commands = [ordered]@{
        runner = ".\scripts\hikcentral\Invoke-HikCentralVendorAckUat.ps1 -Mode $Mode -CardNum <cardNum> -CentralPmsBaseUrl $CentralPmsBaseUrl"
        createPaymentIdempotencyKey = $createPaymentIdempotencyKey
        outcomeIdempotencyKey = $outcomeIdempotencyKey
    }
    rawHikCentralCalculate = $rawCalculate
    centralPmsResolve = $resolve.Body
    paymentAttempt = $payment.Body
    verifiedPaymentOutcome = $outcome.Body
    databaseEvidence = $evidence
    retryDispatcherDuplicateProbe = [ordered]@{
        latestAcknowledgmentRowsForPaymentAttempt = $retryProbe
        note = "A CONFIRMED acknowledgment is not selected by the retry dispatcher query, which only reads RETRY_PENDING due records."
    }
    operatorConsoleVisibility = [ordered]@{
        opsSearchStatusCode = if ($null -ne $opsSearch) { $opsSearch.StatusCode } else { $null }
        opsSearch = if ($null -ne $opsSearch) { $opsSearch.Body } else { $null }
        opsDetailStatusCode = if ($null -ne $opsDetail) { $opsDetail.StatusCode } else { $null }
        opsDetail = if ($null -ne $opsDetail) { $opsDetail.Body } else { $null }
        route = "/operator-console/vendor-acknowledgments"
    }
    physicalTicketValidatorResult = if ([string]::IsNullOrWhiteSpace($PhysicalValidatorResult)) {
        "operator must record physical validator result after running controlled ticket at exit"
    }
    else {
        $PhysicalValidatorResult
    }
    noGateBarrierOpenApiCalled = $true
}

if ($OutputJson) {
    $summary | ConvertTo-Json -Depth 50
}
else {
    Write-Host ""
    Write-Host "UAT PASS" -ForegroundColor Green
    Write-Host "CardNum: $CardNum"
    Write-Host "PaymentAttemptId: $($payment.Body.paymentAttemptId)"
    Write-Host "PaymentConfirmationId: $($outcome.Body.paymentConfirmationId)"
    Write-Host "ExitAuthorizationId: $($outcome.Body.exitAuthorizationId)"
    Write-Host "VendorPaymentAcknowledgmentId: $($evidence.vendor_payment_acknowledgment_id)"
    Write-Host "Vendor acknowledgment status: $($evidence.vendor_acknowledgment_status)"
    Write-Host "Operator Console route: /operator-console/vendor-acknowledgments"
    Write-Host ""
    $summary | ConvertTo-Json -Depth 50
}
