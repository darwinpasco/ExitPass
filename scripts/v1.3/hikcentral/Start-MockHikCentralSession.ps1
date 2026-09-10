[CmdletBinding()]
param(
    [switch] $SmokeTest,
    [switch] $SelfTest
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
$networkName = 'exitpass-ist-persistent'
$exitPassDatabaseContainer = 'exitpass-ist-persistent-db'
$exitPassDatabaseVolume = 'exitpass-ist-persistent-data'
$posDatabaseContainer = 'exitpass-pos-ist-persistent-db'
$posDatabaseVolume = 'exitpass-pos-ist-persistent-data'
$wireMockContainer = 'exitpass-mock-hikcentral'
$mockAdapterContainer = 'exitpass-mock-pitx-site-adapter'
$wireMockImage = 'wiremock/wiremock:3.9.2'
$standardCentralPmsContainer = 'exitpass-central-pms-pitx-local'
$standardCentralPmsAlias = 'exitpass-central-pms-pitx-local'
$legacyCentralPmsContainer = 'exitpass-r41-central-pms'
$centralPmsRuntimeLabel = 'com.exitpass.local-runtime'
$centralPmsRuntimeLabelValue = 'persistent-pitx-central-pms'
$expectedSiteId = '2d1dcdf8-f563-537c-8542-0bde7cc9da97'
$expectedSiteGroupId = 'a6dbadf6-68b5-5bed-a7e0-a75faee70841'
$expectedVendorSystemId = 'afdefaab-6be4-6b25-8f3f-3ad8309662e8'
$expectedAdapterIdentityId = '3986bf89-8373-da14-fc3f-f3818dc6b500'
$expectedCentralIdentityId = '8063c159-dae6-57af-9f1f-e0a07d519fb2'
$expectedParkingLot = '1'
$expectedRoute = 'http://pitx-site-adapter:8080/'
$wrapperLabel = 'com.exitpass.mock-hikcentral-wrapper'
$invocationLabel = 'com.exitpass.mock-hikcentral-invocation'
$privateRoot = if ([string]::IsNullOrWhiteSpace($env:EXITPASS_PERSISTENT_IST_ROOT)) {
    'D:\SourceCodes\ExitPass.local\persistent-ist'
} else { $env:EXITPASS_PERSISTENT_IST_ROOT }
$adapterEnvironmentFile = Join-Path $privateRoot 'runtime\restart-41-business\adapter.env'
$runtimeRoot = Join-Path $env:LOCALAPPDATA 'ExitPass\MockHikCentral'
$statePath = Join-Path $runtimeRoot 'state.json'
$sessionsPath = Join-Path $runtimeRoot 'sessions.json'
$creatorPath = Join-Path $PSScriptRoot 'New-MockHikCentralSession.ps1'
$invocationId = [Guid]::NewGuid().ToString('D')

$realAdapterName = $null
$realAdapterHealthUrl = $null
$mockAdapterStarted = $false
$wireMockStarted = $false
$realAdapterStopped = $false
$routeBefore = $null
$countsBefore = $null
$posCountsBefore = $null
$smokeEvidence = $null

function Invoke-Docker([string[]] $Arguments, [switch] $AllowFailure) {
    $previousErrorAction = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        $output = & docker @Arguments 2>&1
        $exitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previousErrorAction
    }
    if (-not $AllowFailure -and $exitCode -ne 0) {
        throw "Docker command failed: docker $($Arguments -join ' ')`n$($output -join [Environment]::NewLine)"
    }
    return [pscustomobject]@{ ExitCode = $exitCode; Output = @($output) }
}

function Get-ContainerInspect([string] $Name, [switch] $AllowMissing) {
    $result = Invoke-Docker -Arguments @('container', 'inspect', $Name) -AllowFailure
    if ($result.ExitCode -ne 0) {
        if ($AllowMissing) { return $null }
        throw "Required container '$Name' is unavailable."
    }
    return (($result.Output -join "`n") | ConvertFrom-Json)[0]
}

function Get-ObjectProperty($Object, [string] $Name) {
    if ($null -eq $Object) { return $null }
    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property) { return $null }
    return $property.Value
}

function Get-ContainerEnvironment($Container) {
    $values = @{}
    foreach ($entry in @($Container.Config.Env)) {
        $parts = $entry -split '=', 2
        $values[$parts[0]] = if ($parts.Count -eq 2) { $parts[1] } else { '' }
    }
    return $values
}

function Get-NetworkAttachment($Container, [string] $Name) {
    return Get-ObjectProperty $Container.NetworkSettings.Networks $Name
}

function Get-LoopbackContainerUrl($Container, [string] $ContainerPort) {
    $bindings = Get-ObjectProperty $Container.NetworkSettings.Ports $ContainerPort
    if ($null -eq $bindings -or @($bindings).Count -ne 1 -or $bindings[0].HostIp -ne '127.0.0.1') {
        throw "Container '$($Container.Name.TrimStart('/'))' does not have one loopback-only $ContainerPort binding."
    }
    return "http://127.0.0.1:$($bindings[0].HostPort)"
}

function Get-ContainerName($Container) {
    return ([string]$Container.Name).TrimStart('/')
}

