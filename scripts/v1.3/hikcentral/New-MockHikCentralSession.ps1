[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $Ticket,

    [string] $Plate,

    [decimal] $Fee = 25.00,

    [datetime] $EntryTime,

    [string] $VendorRecordGuid
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$wrapperLabel = 'com.exitpass.mock-hikcentral-wrapper'
$invocationLabel = 'com.exitpass.mock-hikcentral-invocation'
$expectedSiteId = '2d1dcdf8-f563-537c-8542-0bde7cc9da97'
$runtimeRoot = Join-Path $env:LOCALAPPDATA 'ExitPass\MockHikCentral'
$statePath = Join-Path $runtimeRoot 'state.json'
$sessionsPath = Join-Path $runtimeRoot 'sessions.json'
$lockPath = Join-Path $runtimeRoot 'sessions.lock'
$invariant = [Globalization.CultureInfo]::InvariantCulture

function Get-ContainerInspect([string] $Name) {
    $output = & docker container inspect $Name 2>$null
    if ($LASTEXITCODE -ne 0) {
        throw "Required mock container '$Name' is unavailable."
    }

    return ($output | ConvertFrom-Json)[0]
}

function Get-ObjectProperty($Object, [string] $Name) {
    if ($null -eq $Object) { return $null }
    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property) { return $null }
    return $property.Value
}

function Assert-OwnedRunningContainer(
    [string] $Name,
    [string] $InvocationId,
    [string] $ExpectedAlias
) {
    $container = Get-ContainerInspect $Name
    if (-not $container.State.Running) {
        throw "Mock container '$Name' is not running."
    }

    $owner = Get-ObjectProperty $container.Config.Labels $wrapperLabel
    $invocation = Get-ObjectProperty $container.Config.Labels $invocationLabel
    if ($owner -ne 'true' -or $invocation -ne $InvocationId) {
        throw "Container '$Name' is not owned by the active mock wrapper invocation."
    }

    if (-not [string]::IsNullOrWhiteSpace($ExpectedAlias)) {
        $network = Get-ObjectProperty $container.NetworkSettings.Networks 'exitpass-ist-persistent'
        if ($null -eq $network -or $network.Aliases -notcontains $ExpectedAlias) {
            throw "Container '$Name' does not have the expected '$ExpectedAlias' network alias."
        }
    }

    return $container
}

function Invoke-WireMockAdmin(
    [ValidateSet('Get', 'Post', 'Delete')]
    [string] $Method,
    [string] $Uri,
    $Body
) {
    $arguments = @{
        Method = $Method
        Uri = $Uri
        UseBasicParsing = $true
        TimeoutSec = 15
    }
    if ($null -ne $Body) {
        $arguments.ContentType = 'application/json'
        $arguments.Body = $Body | ConvertTo-Json -Depth 30 -Compress
    }
    return Invoke-RestMethod @arguments
}

function New-CommonHeaders([string] $AppKey) {
    return @{
        'Content-Type' = @{ matches = '(?i)^application/json(?:\s*;.*)?$' }
        'X-Ca-Key' = @{ equalTo = $AppKey }
        'X-Ca-Signature' = @{ matches = '.+' }
    }
}

function New-PassagewayMapping($Sessions, [string] $AppKey, [string] $ParkingLot) {
    $records = @()
    foreach ($session in $Sessions) {
        $records += @{
            guid = $session.vendorRecordGuid
            parkingLotInfo = @{
                parkingLotIndexCode = $ParkingLot
                parkingLotName = 'PITX Level 3'
            }
            passagewayInfo = @{
                passagewayIndexCode = 'PITX-L3-MOCK-ENTRY'
                passagewayName = 'PITX Level 3 Mock Entry'
            }
            laneInfo = @{
                laneIndexCode = 'PITX-L3-MOCK-LANE'
                laneName = 'PITX Level 3 Mock Lane'
                direction = 'ENTRY'
            }
            personInfo = @{ cardNum = $session.ticketReference }
            carInfo = @{
                plateLicense = $session.plateLicense
                EnterTime = $session.entryTime
                ExitTime = $null
            }
            allowType = '1'
            allowResult = '1'
        }
    }

    return @{
        name = 'ExitPass mock passageway inventory'
        priority = 1
        request = @{
            method = 'POST'
            urlPath = '/artemis/api/vehicle/v1/parkinglot/passageway/record'
            headers = New-CommonHeaders $AppKey
            bodyPatterns = @(
                @{ matchesJsonPath = "`$[?(@.queryInfo.parkingLotIndexCode == '$ParkingLot')]" }
            )
        }
        response = @{
            status = 200
            headers = @{ 'Content-Type' = 'application/json' }
            jsonBody = @{
                code = '0'
                msg = 'Success'
                data = @{
                    total = $records.Count
                    pageIndex = 1
                    pageSize = 100
                    list = $records
                }
            }
        }
    }
}

