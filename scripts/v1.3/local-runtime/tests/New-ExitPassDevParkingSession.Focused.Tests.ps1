[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$target = Join-Path (Split-Path -Parent $PSScriptRoot) 'New-ExitPassDevParkingSession.ps1'
$tokens = $null
$parseErrors = $null
$ast = [System.Management.Automation.Language.Parser]::ParseFile($target, [ref]$tokens, [ref]$parseErrors)
if (@($parseErrors).Count -gt 0) { throw "Dev parking-session helper does not parse: $($parseErrors[0].Message)" }
foreach ($function in $ast.FindAll({
    param($node)
    $node -is [System.Management.Automation.Language.FunctionDefinitionAst]
}, $true)) {
    Invoke-Expression $function.Extent.Text
}

$environmentName = 'DEV'
$composeProject = 'exitpass-dev-20261122'
$networkName = 'exitpass-dev-20261122-network'
$publishNetworkName = 'exitpass-dev-20261122-publish'
$expectedParkingLot = '1'
$wireMockAdminUrl = 'http://fake-wiremock'
$invariant = [Globalization.CultureInfo]::InvariantCulture
$script:Passed = [Collections.Generic.List[string]]::new()

function Assert-Equal([object]$Expected, [object]$Actual, [string]$Because) {
    if ([string]$Expected -cne [string]$Actual) { throw "$Because Expected '$Expected'; found '$Actual'." }
}

function Assert-True([bool]$Value, [string]$Because) {
    if (-not $Value) { throw $Because }
}

function Assert-Throws([scriptblock]$ActionBlock, [string]$Pattern, [string]$Because) {
    try { & $ActionBlock }
    catch {
        if ($_.Exception.Message -like $Pattern) { return }
        throw "$Because Unexpected error: $($_.Exception.Message)"
    }
    throw "$Because No error was raised."
}

function Invoke-Case([string]$Name, [scriptblock]$ActionBlock) {
    & $ActionBlock
    $script:Passed.Add($Name)
    Write-Host "PASS $Name"
}

function New-LabelObject([string]$Project, [string]$Service) {
    $labels = [pscustomobject]@{}
    $labels | Add-Member -NotePropertyName 'com.docker.compose.project' -NotePropertyValue $Project
    $labels | Add-Member -NotePropertyName 'com.docker.compose.service' -NotePropertyValue $Service
    return $labels
}

function New-NetworkObject([string]$Alias, [switch]$AddForeignNetwork) {
    $networks = [pscustomobject]@{}
    $networks | Add-Member -NotePropertyName $networkName -NotePropertyValue ([pscustomobject]@{ Aliases = @('container-name', $Alias) })
    if ($AddForeignNetwork) {
        $networks | Add-Member -NotePropertyName 'unexpected-network' -NotePropertyValue ([pscustomobject]@{ Aliases = @('unexpected') })
    }
    return $networks
}

function New-FakeContainer([string]$Project = $composeProject, [string]$Alias = 'hikcentral', [string]$Service = 'hikcentral', [switch]$AddForeignNetwork) {
    return [pscustomobject]@{
        Name = '/exitpass-dev-20261122-hikcentral'
        State = [pscustomobject]@{ Running = $true }
        Config = [pscustomobject]@{ Labels = (New-LabelObject $Project $Service) }
        NetworkSettings = [pscustomobject]@{ Networks = (New-NetworkObject $Alias -AddForeignNetwork:$AddForeignNetwork) }
    }
}

function New-FakePortBindings([int]$ContainerPort, [int]$HostPort) {
    $bindings = [pscustomobject]@{}
    $bindings | Add-Member -NotePropertyName "$ContainerPort/tcp" -NotePropertyValue @([pscustomobject]@{ HostIp = '127.0.0.1'; HostPort = [string]$HostPort })
    return $bindings
}

function Copy-TestObject($Value) {
    return ConvertFrom-Json (ConvertTo-Json -InputObject $Value -Depth 50)
}

function New-TestSession([string]$TicketValue = 'TICKET-100', [string]$PlateValue = 'PLATE-100', [string]$EntryValue = '2026-11-28T00:00:00+08:00', [string]$FeeValue = '50.00', $CarType = '5') {
    return [pscustomobject]@{
        ticketReference = $TicketValue
        plateLicense = $PlateValue
        vendorRecordGuid = '11111111-1111-1111-1111-111111111111'
        entryTime = $EntryValue
        fee = $FeeValue
        parkingLotIndexCode = '1'
        vehicleType = if ($null -eq $CarType) { 'UNKNOWN' } else { 'CAR' }
        carType = $CarType
    }
}

function New-TestPassageMapping([string]$EntryValue = '2026-11-28T00:00:00+08:00', [bool]$Persistent = $true) {
    $session = New-TestSession -EntryValue $EntryValue
    return [pscustomobject]@{
        id = 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa'
        uuid = 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa'
        persistent = $Persistent
        priority = 5
        request = [pscustomobject]@{ method = 'POST'; urlPath = '/artemis/api/vehicle/v1/parkinglot/passageway/record' }
        response = [pscustomobject]@{
            status = 200
            jsonBody = [pscustomobject]@{
                code = '0'
                data = [pscustomobject]@{ total = 1; list = @([pscustomobject](New-PassageRecord $session)) }
            }
        }
    }
}

function New-TestStoredInventory($Mappings) {
    $items = @()
    foreach ($mapping in @($Mappings)) {
        $items += [pscustomobject]@{ Id = [string]$mapping.id; Name = "mapping-$($mapping.id).json"; File = $null; Mapping = $mapping }
    }
    return [pscustomobject]@{ Path = 'fixture'; Items = @($items) }
}

Invoke-Case 'PowerShell 5.1-compatible public parameters remain present' {
    $parameterNames = @($ast.ParamBlock.Parameters | ForEach-Object { $_.Name.VariablePath.UserPath })
    Assert-ExactStringSet @('Ticket', 'Plate', 'VehicleType', 'Fee', 'EntryTime', 'PreflightOnly') $parameterNames 'Public helper parameters'
    $entry = @($ast.ParamBlock.Parameters | Where-Object { $_.Name.VariablePath.UserPath -eq 'EntryTime' })[0]
    Assert-Equal 'DateTimeOffset' $entry.StaticType.Name 'EntryTime must remain strongly typed.'
    $source = Get-Content -LiteralPath $target -Raw
    Assert-True ($source -match '\[string\]\$VehicleType\s*=\s*''CAR''') 'VehicleType must retain the CAR default.'
    Assert-True ($source -match '\[decimal\]\$Fee\s*=\s*50\.00') 'Fee must retain the 50.00 default.'
}

Invoke-Case 'UNKNOWN vehicle type omits carType and verifies successfully' {
    $session = New-TestSession -CarType $null
    $record = Copy-TestObject (New-PassageRecord $session)
    Assert-True ($null -eq $record.carInfo.PSObject.Properties['carType']) 'UNKNOWN must preserve the intentionally absent HikCentral carType field.'
    Assert-True (Test-PassageRecordVehicleType $record $null) 'Verification must accept an absent carType for UNKNOWN.'
}

Invoke-Case 'explicit vehicle type remains strictly verified' {
    $session = New-TestSession -CarType '5'
    $record = Copy-TestObject (New-PassageRecord $session)
    Assert-True (Test-PassageRecordVehicleType $record '5') 'CAR must verify against HikCentral type 5.'
    Assert-True (-not (Test-PassageRecordVehicleType $record '6')) 'An explicit mismatched HikCentral type must be rejected.'
}

Invoke-Case 'excess fee precision is rejected before mutation' {
    Assert-Equal '50.12' ((Get-AcceptedFee ([decimal]'50.12')).ToString('0.00', $invariant)) 'Two-decimal fees must remain exact.'
    Assert-Throws { Get-AcceptedFee ([decimal]'50.123') } '*representable exactly to two decimal places*' 'A third decimal place must be refused.'
    $source = Get-Content -LiteralPath $target -Raw
    Assert-True ($source.IndexOf('$acceptedFee = Get-AcceptedFee $Fee', [StringComparison]::Ordinal) -lt $source.IndexOf('[IO.Directory]::CreateDirectory($operationRoot)', [StringComparison]::Ordinal)) 'Fee precision must be checked before the first mutation.'
}

Invoke-Case 'approved Dev ownership is accepted' {
    Assert-ContainerIdentity (New-FakeContainer) 'hikcentral' 'hikcentral'
}

Invoke-Case 'wrong Compose project is refused' {
    Assert-Throws { Assert-ContainerIdentity (New-FakeContainer -Project 'exitpass-clean-november-uat') 'hikcentral' 'hikcentral' } '*not owned by the approved Dev Compose project/service*' 'A UAT-owned target must be refused.'
}

Invoke-Case 'unavailable Dev target is refused' {
    $stopped = New-FakeContainer
    $stopped.State.Running = $false
    Assert-Throws { Assert-ContainerIdentity $stopped 'hikcentral' 'hikcentral' } '*is not running*' 'A stopped required Dev target must be refused.'
}

Invoke-Case 'unexpected network is refused' {
    Assert-Throws { Assert-ContainerIdentity (New-FakeContainer -AddForeignNetwork) 'hikcentral' 'hikcentral' } '*attached to an unexpected network*' 'A cross-environment network attachment must be refused.'
}

Invoke-Case 'valid Dev identity with incorrect published port is refused' {
    $ingress = New-FakeContainer -Alias 'ingress' -Service 'ingress'
    $ingress | Add-Member -NotePropertyName HostConfig -NotePropertyValue ([pscustomobject]@{ PortBindings = (New-FakePortBindings 18080 53478) })
    Assert-ContainerIdentity $ingress 'ingress' 'ingress'
    Assert-Throws { Assert-PublishedLoopbackEndpoint $ingress 'http://127.0.0.1:53477' 18080 'Dev WireMock' } '*is not bound to the inspected Dev ingress container port*' 'A valid container identity must not excuse an incorrect host-port binding.'
}

Invoke-Case 'published loopback endpoint and upstream route are accepted' {
    $ingress = New-FakeContainer -Alias 'ingress' -Service 'ingress'
    $ingress | Add-Member -NotePropertyName HostConfig -NotePropertyValue ([pscustomobject]@{ PortBindings = (New-FakePortBindings 18080 53477) })
    Assert-PublishedLoopbackEndpoint $ingress 'http://127.0.0.1:53477' 18080 'Dev WireMock'
    $configuration = "server {`n  listen 18080;`n  location / {`n    proxy_pass http://hikcentral:8080;`n  }`n}"
    Assert-IngressRoute $configuration 18080 'hikcentral:8080' 'Dev WireMock'
}

Invoke-Case 'exact persistence set accepts reordering' {
    Assert-ExactStringSet @('a.json', 'b.json') @('b.json', 'a.json') 'Test mapping set'
}

Invoke-Case 'persistence mismatch is refused' {
    Assert-Throws { Assert-ExactStringSet @('a.json', 'b.json') @('a.json', 'c.json') 'Test mapping set' } '*does not match the approved Dev runtime*' 'A missing or unexpected persisted mapping must fail closed.'
}

Invoke-Case 'intentional live persistent false versus stored true is accepted' {
    $session = New-TestSession
    $live = [pscustomobject](New-CalculateMapping $session 'test-key' 'cardNum' 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb')
    $live.persistent = $false
    $stored = Copy-TestObject $live
    $stored.persistent = $true
    Assert-StoredMappingInventory @($live) (New-TestStoredInventory @($stored)) 'PersistedRuntime' 'Fixture store' 'test-key'
}

Invoke-Case 'source placeholder and validated runtime app key are distinguished' {
    $session = New-TestSession
    $live = [pscustomobject](New-CalculateMapping $session 'test-key' 'cardNum' 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb')
    $live.persistent = $false
    $sourceMirror = Copy-TestObject $live
    $sourceMirror.persistent = $true
    $sourceMirror.request.headers.'X-Ca-Key'.equalTo = 'non-runtime-placeholder'
    Assert-StoredMappingInventory @($live) (New-TestStoredInventory @($sourceMirror)) 'PersistedTemplate' 'Source fixture store' 'test-key'

    $runtimeMirror = Copy-TestObject $sourceMirror
    Assert-Throws { Assert-StoredMappingInventory @($live) (New-TestStoredInventory @($runtimeMirror)) 'PersistedRuntime' 'Runtime fixture store' 'test-key' } '*does not contain the validated Dev runtime X-Ca-Key matcher*' 'A runtime store may not retain an arbitrary source placeholder.'
}

Invoke-Case 'same mapping identity with different persisted fee is refused' {
    $session = New-TestSession
    $live = [pscustomobject](New-CalculateMapping $session 'test-key' 'cardNum' 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb')
    $live.persistent = $false
    $stored = Copy-TestObject $live
    $stored.persistent = $true
    $stored.response.jsonBody.data.fee = '51.00'
    Assert-Throws { Assert-StoredMappingInventory @($live) (New-TestStoredInventory @($stored)) 'PersistedRuntime' 'Fixture store' 'test-key' } '*content differs from live WireMock*' 'An unchanged mapping ID and filename must not hide a different fee.'
}

Invoke-Case 'same passageway identity with different persisted entry time is refused' {
    $live = New-TestPassageMapping -Persistent $false
    $stored = Copy-TestObject $live
    $stored.persistent = $true
    $stored.response.jsonBody.data.list[0].carInfo.EnterTime = '2026-11-28T01:00:00+08:00'
    Assert-Throws { Assert-StoredMappingInventory @($live) (New-TestStoredInventory @($stored)) 'PersistedRuntime' 'Fixture store' 'test-key' } '*content differs from live WireMock*' 'An unchanged passageway identity triple must not hide a different entry time.'
}

Invoke-Case 'new session identifiers are accepted' {
    $inventory = @([pscustomobject]@{
        personInfo = [pscustomobject]@{ cardNum = 'EXISTING-TICKET' }
        carInfo = [pscustomobject]@{ plateLicense = 'EXISTING-PLATE' }
    })
    $mappings = @([pscustomobject]@{ id = 'm1'; name = 'existing'; response = [pscustomobject]@{} })
    Assert-SessionDoesNotExist $mappings $inventory 'NEW-TICKET' 'NEW-PLATE'
}

Invoke-Case 'duplicate ticket is refused without overwrite' {
    $inventory = @([pscustomobject]@{
        personInfo = [pscustomobject]@{ cardNum = 'EXISTING-TICKET' }
        carInfo = [pscustomobject]@{ plateLicense = 'EXISTING-PLATE' }
    })
    Assert-Throws { Assert-SessionDoesNotExist @() $inventory 'EXISTING-TICKET' 'NEW-PLATE' } '*Existing sessions are never overwritten*' 'A duplicate ticket must fail before persistence.'
}

Invoke-Case 'duplicate plate in a mapping is refused' {
    $mapping = [pscustomobject]@{
        id = 'm1'
        request = [pscustomobject]@{ method = 'POST'; urlPath = '/artemis/api/vehicle/v1/parkingfee/calculate'; bodyPatterns = @([pscustomobject]@{ equalToJson = '{"plateLicense":"EXISTING-PLATE"}' }) }
        response = [pscustomobject]@{ jsonBody = [pscustomobject]@{ data = [pscustomobject]@{ plateLicense = 'EXISTING-PLATE' } } }
    }
    Assert-Throws { Assert-SessionDoesNotExist @($mapping) @() 'NEW-TICKET' 'EXISTING-PLATE' } '*Existing sessions are never overwritten*' 'A duplicate plate must fail before persistence.'
}

Invoke-Case 'exact duplicate ticket in a supported mapping is refused' {
    $mapping = [pscustomobject]@{
        id = 'm-ticket'
        request = [pscustomobject]@{ method = 'POST'; urlPath = '/artemis/api/vehicle/v1/parkingfee/calculate'; bodyPatterns = @([pscustomobject]@{ equalToJson = '{"cardNum":"25741195933"}' }) }
        response = [pscustomobject]@{ jsonBody = [pscustomobject]@{ data = [pscustomobject]@{ cardNum = '25741195933'; plateLicense = 'PPG2333' } } }
    }
    Assert-Throws { Assert-SessionDoesNotExist @($mapping) @() '25741195933' 'NEWPLATE' } '*Existing sessions are never overwritten*' 'An exact ticket duplicate must be refused.'
}

Invoke-Case 'distinct prefix and substring identifiers remain valid' {
    $mapping = [pscustomobject]@{
        id = 'm-prefix'
        request = [pscustomobject]@{ method = 'POST'; urlPath = '/artemis/api/vehicle/v1/parkingfee/calculate'; bodyPatterns = @([pscustomobject]@{ equalToJson = '{"cardNum":"257411959330","plateLicense":"XPPG2333X"}' }) }
        response = [pscustomobject]@{ jsonBody = [pscustomobject]@{ data = [pscustomobject]@{ cardNum = '257411959330'; plateLicense = 'XPPG2333X' } } }
    }
    Assert-SessionDoesNotExist @($mapping) @() '25741195933' 'PPG2333'
}

Invoke-Case 'mapping filename is deterministic and bounded' {
    Assert-Equal 'exitpass-mock-confirm-25741195933-11111111-1111-1111-1111-111111111111.json' (New-MappingFileName '25741195933' 'confirm' '11111111-1111-1111-1111-111111111111') 'Durable mapping filenames must identify the session and mapping.'
}

Invoke-Case 'Dev-only durable targets are explicit' {
    $source = Get-Content -LiteralPath $target -Raw
    foreach ($required in @(
        "`$environmentRoot = 'D:\SourceCodes\ExitPass.dev'",
        "`$wireMockVolume = 'exitpass-dev-20261122-wiremock'",
        "D:\SourceCodes\ExitPass.dev\data\wiremock\mappings",
        "D:\SourceCodes\ExitPass.dev\data\wiremock-runtime\mappings",
        "`$centralPmsUrl = 'http://127.0.0.1:58165'"
    )) {
        Assert-True ($source.Contains($required)) "Missing explicit Dev target: $required"
    }
    Assert-True (-not $source.Contains('clean-november-uat')) 'The helper must not embed the UAT runtime.'
    Assert-True (-not $source.Contains('demo-rc1')) 'The helper must not embed the Demo runtime.'
}

$script:FakeMappings = @()
$script:RollbackDeleteFailure = $false
$script:LostResponseInjected = $false
$script:FakeVolumeInventory = $null
$script:FakeMirrorInventories = @{}
$script:ExpectedSourcePassageway = $null
$script:ExpectedRuntimePassageway = $null
$mappingDirectories = @('fixture-source-mirror', 'fixture-runtime-mirror')
$wireMockVolume = 'fixture-named-volume'

function Copy-TestInventory($Inventory) {
    $items = @($Inventory.Items | ForEach-Object {
        [pscustomobject]@{
            Id = [string]$_.Id
            Name = [string]$_.Name
            File = $_.File
            Mapping = Copy-TestObject $_.Mapping
        }
    })
    return [pscustomobject]@{ Path = [string]$Inventory.Path; Items = @($items) }
}

function Invoke-FakeStage($Inventory, [string]$StagePath) {
    $items = @($Inventory.Items)
    $writePath = Join-Path $StagePath 'write'
    if (Test-Path -LiteralPath $writePath -PathType Container) {
        foreach ($file in @(Get-ChildItem -LiteralPath $writePath -File -Filter '*.json')) {
            $mapping = Get-Content -Raw -LiteralPath $file.FullName | ConvertFrom-Json
            $id = [string](Get-PropertyValue $mapping 'id')
            $items = @($items | Where-Object { [string]$_.Name -ne $file.Name -and [string]$_.Id -ne $id })
            $items += [pscustomobject]@{ Id = $id; Name = $file.Name; File = $file.FullName; Mapping = $mapping }
        }
    }
    $removePath = Join-Path $StagePath 'remove'
    if (Test-Path -LiteralPath $removePath -PathType Container) {
        foreach ($marker in @(Get-ChildItem -LiteralPath $removePath -File -Filter '*.remove')) {
            $fileName = $marker.Name.Substring(0, $marker.Name.Length - '.remove'.Length)
            $items = @($items | Where-Object { [string]$_.Name -ne $fileName })
        }
    }
    return [pscustomobject]@{ Path = [string]$Inventory.Path; Items = @($items) }
}

function Get-MirrorInventory([string]$Directory) {
    return Copy-TestInventory $script:FakeMirrorInventories[$Directory]
}

function Get-VolumeInventory {
    return Copy-TestInventory $script:FakeVolumeInventory
}

function Invoke-VolumeStage {
    param([string]$StagePath, [string]$Image)
    $script:FakeVolumeInventory = Invoke-FakeStage $script:FakeVolumeInventory $StagePath
}

function Copy-StageToMirror {
    param([string]$StagePath, [string]$Directory)
    $script:FakeMirrorInventories[$Directory] = Invoke-FakeStage $script:FakeMirrorInventories[$Directory] $StagePath
}

function Copy-StageToMirrors {
    param([string]$StagePath)
    foreach ($directory in $mappingDirectories) { Copy-StageToMirror $StagePath $directory }
}

function Invoke-WireMockAdmin {
    param([string]$Method, [string]$Uri, $Body)
    switch ($Method) {
        'Get' { return [pscustomobject]@{ mappings = @($script:FakeMappings) } }
        'Put' {
            $id = [string]$Body.id
            $script:FakeMappings = @($script:FakeMappings | Where-Object { [string]$_.id -ne $id }) + @($Body)
            return [pscustomobject]@{}
        }
        'Post' {
            $script:FakeMappings += $Body
            $script:LostResponseInjected = $true
            throw 'simulated lost POST response after apply'
        }
        'Delete' {
            $id = ($Uri -split '/')[-1]
            if ($script:RollbackDeleteFailure) { throw "simulated delete failure for $id" }
            $script:FakeMappings = @($script:FakeMappings | Where-Object { [string]$_.id -ne $id })
            return [pscustomobject]@{}
        }
    }
}

function New-FakeRegistrationPlan([string]$BackupDirectory) {
    $livePassageway = New-TestPassageMapping -Persistent $false
    $livePassageway.request | Add-Member -NotePropertyName headers -NotePropertyValue (New-CommonHeaders 'test-key')

    $sourcePassageway = Copy-TestObject $livePassageway
    $sourcePassageway.persistent = $true
    $sourcePassageway.request.headers.'X-Ca-Key'.equalTo = 'non-runtime-placeholder'
    $sourcePassageway | Add-Member -NotePropertyName insertionIndex -NotePropertyValue 17

    $runtimePassageway = Copy-TestObject $sourcePassageway
    $runtimePassageway.request.headers.'X-Ca-Key'.equalTo = 'test-key'
    $script:ExpectedSourcePassageway = Copy-TestObject $sourcePassageway
    $script:ExpectedRuntimePassageway = Copy-TestObject $runtimePassageway

    $sourceInventory = New-TestStoredInventory @($sourcePassageway)
    $sourceInventory.Path = $mappingDirectories[0]
    $runtimeInventory = New-TestStoredInventory @($runtimePassageway)
    $runtimeInventory.Path = $mappingDirectories[1]
    $script:FakeMirrorInventories = @{
        $mappingDirectories[0] = Copy-TestInventory $sourceInventory
        $mappingDirectories[1] = Copy-TestInventory $runtimeInventory
    }
    $script:FakeVolumeInventory = Copy-TestInventory $runtimeInventory
    $script:FakeVolumeInventory.Path = $wireMockVolume
    $script:FakeMappings = @($livePassageway)

    $updatedPassageway = Copy-TestObject $livePassageway
    $updatedRecords = @($updatedPassageway.response.jsonBody.data.list) + @(
        New-PassageRecord (New-TestSession -TicketValue 'TICKET-200' -PlateValue 'PLATE-200')
    )
    $updatedPassageway.response.jsonBody.data.total = $updatedRecords.Count
    $updatedPassageway.response.jsonBody.data.list = $updatedRecords
    $updatedPassageway.persistent = $true

    $storedNewMapping = [pscustomobject](New-CalculateMapping (New-TestSession -TicketValue 'TICKET-200' -PlateValue 'PLATE-200') 'test-key' 'cardNum' 'cccccccc-cccc-cccc-cccc-cccccccccccc')
    $liveNewMapping = Copy-TestObject $storedNewMapping
    $liveNewMapping.persistent = $false
    $newFiles = [ordered]@{ 'new-mapping.json' = $storedNewMapping }
    $context = [pscustomobject]@{
        Wire = [pscustomobject]@{ Image = 'fake-image' }
        AppKey = 'test-key'
        LiveMappings = @($livePassageway)
        Persistence = [pscustomobject]@{
            PassageFileName = 'mapping-aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa.json'
            Mirrors = @($sourceInventory, $runtimeInventory)
        }
    }
    return New-RegistrationPlan $context $livePassageway $updatedPassageway @($liveNewMapping) $newFiles $BackupDirectory 1
}

Invoke-Case 'lost response restores distinct live and stored passageway baselines' {
    $root = Join-Path ([IO.Path]::GetTempPath()) "exitpass-dev-helper-lost-response-$([Guid]::NewGuid().ToString('N'))"
    try {
        $plan = New-FakeRegistrationPlan $root
        $script:FakeMappings = @($plan.BeforeMappings)
        $script:RollbackDeleteFailure = $false
        $script:LostResponseInjected = $false
        $message = $null
        try { [void](Invoke-SessionRegistration $plan) } catch { $message = $_.Exception.Message }
        Assert-True $script:LostResponseInjected 'The fixture must apply the mapping before simulating the lost response.'
        Assert-True ($message -like '*simulated lost POST response after apply*') 'The original POST failure details must be preserved.'
        Assert-True ($message -like '*Rollback verified against the complete pre-operation baseline*') 'Rollback must be explicitly verified.'
        Assert-True ($message -like '*cccccccc-cccc-cccc-cccc-cccccccccccc*') 'The attempted mapping ID must be reported.'
        Assert-Equal '1' @($script:FakeMappings).Count 'The applied mapping must be removed during rollback.'
        Assert-Equal 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa' ([string]$script:FakeMappings[0].id) 'The original passageway mapping must remain.'
        Assert-True (-not [bool]$script:FakeMappings[0].persistent) 'The restored live passageway must retain persistent=false.'

        $restored = Get-PersistenceState @($script:FakeMappings) 'test-key'
        Assert-True $restored.ContentVerified 'The real persistence-content validator must accept the restored state.'
        $sourceRestored = @($script:FakeMirrorInventories[$mappingDirectories[0]].Items | Where-Object Id -eq 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa')[0].Mapping
        $runtimeRestored = @($script:FakeMirrorInventories[$mappingDirectories[1]].Items | Where-Object Id -eq 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa')[0].Mapping
        $volumeRestored = @($script:FakeVolumeInventory.Items | Where-Object Id -eq 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa')[0].Mapping
        Assert-True ([bool]$sourceRestored.persistent) 'The source-mirror passageway must retain persistent=true.'
        Assert-True ([bool]$runtimeRestored.persistent) 'The runtime-mirror passageway must retain persistent=true.'
        Assert-True ([bool]$volumeRestored.persistent) 'The named-volume passageway must retain persistent=true.'
        Assert-Equal (ConvertTo-CanonicalJson $script:ExpectedSourcePassageway) (ConvertTo-CanonicalJson $sourceRestored) 'The source-mirror passageway must regain its exact pre-operation content.'
        Assert-Equal (ConvertTo-CanonicalJson $script:ExpectedRuntimePassageway) (ConvertTo-CanonicalJson $runtimeRestored) 'The runtime-mirror passageway must regain its exact pre-operation content.'
        Assert-Equal (ConvertTo-CanonicalJson $script:ExpectedRuntimePassageway) (ConvertTo-CanonicalJson $volumeRestored) 'The named-volume passageway must regain its exact pre-operation content.'
        foreach ($inventory in @($script:FakeVolumeInventory, $script:FakeMirrorInventories[$mappingDirectories[0]], $script:FakeMirrorInventories[$mappingDirectories[1]])) {
            Assert-Equal '0' @($inventory.Items | Where-Object Id -eq 'cccccccc-cccc-cccc-cccc-cccccccccccc').Count 'All generated persisted mappings must be removed during rollback.'
        }
    }
    finally {
        if (Test-Path -LiteralPath $root) { [IO.Directory]::Delete($root, $true) }
    }
}

Invoke-Case 'rollback failure reports incomplete restoration explicitly' {
    $root = Join-Path ([IO.Path]::GetTempPath()) "exitpass-dev-helper-rollback-failure-$([Guid]::NewGuid().ToString('N'))"
    try {
        $plan = New-FakeRegistrationPlan $root
        $script:FakeMappings = @($plan.BeforeMappings)
        $script:RollbackDeleteFailure = $true
        $script:LostResponseInjected = $false
        $message = $null
        try { [void](Invoke-SessionRegistration $plan) } catch { $message = $_.Exception.Message }
        Assert-True ($message -like '*simulated lost POST response after apply*') 'The original registration failure must remain visible.'
        Assert-True ($message -like '*ROLLBACK INCOMPLETE*') 'A failed restoration must never be reported as successful.'
        Assert-True ($message -like '*simulated delete failure*') 'The cleanup failure must be reported.'
        Assert-True ($message -like '*cccccccc-cccc-cccc-cccc-cccccccccccc*') 'The affected mapping ID must be reported.'
        Assert-True ($message -like "*$root*") 'The backup location must be reported.'
        Assert-Equal '2' @($script:FakeMappings).Count 'The fixture must retain the unresolved mapping to prove incomplete restoration.'
    }
    finally {
        $script:RollbackDeleteFailure = $false
        if (Test-Path -LiteralPath $root) { [IO.Directory]::Delete($root, $true) }
    }
}

Write-Host "PASS total=$($script:Passed.Count)"