function Select-CentralPmsRuntime([object[]] $Containers) {
    $candidates = @()
    foreach ($container in @($Containers)) {
        $name = Get-ContainerName $container
        $attachment = Get-NetworkAttachment $container $networkName
        $label = Get-ObjectProperty $container.Config.Labels $centralPmsRuntimeLabel
        $hasStandardName = $name -eq $standardCentralPmsContainer
        $hasStandardAlias = $null -ne $attachment -and $attachment.Aliases -contains $standardCentralPmsAlias
        $hasStandardLabel = $label -eq $centralPmsRuntimeLabelValue
        $hasStandardMarker = $hasStandardName -or $hasStandardAlias -or $hasStandardLabel
        if ($hasStandardMarker -or $name -eq $legacyCentralPmsContainer) {
            $candidates += [pscustomobject]@{
                Container = $container
                Name = $name
                Attachment = $attachment
                Label = $label
                IsStandardized = $hasStandardMarker
            }
        }
    }

    $running = @($candidates | Where-Object { $_.Container.State.Running })
    if ($running.Count -eq 0) {
        if ($candidates.Count -gt 0) {
            $stoppedNames = @($candidates | ForEach-Object { $_.Name }) -join ', '
            throw "No approved persistent PITX Central PMS runtime is running. Stopped candidate(s): $stoppedNames."
        }
        throw 'No approved persistent PITX Central PMS runtime was found. Start scripts/v1.3/local-runtime/Start-CentralPms.ps1 and retry.'
    }

    $approved = @()
    foreach ($candidate in $running) {
        if ($null -eq $candidate.Attachment) {
            throw "Central PMS runtime '$($candidate.Name)' is not attached to expected persistent network '$networkName'."
        }
        if ($candidate.IsStandardized) {
            if ($candidate.Label -ne $centralPmsRuntimeLabelValue) {
                throw "Standardized Central PMS runtime '$($candidate.Name)' has missing or incorrect label '$centralPmsRuntimeLabel=$centralPmsRuntimeLabelValue'."
            }
            if ($candidate.Attachment.Aliases -notcontains $standardCentralPmsAlias) {
                throw "Standardized Central PMS runtime '$($candidate.Name)' does not own expected network alias '$standardCentralPmsAlias'."
            }
            $kind = 'standardized'
        }
        else {
            $kind = 'legacy'
        }

        $approved += [pscustomobject]@{
            Container = $candidate.Container
            Name = $candidate.Name
            Kind = $kind
            HostUrl = Get-LoopbackContainerUrl $candidate.Container '8080/tcp'
        }
    }

    if ($approved.Count -gt 1) {
        $names = @($approved | ForEach-Object { $_.Name }) -join ', '
        throw "Multiple approved persistent PITX Central PMS runtimes are running ($names). Stop the duplicate runtime and retry."
    }
    return $approved[0]
}

function Resolve-CentralPmsRuntime([object[]] $Containers, [scriptblock] $ReadinessProbe) {
    if ($null -eq $Containers) {
        $names = (Invoke-Docker -Arguments @('ps', '-a', '--format', '{{.Names}}')).Output
        $Containers = @($names |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
            ForEach-Object { Get-ContainerInspect $_ })
    }

    $runtime = Select-CentralPmsRuntime $Containers
    $readyUrl = "$($runtime.HostUrl)/health/ready"
    if ($null -eq $ReadinessProbe) {
        Wait-Http $readyUrl
    }
    else {
        & $ReadinessProbe $readyUrl
    }
    return $runtime
}

function Wait-Http([string] $Url, [int] $ExpectedStatus = 200, [int] $Seconds = 60) {
    $deadline = [DateTime]::UtcNow.AddSeconds($Seconds)
    do {
        try {
            $request = [Net.HttpWebRequest]::Create($Url)
            $request.Timeout = 3000
            $request.AllowAutoRedirect = $false
            try {
                $response = $request.GetResponse()
            } catch [Net.WebException] {
                $response = $_.Exception.Response
            }
            if ($null -ne $response) {
                $status = [int]$response.StatusCode
                $response.Dispose()
                if ($status -eq $ExpectedStatus) { return }
            }
        } catch { }
        Start-Sleep -Milliseconds 750
    } while ([DateTime]::UtcNow -lt $deadline)
    throw "Readiness timeout for $Url (expected HTTP $ExpectedStatus)."
}

function Invoke-ExitPassSql([string] $Sql) {
    $result = Invoke-Docker -Arguments @('exec', $exitPassDatabaseContainer, 'psql', '-U', 'exitpass_ist', '-d', 'exitpass_ist', '-Atq', '-c', $Sql)
    return ($result.Output -join "`n").Trim()
}

function Invoke-PosSql([string] $Sql) {
    $result = Invoke-Docker -Arguments @('exec', $posDatabaseContainer, 'psql', '-U', 'exitpass_ist', '-d', 'exitpass_pos_ist', '-Atq', '-c', $Sql)
    return ($result.Output -join "`n").Trim()
}

function Get-SiteAdapterRoute {
    $sql = @"
SELECT vs.base_url_ref || '|' || vs.environment_code || '|' || ai.service_identity_id::text
FROM integration.vendor_systems vs
JOIN integration.adapter_mappings am ON am.vendor_system_id = vs.vendor_system_id
JOIN integration.vendor_endpoints ve ON ve.vendor_system_id = vs.vendor_system_id AND ve.endpoint_code = 'SITE_ADAPTER_API'
JOIN identity.service_identities ai ON ai.service_identity_id = am.vendor_object_ref::uuid
WHERE am.site_id = '$expectedSiteId'::uuid
  AND am.site_group_id = '$expectedSiteGroupId'::uuid
  AND vs.vendor_system_id = '$expectedVendorSystemId'::uuid
  AND am.mapping_status::text = 'ACTIVE'
  AND ve.endpoint_status::text = 'ACTIVE'
  AND vs.vendor_system_status::text = 'ACTIVE';
"@
    return Invoke-ExitPassSql $sql
}

