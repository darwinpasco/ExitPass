[CmdletBinding()]
param(
    [string]$Ticket,
    [string]$Plate,
    [ValidateSet('UNKNOWN', 'OTHER', 'BUS', 'TRUCK', 'CAR', 'VAN', 'LIGHT_TRUCK')]
    [string]$VehicleType = 'CAR',
    [ValidateRange(0, 9999999)]
    [decimal]$Fee = 50.00,
    [DateTimeOffset]$EntryTime,
    [switch]$PreflightOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$environmentName = 'DEV'
$environmentRoot = 'D:\SourceCodes\ExitPass.dev'
$composeProject = 'exitpass-dev-20261122'
$networkName = 'exitpass-dev-20261122-network'
$wireMockContainer = 'exitpass-dev-20261122-hikcentral'
$siteAdapterContainer = 'exitpass-dev-20261122-site-adapter'
$centralApiContainer = 'exitpass-dev-20261122-central'
$centralContainer = 'exitpass-dev-20261122-central-db'
$posContainer = 'exitpass-dev-20261122-pos-db'
$ingressContainer = 'exitpass-dev-20261122-ingress'
$publishNetworkName = 'exitpass-dev-20261122-publish'
$centralDatabase = 'exitpass_dev_central'
$posDatabase = 'exitpass_dev_pos'
$centralPmsUrl = 'http://127.0.0.1:58165'
$siteAdapterUrl = 'http://127.0.0.1:53648'
$wireMockAdminUrl = 'http://127.0.0.1:53477'
$wireMockVolume = 'exitpass-dev-20261122-wiremock'
$wireMockVolumePath = '/home/wiremock/mappings'
$mappingDirectories = @(
    'D:\SourceCodes\ExitPass.dev\data\wiremock\mappings',
    'D:\SourceCodes\ExitPass.dev\data\wiremock-runtime\mappings'
)
$operationRoot = 'D:\SourceCodes\ExitPass.dev\data\environment-tools\New-ExitPassDevParkingSession'
$expectedHikCentralUrl = 'http://exitpass-dev-20261122-hikcentral:8080'
$expectedAppKeyFile = '/dev-private/secrets/hik-app-key.txt'
$expectedSiteId = '2d1dcdf8-f563-537c-8542-0bde7cc9da97'
$expectedSiteGroupId = 'a6dbadf6-68b5-5bed-a7e0-a75faee70841'
$expectedVendorSystemId = 'afdefaab-6be4-6b25-8f3f-3ad8309662e8'
$expectedParkingLot = '1'
$invariant = [Globalization.CultureInfo]::InvariantCulture

function Get-PropertyValue($Object, [string]$Name) {
    if ($null -eq $Object) { return $null }
    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property) { return $null }
    return $property.Value
}

function Get-Container([string]$Name) {
    $json = & docker inspect $Name 2>$null
    if ($LASTEXITCODE -ne 0) { throw "Required Dev container '$Name' is unavailable." }
    return ($json | ConvertFrom-Json)[0]
}

function Get-EnvironmentValue($Container, [string]$Name) {
    $prefix = "$Name="
    $matches = @($Container.Config.Env | Where-Object { $_.StartsWith($prefix, [StringComparison]::Ordinal) })
    if ($matches.Count -eq 0) { throw "Container '$($Container.Name.TrimStart('/'))' is missing $Name." }
    return $matches[$matches.Count - 1].Substring($prefix.Length)
}

function Assert-ContainerIdentity($Container, [string]$ExpectedAlias, [string]$ExpectedService, [string[]]$AllowedAdditionalNetworks = @()) {
    $name = $Container.Name.TrimStart('/')
    if (-not $Container.State.Running) { throw "Required Dev container '$name' is not running." }
    $project = [string](Get-PropertyValue $Container.Config.Labels 'com.docker.compose.project')
    $service = [string](Get-PropertyValue $Container.Config.Labels 'com.docker.compose.service')
    if ($project -ne $composeProject -or $service -ne $ExpectedService) {
        throw "Container '$name' is not owned by the approved Dev Compose project/service."
    }
    $network = Get-PropertyValue $Container.NetworkSettings.Networks $networkName
    if ($null -eq $network -or @($network.Aliases) -notcontains $ExpectedAlias) {
        throw "Container '$name' is not isolated on '$networkName' as '$ExpectedAlias'."
    }
    $allowedNetworks = @($networkName) + @($AllowedAdditionalNetworks)
    if (@($Container.NetworkSettings.Networks.PSObject.Properties.Name | Where-Object { $allowedNetworks -notcontains $_ }).Count -ne 0) {
        throw "Container '$name' is attached to an unexpected network."
    }
}

function Assert-PublishedLoopbackEndpoint($Ingress, [string]$Url, [int]$ContainerPort, [string]$Description) {
    try { $uri = [Uri]$Url }
    catch { throw "$Description endpoint '$Url' is invalid." }
    if ($uri.Scheme -notin @('http', 'https') -or $uri.Host -notin @('127.0.0.1', 'localhost')) {
        throw "$Description endpoint must use an approved loopback HTTP origin."
    }
    $binding = Get-PropertyValue $Ingress.HostConfig.PortBindings "$ContainerPort/tcp"
    $matches = @($binding | Where-Object {
        $_.HostIp -eq '127.0.0.1' -and [int]$_.HostPort -eq $uri.Port
    })
    if (@($binding).Count -ne 1 -or $matches.Count -ne 1) {
        throw "$Description endpoint '$Url' is not bound to the inspected Dev ingress container port $ContainerPort on loopback."
    }
}

function Assert-IngressRoute([string]$Configuration, [int]$ListenPort, [string]$ExpectedUpstream, [string]$Description) {
    $blocks = @([regex]::Matches($Configuration, '(?s)\bserver\s*\{(?<body>.*?)\}') | Where-Object {
        $_.Groups['body'].Value -match "\blisten\s+$ListenPort(?:\s+[^;]*)?;"
    })
    if ($blocks.Count -ne 1 -or $blocks[0].Groups['body'].Value -notmatch "\bproxy_pass\s+(?:http://)?$([regex]::Escape($ExpectedUpstream))/?\s*;") {
        throw "$Description ingress route does not target '$ExpectedUpstream'."
    }
}

function Get-IngressConfiguration {
    $output = & docker exec $ingressContainer /bin/sh -c 'nginx -T 2>&1'
    if ($LASTEXITCODE -ne 0) { throw 'Unable to inspect the Dev ingress configuration.' }
    return ($output -join "`n")
}

function Invoke-WireMockAdmin {
    param(
        [ValidateSet('Get', 'Post', 'Put', 'Delete')][string]$Method,
        [string]$Uri,
        $Body
    )
    $arguments = @{ Method = $Method; Uri = $Uri; UseBasicParsing = $true; TimeoutSec = 15 }
    if ($null -ne $Body) {
        $arguments.ContentType = 'application/json'
        $arguments.Body = ConvertTo-Json -InputObject $Body -Depth 50 -Compress
    }
    return Invoke-RestMethod @arguments
}