function New-CalculateMapping($Session, [string] $AppKey) {
    $entry = [DateTimeOffset]::Parse($Session.entryTime, $invariant)
    $duration = [Math]::Max(1, [int][Math]::Floor(([DateTimeOffset]::Now - $entry).TotalMinutes))
    $expectedBody = @{ cardNum = $Session.ticketReference } | ConvertTo-Json -Compress

    return @{
        name = "ExitPass mock calculate $($Session.ticketReference)"
        priority = 1
        request = @{
            method = 'POST'
            urlPath = '/artemis/api/vehicle/v1/parkingfee/calculate'
            headers = New-CommonHeaders $AppKey
            bodyPatterns = @(
                @{ equalToJson = $expectedBody; ignoreArrayOrder = $true; ignoreExtraElements = $false }
            )
        }
        response = @{
            status = 200
            headers = @{ 'Content-Type' = 'application/json' }
            jsonBody = @{
                code = '0'
                msg = 'Success'
                data = @{
                    plateLicense = $Session.plateLicense
                    cardNum = $Session.ticketReference
                    parkingInTime = $Session.entryTime
                    parkingDuration = $duration
                    feeRuleType = 0
                    feeRuleIndexCode = 'PITX-MOCK-RULE'
                    feeRuleName = 'PITX Mock Parking Rule'
                    fee = $Session.fee
                }
            }
        }
    }
}

function New-ConfirmMapping($Session, [string] $AppKey) {
    $expectedBody = @{
        cardNum = $Session.ticketReference
        immediatelyLeave = 0
        fee = $Session.fee
    } | ConvertTo-Json -Compress

    return @{
        name = "ExitPass mock confirm $($Session.ticketReference)"
        priority = 1
        request = @{
            method = 'POST'
            urlPath = '/artemis/api/vehicle/v1/parkingfee/confirm'
            headers = New-CommonHeaders $AppKey
            bodyPatterns = @(
                @{ equalToJson = $expectedBody; ignoreArrayOrder = $true; ignoreExtraElements = $false }
            )
        }
        response = @{
            status = 200
            headers = @{ 'Content-Type' = 'application/json' }
            jsonBody = @{
                code = '0'
                msg = 'Success'
                data = @{
                    fee = $Session.fee
                    feeTime = [DateTimeOffset]::Now.ToString('o', $invariant)
                }
            }
        }
    }
}

function New-AuthenticationFailureMapping {
    return @{
        name = 'ExitPass mock signed-request fallback'
        priority = 100
        request = @{
            method = 'POST'
            urlPathPattern = '/artemis/api/vehicle/v1/.*'
        }
        response = @{
            status = 200
            headers = @{ 'Content-Type' = 'application/json' }
            jsonBody = @{ code = '401'; msg = 'Authentication or mock fixture mismatch'; data = $null }
        }
    }
}

function Set-WireMockMappings($Sessions, [string] $AppKey, $State) {
    $baseUrl = $State.wireMockAdminUrl.TrimEnd('/')
    Invoke-WireMockAdmin Delete "$baseUrl/__admin/mappings" $null | Out-Null

    $mappings = @(
        (New-PassagewayMapping $Sessions $AppKey $State.parkingLotIndexCode)
    )
    foreach ($session in $Sessions) {
        $mappings += New-CalculateMapping $session $AppKey
        $mappings += New-ConfirmMapping $session $AppKey
    }
    $mappings += New-AuthenticationFailureMapping

    foreach ($mapping in $mappings) {
        Invoke-WireMockAdmin Post "$baseUrl/__admin/mappings" $mapping | Out-Null
    }

    $registered = Invoke-WireMockAdmin Get "$baseUrl/__admin/mappings" $null
    if (@($registered.mappings).Count -ne $mappings.Count) {
        throw 'WireMock did not retain the complete mock mapping set.'
    }
}

if (-not (Test-Path -LiteralPath $statePath -PathType Leaf)) {
    throw @"
Mock HikCentral is not running.
Start it first with:
.\scripts\v1.3\hikcentral\Start-MockHikCentralSession.ps1
"@
}
if (-not (Test-Path -LiteralPath $sessionsPath -PathType Leaf)) {
    throw 'The mock HikCentral session registry is unavailable. Restart the mock wrapper.'
}

$Ticket = $Ticket.Trim()
if ([string]::IsNullOrWhiteSpace($Ticket) -or $Ticket.Length -gt 32) {
    throw 'Ticket must be nonblank and no longer than 32 characters.'
}
if ($Fee -lt 0) {
    throw 'Fee must be greater than or equal to zero.'
}