function Get-BusinessCounts {
    $sql = "SELECT (SELECT count(*) FROM core.payment_attempts) || '|' || (SELECT count(*) FROM core.payment_confirmations) || '|' || (SELECT count(*) FROM core.exit_authorizations);"
    return Invoke-ExitPassSql $sql
}

function Get-PosCounts {
    $sql = "SELECT (SELECT count(*) FROM pos.fiscal_documents) || '|' || (SELECT count(*) FROM pos.electronic_journal_records);"
    return Invoke-PosSql $sql
}

function Assert-ContainerAndVolume([string] $ContainerName, [string] $VolumeName) {
    $container = Get-ContainerInspect $ContainerName
    if (-not $container.State.Running) { throw "Persistent container '$ContainerName' is not running." }
    $volume = Invoke-Docker -Arguments @('volume', 'inspect', $VolumeName) -AllowFailure
    if ($volume.ExitCode -ne 0) { throw "Persistent volume '$VolumeName' is unavailable." }
    if (@($container.Mounts | Where-Object { $_.Type -eq 'volume' -and $_.Name -eq $VolumeName }).Count -ne 1) {
        throw "Container '$ContainerName' is not attached to expected volume '$VolumeName'."
    }
}

function Find-RealAdapter {
    $names = (Invoke-Docker -Arguments @('ps', '-a', '--filter', "network=$networkName", '--format', '{{.Names}}')).Output
    $matches = @()
    foreach ($name in @($names | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })) {
        if ($name -in @($wireMockContainer, $mockAdapterContainer)) { continue }
        $container = Get-ContainerInspect $name
        $attachment = Get-NetworkAttachment $container $networkName
        if ($null -ne $attachment -and $attachment.Aliases -contains 'pitx-site-adapter') {
            $matches += $container
        }
    }
    if ($matches.Count -ne 1) {
        throw "Expected exactly one ordinary adapter owning pitx-site-adapter; found $($matches.Count)."
    }

    $adapter = $matches[0]
    $environment = Get-ContainerEnvironment $adapter
    $requirements = @{
        'SiteAdapter__SiteId' = $expectedSiteId
        'SiteAdapter__SiteGroupId' = $expectedSiteGroupId
        'SiteAdapter__VendorSystemId' = $expectedVendorSystemId
        'SiteAdapter__AdapterIdentityId' = $expectedAdapterIdentityId
        'SiteAdapter__AllowedCentralPmsServiceIdentityId' = $expectedCentralIdentityId
        'SiteAdapter__ParkingLotIndexCode' = $expectedParkingLot
        'SiteAdapter__ConfirmPaymentEnabled' = 'true'
    }
    foreach ($item in $requirements.GetEnumerator()) {
        if (-not $environment.ContainsKey($item.Key) -or $environment[$item.Key] -ne $item.Value) {
            throw "The adapter owning pitx-site-adapter is not the expected PITX non-production adapter ($($item.Key))."
        }
    }
    if ($environment['SiteAdapter__AllowedOperations__3'] -ne 'PAYMENT_CONFIRMATION') {
        throw 'The ordinary PITX adapter does not permit PAYMENT_CONFIRMATION.'
    }
    if (-not $adapter.State.Running) { throw 'The ordinary PITX adapter is not running.' }
    return $adapter
}

function Assert-ImageMatchesCurrentAdapterSource($Adapter) {
    $image = ((Invoke-Docker -Arguments @('image', 'inspect', $Adapter.Config.Image)).Output -join "`n") | ConvertFrom-Json
    $sourceSha = Get-ObjectProperty $image[0].Config.Labels 'exitpass.source.sha'
    if ([string]::IsNullOrWhiteSpace($sourceSha)) {
        throw 'The Site Adapter image does not identify its source revision.'
    }
    & git -C $repositoryRoot cat-file -e "$sourceSha^{commit}" 2>$null
    if ($LASTEXITCODE -ne 0) { throw 'The Site Adapter image source revision is not available in this repository.' }
    & git -C $repositoryRoot diff --quiet $sourceSha HEAD -- src/Services/VendorPmsAdapter
    if ($LASTEXITCODE -ne 0) { throw 'The running Site Adapter image does not match the current Site Adapter source.' }
}

function Assert-NoWrapperCollision {
    foreach ($name in @($wireMockContainer, $mockAdapterContainer)) {
        if ($null -ne (Get-ContainerInspect $name -AllowMissing)) {
            throw "Wrapper resource '$name' already exists. Refusing to replace it."
        }
    }
    $owned = (Invoke-Docker -Arguments @('ps', '-a', '--filter', "label=$wrapperLabel=true", '--format', '{{.Names}}')).Output
    if (@($owned | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }).Count -gt 0) {
        throw 'Another mock HikCentral wrapper resource exists. Clean up that invocation first.'
    }
    if (Test-Path -LiteralPath $statePath) {
        throw "Mock runtime state already exists at $statePath. Refusing an ambiguous startup."
    }
}

function Assert-WrapperResourceOwnership($Container) {
    $name = Get-ContainerName $Container
    $owner = Get-ObjectProperty $Container.Config.Labels $wrapperLabel
    $ownerInvocation = Get-ObjectProperty $Container.Config.Labels $invocationLabel
    if ($owner -ne 'true' -or $ownerInvocation -ne $invocationId) {
        throw "Refusing to remove non-wrapper resource '$name'."
    }
}