function Write-DurableJson([string]$Path, $Value, [int]$Depth = 30) {
    $temporary = "$Path.$PID.tmp"
    try {
        ConvertTo-Json -InputObject $Value -Depth $Depth | Set-Content -LiteralPath $temporary -Encoding UTF8
        Move-Item -LiteralPath $temporary -Destination $Path -Force
    }
    finally {
        if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary -Force }
    }
}

function Get-HikCentralCarType([string]$Type) {
    switch ($Type.ToUpperInvariant()) {
        'UNKNOWN' { return $null }
        'OTHER' { return '0' }
        'BUS' { return '3' }
        'TRUCK' { return '4' }
        'CAR' { return '5' }
        'VAN' { return '6' }
        'LIGHT_TRUCK' { return '7' }
    }
}

function Get-AcceptedFee([decimal]$Value) {
    $minorUnits = $Value * 100
    if ([decimal]::Truncate($minorUnits) -ne $minorUnits) {
        throw 'Fee must be representable exactly to two decimal places.'
    }
    return [decimal]::Round($Value, 2, [MidpointRounding]::AwayFromZero)
}

function Test-PassageRecordVehicleType($Record, $ExpectedCarType) {
    $actual = Get-PropertyValue $Record.carInfo 'carType'
    if ($null -eq $ExpectedCarType) {
        return $null -eq $actual -or [string]::IsNullOrWhiteSpace([string]$actual)
    }
    return [string]$actual -eq [string]$ExpectedCarType
}

function New-CommonHeaders([string]$AppKey) {
    return @{
        'Content-Type' = @{ matches = '(?i)^application/json(?:\s*;.*)?$' }
        'X-Ca-Key' = @{ equalTo = $AppKey }
        'X-Ca-Signature' = @{ matches = '.+' }
    }
}

function New-PassageRecord($Session) {
    $carInfo = [ordered]@{
        plateLicense = $Session.plateLicense
        EnterTime = $Session.entryTime
        ExitTime = $null
    }
    if (-not [string]::IsNullOrWhiteSpace([string]$Session.carType)) {
        $carInfo.carType = [string]$Session.carType
    }
    return [ordered]@{
        guid = $Session.vendorRecordGuid
        parkingLotInfo = [ordered]@{ parkingLotIndexCode = $expectedParkingLot; parkingLotName = 'PITX Level 3' }
        passagewayInfo = [ordered]@{ passagewayIndexCode = 'PITX-L3-MOCK-ENTRY'; passagewayName = 'PITX Level 3 Mock Entry' }
        laneInfo = [ordered]@{ laneIndexCode = 'PITX-L3-MOCK-LANE'; laneName = 'PITX Level 3 Mock Lane'; direction = 'ENTRY' }
        personInfo = [ordered]@{ cardNum = $Session.ticketReference }
        carInfo = $carInfo
        allowType = '1'
        allowResult = '1'
    }
}

