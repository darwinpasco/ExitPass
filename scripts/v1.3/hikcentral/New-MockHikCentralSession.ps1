[CmdletBinding()]
param(
    [string] $Ticket,

    [string] $Plate,

    [decimal] $Fee = 25.00,

    [datetime] $EntryTime,

    [ValidateSet('UNKNOWN', 'OTHER', 'BUS', 'TRUCK', 'CAR', 'VAN', 'LIGHT_TRUCK')]
    [string] $VehicleType = 'UNKNOWN',

    [switch] $ProjectionOnly,

    [string] $VendorRecordGuid,

    [switch] $RefreshMappings,

    [switch] $RecoveredRuntime,

    [string] $RecoveredWireMockContainer = 'exitpass-mock-hikcentral',

    [string] $RecoveredSiteAdapterContainer = 'exitpass-mock-pitx-site-adapter'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$wrapperLabel = 'com.exitpass.mock-hikcentral-wrapper'
$invocationLabel = 'com.exitpass.mock-hikcentral-invocation'
$expectedSiteId = '2d1dcdf8-f563-537c-8542-0bde7cc9da97'
$expectedSiteGroupId = 'a6dbadf6-68b5-5bed-a7e0-a75faee70841'
$expectedVendorSystemId = 'afdefaab-6be4-6b25-8f3f-3ad8309662e8'
$expectedNetwork = 'exitpass-ist-persistent'
$expectedParkingLot = '1'
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

function Get-ContainerEnvironmentValue($Container, [string] $Name) {
    $prefix = "$Name="
    $matches = @($Container.Config.Env | Where-Object { $_.StartsWith($prefix, [StringComparison]::Ordinal) })
    if ($matches.Count -ne 1) {
        throw "Container '$($Container.Name.TrimStart('/'))' does not have exactly one '$Name' setting."
    }
    return $matches[0].Substring($prefix.Length)
}

function Get-EffectiveContainerEnvironmentValue($Container, [string] $Name) {
    $prefix = "$Name="
    $matches = @($Container.Config.Env | Where-Object { $_.StartsWith($prefix, [StringComparison]::Ordinal) })
    if ($matches.Count -eq 0) {
        throw "Container '$($Container.Name.TrimStart('/'))' does not have a '$Name' setting."
    }
    return $matches[$matches.Count - 1].Substring($prefix.Length)
}

function Get-LoopbackContainerUrl($Container, [string] $ContainerPort) {
    $bindings = @(Get-ObjectProperty $Container.NetworkSettings.Ports $ContainerPort)
    if ($bindings.Count -ne 1 -or $bindings[0].HostIp -ne '127.0.0.1' -or
        [string]::IsNullOrWhiteSpace([string]$bindings[0].HostPort)) {
        throw "Container '$($Container.Name.TrimStart('/'))' does not expose '$ContainerPort' on one loopback-only port."
    }
    return "http://127.0.0.1:$($bindings[0].HostPort)"
}

function Assert-RecoveredRuntimeContainer(
    $Container,
    [string] $ExpectedAlias
) {
    $name = $Container.Name.TrimStart('/')
    if (-not $Container.State.Running) {
        throw "Recovered runtime container '$name' is not running."
    }

    $network = Get-ObjectProperty $Container.NetworkSettings.Networks $expectedNetwork
    if ($null -eq $network -or $network.Aliases -notcontains $ExpectedAlias) {
        throw "Recovered runtime container '$name' does not have the expected '$ExpectedAlias' alias on '$expectedNetwork'."
    }
}

function Resolve-RecoveredRuntimeState {
    $wireMock = Get-ContainerInspect $RecoveredWireMockContainer
    $mockAdapter = Get-ContainerInspect $RecoveredSiteAdapterContainer
    Assert-RecoveredRuntimeContainer $wireMock 'mock-hikcentral'
    Assert-RecoveredRuntimeContainer $mockAdapter 'pitx-site-adapter'

    $siteId = Get-ContainerEnvironmentValue $mockAdapter 'SiteAdapter__SiteId'
    $siteGroupId = Get-ContainerEnvironmentValue $mockAdapter 'SiteAdapter__SiteGroupId'
    $vendorSystemId = Get-ContainerEnvironmentValue $mockAdapter 'SiteAdapter__VendorSystemId'
    $parkingLot = Get-ContainerEnvironmentValue $mockAdapter 'SiteAdapter__ParkingLotIndexCode'
    $hikCentralBaseUrl = Get-EffectiveContainerEnvironmentValue $mockAdapter 'SiteAdapter__HikCentralBaseUrl'
    if ($siteId -ne $expectedSiteId -or $siteGroupId -ne $expectedSiteGroupId -or
        $vendorSystemId -ne $expectedVendorSystemId -or $parkingLot -ne $expectedParkingLot -or
        $hikCentralBaseUrl -ne 'http://mock-hikcentral:8080') {
        throw 'The recovered Site Adapter configuration does not match the approved PITX mock runtime.'
    }

    return [pscustomobject][ordered]@{
        runtimeMode = 'RECOVERED_RUNTIME'
        stateVersion = 1
        wireMockContainer = $RecoveredWireMockContainer
        wireMockContainerId = $wireMock.Id
        wireMockImageId = $wireMock.Image
        wireMockAdminUrl = Get-LoopbackContainerUrl $wireMock '8080/tcp'
        mockAdapterContainer = $RecoveredSiteAdapterContainer
        mockAdapterContainerId = $mockAdapter.Id
        mockAdapterImageId = $mockAdapter.Image
        network = $expectedNetwork
        siteId = $siteId
        siteGroupId = $siteGroupId
        vendorSystemId = $vendorSystemId
        parkingLotIndexCode = $parkingLot
        recoveredAt = [DateTimeOffset]::Now.ToString('o', $invariant)
    }
}

function Invoke-WireMockAdmin(
    [ValidateSet('Get', 'Post', 'Put', 'Delete')]
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

function Resolve-HikCentralCarType([string] $CanonicalVehicleType) {
    switch ($CanonicalVehicleType.Trim().ToUpperInvariant()) {
        'UNKNOWN' { return $null }
        'OTHER' { return '0' }
        'BUS' { return '3' }
        'TRUCK' { return '4' }
        'CAR' { return '5' }
        'VAN' { return '6' }
        'LIGHT_TRUCK' { return '7' }
        default { throw "Unsupported canonical vehicle type '$CanonicalVehicleType'." }
    }
}

function New-PassagewayMapping($Sessions, [string] $AppKey, [string] $ParkingLot) {
    $records = @()
    foreach ($session in $Sessions) {
        $carInfo = [ordered]@{
            plateLicense = $session.plateLicense
            EnterTime = $session.entryTime
            ExitTime = $null
        }
        $carType = Get-ObjectProperty $session 'carType'
        if (-not [string]::IsNullOrWhiteSpace([string]$carType)) {
            $carInfo.carType = [string]$carType
        }

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
            carInfo = $carInfo
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

function New-CalculateMapping($Session, [string] $AppKey, [string] $LookupField) {
    $entry = [DateTimeOffset]::Parse($Session.entryTime, $invariant)
    $duration = [Math]::Max(1, [int][Math]::Floor(([DateTimeOffset]::Now - $entry).TotalMinutes))
    $lookupValue = if ($LookupField -eq 'cardNum') { $Session.ticketReference } else { $Session.plateLicense }
    $expectedBody = @{ $LookupField = $lookupValue } | ConvertTo-Json -Compress

    return @{
        name = "ExitPass mock calculate $($Session.ticketReference) by $LookupField"
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
        $projectionOnly = [bool](Get-ObjectProperty $session 'projectionOnly')
        if (-not $projectionOnly) {
            $mappings += New-CalculateMapping $session $AppKey 'cardNum'
            $mappings += New-CalculateMapping $session $AppKey 'plateLicense'
            $mappings += New-ConfirmMapping $session $AppKey
        }
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

function Write-DurableJson([string] $Path, $Value, [int] $Depth = 10) {
    $temporaryPath = "$Path.$PID.tmp"
    try {
        ConvertTo-Json -InputObject $Value -Depth $Depth | Set-Content -LiteralPath $temporaryPath -Encoding UTF8
        Move-Item -LiteralPath $temporaryPath -Destination $Path -Force
    }
    finally {
        if (Test-Path -LiteralPath $temporaryPath) {
            Remove-Item -LiteralPath $temporaryPath -Force
        }
    }
}

function Initialize-RecoveredRuntimeRegistry($State) {
    [IO.Directory]::CreateDirectory($runtimeRoot) | Out-Null
    $registered = Invoke-WireMockAdmin Get "$($State.wireMockAdminUrl.TrimEnd('/'))/__admin/mappings" $null
    $passagewayMappings = @($registered.mappings | Where-Object {
        $_.request.method -eq 'POST' -and
        (Get-ObjectProperty $_.request 'urlPath') -eq '/artemis/api/vehicle/v1/parkinglot/passageway/record'
    })
    if ($passagewayMappings.Count -ne 1) {
        throw "Recovered WireMock must contain exactly one passageway mapping (found $($passagewayMappings.Count))."
    }

    if (Test-Path -LiteralPath $statePath -PathType Leaf) {
        $persistedState = Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json
        if ($persistedState.runtimeMode -ne 'RECOVERED_RUNTIME' -or
            $persistedState.wireMockContainerId -ne $State.wireMockContainerId -or
            $persistedState.mockAdapterContainerId -ne $State.mockAdapterContainerId -or
            $persistedState.siteId -ne $State.siteId -or
            $persistedState.parkingLotIndexCode -ne $State.parkingLotIndexCode) {
            throw 'The durable recovered-runtime state does not match the running mock containers.'
        }
    }
    else {
        Write-DurableJson $statePath $State 8
    }

    if (-not (Test-Path -LiteralPath $sessionsPath -PathType Leaf)) {
        $records = @($passagewayMappings[0].response.jsonBody.data.list)
        if ($records.Count -ne 0) {
            throw 'The recovered passageway mapping contains sessions but no authoritative durable session registry exists.'
        }
        Write-DurableJson $sessionsPath @() 4
    }
}

function Save-RecoveredMappingBackup($RegisteredMappings) {
    $backupRoot = Join-Path $runtimeRoot 'mapping-backups'
    [IO.Directory]::CreateDirectory($backupRoot) | Out-Null
    $stamp = [DateTimeOffset]::Now.ToString('yyyyMMdd-HHmmssfff', $invariant)
    $path = Join-Path $backupRoot "wiremock-mappings-$stamp.json"
    Write-DurableJson $path $RegisteredMappings 40
    return $path
}

function Set-RecoveredWireMockMappings(
    [Parameter(Mandatory)]
    [AllowEmptyCollection()]
    [object[]] $ExistingSessions,
    [Parameter(Mandatory)]
    [AllowEmptyCollection()]
    [object[]] $Sessions,
    [string] $AppKey,
    $State,
    [string] $RegistryPath
) {
    $baseUrl = $State.wireMockAdminUrl.TrimEnd('/')
    $registered = Invoke-WireMockAdmin Get "$baseUrl/__admin/mappings" $null
    $beforeMappings = @($registered.mappings)
    Save-RecoveredMappingBackup $registered | Out-Null

    $passagewayMappings = @($beforeMappings | Where-Object {
        $_.request.method -eq 'POST' -and
        (Get-ObjectProperty $_.request 'urlPath') -eq '/artemis/api/vehicle/v1/parkinglot/passageway/record'
    })
    if ($passagewayMappings.Count -ne 1) {
        throw "Recovered WireMock must contain exactly one passageway mapping (found $($passagewayMappings.Count))."
    }
    $currentPassageRecords = @($passagewayMappings[0].response.jsonBody.data.list)
    if ($currentPassageRecords.Count -ne @($ExistingSessions).Count) {
        throw 'The recovered passageway mapping does not reconcile to the durable session registry.'
    }

    $updatedOriginals = [ordered]@{}
    $createdIds = [Collections.Generic.List[string]]::new()
    try {
        $passagewayId = [string]$passagewayMappings[0].id
        if ([string]::IsNullOrWhiteSpace($passagewayId)) {
            throw 'The recovered passageway mapping has no durable mapping ID.'
        }
        $originalPassageway = ConvertFrom-Json (ConvertTo-Json $passagewayMappings[0] -Depth 30)
        $updatedOriginals[$passagewayId] = $originalPassageway
        $passageway = ConvertFrom-Json (ConvertTo-Json $passagewayMappings[0] -Depth 30)
        $desiredPassageway = New-PassagewayMapping $Sessions $AppKey $State.parkingLotIndexCode
        $passageway.response.jsonBody.data.total = $desiredPassageway.response.jsonBody.data.total
        $passageway.response.jsonBody.data.list = $desiredPassageway.response.jsonBody.data.list
        Invoke-WireMockAdmin Put "$baseUrl/__admin/mappings/$passagewayId" $passageway | Out-Null

        $desiredMappings = @()
        foreach ($session in $Sessions) {
            $projectionOnly = [bool](Get-ObjectProperty $session 'projectionOnly')
            if (-not $projectionOnly) {
                $desiredMappings += New-CalculateMapping $session $AppKey 'cardNum'
                $desiredMappings += New-CalculateMapping $session $AppKey 'plateLicense'
                $desiredMappings += New-ConfirmMapping $session $AppKey
            }
        }
        $desiredMappings += New-AuthenticationFailureMapping

        foreach ($desired in $desiredMappings) {
            $matches = @($beforeMappings | Where-Object { $_.name -eq $desired.name })
            if ($matches.Count -gt 1) {
                throw "Recovered WireMock contains duplicate mapping name '$($desired.name)'."
            }
            if ($matches.Count -eq 1) {
                $mappingId = [string]$matches[0].id
                $updatedOriginals[$mappingId] = $matches[0]
                $desired['id'] = $mappingId
                Invoke-WireMockAdmin Put "$baseUrl/__admin/mappings/$mappingId" $desired | Out-Null
            }
            else {
                $mappingId = [Guid]::NewGuid().ToString('D')
                $desired['id'] = $mappingId
                Invoke-WireMockAdmin Post "$baseUrl/__admin/mappings" $desired | Out-Null
                $createdIds.Add($mappingId)
            }
        }

        $after = Invoke-WireMockAdmin Get "$baseUrl/__admin/mappings" $null
        $afterMappings = @($after.mappings)
        if ($afterMappings.Count -ne ($beforeMappings.Count + $createdIds.Count)) {
            throw 'Recovered WireMock did not retain the expected mapping count.'
        }
        $afterPassageway = @($afterMappings | Where-Object { [string]$_.id -eq $passagewayId })
        if ($afterPassageway.Count -ne 1 -or
            @($afterPassageway[0].response.jsonBody.data.list).Count -ne @($Sessions).Count) {
            throw 'Recovered WireMock did not retain the updated passageway inventory.'
        }
        foreach ($desired in $desiredMappings) {
            if (@($afterMappings | Where-Object { $_.name -eq $desired.name }).Count -ne 1) {
                throw "Recovered WireMock did not retain exactly one '$($desired.name)' mapping."
            }
        }
        foreach ($original in $beforeMappings) {
            if (@($afterMappings | Where-Object { [string]$_.id -eq [string]$original.id }).Count -ne 1) {
                throw "Recovered WireMock lost pre-existing mapping '$($original.id)'."
            }
        }
        Write-DurableJson $RegistryPath @($Sessions) 10
    }
    catch {
        $registrationError = $_
        $rollbackErrors = [Collections.Generic.List[string]]::new()
        foreach ($createdId in $createdIds) {
            try { Invoke-WireMockAdmin Delete "$baseUrl/__admin/mappings/$createdId" $null | Out-Null }
            catch { $rollbackErrors.Add("delete $createdId failed") }
        }
        foreach ($entry in $updatedOriginals.GetEnumerator()) {
            try { Invoke-WireMockAdmin Put "$baseUrl/__admin/mappings/$($entry.Key)" $entry.Value | Out-Null }
            catch { $rollbackErrors.Add("restore $($entry.Key) failed") }
        }
        if ($rollbackErrors.Count -gt 0) {
            throw "Mock session registration failed and mapping rollback was incomplete: $($rollbackErrors -join '; '). Original error: $($registrationError.Exception.Message)"
        }
        throw $registrationError
    }
}

if ($RecoveredRuntime) {
    $recoveredState = Resolve-RecoveredRuntimeState
    Initialize-RecoveredRuntimeRegistry $recoveredState
}
else {
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
}

if (-not $RefreshMappings) {
    $Ticket = $Ticket.Trim()
    if ([string]::IsNullOrWhiteSpace($Ticket) -or $Ticket.Length -gt 32) {
        throw 'Ticket must be nonblank and no longer than 32 characters.'
    }
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

if ($RecoveredRuntime) {
    $wireMock = Get-ContainerInspect $state.wireMockContainer
    $mockAdapter = Get-ContainerInspect $state.mockAdapterContainer
    Assert-RecoveredRuntimeContainer $wireMock 'mock-hikcentral'
    Assert-RecoveredRuntimeContainer $mockAdapter 'pitx-site-adapter'
}
else {
    $wireMock = Assert-OwnedRunningContainer $state.wireMockContainer $state.invocationId 'mock-hikcentral'
    $mockAdapter = Assert-OwnedRunningContainer $state.mockAdapterContainer $state.invocationId 'pitx-site-adapter'
}

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
    [object[]] $existing = @()
    if (-not [string]::IsNullOrWhiteSpace($registryJson) -and $registryJson.Trim() -ne '[]') {
        $parsedRegistry = ConvertFrom-Json -InputObject $registryJson
        $existing = @($parsedRegistry | ForEach-Object { $_ })
    }
    if ($RefreshMappings) {
        if ($RecoveredRuntime) {
            Set-RecoveredWireMockMappings -ExistingSessions $existing -Sessions $existing -AppKey $appKey -State $state -RegistryPath $sessionsPath
        }
        else {
            Set-WireMockMappings $existing $appKey $state
        }
        Write-Output "Refreshed mock HikCentral mappings for $($existing.Count) existing sessions."
        return
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
        if (@($existing | Where-Object { $_.plateLicense -ieq $Plate }).Count -gt 0) {
            throw "Mock plate '$Plate' already exists. Duplicate plates are not overwritten."
        }
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

    $normalizedVehicleType = $VehicleType.Trim().ToUpperInvariant()
    $hikCentralCarType = Resolve-HikCentralCarType $normalizedVehicleType

    $session = [ordered]@{
        ticketReference = $Ticket
        plateLicense = $Plate
        vendorRecordGuid = $vendorGuid.ToString('D')
        entryTime = $entry.ToString('o', $invariant)
        fee = $Fee.ToString('0.00', $invariant)
        parkingLotIndexCode = $state.parkingLotIndexCode
        vehicleType = $normalizedVehicleType
        carType = $hikCentralCarType
        projectionOnly = [bool]$ProjectionOnly
    }
    $updated = @($existing) + @([pscustomobject]$session)

    if ($RecoveredRuntime) {
        Set-RecoveredWireMockMappings -ExistingSessions $existing -Sessions $updated -AppKey $appKey -State $state -RegistryPath $sessionsPath
    }
    else {
        try {
            Set-WireMockMappings $updated $appKey $state
        }
        catch {
            try { Set-WireMockMappings $existing $appKey $state } catch { }
            throw
        }
    }

    if (-not $RecoveredRuntime) {
        Write-DurableJson $sessionsPath @($updated) 10
    }

    Write-Output ''
    Write-Output 'Mock HikCentral parking session created.'
    Write-Output ''
    Write-Output ("Ticket:       {0}" -f $session.ticketReference)
    Write-Output ("Plate:        {0}" -f $session.plateLicense)
    Write-Output ("Entry time:   {0}" -f $entry.ToString('yyyy-MM-dd HH:mm:ss zzz', $invariant))
    Write-Output ("Vehicle type: {0}" -f $session.vehicleType)
    Write-Output ("HCP carType:  {0}" -f $(if ($null -eq $session.carType) { '<omitted>' } else { $session.carType }))
    Write-Output ("Mode:         {0}" -f $(if ($session.projectionOnly) { 'PROJECTION_ONLY' } else { 'LIVE_FEE_AND_PROJECTION' }))
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