function Start-WireMock {
    $result = Invoke-Docker -Arguments @(
        'run', '--detach', '--name', $wireMockContainer,
        '--network', $networkName, '--network-alias', 'mock-hikcentral',
        '--publish', '127.0.0.1::8080',
        '--label', "$wrapperLabel=true", '--label', "$invocationLabel=$invocationId",
        $wireMockImage, '--global-response-templating', '--disable-gzip'
    )
    if ([string]::IsNullOrWhiteSpace(($result.Output -join ''))) { throw 'WireMock did not return a container ID.' }
}

function Start-MockAdapter($RealAdapter) {
    $arguments = [Collections.Generic.List[string]]::new()
    @(
        'run', '--detach', '--name', $mockAdapterContainer,
        '--network', $networkName, '--network-alias', 'pitx-site-adapter',
        '--publish', '127.0.0.1::8080',
        '--env-file', $adapterEnvironmentFile,
        '--env', 'ASPNETCORE_ENVIRONMENT=IntegrationTest',
        '--env', 'SiteAdapter__HikCentralBaseUrl=http://mock-hikcentral:8080',
        '--env', 'SiteAdapter__AllowTaskOwnedHttp=true',
        '--label', "$wrapperLabel=true", '--label', "$invocationLabel=$invocationId"
    ) | ForEach-Object { $arguments.Add($_) }
    foreach ($mount in @($RealAdapter.Mounts | Where-Object { $_.Type -eq 'bind' })) {
        $arguments.Add('--mount')
        $mountValue = "type=bind,source=$($mount.Source),target=$($mount.Destination)"
        if (-not $mount.RW) { $mountValue += ',readonly' }
        $arguments.Add($mountValue)
    }
    $arguments.Add($RealAdapter.Config.Image)
    Invoke-Docker -Arguments ($arguments.ToArray()) | Out-Null
}

function Write-RuntimeState([string] $WireMockAdminUrl) {
    [IO.Directory]::CreateDirectory($runtimeRoot) | Out-Null
    ConvertTo-Json -InputObject @() | Set-Content -LiteralPath $sessionsPath -Encoding UTF8
    $state = [ordered]@{
        wrapperVersion = 1
        invocationId = $invocationId
        wireMockContainer = $wireMockContainer
        wireMockAdminUrl = $WireMockAdminUrl
        mockAdapterContainer = $mockAdapterContainer
        network = $networkName
        siteId = $expectedSiteId
        siteGroupId = $expectedSiteGroupId
        vendorSystemId = $expectedVendorSystemId
        parkingLotIndexCode = $expectedParkingLot
        originalAdapterContainer = $realAdapterName
        startedAt = [DateTimeOffset]::Now.ToString('o')
    }
    $state | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $statePath -Encoding UTF8
}

function Resolve-MockSession([string] $Ticket, [string] $CentralUrl) {
    $correlationId = [Guid]::NewGuid()
    $body = @{
        siteGroupId = $expectedSiteGroupId
        siteId = $expectedSiteId
        vendorSystemId = $expectedVendorSystemId
        ticketReference = $Ticket
        correlationId = $correlationId
    } | ConvertTo-Json -Compress
    $response = Invoke-RestMethod -Method Post -Uri "$CentralUrl/v1/vendor-parking/resolve" -ContentType 'application/json' -Body $body -TimeoutSec 30
    if ($response.lookupOutcome -ne 'resolved' -or [string]::IsNullOrWhiteSpace($response.parkingSessionId) -or [string]::IsNullOrWhiteSpace($response.tariffSnapshotId)) {
        throw "Normal Central PMS resolve failed for '$Ticket'."
    }
    $persisted = Invoke-ExitPassSql "SELECT count(*) FROM core.parking_sessions ps JOIN core.tariff_snapshots ts ON ts.parking_session_id = ps.parking_session_id WHERE ps.parking_session_id = '$($response.parkingSessionId)'::uuid AND ts.tariff_snapshot_id = '$($response.tariffSnapshotId)'::uuid;"
    if ($persisted -ne '1') { throw "Central PMS did not persist the expected session and tariff for '$Ticket'." }
    return [pscustomobject]@{ Ticket=$Ticket; ParkingSessionId=$response.parkingSessionId; TariffSnapshotId=$response.tariffSnapshotId; CorrelationId=$correlationId; AmountMinorUnits=$response.netPayableMinorUnits }
}