function New-CalculateMapping($Session, [string]$AppKey, [string]$LookupField, [string]$MappingId) {
    $entry = [DateTimeOffset]::Parse([string]$Session.entryTime, $invariant)
    $duration = [Math]::Max(1, [int][Math]::Floor(([DateTimeOffset]::Now - $entry).TotalMinutes))
    $lookupValue = if ($LookupField -eq 'cardNum') { $Session.ticketReference } else { $Session.plateLicense }
    $expectedBody = @{ $LookupField = $lookupValue } | ConvertTo-Json -Compress
    return [ordered]@{
        id = $MappingId
        uuid = $MappingId
        persistent = $true
        name = "ExitPass mock calculate $($Session.ticketReference) by $LookupField"
        priority = 1
        request = [ordered]@{
            method = 'POST'
            urlPath = '/artemis/api/vehicle/v1/parkingfee/calculate'
            headers = New-CommonHeaders $AppKey
            bodyPatterns = @(@{ equalToJson = $expectedBody; ignoreArrayOrder = $true; ignoreExtraElements = $false })
        }
        response = [ordered]@{
            status = 200
            headers = @{ 'Content-Type' = 'application/json' }
            jsonBody = [ordered]@{
                code = '0'
                msg = 'Success'
                data = [ordered]@{
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

function New-ConfirmMapping($Session, [string]$AppKey, [string]$MappingId) {
    $expectedBody = @{ cardNum = $Session.ticketReference; immediatelyLeave = 0; fee = $Session.fee } | ConvertTo-Json -Compress
    return [ordered]@{
        id = $MappingId
        uuid = $MappingId
        persistent = $true
        name = "ExitPass mock confirm $($Session.ticketReference)"
        priority = 1
        request = [ordered]@{
            method = 'POST'
            urlPath = '/artemis/api/vehicle/v1/parkingfee/confirm'
            headers = New-CommonHeaders $AppKey
            bodyPatterns = @(@{ equalToJson = $expectedBody; ignoreArrayOrder = $true; ignoreExtraElements = $false })
        }
        response = [ordered]@{
            status = 200
            headers = @{ 'Content-Type' = 'application/json' }
            jsonBody = [ordered]@{
                code = '0'
                msg = 'Success'
                data = [ordered]@{ fee = $Session.fee; feeTime = [DateTimeOffset]::Now.ToString('o', $invariant) }
            }
        }
    }
}

function Invoke-DatabaseScalar([string]$ContainerName, [string]$DatabaseName, [string]$Sql) {
    $container = Get-Container $ContainerName
    $user = Get-EnvironmentValue $container 'POSTGRES_USER'
    $password = Get-EnvironmentValue $container 'POSTGRES_PASSWORD'
    $configuredDatabase = Get-EnvironmentValue $container 'POSTGRES_DB'
    if ($configuredDatabase -ne $DatabaseName) {
        throw "Database container '$ContainerName' targets '$configuredDatabase', not '$DatabaseName'."
    }
    $output = & docker exec -e "PGPASSWORD=$password" $ContainerName psql -X -A -t -v ON_ERROR_STOP=1 -U $user -d $DatabaseName -c $Sql 2>&1
    if ($LASTEXITCODE -ne 0) { throw "Read-only database verification failed for '$DatabaseName'." }
    return (($output -join "`n").Trim())
}

function Get-FinancialSnapshot {
    $centralSql = @"
SELECT concat_ws('|',
  (SELECT count(*) FROM core.payment_attempts),
  (SELECT count(*) FROM core.payment_confirmations),
  (SELECT count(*) FROM core.fiscal_issuance_references),
  (SELECT count(*) FROM core.exit_authorizations),
  (SELECT count(*) FROM core.terminal_cash_payment_commands));
"@
    $posSql = @"
SELECT concat_ws('|',
  (SELECT count(*) FROM pos.fiscal_documents),
  (SELECT count(*) FROM pos.electronic_journal_records),
  (SELECT count(*) FROM pos.fiscal_reporting_periods));
"@
    return "$(Invoke-DatabaseScalar $centralContainer $centralDatabase $centralSql)::$(Invoke-DatabaseScalar $posContainer $posDatabase $posSql)"
}

function Get-ProjectionCount([string]$TicketValue, [string]$PlateValue) {
    $sql = @"
SELECT count(*)
FROM sessions.vendor_session_projections
WHERE site_id = '$expectedSiteId'::uuid
  AND site_group_id = '$expectedSiteGroupId'::uuid
  AND vendor_system_id = '$expectedVendorSystemId'::uuid
  AND parking_lot_index_code = '$expectedParkingLot'
  AND card_num = '$TicketValue'
  AND upper(plate_license) = upper('$PlateValue');
"@
    return [int](Invoke-DatabaseScalar $centralContainer $centralDatabase $sql)
}

function Get-MirrorInventory([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path -PathType Container)) { throw "Dev mapping mirror is unavailable: $Path" }
    $items = @()
    foreach ($file in @(Get-ChildItem -LiteralPath $Path -File -Filter '*.json')) {
        try { $mapping = Get-Content -LiteralPath $file.FullName -Raw | ConvertFrom-Json }
        catch { throw "Dev mapping file is unreadable or invalid JSON: $($file.FullName)" }
        $id = [string](Get-PropertyValue $mapping 'id')
        if ([string]::IsNullOrWhiteSpace($id)) { $id = [string](Get-PropertyValue $mapping 'uuid') }
        if ([string]::IsNullOrWhiteSpace($id)) { throw "Dev mapping file has no identity: $($file.FullName)" }
        $items += [pscustomobject]@{ Id = $id; Name = $file.Name; File = $file; Mapping = $mapping }
    }
    if (@($items | Group-Object Id | Where-Object Count -ne 1).Count -ne 0) { throw "Dev mapping mirror '$Path' contains duplicate mapping IDs." }
    return [pscustomobject]@{ Path = $Path; Items = @($items) }
}

function Assert-ExactStringSet([string[]]$Expected, [string[]]$Actual, [string]$Description) {
    $left = @($Expected | Sort-Object -Unique)
    $right = @($Actual | Sort-Object -Unique)
    if ($left.Count -ne $right.Count -or @(Compare-Object -ReferenceObject $left -DifferenceObject $right).Count -ne 0) {
        throw "$Description does not match the approved Dev runtime."
    }
}

function ConvertTo-CanonicalJson($Value) {
    if ($null -eq $Value) { return 'null' }
    if ($Value -is [string] -or $Value -is [char]) {
        return [string](ConvertTo-Json -InputObject ([string]$Value) -Compress)
    }
    if ($Value -is [Collections.IDictionary]) {
        $names = [Collections.Generic.List[string]]::new()
        foreach ($key in $Value.Keys) { $names.Add([string]$key) }
        $names.Sort([StringComparer]::Ordinal)
        $parts = @($names | ForEach-Object {
            "$(ConvertTo-Json -InputObject $_ -Compress):$(ConvertTo-CanonicalJson $Value[$_])"
        })
        return "{$($parts -join ',')}"
    }
    if ($Value -is [Collections.IEnumerable] -and $Value -isnot [string]) {
        $parts = @($Value | ForEach-Object { ConvertTo-CanonicalJson $_ })
        return "[$($parts -join ',')]"
    }
    $properties = @($Value.PSObject.Properties | Where-Object {
        $_.MemberType -in @('NoteProperty', 'Property', 'AliasProperty')
    })
    if ($properties.Count -gt 0 -and $Value -isnot [ValueType]) {
        $names = [Collections.Generic.List[string]]::new()
        foreach ($property in $properties) { $names.Add([string]$property.Name) }
        $names.Sort([StringComparer]::Ordinal)
        $parts = @($names | ForEach-Object {
            "$(ConvertTo-Json -InputObject $_ -Compress):$(ConvertTo-CanonicalJson (Get-PropertyValue $Value $_))"
        })
        return "{$($parts -join ',')}"
    }
    return [string](ConvertTo-Json -InputObject $Value -Compress)
}

function Get-ComparableMappingJson(
    $Mapping,
    [ValidateSet('Live', 'PersistedTemplate', 'PersistedRuntime')][string]$SourceKind,
    [string]$Description,
    [string]$AppKey
) {
    $clone = ConvertFrom-Json (ConvertTo-Json -InputObject $Mapping -Depth 50)
    $persistentProperty = $clone.PSObject.Properties['persistent']
    if ($null -eq $persistentProperty) { throw "$Description has no persistent setting." }
    if ($SourceKind -ne 'Live' -and $persistentProperty.Value -ne $true) {
        throw "$Description must be stored with persistent=true."
    }
    # Live registrations intentionally use persistent=false because the runtime volume is read-only.
    # Stored mappings must remain persistent=true. WireMock may persist insertionIndex ordering metadata
    # that its live admin representation omits. Normalize only those explicit runtime differences here.
    $clone.persistent = $true
    if ($null -ne $clone.PSObject.Properties['insertionIndex']) {
        $clone.PSObject.Properties.Remove('insertionIndex')
    }
    $headers = Get-PropertyValue (Get-PropertyValue $clone 'request') 'headers'
    $keyMatcher = Get-PropertyValue $headers 'X-Ca-Key'
    $keyProperty = if ($null -eq $keyMatcher) { $null } else { $keyMatcher.PSObject.Properties['equalTo'] }
    if ($null -ne $keyProperty) {
        $mappingKey = [string]$keyProperty.Value
        if ([string]::IsNullOrWhiteSpace($mappingKey)) { throw "$Description has a blank X-Ca-Key matcher." }
        if ($SourceKind -in @('Live', 'PersistedRuntime')) {
            if ([string]::IsNullOrWhiteSpace($AppKey) -or $mappingKey -cne $AppKey) {
                throw "$Description does not contain the validated Dev runtime X-Ca-Key matcher."
            }
        }
        # The source mirror may intentionally retain its non-runtime placeholder. The runtime mirror,
        # named volume, and live registration must all contain the app key read from the approved
        # read-only private volume. Normalize only this one deployment materialization after validation.
        $keyProperty.Value = '__EXITPASS_VALIDATED_DEV_APP_KEY__'
    }
    return ConvertTo-CanonicalJson $clone
}

function Assert-MappingContentEquivalent(
    $LiveMapping,
    $PersistedMapping,
    [ValidateSet('PersistedTemplate', 'PersistedRuntime')][string]$PersistedKind,
    [string]$Description,
    [string]$AppKey
) {
    $liveId = [string](Get-PropertyValue $LiveMapping 'id')
    $persistedId = [string](Get-PropertyValue $PersistedMapping 'id')
    if ($liveId -ne $persistedId) { throw "$Description mapping identity differs from live WireMock." }
    $liveJson = Get-ComparableMappingJson $LiveMapping 'Live' "Live mapping '$liveId'" $AppKey
    $persistedJson = Get-ComparableMappingJson $PersistedMapping $PersistedKind $Description $AppKey
    if ($liveJson -cne $persistedJson) {
        throw "$Description content differs from live WireMock for mapping '$liveId'."
    }
}

function Assert-StoredMappingInventory(
    $LiveMappings,
    $Inventory,
    [ValidateSet('PersistedTemplate', 'PersistedRuntime')][string]$PersistedKind,
    [string]$Description,
    [string]$AppKey
) {
    $liveIds = @($LiveMappings | ForEach-Object { [string]$_.id })
    $storedIds = @($Inventory.Items | ForEach-Object { [string]$_.Id })
    Assert-ExactStringSet $liveIds $storedIds "$Description mapping IDs"
    foreach ($live in $LiveMappings) {
        $match = @($Inventory.Items | Where-Object { [string]$_.Id -eq [string]$live.id })
        if ($match.Count -ne 1) { throw "$Description does not uniquely contain mapping '$($live.id)'." }
        Assert-MappingContentEquivalent $live $match[0].Mapping $PersistedKind "$Description file '$($match[0].Name)'" $AppKey
    }
}

function Assert-PersistedMappingInventoriesEquivalent(
    $Reference,
    [ValidateSet('PersistedTemplate', 'PersistedRuntime')][string]$ReferenceKind,
    $Candidate,
    [ValidateSet('PersistedTemplate', 'PersistedRuntime')][string]$CandidateKind,
    [string]$Description,
    [string]$AppKey
) {
    Assert-ExactStringSet @($Reference.Items | ForEach-Object Id) @($Candidate.Items | ForEach-Object Id) "$Description mapping IDs"
    Assert-ExactStringSet @($Reference.Items | ForEach-Object Name) @($Candidate.Items | ForEach-Object Name) "$Description mapping filenames"
    foreach ($referenceItem in @($Reference.Items)) {
        $candidateItem = @($Candidate.Items | Where-Object { [string]$_.Id -eq [string]$referenceItem.Id })
        if ($candidateItem.Count -ne 1 -or $candidateItem[0].Name -ne $referenceItem.Name) {
            throw "$Description does not uniquely preserve file '$($referenceItem.Name)'."
        }
        $referenceJson = Get-ComparableMappingJson $referenceItem.Mapping $ReferenceKind "$Description reference file '$($referenceItem.Name)'" $AppKey
        $candidateJson = Get-ComparableMappingJson $candidateItem[0].Mapping $CandidateKind "$Description candidate file '$($candidateItem[0].Name)'" $AppKey
        if ($referenceJson -cne $candidateJson) {
            throw "$Description persisted content differs for file '$($referenceItem.Name)'."
        }
    }
}

function Get-VolumeInventory {
    $readScript = 'for f in /home/wiremock/mappings/*.json; do [ -e "$f" ] || continue; printf ''__EXITPASS_FILE__%s\n'' "${f##*/}"; base64 "$f"; printf ''__EXITPASS_END__\n''; done'
    $output = & docker exec $wireMockContainer /bin/sh -c $readScript 2>&1
    if ($LASTEXITCODE -ne 0) { throw 'Unable to read the Dev WireMock named volume.' }
    $items = @()
    $name = $null
    $encoded = [Text.StringBuilder]::new()
    foreach ($lineObject in @($output)) {
        $line = [string]$lineObject
        if ($line.StartsWith('__EXITPASS_FILE__', [StringComparison]::Ordinal)) {
            if ($null -ne $name) { throw 'Dev WireMock volume inventory framing is invalid.' }
            $name = $line.Substring('__EXITPASS_FILE__'.Length)
            if ($name -notmatch '^[A-Za-z0-9._-]+\.json$') { throw 'Dev WireMock volume contains an unsafe mapping filename.' }
            [void]$encoded.Clear()
        }
        elseif ($line -eq '__EXITPASS_END__') {
            if ($null -eq $name) { throw 'Dev WireMock volume inventory framing is invalid.' }
            try {
                $json = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($encoded.ToString())).TrimStart([char]0xFEFF)
                $mapping = $json | ConvertFrom-Json
            }
            catch { throw "Dev WireMock volume mapping '$name' is unreadable or invalid JSON." }
            $id = [string](Get-PropertyValue $mapping 'id')
            if ([string]::IsNullOrWhiteSpace($id)) { $id = [string](Get-PropertyValue $mapping 'uuid') }
            if ([string]::IsNullOrWhiteSpace($id)) { throw "Dev WireMock volume mapping '$name' has no identity." }
            $items += [pscustomobject]@{ Id = $id; Name = $name; File = $null; Mapping = $mapping }
            $name = $null
            [void]$encoded.Clear()
        }
        elseif ($null -ne $name) {
            [void]$encoded.Append($line.Trim())
        }
    }
    if ($null -ne $name) { throw 'Dev WireMock volume inventory ended before a mapping was complete.' }
    if (@($items | Group-Object Id | Where-Object Count -ne 1).Count -ne 0) { throw 'Dev WireMock volume contains duplicate mapping IDs.' }
    if (@($items | Group-Object Name | Where-Object Count -ne 1).Count -ne 0) { throw 'Dev WireMock volume contains duplicate mapping filenames.' }
    return [pscustomobject]@{ Path = $wireMockVolume; Items = @($items) }
}

function Get-PersistenceState($LiveMappings, [string]$AppKey) {
    $liveIds = @($LiveMappings | ForEach-Object { [string]$_.id })
    if (@($liveIds | Group-Object | Where-Object Count -ne 1).Count -ne 0) { throw 'WireMock contains duplicate mapping IDs.' }
    $livePassageway = @($LiveMappings | Where-Object {
        $_.request.method -eq 'POST' -and (Get-PropertyValue $_.request 'urlPath') -eq '/artemis/api/vehicle/v1/parkinglot/passageway/record'
    })
    if ($livePassageway.Count -ne 1) { throw "Expected exactly one Dev passageway inventory mapping; found $($livePassageway.Count)." }

    $mirrors = @($mappingDirectories | ForEach-Object { Get-MirrorInventory $_ })
    $volume = Get-VolumeInventory
    Assert-StoredMappingInventory $LiveMappings $mirrors[0] 'PersistedTemplate' "Dev source mapping mirror '$($mirrors[0].Path)'" $AppKey
    Assert-StoredMappingInventory $LiveMappings $mirrors[1] 'PersistedRuntime' "Dev runtime mapping mirror '$($mirrors[1].Path)'" $AppKey
    Assert-StoredMappingInventory $LiveMappings $volume 'PersistedRuntime' "Dev named volume '$wireMockVolume'" $AppKey
    Assert-PersistedMappingInventoriesEquivalent $mirrors[0] 'PersistedTemplate' $mirrors[1] 'PersistedRuntime' 'Dev source and runtime mapping mirrors' $AppKey
    Assert-PersistedMappingInventoriesEquivalent $mirrors[1] 'PersistedRuntime' $volume 'PersistedRuntime' 'Dev runtime mapping mirror and named volume' $AppKey
    Assert-ExactStringSet @($mirrors[0].Items | ForEach-Object Name) @($mirrors[1].Items | ForEach-Object Name) 'Dev mapping mirror filenames'
    Assert-ExactStringSet @($mirrors[0].Items | ForEach-Object Name) @($volume.Items | ForEach-Object Name) 'Dev WireMock volume filenames'

    $passageFiles = @()
    foreach ($mirror in $mirrors) {
        $match = @($mirror.Items | Where-Object Id -eq ([string]$livePassageway[0].id))
        if ($match.Count -ne 1) { throw "The passageway mapping is not uniquely persisted in '$($mirror.Path)'." }
        $passageFiles += $match[0]
    }
    if ($passageFiles[0].Name -ne $passageFiles[1].Name) { throw 'Dev passageway mapping filenames differ between mirrors.' }
    return [pscustomobject]@{
        LivePassageway = $livePassageway[0]
        PassageFileName = $passageFiles[0].Name
        Mirrors = $mirrors
        MappingCount = $LiveMappings.Count
        SessionCount = @($livePassageway[0].response.jsonBody.data.list).Count
        VolumeFileCount = $volume.Items.Count
        ContentVerified = $true
    }
}

function Get-SupportedMappingIdentifiers($Mapping) {
    $tickets = [Collections.Generic.List[string]]::new()
    $plates = [Collections.Generic.List[string]]::new()
    $request = Get-PropertyValue $Mapping 'request'
    $urlPath = [string](Get-PropertyValue $request 'urlPath')
    if ((Get-PropertyValue $request 'method') -ne 'POST' -or $urlPath -notin @(
        '/artemis/api/vehicle/v1/parkingfee/calculate',
        '/artemis/api/vehicle/v1/parkingfee/confirm'
    )) {
        return [pscustomobject]@{ Tickets = @(); Plates = @() }
    }
    foreach ($pattern in @((Get-PropertyValue $request 'bodyPatterns'))) {
        $body = Get-PropertyValue $pattern 'equalToJson'
        if ($null -eq $body) { continue }
        try { $payload = if ($body -is [string]) { $body | ConvertFrom-Json } else { $body } }
        catch { throw "Supported mapping '$($Mapping.id)' has invalid equalToJson content." }
        $ticket = Get-PropertyValue $payload 'cardNum'
        $plate = Get-PropertyValue $payload 'plateLicense'
        if (-not [string]::IsNullOrWhiteSpace([string]$ticket)) { $tickets.Add([string]$ticket) }
        if (-not [string]::IsNullOrWhiteSpace([string]$plate)) { $plates.Add(([string]$plate).ToUpperInvariant()) }
    }
    $response = Get-PropertyValue $Mapping 'response'
    $data = Get-PropertyValue (Get-PropertyValue $response 'jsonBody') 'data'
    $ticket = Get-PropertyValue $data 'cardNum'
    $plate = Get-PropertyValue $data 'plateLicense'
    if (-not [string]::IsNullOrWhiteSpace([string]$ticket)) { $tickets.Add([string]$ticket) }
    if (-not [string]::IsNullOrWhiteSpace([string]$plate)) { $plates.Add(([string]$plate).ToUpperInvariant()) }
    return [pscustomobject]@{ Tickets = @($tickets); Plates = @($plates) }
}

function Assert-SessionDoesNotExist($Mappings, $Inventory, [string]$TicketValue, [string]$PlateValue) {
    $duplicateInventory = @($Inventory | Where-Object {
        $_.personInfo.cardNum -ieq $TicketValue -or $_.carInfo.plateLicense -ieq $PlateValue
    })
    $duplicateMappingIds = [Collections.Generic.List[string]]::new()
    foreach ($mapping in $Mappings) {
        $identifiers = Get-SupportedMappingIdentifiers $mapping
        if (@($identifiers.Tickets | Where-Object { [string]::Equals($_, $TicketValue, [StringComparison]::OrdinalIgnoreCase) }).Count -ne 0 -or
            @($identifiers.Plates | Where-Object { [string]::Equals($_, $PlateValue, [StringComparison]::OrdinalIgnoreCase) }).Count -ne 0) {
            $duplicateMappingIds.Add([string]$mapping.id)
        }
    }
    if ($duplicateInventory.Count -ne 0 -or $duplicateMappingIds.Count -ne 0) {
        throw "Ticket '$TicketValue' or plate '$PlateValue' already exists. Existing sessions are never overwritten."
    }
}

function Get-HikCentralAppKey($Adapter) {
    $configured = Get-EnvironmentValue $Adapter 'SiteAdapter__HikCentralAppKeyFile'
    if ($configured -ne $expectedAppKeyFile) { throw 'Dev Site Adapter uses an unexpected app-key path.' }
    $mount = @($Adapter.Mounts | Where-Object { $_.Destination -eq '/dev-private' -and $_.Type -eq 'volume' })
    if ($mount.Count -ne 1 -or $mount[0].Name -ne 'exitpass-dev-20261122-private' -or $mount[0].RW) {
        throw 'Dev Site Adapter does not own the approved read-only private volume.'
    }
    $value = & docker exec $siteAdapterContainer cat $configured 2>$null
    if ($LASTEXITCODE -ne 0) { throw 'Dev HikCentral app key cannot be read from the approved private volume.' }
    $appKey = (($value -join "`n").Trim())
    if ([string]::IsNullOrWhiteSpace($appKey)) { throw 'The Dev HikCentral app key is blank.' }
    return $appKey
}

function Get-DevRuntimeContext {
    $wire = Get-Container $wireMockContainer
    $adapter = Get-Container $siteAdapterContainer
    $centralApi = Get-Container $centralApiContainer
    $centralDb = Get-Container $centralContainer
    $posDb = Get-Container $posContainer
    $ingress = Get-Container $ingressContainer
    Assert-ContainerIdentity $wire 'hikcentral' 'hikcentral'
    Assert-ContainerIdentity $adapter 'pitx-site-adapter' 'site-adapter'
    Assert-ContainerIdentity $centralApi 'central' 'central'
    Assert-ContainerIdentity $centralDb 'central-db' 'central-db'
    Assert-ContainerIdentity $posDb 'pos-db' 'pos-db'
    Assert-ContainerIdentity $ingress 'ingress' 'ingress' @($publishNetworkName)

    if ((Get-EnvironmentValue $adapter 'SiteAdapter__SiteId') -ne $expectedSiteId -or
        (Get-EnvironmentValue $adapter 'SiteAdapter__SiteGroupId') -ne $expectedSiteGroupId -or
        (Get-EnvironmentValue $adapter 'SiteAdapter__VendorSystemId') -ne $expectedVendorSystemId -or
        (Get-EnvironmentValue $adapter 'SiteAdapter__ParkingLotIndexCode') -ne $expectedParkingLot -or
        (Get-EnvironmentValue $adapter 'SiteAdapter__HikCentralBaseUrl') -ne $expectedHikCentralUrl) {
        throw 'Dev Site Adapter routing does not match the approved exitpass-dev-20261122 environment.'
    }
    $wireMount = @($wire.Mounts | Where-Object { $_.Destination -eq '/home/wiremock' -and $_.Type -eq 'volume' })
    if ($wireMount.Count -ne 1 -or $wireMount[0].Name -ne $wireMockVolume -or $wireMount[0].RW) {
        throw 'Dev WireMock does not own the approved read-only named volume.'
    }
    Assert-PublishedLoopbackEndpoint $ingress $wireMockAdminUrl 18080 'Dev WireMock'
    Assert-PublishedLoopbackEndpoint $ingress $siteAdapterUrl 18081 'Dev Site Adapter'
    Assert-PublishedLoopbackEndpoint $ingress $centralPmsUrl 18083 'Dev Central PMS'
    $ingressConfiguration = Get-IngressConfiguration
    Assert-IngressRoute $ingressConfiguration 18080 'hikcentral:8080' 'Dev WireMock'
    Assert-IngressRoute $ingressConfiguration 18081 'site-adapter:8080' 'Dev Site Adapter'
    Assert-IngressRoute $ingressConfiguration 18083 'central:8080' 'Dev Central PMS'
    $appKey = Get-HikCentralAppKey $adapter
    foreach ($url in @("$centralPmsUrl/health/ready", "$siteAdapterUrl/health/ready", "$wireMockAdminUrl/__admin/health")) {
        $health = Invoke-WebRequest -Uri $url -UseBasicParsing -TimeoutSec 10
        if ($health.StatusCode -ne 200) { throw "Required Dev readiness endpoint failed: $url" }
    }
    $registered = Invoke-WireMockAdmin Get "$wireMockAdminUrl/__admin/mappings" $null
    $liveMappings = @($registered.mappings)
    $persistence = Get-PersistenceState $liveMappings $appKey
    return [pscustomobject]@{
        Wire = $wire
        Adapter = $adapter
        CentralApi = $centralApi
        Ingress = $ingress
        AppKey = $appKey
        Registered = $registered
        LiveMappings = $liveMappings
        Persistence = $persistence
    }
}

function Invoke-VolumeStage([string]$StagePath, [string]$Image) {
    $helperName = "exitpass-dev-session-volume-$PID-$([Guid]::NewGuid().ToString('N').Substring(0, 8))"
    $volumeMount = "type=volume,source=$wireMockVolume,target=/wiremock"
    $stageMount = "type=bind,source=$StagePath,target=/stage,readonly"
    $applyScript = @'
set -eu
if [ -d /stage/write ]; then
  for f in /stage/write/*.json; do
    [ -e "$f" ] || continue
    cp "$f" "/wiremock/mappings/$(basename "$f")"
  done
fi
if [ -d /stage/remove ]; then
  for marker in /stage/remove/*.remove; do
    [ -e "$marker" ] || continue
    name=$(basename "$marker" .remove)
    rm -f "/wiremock/mappings/$name"
  done
fi
'@
    [IO.File]::WriteAllText((Join-Path $StagePath 'apply.sh'), $applyScript.Replace("`r`n", "`n"), [Text.UTF8Encoding]::new($false))
    $arguments = @('run', '--rm', '--name', $helperName, '--network', 'none', '--mount', $volumeMount, '--mount', $stageMount, '--entrypoint', '/bin/sh', $Image, '/stage/apply.sh')
    $output = & docker @arguments 2>&1
    if ($LASTEXITCODE -ne 0) { throw "Unable to update the Dev WireMock named volume: $(($output -join ' ').Trim())" }
}

function Add-RemoveMarker([string]$StagePath, [string]$FileName) {
    if ($FileName -notmatch '^[A-Za-z0-9._-]+\.json$') { throw 'Unsafe Dev mapping filename.' }
    $removePath = Join-Path $StagePath 'remove'
    [IO.Directory]::CreateDirectory($removePath) | Out-Null
    [IO.File]::WriteAllText((Join-Path $removePath "$FileName.remove"), '')
}

function Copy-StageToMirrors([string]$StagePath) {
    $writePath = Join-Path $StagePath 'write'
    foreach ($directory in $mappingDirectories) {
        foreach ($file in @(Get-ChildItem -LiteralPath $writePath -File -Filter '*.json')) {
            Copy-Item -LiteralPath $file.FullName -Destination (Join-Path $directory $file.Name) -Force
        }
        $removePath = Join-Path $StagePath 'remove'
        if (Test-Path -LiteralPath $removePath -PathType Container) {
            foreach ($marker in @(Get-ChildItem -LiteralPath $removePath -File -Filter '*.remove')) {
                $fileName = $marker.BaseName
                $target = Join-Path $directory $fileName
                if (Test-Path -LiteralPath $target -PathType Leaf) { Remove-Item -LiteralPath $target -Force }
            }
        }
    }
}

function New-MappingFileName([string]$TicketValue, [string]$Kind, [string]$Id) {
    return "exitpass-mock-$Kind-$TicketValue-$Id.json"
}

function Assert-LiveMappingSetEquivalent($ExpectedMappings, $ActualMappings, [string]$Description, [string]$AppKey) {
    $expectedIds = @($ExpectedMappings | ForEach-Object { [string]$_.id })
    $actualIds = @($ActualMappings | ForEach-Object { [string]$_.id })
    Assert-ExactStringSet $expectedIds $actualIds "$Description mapping IDs"
    foreach ($expected in $ExpectedMappings) {
        $actual = @($ActualMappings | Where-Object { [string]$_.id -eq [string]$expected.id })
        if ($actual.Count -ne 1) { throw "$Description does not uniquely contain mapping '$($expected.id)'." }
        $expectedJson = Get-ComparableMappingJson $expected 'Live' "$Description expected mapping '$($expected.id)'" $AppKey
        $actualJson = Get-ComparableMappingJson $actual[0] 'Live' "$Description actual mapping '$($expected.id)'" $AppKey
        if ($expectedJson -cne $actualJson) { throw "$Description content differs for mapping '$($expected.id)'." }
    }
}

function Restore-RegistrationBaseline($Plan) {
    $issues = [Collections.Generic.List[string]]::new()
    foreach ($id in @($Plan.AttemptedIds)) {
        try { Invoke-WireMockAdmin Delete "$wireMockAdminUrl/__admin/mappings/$id" $null | Out-Null }
        catch { $issues.Add("live delete '$id' failed: $($_.Exception.Message)") }
    }
    try {
        Invoke-WireMockAdmin Put "$wireMockAdminUrl/__admin/mappings/$($Plan.PassagewayId)" $Plan.LiveOriginalPassageway | Out-Null
    }
    catch { $issues.Add("live passageway restore failed: $($_.Exception.Message)") }

    $rollbackStage = Join-Path $Plan.BackupDirectory 'rollback-stage'
    try {
        $rollbackWrite = Join-Path $rollbackStage 'write'
        [IO.Directory]::CreateDirectory($rollbackWrite) | Out-Null
        Write-DurableJson (Join-Path $rollbackWrite $Plan.PassageFileName) $Plan.OriginalPassageway 50
        foreach ($fileName in @($Plan.NewFileNames)) { Add-RemoveMarker $rollbackStage $fileName }
    }
    catch { $issues.Add("rollback stage preparation failed: $($_.Exception.Message)") }

    if (Test-Path -LiteralPath $rollbackStage -PathType Container) {
        try { Invoke-VolumeStage $rollbackStage $Plan.WireImage }
        catch { $issues.Add("named-volume restore failed: $($_.Exception.Message)") }
        try { Copy-StageToMirrors $rollbackStage }
        catch { $issues.Add("mapping-mirror restore failed: $($_.Exception.Message)") }
    }

    $verified = $false
    try {
        $restoredMappings = @((Invoke-WireMockAdmin Get "$wireMockAdminUrl/__admin/mappings" $null).mappings)
        Assert-LiveMappingSetEquivalent $Plan.BeforeMappings $restoredMappings 'Rollback live baseline' $Plan.AppKey
        [void](Get-PersistenceState $restoredMappings $Plan.AppKey)
        foreach ($id in @($Plan.AttemptedIds)) {
            if (@($restoredMappings | Where-Object { [string]$_.id -eq $id }).Count -ne 0) {
                throw "Attempted mapping '$id' remains registered after rollback."
            }
        }
        $verified = $true
    }
    catch { $issues.Add("baseline verification failed: $($_.Exception.Message)") }

    return [pscustomobject]@{
        Complete = $verified
        Issues = @($issues)
        AttemptedIds = @($Plan.AttemptedIds)
        BackupDirectory = $Plan.BackupDirectory
    }
}

function Invoke-SessionRegistration($Plan) {
    try {
        Invoke-VolumeStage $Plan.StageDirectory $Plan.WireImage
        Copy-StageToMirrors $Plan.StageDirectory
        Invoke-WireMockAdmin Put "$wireMockAdminUrl/__admin/mappings/$($Plan.PassagewayId)" $Plan.LiveUpdatedPassageway | Out-Null
        foreach ($mapping in @($Plan.LiveNewMappings)) {
            # The ID is already in AttemptedIds before POST. A lost response may still mean the mapping was applied.
            Invoke-WireMockAdmin Post "$wireMockAdminUrl/__admin/mappings" $mapping | Out-Null
        }
        $afterMappings = @((Invoke-WireMockAdmin Get "$wireMockAdminUrl/__admin/mappings" $null).mappings)
        if ($afterMappings.Count -ne ($Plan.BeforeMappings.Count + $Plan.LiveNewMappings.Count)) {
            throw 'Dev WireMock mapping count did not reconcile.'
        }
        foreach ($original in @($Plan.BeforeMappings)) {
            if (@($afterMappings | Where-Object { [string]$_.id -eq [string]$original.id }).Count -ne 1) {
                throw "Pre-existing Dev mapping '$($original.id)' was not preserved."
            }
        }
        $afterPersistence = Get-PersistenceState $afterMappings $Plan.AppKey
        if ($afterPersistence.SessionCount -ne ($Plan.InventoryCount + 1)) {
            throw 'Dev passageway session count did not reconcile.'
        }
        return $afterPersistence
    }
    catch {
        $registrationError = $_
        $rollback = Restore-RegistrationBaseline $Plan
        $identifiers = @($Plan.AttemptedIds) -join ', '
        $issueText = if ($rollback.Issues.Count -eq 0) { 'none' } else { $rollback.Issues -join ' | ' }
        if ($rollback.Complete) {
            $message = "Registration failed: $($registrationError.Exception.Message) Rollback verified against the complete pre-operation baseline. Cleanup issues: $issueText. Attempted mapping IDs: $identifiers. Backup: $($Plan.BackupDirectory)"
        }
        else {
            $message = "Registration failed: $($registrationError.Exception.Message) ROLLBACK INCOMPLETE; the pre-operation baseline could not be verified. Affected mapping IDs: $identifiers. Cleanup issues: $issueText. Backup: $($Plan.BackupDirectory)"
        }
        throw [InvalidOperationException]::new($message, $registrationError.Exception)
    }
}

$acceptedFee = $null
if (-not $PreflightOnly) {
    foreach ($name in @('Ticket', 'Plate', 'EntryTime')) {
        if (-not $PSBoundParameters.ContainsKey($name)) { throw "$name is required unless -PreflightOnly is used." }
    }
    $Ticket = $Ticket.Trim()
    $Plate = $Plate.Trim().ToUpperInvariant()
    if ($Ticket.Length -gt 32 -or $Plate.Length -gt 32 -or $Ticket -notmatch '^[A-Za-z0-9_-]+$' -or $Plate -notmatch '^[A-Z0-9_-]+$') {
        throw 'Ticket or Plate is invalid.'
    }
    $normalizedVehicleType = $VehicleType.Trim().ToUpperInvariant()
    $acceptedFee = Get-AcceptedFee $Fee
}

$context = Get-DevRuntimeContext
$financialSnapshot = Get-FinancialSnapshot

if ($PreflightOnly) {
    [ordered]@{
        environment = $environmentName
        environmentRoot = $environmentRoot
        composeProject = $composeProject
        centralPms = $centralPmsUrl
        centralApi = $centralApiContainer
        ingress = $ingressContainer
        centralDatabase = $centralDatabase
        posDatabase = $posDatabase
        wireMock = $wireMockContainer
        wireMockAdmin = $wireMockAdminUrl
        siteAdapter = $siteAdapterContainer
        siteAdapterUrl = $siteAdapterUrl
        network = $networkName
        namedVolume = $wireMockVolume
        mappingMirrors = $mappingDirectories
        mappingCount = $context.Persistence.MappingCount
        passagewayMappingId = [string]$context.Persistence.LivePassageway.id
        passagewaySessionCount = $context.Persistence.SessionCount
        volumeFileCount = $context.Persistence.VolumeFileCount
        financialSnapshot = $financialSnapshot
        mutationPerformed = $false
        ownership = 'PASS'
        persistence = 'PASS'
        persistenceContentVerified = $context.Persistence.ContentVerified
        isolation = 'PASS'
    } | ConvertTo-Json -Depth 6
    return
}

$lock = $null
$session = $null
$backupDirectory = $null
try {
    [IO.Directory]::CreateDirectory($operationRoot) | Out-Null
    $backupRoot = Join-Path $operationRoot 'mapping-backups'
    [IO.Directory]::CreateDirectory($backupRoot) | Out-Null
    $lockPath = Join-Path $operationRoot 'sessions.lock'
    $lock = [IO.File]::Open($lockPath, [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)

    $context = Get-DevRuntimeContext
    $beforeMappings = @($context.LiveMappings)
    $passageway = $context.Persistence.LivePassageway
    $inventory = @($passageway.response.jsonBody.data.list)
    Assert-SessionDoesNotExist $beforeMappings $inventory $Ticket $Plate

    $session = [pscustomobject][ordered]@{
        ticketReference = $Ticket
        plateLicense = $Plate
        vendorRecordGuid = [Guid]::NewGuid().ToString('D')
        entryTime = $EntryTime.ToOffset([TimeSpan]::FromHours(8)).ToString('o', $invariant)
        fee = $acceptedFee.ToString('0.00', $invariant)
        parkingLotIndexCode = $expectedParkingLot
        vehicleType = $normalizedVehicleType
        carType = Get-HikCentralCarType $normalizedVehicleType
    }
    $originalPassageway = ConvertFrom-Json (ConvertTo-Json -InputObject $passageway -Depth 50)
    $updatedPassageway = ConvertFrom-Json (ConvertTo-Json -InputObject $passageway -Depth 50)
    $updatedRecords = @($inventory) + @(New-PassageRecord $session)
    $updatedPassageway.response.jsonBody.data.total = $updatedRecords.Count
    $updatedPassageway.response.jsonBody.data.list = $updatedRecords
    $updatedPassageway.persistent = $true

    $ticketMapping = New-CalculateMapping $session $context.AppKey 'cardNum' ([Guid]::NewGuid().ToString('D'))
    $plateMapping = New-CalculateMapping $session $context.AppKey 'plateLicense' ([Guid]::NewGuid().ToString('D'))
    $confirmMapping = New-ConfirmMapping $session $context.AppKey ([Guid]::NewGuid().ToString('D'))
    $newMappings = @($ticketMapping, $plateMapping, $confirmMapping)
    $liveOriginalPassageway = ConvertFrom-Json (ConvertTo-Json -InputObject $originalPassageway -Depth 50)
    $liveOriginalPassageway.persistent = $false
    $liveUpdatedPassageway = ConvertFrom-Json (ConvertTo-Json -InputObject $updatedPassageway -Depth 50)
    $liveUpdatedPassageway.persistent = $false
    $liveNewMappings = @($newMappings | ForEach-Object {
        $liveMapping = ConvertFrom-Json (ConvertTo-Json -InputObject $_ -Depth 50)
        $liveMapping.persistent = $false
        $liveMapping
    })
    $newFiles = [ordered]@{
        (New-MappingFileName $Ticket 'calculate-by-cardnum' ([string]$ticketMapping.id)) = $ticketMapping
        (New-MappingFileName $Ticket 'calculate-by-platelicense' ([string]$plateMapping.id)) = $plateMapping
        (New-MappingFileName $Ticket 'confirm' ([string]$confirmMapping.id)) = $confirmMapping
    }

    $stamp = [DateTimeOffset]::Now.ToString('yyyyMMdd-HHmmssfff', $invariant)
    $backupDirectory = Join-Path $backupRoot $stamp
    $stageDirectory = Join-Path $backupDirectory 'apply-stage'
    $writeDirectory = Join-Path $stageDirectory 'write'
    [IO.Directory]::CreateDirectory($writeDirectory) | Out-Null
    Write-DurableJson (Join-Path $backupDirectory 'passageway-before.json') $originalPassageway 50
    Write-DurableJson (Join-Path $writeDirectory $context.Persistence.PassageFileName) $updatedPassageway 50
    foreach ($entry in $newFiles.GetEnumerator()) {
        Write-DurableJson (Join-Path $writeDirectory $entry.Key) $entry.Value 50
    }

    $registrationPlan = [pscustomobject]@{
        StageDirectory = $stageDirectory
        BackupDirectory = $backupDirectory
        WireImage = [string]$context.Wire.Image
        PassagewayId = [string]$passageway.id
        PassageFileName = $context.Persistence.PassageFileName
        OriginalPassageway = $originalPassageway
        LiveOriginalPassageway = $liveOriginalPassageway
        LiveUpdatedPassageway = $liveUpdatedPassageway
        LiveNewMappings = @($liveNewMappings)
        AttemptedIds = @($liveNewMappings | ForEach-Object { [string]$_.id })
        NewFileNames = @($newFiles.Keys)
        BeforeMappings = @($beforeMappings)
        InventoryCount = $inventory.Count
        AppKey = $context.AppKey
    }
    [void](Invoke-SessionRegistration $registrationPlan)

    try {
        $headers = @{ 'Content-Type' = 'application/json'; 'X-Ca-Key' = $context.AppKey; 'X-Ca-Signature' = 'DEV-session-helper-verification' }
        $passageResponse = Invoke-RestMethod -Method Post -Uri "$wireMockAdminUrl/artemis/api/vehicle/v1/parkinglot/passageway/record" -Headers $headers -Body (@{ queryInfo = @{ parkingLotIndexCode = $expectedParkingLot } } | ConvertTo-Json -Compress) -TimeoutSec 15
        $createdRecord = @($passageResponse.data.list | Where-Object {
            $_.personInfo.cardNum -eq $Ticket -and $_.carInfo.plateLicense -eq $Plate -and $_.guid -eq $session.vendorRecordGuid
        })
        if ($createdRecord.Count -ne 1 -or -not (Test-PassageRecordVehicleType $createdRecord[0] $session.carType)) {
            throw 'Dev passageway inventory verification failed.'
        }
        $ticketResult = Invoke-RestMethod -Method Post -Uri "$wireMockAdminUrl/artemis/api/vehicle/v1/parkingfee/calculate" -Headers $headers -Body (@{ cardNum = $Ticket } | ConvertTo-Json -Compress) -TimeoutSec 15
        $plateResult = Invoke-RestMethod -Method Post -Uri "$wireMockAdminUrl/artemis/api/vehicle/v1/parkingfee/calculate" -Headers $headers -Body (@{ plateLicense = $Plate } | ConvertTo-Json -Compress) -TimeoutSec 15
        $expectedEntry = [DateTimeOffset]::Parse($session.entryTime, $invariant)
        $ticketEntry = [DateTimeOffset]::Parse([string]$ticketResult.data.parkingInTime, $invariant)
        $plateEntry = [DateTimeOffset]::Parse([string]$plateResult.data.parkingInTime, $invariant)
        if ($ticketResult.code -ne '0' -or $plateResult.code -ne '0' -or
            [decimal]$ticketResult.data.fee -ne $acceptedFee -or [decimal]$plateResult.data.fee -ne $acceptedFee -or
            $ticketResult.data.cardNum -ne $Ticket -or $plateResult.data.cardNum -ne $Ticket -or
            $ticketEntry.ToUniversalTime() -ne $expectedEntry.ToUniversalTime() -or
            $plateEntry.ToUniversalTime() -ne $expectedEntry.ToUniversalTime()) {
            throw 'Dev ticket or plate fee lookup verification failed.'
        }

        $projectionVerified = $false
        $deadline = [DateTimeOffset]::UtcNow.AddSeconds(60)
        do {
            if ((Get-ProjectionCount $Ticket $Plate) -eq 1) { $projectionVerified = $true; break }
            Start-Sleep -Seconds 2
        } while ([DateTimeOffset]::UtcNow -lt $deadline)
        if (-not $projectionVerified) { throw 'The Dev Site Adapter did not project the new session into Dev Central PMS.' }
        if ((Get-FinancialSnapshot) -ne $financialSnapshot) { throw 'Financial or fiscal record counts changed during Dev session creation.' }
    }
    catch {
        throw "PARTIAL RESULT: Dev session '$Ticket' / '$Plate' was durably created as '$($session.vendorRecordGuid)', but required verification failed: $($_.Exception.Message) Backup: $backupDirectory"
    }

    [ordered]@{
        environment = $environmentName
        environmentRoot = $environmentRoot
        composeProject = $composeProject
        ticket = $Ticket
        plate = $Plate
        vehicleType = $normalizedVehicleType
        hikCentralCarType = $session.carType
        fee = $session.fee
        entryTime = $session.entryTime
        vendorRecordGuid = $session.vendorRecordGuid
        network = $networkName
        namedVolume = $wireMockVolume
        mappingMirrors = $mappingDirectories
        mappingCount = @((Invoke-WireMockAdmin Get "$wireMockAdminUrl/__admin/mappings" $null).mappings).Count
        passagewayVerified = $true
        ticketLookupVerified = $true
        plateLookupVerified = $true
        centralProjectionVerified = $true
        durablePersistenceVerified = $true
        financialAndFiscalCountsUnchanged = $true
        backupDirectory = $backupDirectory
    } | ConvertTo-Json -Depth 6
}
finally {
    if ($null -ne $lock) { $lock.Dispose() }
}
