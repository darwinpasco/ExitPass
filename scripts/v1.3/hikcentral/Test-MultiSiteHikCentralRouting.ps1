[CmdletBinding()]
param(
    [string]$EvidenceDirectory = ""
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0

if ($PSVersionTable.PSVersion.Major -lt 5) { throw 'PowerShell 5.1 or newer is required.' }

$sourceRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
$composeFile = Join-Path $sourceRoot 'tests\integration\multi-site-hikcentral\compose.yml'
$revision = (& git -C $sourceRoot rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0) { throw 'Unable to resolve source revision.' }
$invocationId = 'MSH-' + [Guid]::NewGuid().ToString('N').Substring(0, 12)
$project = ('exitpass-' + $invocationId).ToLowerInvariant()
$proofRoot = Join-Path ([IO.Path]::GetTempPath()) ('exitpass-' + $invocationId)
$utf8 = New-Object Text.UTF8Encoding($false)
$siteGroupId = '91000000-0000-0000-0000-000000000001'
$siteAId = '91000000-0000-0000-0000-00000000000a'
$siteBId = '91000000-0000-0000-0000-00000000000b'
$vendorAId = '92000000-0000-0000-0000-00000000000a'
$vendorBId = '92000000-0000-0000-0000-00000000000b'
$adapterAId = '93000000-0000-0000-0000-00000000000a'
$adapterBId = '93000000-0000-0000-0000-00000000000b'
$centralId = '12000000-0000-0000-0000-000000000002'
$result = [ordered]@{ invocationId=$invocationId; sourceRevision=$revision; succeeded=$false }

function New-RandomValue([int]$bytes = 32) {
    $buffer = New-Object byte[] $bytes
    $rng = [Security.Cryptography.RandomNumberGenerator]::Create()
    try { $rng.GetBytes($buffer) } finally { $rng.Dispose() }
    return [Convert]::ToBase64String($buffer)
}

function Write-Utf8([string]$path, [string]$content) {
    $parent = Split-Path -Parent $path
    if ($parent) { [IO.Directory]::CreateDirectory($parent) | Out-Null }
    [IO.File]::WriteAllText($path, $content, $utf8)
}

function Invoke-Compose([string[]]$arguments) {
    & docker compose --project-name $project --env-file $envFile -f $composeFile @arguments
    if ($LASTEXITCODE -ne 0) { throw "Docker Compose failed safely (exit $LASTEXITCODE)." }
}

function Wait-Http([string]$url, [int]$seconds = 180) {
    $deadline = [DateTime]::UtcNow.AddSeconds($seconds)
    do {
        try {
            $response = Invoke-WebRequest -UseBasicParsing -Uri $url -TimeoutSec 3
            if ($response.StatusCode -eq 200) { return }
        } catch { Start-Sleep -Seconds 2 }
    } while ([DateTime]::UtcNow -lt $deadline)
    throw "Readiness timeout for $url"
}

function Get-MockPassagewayCount([int]$port) {
    $journal = Invoke-RestMethod -Method Get -Uri ("http://127.0.0.1:{0}/__admin/requests" -f $port)
    return @($journal.requests | Where-Object { $_.request.url -eq '/artemis/api/vehicle/v1/parkinglot/passageway/record' }).Count
}

function New-MockMappings([string]$directory, [string]$appKey, [string]$lot, [string]$guid, [string]$card, [string]$plate) {
    $mappingDirectory = Join-Path $directory 'mappings'
    [IO.Directory]::CreateDirectory($mappingDirectory) | Out-Null
    $success = @{
        priority = 1
        request = @{
            method = 'POST'
            urlPath = '/artemis/api/vehicle/v1/parkinglot/passageway/record'
            headers = @{ 'X-Ca-Key' = @{ equalTo = $appKey }; 'X-Ca-Signature' = @{ matches = '.+' } }
        }
        response = @{
            status = 200
            headers = @{ 'Content-Type' = 'application/json' }
            jsonBody = @{ code='0'; msg='Success'; data=@{ total=1; pageIndex=1; pageSize=100; list=@(@{
                guid=$guid; parkingLotInfo=@{parkingLotIndexCode=$lot;parkingLotName="IST $lot"};
                passagewayInfo=@{passagewayIndexCode="PW-$lot";passagewayName="Entry $lot"};
                laneInfo=@{laneIndexCode="LN-$lot";laneName="Lane $lot";direction='ENTRY'};
                personInfo=@{cardNum=$card}; carInfo=@{plateLicense=$plate;EnterTime='2026-08-20T10:00:00+08:00'};
                allowType='1';allowResult='1'
            })} }
        }
    }
    $fee = @{
        priority=1; request=@{method='POST';urlPath='/artemis/api/vehicle/v1/parkingfee/calculate';headers=@{'X-Ca-Key'=@{equalTo=$appKey};'X-Ca-Signature'=@{matches='.+'}}};
        response=@{status=200;headers=@{'Content-Type'='application/json'};jsonBody=@{code='0';msg='Success';data=@{plateLicense=$plate;cardNum=$card;parkingInTime='2026-08-20T10:00:00+08:00';parkingDuration=60;feeRuleType=0;feeRuleIndexCode='IST-RULE';feeRuleName='IST Rule';fee='25.00'}}}
    }
    $confirm = @{
        priority=1; request=@{method='POST';urlPath='/artemis/api/vehicle/v1/parkingfee/confirm';headers=@{'X-Ca-Key'=@{equalTo=$appKey};'X-Ca-Signature'=@{matches='.+'}}};
        response=@{status=200;headers=@{'Content-Type'='application/json'};jsonBody=@{code='0';msg='Success';data=@{fee='25.00';feeTime='2026-08-20T10:01:00+08:00'}}}
    }
    $authFailure = @{
        priority=10; request=@{method='POST';urlPathPattern='/artemis/api/vehicle/v1/.*'};
        response=@{status=200;headers=@{'Content-Type'='application/json'};jsonBody=@{code='401';msg='Authentication failed';data=$null}}
    }
    Write-Utf8 (Join-Path $mappingDirectory '01-passageway.json') ($success | ConvertTo-Json -Depth 20)
    Write-Utf8 (Join-Path $mappingDirectory '02-fee.json') ($fee | ConvertTo-Json -Depth 20)
    Write-Utf8 (Join-Path $mappingDirectory '03-confirm.json') ($confirm | ConvertTo-Json -Depth 20)
    Write-Utf8 (Join-Path $mappingDirectory '99-auth-failure.json') ($authFailure | ConvertTo-Json -Depth 20)
}

try {
    & docker version --format '{{.Server.Version}}' | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Docker is unavailable.' }
    [IO.Directory]::CreateDirectory($proofRoot) | Out-Null
    if ([string]::IsNullOrWhiteSpace($EvidenceDirectory)) {
        $EvidenceDirectory = Join-Path ([IO.Path]::GetTempPath()) ('exitpass-multisite-evidence-' + $invocationId)
    }
    [IO.Directory]::CreateDirectory($EvidenceDirectory) | Out-Null

    $databasePassword = New-RandomValue
    $rabbitPassword = New-RandomValue
    $totpKey = New-RandomValue
    $appKeyA = 'IST-A-' + [Guid]::NewGuid().ToString('N')
    $appKeyB = 'IST-B-' + [Guid]::NewGuid().ToString('N')
    $appSecretA = New-RandomValue
    $appSecretB = New-RandomValue
    $centralKeyA = New-RandomValue
    $centralKeyB = New-RandomValue

    foreach ($name in @('adapter-a','adapter-b','central')) { [IO.Directory]::CreateDirectory((Join-Path $proofRoot "secrets/$name")) | Out-Null }
    Write-Utf8 (Join-Path $proofRoot 'secrets/adapter-a/app-key') $appKeyA
    Write-Utf8 (Join-Path $proofRoot 'secrets/adapter-a/app-secret') $appSecretA
    Write-Utf8 (Join-Path $proofRoot 'secrets/adapter-a/central-key') $centralKeyA
    Write-Utf8 (Join-Path $proofRoot 'secrets/adapter-b/app-key') $appKeyB
    Write-Utf8 (Join-Path $proofRoot 'secrets/adapter-b/app-secret') $appSecretB
    Write-Utf8 (Join-Path $proofRoot 'secrets/adapter-b/central-key') $centralKeyB
    Write-Utf8 (Join-Path $proofRoot 'secrets/central/adapter-a.key') $centralKeyA
    Write-Utf8 (Join-Path $proofRoot 'secrets/central/adapter-b.key') $centralKeyB
    New-MockMappings (Join-Path $proofRoot 'mock-a') $appKeyA 'A-LOT' 'A0000000-0000-0000-0000-000000000001' 'IST-CARD-A' 'IST-PLATE-A'
    New-MockMappings (Join-Path $proofRoot 'mock-b') $appKeyB 'B-LOT' 'B0000000-0000-0000-0000-000000000001' 'IST-CARD-B' 'IST-PLATE-B'

    $fixtureSql = @"
BEGIN;
INSERT INTO identity.service_identities (service_identity_id,service_identity_code,service_identity_name,identity_type,identity_status,owning_service_name,credential_type,effective_from)
VALUES ('$adapterAId','ist-site-adapter-a','IST Site Adapter A','ADAPTER','ACTIVE','site-integration-adapter','NONE','2026-01-01T00:00:00Z'),
       ('$adapterBId','ist-site-adapter-b','IST Site Adapter B','ADAPTER','ACTIVE','site-integration-adapter','NONE','2026-01-01T00:00:00Z');
INSERT INTO sites.site_groups (site_group_id,site_group_code,site_group_name,business_label,timezone_name,default_currency_code,site_group_status,public_lookup_enabled,default_payment_enabled,effective_from)
VALUES ('$siteGroupId','IST_MULTI_SITE_GROUP','IST Multi-Site Group','IST','Asia/Manila','PHP','ACTIVE',false,false,'2026-01-01T00:00:00Z');
INSERT INTO sites.sites (site_id,site_group_id,site_code,site_name,site_type,timezone_name,country_code,site_status,public_lookup_enabled,payment_enabled,effective_from)
VALUES ('$siteAId','$siteGroupId','IST_SITE_A','IST Site A','MIXED_USE_PROPERTY','Asia/Manila','PH','ACTIVE',false,false,'2026-01-01T00:00:00Z'),
       ('$siteBId','$siteGroupId','IST_SITE_B','IST Site B','MIXED_USE_PROPERTY','Asia/Manila','PH','ACTIVE',false,false,'2026-01-01T00:00:00Z');
INSERT INTO integration.vendor_systems (vendor_system_id,vendor_code,vendor_name,vendor_system_type,vendor_system_status,environment_code,base_url_ref,api_version,effective_from)
VALUES ('$vendorAId','IST_HIKCENTRAL_A','IST HikCentral A','VENDOR_PMS','ACTIVE','IST','http://adapter-a:8080','v3.1.0','2026-01-01T00:00:00Z'),
       ('$vendorBId','IST_HIKCENTRAL_B','IST HikCentral B','VENDOR_PMS','ACTIVE','IST','http://adapter-b:8080','v3.1.0','2026-01-01T00:00:00Z');
INSERT INTO integration.integration_credential_references (integration_credential_reference_id,vendor_system_id,service_identity_id,credential_code,credential_name,credential_type,secret_store_type,secret_reference,credential_status,created_at)
VALUES ('94000000-0000-0000-0000-00000000000a','$vendorAId','$centralId','IST_ADAPTER_A_KEY','IST Adapter A Key','API_KEY_REFERENCE','OTHER','file:adapter-a.key','ACTIVE','2026-01-01T00:00:00Z'),
       ('94000000-0000-0000-0000-00000000000b','$vendorBId','$centralId','IST_ADAPTER_B_KEY','IST Adapter B Key','API_KEY_REFERENCE','OTHER','file:adapter-b.key','ACTIVE','2026-01-01T00:00:00Z');
INSERT INTO integration.vendor_endpoints (vendor_endpoint_id,vendor_system_id,endpoint_code,endpoint_name,endpoint_type,http_method,path_template,credential_reference_id,endpoint_status,effective_from)
VALUES ('95000000-0000-0000-0000-00000000000a','$vendorAId','SITE_ADAPTER_API','IST Adapter A API','OTHER','POST','/v1/vendor/*','94000000-0000-0000-0000-00000000000a','ACTIVE','2026-01-01T00:00:00Z'),
       ('95000000-0000-0000-0000-00000000000b','$vendorBId','SITE_ADAPTER_API','IST Adapter B API','OTHER','POST','/v1/vendor/*','94000000-0000-0000-0000-00000000000b','ACTIVE','2026-01-01T00:00:00Z');
INSERT INTO integration.adapter_mappings (adapter_mapping_id,vendor_system_id,mapping_type,site_group_id,site_id,vendor_object_type,vendor_object_ref,vendor_object_name,mapping_status,mapping_confidence,effective_from)
VALUES ('96000000-0000-0000-0000-00000000000a','$vendorAId','SITE','$siteGroupId','$siteAId','SITE_ADAPTER','$adapterAId','IST Adapter A','ACTIVE','MANUAL_APPROVED','2026-01-01T00:00:00Z'),
       ('96000000-0000-0000-0000-00000000000b','$vendorBId','SITE','$siteGroupId','$siteBId','SITE_ADAPTER','$adapterBId','IST Adapter B','ACTIVE','MANUAL_APPROVED','2026-01-01T00:00:00Z');
INSERT INTO sessions.vendor_session_projection_sync_targets (projection_sync_target_id,site_id,site_group_id,vendor_system_id,parking_lot_index_code,parking_lot_name,enabled_flag,poll_interval_seconds,lookback_window_minutes,page_size,health_status,failure_count,created_at,updated_at)
VALUES ('97000000-0000-0000-0000-00000000000a','$siteAId','$siteGroupId','$vendorAId','A-LOT','IST A',true,60,60,100,'UNKNOWN',0,now(),now()),
       ('97000000-0000-0000-0000-00000000000b','$siteBId','$siteGroupId','$vendorBId','B-LOT','IST B',true,60,60,100,'UNKNOWN',0,now(),now());
COMMIT;
"@
    Write-Utf8 (Join-Path $proofRoot 'sql/06-two-site-fixture.sql') $fixtureSql

    $basePort = Get-Random -Minimum 20000 -Maximum 26000
    $envValues = [ordered]@{
        COMPOSE_PROJECT_NAME=$project; INVOCATION_ID=$invocationId; SOURCE_REVISION=$revision;
        SOURCE_ROOT=($sourceRoot -replace '\\','/'); PROOF_ROOT=($proofRoot -replace '\\','/'); IMAGE_PREFIX=$project;
        DATABASE_NAME=('exitpass_multisite_' + $invocationId.ToLowerInvariant().Replace('-','_')); DATABASE_USER='exitpass_ist'; DATABASE_PASSWORD=$databasePassword;
        RABBIT_USER='exitpass_ist'; RABBIT_PASSWORD=$rabbitPassword; TOTP_KEY=$totpKey;
        SITE_GROUP_ID=$siteGroupId; SITE_A_ID=$siteAId; SITE_B_ID=$siteBId; VENDOR_A_ID=$vendorAId; VENDOR_B_ID=$vendorBId;
        ADAPTER_A_ID=$adapterAId; ADAPTER_B_ID=$adapterBId; CENTRAL_ID=$centralId;
        MOCK_A_PORT=$basePort; MOCK_B_PORT=($basePort+1); ADAPTER_A_PORT=($basePort+2); ADAPTER_B_PORT=($basePort+3); CENTRAL_PORT=($basePort+4)
    }
    $envFile = Join-Path $proofRoot '.env'
    Write-Utf8 $envFile (($envValues.GetEnumerator() | ForEach-Object { "{0}={1}" -f $_.Key,$_.Value }) -join "`n")

    Invoke-Compose @('up','--build','-d')
    Wait-Http ("http://127.0.0.1:{0}/health/ready" -f $envValues.ADAPTER_A_PORT)
    Wait-Http ("http://127.0.0.1:{0}/health/ready" -f $envValues.ADAPTER_B_PORT)
    Wait-Http ("http://127.0.0.1:{0}/health/live" -f $envValues.CENTRAL_PORT) 240

    $query = "SELECT site_id, vendor_system_id, source_adapter_identity_id, card_num, plate_license FROM sessions.vendor_session_projections ORDER BY site_id; SELECT projection_sync_target_id, health_status, last_success_at IS NOT NULL FROM sessions.vendor_session_projection_sync_targets ORDER BY projection_sync_target_id;"
    $projectionDeadline = [DateTime]::UtcNow.AddSeconds(180)
    do {
        $databaseEvidence = (& docker compose --project-name $project --env-file $envFile -f $composeFile exec -T postgres psql -v ON_ERROR_STOP=1 -U $envValues.DATABASE_USER -d $envValues.DATABASE_NAME -At -c $query) -join "`n"
        if ($LASTEXITCODE -ne 0) { throw 'Projection database verification failed.' }
        if ($databaseEvidence -match 'IST-CARD-A' -and $databaseEvidence -match 'IST-CARD-B' -and
            ([regex]::Matches($databaseEvidence, '\|HEALTHY\|t').Count -eq 2)) { break }
        Start-Sleep -Seconds 2
    } while ([DateTime]::UtcNow -lt $projectionDeadline)
    if ($databaseEvidence -notmatch 'IST-CARD-A' -or $databaseEvidence -notmatch 'IST-CARD-B' -or
        $databaseEvidence -notmatch 'IST-PLATE-A' -or $databaseEvidence -notmatch 'IST-PLATE-B' -or
        ([regex]::Matches($databaseEvidence, '\|HEALTHY\|t').Count -ne 2)) {
        throw 'Two-Site projection or target health did not reach the expected state.'
    }

    $beforeA = Get-MockPassagewayCount $envValues.MOCK_A_PORT
    $beforeB = Get-MockPassagewayCount $envValues.MOCK_B_PORT
    $wrongContext = @{
        plateNumber=$null; ticketReference='IST-CARD-B'; correlationId=[Guid]::NewGuid();
        context=@{siteId=$siteBId;siteGroupId=$siteGroupId;vendorSystemId=$vendorBId;adapterIdentityId=$adapterBId}
    } | ConvertTo-Json -Depth 5
    $crossStatus = 0
    try {
        Invoke-WebRequest -UseBasicParsing -Method Post -Uri ("http://127.0.0.1:{0}/v1/vendor/sessions/resolve" -f $envValues.ADAPTER_A_PORT) `
            -Headers @{'X-ExitPass-Service-Identity'=$centralId;'X-ExitPass-Adapter-Key'=$centralKeyA;'X-Correlation-Id'='cross-site-proof'} `
            -ContentType 'application/json' -Body $wrongContext | Out-Null
        throw 'Cross-Site request unexpectedly succeeded.'
    } catch {
        if ($null -ne $_.Exception.Response -and $null -ne $_.Exception.Response.StatusCode) {
            $crossStatus = [int]$_.Exception.Response.StatusCode
        } else {
            throw
        }
    }
    if ($crossStatus -ne 409 -or (Get-MockPassagewayCount $envValues.MOCK_A_PORT) -ne $beforeA) {
        throw 'Cross-Site rejection did not fail before HikCentral I/O.'
    }

    $replayDeadline = [DateTime]::UtcNow.AddSeconds(120)
    do {
        Start-Sleep -Seconds 5
        $afterA = Get-MockPassagewayCount $envValues.MOCK_A_PORT
        $afterB = Get-MockPassagewayCount $envValues.MOCK_B_PORT
    } while (($afterA -le $beforeA -or $afterB -le $beforeB) -and [DateTime]::UtcNow -lt $replayDeadline)
    $projectionCount = (& docker compose --project-name $project --env-file $envFile -f $composeFile exec -T postgres psql -U $envValues.DATABASE_USER -d $envValues.DATABASE_NAME -At -c 'SELECT count(*) FROM sessions.vendor_session_projections;').Trim()
    if ($projectionCount -ne '2' -or $afterA -le $beforeA -or $afterB -le $beforeB) {
        throw 'Projection replay did not remain idempotent or did not revisit both adapters.'
    }

    $result.succeeded = $true
    $result.projectionCount = 2
    $result.siteA = @{siteId=$siteAId;vendorSystemId=$vendorAId;adapterIdentityId=$adapterAId;card='IST-CARD-A';plate='IST-PLATE-A';mockPassagewayCalls=$afterA}
    $result.siteB = @{siteId=$siteBId;vendorSystemId=$vendorBId;adapterIdentityId=$adapterBId;card='IST-CARD-B';plate='IST-PLATE-B';mockPassagewayCalls=$afterB}
    $result.crossSiteStatus = $crossStatus
    $result.databaseEvidence = $databaseEvidence
    Write-Utf8 (Join-Path $EvidenceDirectory 'two-site-proof.json') ($result | ConvertTo-Json -Depth 10)
    $result | ConvertTo-Json -Depth 10
}
finally {
    if (Test-Path variable:envFile) {
        try { Invoke-Compose @('down','--volumes','--remove-orphans') } catch { Write-Warning $_.Exception.Message }
    }
    if (Test-Path $proofRoot) { Remove-Item -LiteralPath $proofRoot -Recurse -Force }
}