function Test-WireMockEvidence([string] $AdminUrl) {
    $journal = Invoke-RestMethod -Method Get -Uri "$AdminUrl/__admin/requests" -TimeoutSec 15
    $calculate = @($journal.requests | Where-Object { $_.request.url -eq '/artemis/api/vehicle/v1/parkingfee/calculate' })
    foreach ($ticket in @('MOCK-SESSION-001', 'MOCK-SESSION-002')) {
        $matching = @($calculate | Where-Object { $_.request.body -match [Regex]::Escape($ticket) })
        if ($matching.Count -lt 1) { throw "WireMock journal has no calculate request for '$ticket'." }
        $headers = $matching[0].request.headers
        foreach ($header in @('X-Ca-Key', 'X-Ca-Signature', 'X-Ca-Timestamp', 'X-Ca-Signature-Headers', 'userId')) {
            $value = Get-ObjectProperty $headers $header
            if ($null -eq $value -or [string]::IsNullOrWhiteSpace((@($value) -join ''))) {
                throw "Signed adapter calculate request is missing '$header'."
            }
        }
    }

    $mappingResult = Invoke-RestMethod -Method Get -Uri "$AdminUrl/__admin/mappings" -TimeoutSec 15
    $mappings = @($mappingResult.mappings)
    $passageway = @($mappings | Where-Object { (Get-ObjectProperty $_.request 'urlPath') -eq '/artemis/api/vehicle/v1/parkinglot/passageway/record' })
    $calculates = @($mappings | Where-Object { (Get-ObjectProperty $_.request 'urlPath') -eq '/artemis/api/vehicle/v1/parkingfee/calculate' })
    $confirms = @($mappings | Where-Object { (Get-ObjectProperty $_.request 'urlPath') -eq '/artemis/api/vehicle/v1/parkingfee/confirm' })
    if ($passageway.Count -ne 1 -or @($passageway[0].response.jsonBody.data.list).Count -ne 2) {
        throw 'Passageway mapping does not expose both active mock sessions.'
    }
    $passageTickets = @($passageway[0].response.jsonBody.data.list | ForEach-Object { $_.personInfo.cardNum })
    if ($passageTickets -notcontains 'MOCK-SESSION-001' -or $passageTickets -notcontains 'MOCK-SESSION-002') {
        throw 'Passageway inventory does not contain both mock ticket references.'
    }
    if ($calculates.Count -ne 2) { throw 'A distinct calculate mapping was not created for each mock session.' }
    if ($confirms.Count -ne 2) { throw 'A distinct confirm mapping was not created for each mock session.' }

    foreach ($expected in @(
        [pscustomobject]@{ Ticket='MOCK-SESSION-001'; Plate='MOCK001'; Fee='25.00' },
        [pscustomobject]@{ Ticket='MOCK-SESSION-002'; PlatePattern='^MOCK\d{4}$'; Fee='30.00' }
    )) {
        $calculateMapping = @($calculates | Where-Object { $_.response.jsonBody.data.cardNum -eq $expected.Ticket })
        if ($calculateMapping.Count -ne 1 -or $calculateMapping[0].response.jsonBody.data.fee -ne $expected.Fee) {
            throw "Calculate mapping is incorrect for '$($expected.Ticket)'."
        }
        if ($expected.PSObject.Properties['Plate'] -and $calculateMapping[0].response.jsonBody.data.plateLicense -ne $expected.Plate) {
            throw "Calculate plate is incorrect for '$($expected.Ticket)'."
        }
        if ($expected.PSObject.Properties['PlatePattern'] -and $calculateMapping[0].response.jsonBody.data.plateLicense -notmatch $expected.PlatePattern) {
            throw "Generated calculate plate is incorrect for '$($expected.Ticket)'."
        }

        $confirmMapping = @($confirms | Where-Object { $_.name -eq "ExitPass mock confirm $($expected.Ticket)" })
        if ($confirmMapping.Count -ne 1) { throw "Confirm mapping is missing for '$($expected.Ticket)'." }
        $confirmBody = $confirmMapping[0].request.bodyPatterns[0].equalToJson | ConvertFrom-Json
        if ($confirmBody.cardNum -ne $expected.Ticket -or $confirmBody.fee -ne $expected.Fee -or $confirmBody.immediatelyLeave -ne 0) {
            throw "Confirm matching is incorrect for '$($expected.Ticket)'."
        }
    }
    return [pscustomobject]@{ CalculateRequestCount=$calculate.Count; PassagewaySessionCount=2; ConfirmMappingCount=$confirms.Count }
}

function Invoke-CreatorExpectFailure([string[]] $Arguments) {
    $previousErrorAction = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $creatorPath @Arguments *> $null
        return $LASTEXITCODE -ne 0
    }
    finally {
        $ErrorActionPreference = $previousErrorAction
    }
}

function Invoke-SmokeTest([string] $CentralUrl, [string] $AdminUrl) {
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $creatorPath -Ticket 'MOCK-SESSION-001' -Plate 'MOCK001' -Fee 25 -VendorRecordGuid '19000000-0000-0000-0000-000000000001' | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Failed to create MOCK-SESSION-001.' }
    $first = Resolve-MockSession 'MOCK-SESSION-001' $CentralUrl

    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $creatorPath -Ticket 'MOCK-SESSION-002' -Fee 30 | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Failed to create MOCK-SESSION-002.' }
    $parsedRegistry = ConvertFrom-Json -InputObject (Get-Content -LiteralPath $sessionsPath -Raw)
    $registry = @($parsedRegistry | ForEach-Object { $_ })
    $generated = @($registry | Where-Object { $_.ticketReference -eq 'MOCK-SESSION-002' })
    $generatedGuid = [Guid]::Empty
    $validGeneratedGuid = $generated.Count -eq 1 -and [Guid]::TryParse($generated[0].vendorRecordGuid, [ref]$generatedGuid)
    if ($generated.Count -ne 1 -or $generated[0].plateLicense -notmatch '^MOCK\d{4}$' -or
        [string]::IsNullOrWhiteSpace($generated[0].entryTime) -or
        -not $validGeneratedGuid -or $generatedGuid -eq [Guid]::Empty) {
        throw ("Omitted defaults were not generated correctly (count={0}, plate={1}, entry={2}, guid={3})." -f
            $generated.Count,
            $(if ($generated.Count -eq 1) { $generated[0].plateLicense } else { '<missing>' }),
            $(if ($generated.Count -eq 1 -and -not [string]::IsNullOrWhiteSpace($generated[0].entryTime)) { 'present' } else { 'missing' }),
            $validGeneratedGuid)
    }
    $second = Resolve-MockSession 'MOCK-SESSION-002' $CentralUrl
    $firstAgain = Resolve-MockSession 'MOCK-SESSION-001' $CentralUrl
    if ($firstAgain.ParkingSessionId -ne $first.ParkingSessionId) {
        throw 'The first mock session did not remain independently resolvable after adding the second.'
    }

    if (-not (Invoke-CreatorExpectFailure @('-Ticket', 'MOCK-SESSION-001', '-Fee', '25'))) {
        throw 'Duplicate mock ticket creation was not rejected.'
    }
    if (-not (Invoke-CreatorExpectFailure @('-Ticket', 'MOCK-NEGATIVE-FEE', '-Fee', '-1'))) {
        throw 'Negative mock fee creation was not rejected.'
    }

    $evidence = Test-WireMockEvidence $AdminUrl
    return [pscustomobject]@{
        First=$first
        Second=$second
        Journal=$evidence
        DuplicateRejected=$true
        NegativeFeeRejected=$true
        DefaultsGenerated=$true
        AddedWithoutRestart=$true
    }
}