$state = Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json
if ($state.siteId -ne $expectedSiteId -or [string]::IsNullOrWhiteSpace($state.parkingLotIndexCode)) {
    throw 'The active mock runtime state does not match the expected PITX Site and parking lot.'
}
if ($state.wireMockAdminUrl -notmatch '^http://127\.0\.0\.1:\d+$') {
    throw 'The mock WireMock administration endpoint is not loopback-only.'
}

$wireMock = Assert-OwnedRunningContainer $state.wireMockContainer $state.invocationId 'mock-hikcentral'
$mockAdapter = Assert-OwnedRunningContainer $state.mockAdapterContainer $state.invocationId 'pitx-site-adapter'

$appKeyMount = @($mockAdapter.Mounts | Where-Object { $_.Destination -eq '/run/exitpass/secrets/hikcentral/app-key' })
if ($appKeyMount.Count -ne 1 -or -not (Test-Path -LiteralPath $appKeyMount[0].Source -PathType Leaf)) {
    throw 'The active mock adapter HikCentral app-key mount is unavailable.'
}
$appKey = (Get-Content -LiteralPath $appKeyMount[0].Source -Raw).Trim()
if ([string]::IsNullOrWhiteSpace($appKey)) {
    throw 'The configured HikCentral app key is blank.'
}

$lockStream = $null
try {
    $lockStream = [IO.File]::Open($lockPath, [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
    $registryJson = Get-Content -LiteralPath $sessionsPath -Raw
    $existing = if ([string]::IsNullOrWhiteSpace($registryJson) -or $registryJson.Trim() -eq '[]') {
        @()
    }
    else {
        $parsedRegistry = ConvertFrom-Json -InputObject $registryJson
        @($parsedRegistry | ForEach-Object { $_ })
    }
    if (@($existing | Where-Object { $_.ticketReference -ieq $Ticket }).Count -gt 0) {
        throw "Mock ticket '$Ticket' already exists. Duplicate tickets are not overwritten."
    }

    if ([string]::IsNullOrWhiteSpace($Plate)) {
        $suffix = 1
        do {
            $Plate = 'MOCK{0:D4}' -f $suffix
            $suffix++
        } while (@($existing | Where-Object { $_.plateLicense -ieq $Plate }).Count -gt 0)
    }
    else {
        $Plate = $Plate.Trim()
        if ($Plate.Length -gt 32) { throw 'Plate must be no longer than 32 characters.' }
    }

    if (-not $PSBoundParameters.ContainsKey('EntryTime')) {
        $entry = [DateTimeOffset]::Now.AddHours(-1)
    }
    else {
        $entry = [DateTimeOffset]$EntryTime
    }

    if ([string]::IsNullOrWhiteSpace($VendorRecordGuid)) {
        $vendorGuid = [Guid]::NewGuid()
    }
    else {
        $vendorGuid = [Guid]::Empty
        if (-not [Guid]::TryParse($VendorRecordGuid, [ref]$vendorGuid) -or $vendorGuid -eq [Guid]::Empty) {
            throw 'VendorRecordGuid must be a non-empty valid GUID.'
        }
    }

    $session = [ordered]@{
        ticketReference = $Ticket
        plateLicense = $Plate
        vendorRecordGuid = $vendorGuid.ToString('D')
        entryTime = $entry.ToString('o', $invariant)
        fee = $Fee.ToString('0.00', $invariant)
        parkingLotIndexCode = $state.parkingLotIndexCode
    }
    $updated = @($existing) + @([pscustomobject]$session)

    try {
        Set-WireMockMappings $updated $appKey $state
    }
    catch {
        try { Set-WireMockMappings $existing $appKey $state } catch { }
        throw
    }

    $temporaryRegistry = "$sessionsPath.$PID.tmp"
    ConvertTo-Json -InputObject @($updated) -Depth 10 | Set-Content -LiteralPath $temporaryRegistry -Encoding UTF8
    Move-Item -LiteralPath $temporaryRegistry -Destination $sessionsPath -Force

    Write-Output ''
    Write-Output 'Mock HikCentral parking session created.'
    Write-Output ''
    Write-Output ("Ticket:       {0}" -f $session.ticketReference)
    Write-Output ("Plate:        {0}" -f $session.plateLicense)
    Write-Output ("Entry time:   {0}" -f $entry.ToString('yyyy-MM-dd HH:mm:ss zzz', $invariant))
    Write-Output ("Fee:          PHP {0}" -f $session.fee)
    Write-Output ("Vendor GUID:  {0}" -f $session.vendorRecordGuid)
    Write-Output ("Parking lot:  {0}" -f $session.parkingLotIndexCode)
    Write-Output ''
    Write-Output 'Use normally through WebPay/APT/Central PMS.'
    Write-Output 'No payment or fiscal state was created.'
}
finally {
    if ($null -ne $lockStream) { $lockStream.Dispose() }
}