function Invoke-SelfTest {
    function New-TestContainer {
        param(
            [Parameter(Mandatory)][string] $Name,
            [bool] $Running = $true,
            [bool] $Attached = $true,
            [string[]] $Aliases = @(),
            [AllowNull()][string] $RuntimeLabel = $null,
            [string] $HostPort = '56065',
            [hashtable] $AdditionalLabels = @{}
        )
        $labels = [pscustomobject]@{}
        if ($null -ne $RuntimeLabel) {
            $labels | Add-Member -NotePropertyName $centralPmsRuntimeLabel -NotePropertyValue $RuntimeLabel
        }
        foreach ($entry in $AdditionalLabels.GetEnumerator()) {
            $labels | Add-Member -NotePropertyName $entry.Key -NotePropertyValue $entry.Value
        }
        $networks = [pscustomobject]@{}
        if ($Attached) {
            $networks | Add-Member -NotePropertyName $networkName -NotePropertyValue ([pscustomobject]@{ Aliases = @($Aliases) })
        }
        $ports = [pscustomobject]@{}
        $ports | Add-Member -NotePropertyName '8080/tcp' -NotePropertyValue @([pscustomobject]@{ HostIp = '127.0.0.1'; HostPort = $HostPort })
        return [pscustomobject]@{
            Name = "/$Name"
            State = [pscustomobject]@{ Running = $Running }
            Config = [pscustomobject]@{ Labels = $labels; Env = @() }
            NetworkSettings = [pscustomobject]@{ Networks = $networks; Ports = $ports }
        }
    }

    function Assert-Test([bool] $Condition, [string] $Message) {
        if (-not $Condition) { throw $Message }
    }

    function Assert-Throws([scriptblock] $Action, [string] $Pattern) {
        try {
            & $Action
        }
        catch {
            if ($_.Exception.Message -notmatch $Pattern) {
                throw "Expected error matching '$Pattern', got: $($_.Exception.Message)"
            }
            return
        }
        throw "Expected error matching '$Pattern', but no error was raised."
    }

    function Invoke-TestCase([string] $Name, [scriptblock] $Action) {
        try {
            & $Action | Out-Null
            return 1
        }
        catch {
            throw "Self-test '$Name' failed: $($_.Exception.Message)"
        }
    }

    if ($networkName -eq 'exitpass-dev-synthetic' -or $wireMockContainer -eq $exitPassDatabaseContainer) {
        throw 'Wrapper resource constants violate the persistent PITX boundary.'
    }
    if ($expectedRoute -ne 'http://pitx-site-adapter:8080/') { throw 'The persisted adapter route constant changed.' }
    if (-not (Test-Path -LiteralPath $creatorPath -PathType Leaf)) { throw 'The on-demand creator script is missing.' }
    $forbidden = Select-String -LiteralPath $creatorPath -Pattern 'psql|Npgsql|payment_attempt|payment_confirmation|exit_authorization|fiscal_document|electronic_journal' -CaseSensitive:$false
    if ($null -ne $forbidden) { throw 'The creator script contains a prohibited database or business-state operation.' }

    $standard = New-TestContainer -Name $standardCentralPmsContainer -Aliases @($standardCentralPmsAlias) -RuntimeLabel $centralPmsRuntimeLabelValue
    $legacy = New-TestContainer -Name $legacyCentralPmsContainer -Aliases @('exitpass-r41-central-pms')
    $testCount = 0
    $testCount += Invoke-TestCase 'standardized runtime discovery' {
        $result = Select-CentralPmsRuntime @($standard)
        Assert-Test ($result.Name -eq $standardCentralPmsContainer -and $result.Kind -eq 'standardized') 'The standardized runtime was not selected.'
    }
    $testCount += Invoke-TestCase 'legacy runtime compatibility' {
        $result = Select-CentralPmsRuntime @($legacy)
        Assert-Test ($result.Name -eq $legacyCentralPmsContainer -and $result.Kind -eq 'legacy') 'The legacy runtime was not selected.'
    }
    $testCount += Invoke-TestCase 'duplicate runtime ambiguity' {
        Assert-Throws { Select-CentralPmsRuntime @($standard, $legacy) } 'Multiple approved.*Stop the duplicate runtime'
    }
    $testCount += Invoke-TestCase 'missing runtime' {
        Assert-Throws { Select-CentralPmsRuntime @() } 'No approved.*was found'
    }
    $testCount += Invoke-TestCase 'stopped runtime' {
        $stopped = New-TestContainer -Name $standardCentralPmsContainer -Running $false -Aliases @($standardCentralPmsAlias) -RuntimeLabel $centralPmsRuntimeLabelValue
        Assert-Throws { Select-CentralPmsRuntime @($stopped) } 'No approved.*is running.*Stopped candidate'
    }
    $testCount += Invoke-TestCase 'persistent network required' {
        $detached = New-TestContainer -Name $standardCentralPmsContainer -Attached $false -RuntimeLabel $centralPmsRuntimeLabelValue
        Assert-Throws { Select-CentralPmsRuntime @($detached) } 'not attached to expected persistent network'
    }
    $testCount += Invoke-TestCase 'canonical runtime label required' {
        $wrongLabel = New-TestContainer -Name $standardCentralPmsContainer -Aliases @($standardCentralPmsAlias) -RuntimeLabel 'wrong-runtime'
        Assert-Throws { Select-CentralPmsRuntime @($wrongLabel) } 'missing or incorrect label'
    }
    $testCount += Invoke-TestCase 'readiness failure prevents resolution' {
        Assert-Throws { Resolve-CentralPmsRuntime @($standard) { param($Url) throw "Synthetic readiness failure at $Url" } } 'Synthetic readiness failure.*health/ready'
    }
    $testCount += Invoke-TestCase 'loopback URL resolution' {
        $dynamicPort = New-TestContainer -Name $standardCentralPmsContainer -Aliases @($standardCentralPmsAlias) -RuntimeLabel $centralPmsRuntimeLabelValue -HostPort '49123'
        $result = Select-CentralPmsRuntime @($dynamicPort)
        Assert-Test ($result.HostUrl -eq 'http://127.0.0.1:49123') 'The published loopback port was not resolved from Docker metadata.'
    }
    $testCount += Invoke-TestCase 'adapter restoration remains in finally' {
        $source = Get-Content -LiteralPath $PSCommandPath -Raw
        Assert-Test ($source -match '(?s)finally\s*\{.*if \(\$realAdapterStopped.*docker.*start.*Wait-Http \$realAdapterHealthUrl') 'The finally block no longer starts and verifies the ordinary adapter.'
    }
    $testCount += Invoke-TestCase 'persisted route invariance' {
        $source = Get-Content -LiteralPath $PSCommandPath -Raw
        Assert-Test ($expectedRoute -eq 'http://pitx-site-adapter:8080/') 'The canonical persisted route changed.'
        Assert-Test (([regex]::Matches($source, 'Get-SiteAdapterRoute\) -ne \$routeBefore')).Count -ge 2) 'Route invariance is not checked during startup and restoration.'
    }
    $testCount += Invoke-TestCase 'owned cleanup boundary' {
        $owned = New-TestContainer -Name $wireMockContainer -AdditionalLabels @{ $wrapperLabel = 'true'; $invocationLabel = $invocationId }
        Assert-WrapperResourceOwnership $owned
        $foreign = New-TestContainer -Name $wireMockContainer -AdditionalLabels @{ $wrapperLabel = 'true'; $invocationLabel = [Guid]::NewGuid().ToString('D') }
        Assert-Throws { Assert-WrapperResourceOwnership $foreign } 'Refusing to remove non-wrapper resource'
    }
    Write-Output "MOCK_HIKCENTRAL_SELF_TEST_PASS ($testCount tests)"
}

if ($SelfTest) {
    Invoke-SelfTest
    return
}

try {
    & docker version *> $null
    if ($LASTEXITCODE -ne 0) { throw 'Docker is unavailable. Start Docker Desktop and retry.' }
    if ((Invoke-Docker -Arguments @('network', 'inspect', $networkName) -AllowFailure).ExitCode -ne 0) {
        throw "Required persistent network '$networkName' is unavailable."
    }
    Assert-ContainerAndVolume $exitPassDatabaseContainer $exitPassDatabaseVolume
    Assert-ContainerAndVolume $posDatabaseContainer $posDatabaseVolume
    if (-not (Test-Path -LiteralPath $adapterEnvironmentFile -PathType Leaf)) {
        throw "PITX Site Adapter private configuration is unavailable at $adapterEnvironmentFile."
    }
    if (-not (Test-Path -LiteralPath $creatorPath -PathType Leaf)) { throw 'The on-demand creator script is missing.' }
    Assert-NoWrapperCollision

    $siteCount = Invoke-ExitPassSql "SELECT count(*) FROM sites.sites WHERE site_id = '$expectedSiteId'::uuid AND site_group_id = '$expectedSiteGroupId'::uuid;"
    if ($siteCount -ne '1') { throw 'The expected PITX Level 3 Site configuration is unavailable.' }
    $routeBefore = Get-SiteAdapterRoute
    if ($routeBefore -ne "$expectedRoute|IST|$expectedAdapterIdentityId") {
        throw "The persisted PITX Site Adapter route is not the expected local non-production route: $routeBefore"
    }

    $realAdapter = Find-RealAdapter
    Assert-ImageMatchesCurrentAdapterSource $realAdapter
    $realAdapterName = $realAdapter.Name.TrimStart('/')
    $realAdapterHealthUrl = "$(Get-LoopbackContainerUrl $realAdapter '8080/tcp')/health/ready"
    Wait-Http $realAdapterHealthUrl

    $centralRuntime = Resolve-CentralPmsRuntime
    $centralUrl = $centralRuntime.HostUrl
    Write-Output "Central PMS runtime discovered: $($centralRuntime.Name) ($($centralRuntime.Kind))"
    Write-Output "Central PMS host URL: $centralUrl"

    $countsBefore = Get-BusinessCounts
    $posCountsBefore = Get-PosCounts

    Invoke-Docker -Arguments @('stop', $realAdapterName) | Out-Null
    $realAdapterStopped = $true

    Start-WireMock
    $wireMockStarted = $true
    $wireMock = Get-ContainerInspect $wireMockContainer
    $wireMockAdminUrl = Get-LoopbackContainerUrl $wireMock '8080/tcp'
    Wait-Http "$wireMockAdminUrl/__admin/mappings"

    Start-MockAdapter $realAdapter
    $mockAdapterStarted = $true
    $mockAdapter = Get-ContainerInspect $mockAdapterContainer
    $mockAdapterUrl = Get-LoopbackContainerUrl $mockAdapter '8080/tcp'
    Wait-Http "$mockAdapterUrl/health/ready"
    if ((Get-SiteAdapterRoute) -ne $routeBefore) { throw 'The persisted Central PMS Site Adapter route changed during mock startup.' }

    Write-RuntimeState $wireMockAdminUrl

    Write-Output ''
    Write-Output 'MOCK HIKCENTRAL ACTIVE'
    Write-Output 'SIMULATED HIKCENTRAL SESSION - NOT REAL PITX ACCEPTANCE'
    Write-Output ''
    Write-Output 'Create a session from another terminal with:'
    Write-Output '.\scripts\v1.3\hikcentral\New-MockHikCentralSession.ps1 -Ticket MOCK-SESSION-001 -Plate MOCK001 -Fee 25'
    Write-Output ''

    if ($SmokeTest) {
        $smokeEvidence = Invoke-SmokeTest $centralUrl $wireMockAdminUrl
        if ((Get-BusinessCounts) -ne $countsBefore) { throw 'Mock validation changed payment, confirmation, or exit-authorization counts.' }
        if ((Get-PosCounts) -ne $posCountsBefore) { throw 'Mock validation changed fiscal-document or EJ counts.' }
        Write-Output ("SMOKE_FIRST_PARKING_SESSION_ID={0}" -f $smokeEvidence.First.ParkingSessionId)
        Write-Output ("SMOKE_FIRST_TARIFF_SNAPSHOT_ID={0}" -f $smokeEvidence.First.TariffSnapshotId)
        Write-Output ("SMOKE_SECOND_PARKING_SESSION_ID={0}" -f $smokeEvidence.Second.ParkingSessionId)
        Write-Output ("SMOKE_SECOND_TARIFF_SNAPSHOT_ID={0}" -f $smokeEvidence.Second.TariffSnapshotId)
        Write-Output ("SMOKE_CALCULATE_REQUEST_COUNT={0}" -f $smokeEvidence.Journal.CalculateRequestCount)
        Write-Output 'MOCK_HIKCENTRAL_SMOKE_PASS'
    }
    else {
        Write-Output 'Press Ctrl+C to stop mock mode and restore the ordinary PITX Site Adapter.'
        Invoke-Docker -Arguments @('logs', '--follow', $mockAdapterContainer) | Out-Null
    }
}
finally {
    $restorationError = $null
    try {
        if (Test-Path -LiteralPath $statePath) { Remove-Item -LiteralPath $statePath -Force }
        if (Test-Path -LiteralPath $sessionsPath) { Remove-Item -LiteralPath $sessionsPath -Force }
        $lockPath = Join-Path $runtimeRoot 'sessions.lock'
        if (Test-Path -LiteralPath $lockPath) { Remove-Item -LiteralPath $lockPath -Force }

        foreach ($name in @($mockAdapterContainer, $wireMockContainer)) {
            $container = Get-ContainerInspect $name -AllowMissing
            if ($null -ne $container) {
                Assert-WrapperResourceOwnership $container
                Invoke-Docker -Arguments @('rm', '--force', $name) | Out-Null
            }
        }

        if ($realAdapterStopped -and -not [string]::IsNullOrWhiteSpace($realAdapterName)) {
            Invoke-Docker -Arguments @('start', $realAdapterName) | Out-Null
            Wait-Http $realAdapterHealthUrl 200 90
            $restored = Get-ContainerInspect $realAdapterName
            $attachment = Get-NetworkAttachment $restored $networkName
            if ($null -eq $attachment -or $attachment.Aliases -notcontains 'pitx-site-adapter') {
                throw 'The restored adapter does not own the pitx-site-adapter alias.'
            }
            if (-not [string]::IsNullOrWhiteSpace($routeBefore) -and (Get-SiteAdapterRoute) -ne $routeBefore) {
                throw 'The persisted Site Adapter route differs after restoration.'
            }
            Write-Output "Ordinary PITX Site Adapter restored: $realAdapterName"
        }
    }
    catch {
        $restorationError = $_
    }

    if ($null -ne $restorationError) {
        Write-Error @"
SITE ADAPTER RESTORATION FAILED
$($restorationError.Exception.Message)
Recovery command:
docker start $realAdapterName
Then verify: $realAdapterHealthUrl
"@
    }
}
