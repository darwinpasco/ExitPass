[CmdletBinding(SupportsShouldProcess)]
param(
    [ValidateSet('Inspect', 'Apply')]
    [string] $Mode = 'Inspect',
    [string] $DatabaseContainer = 'exitpass-ist-persistent-db',
    [string] $DatabaseName = 'exitpass_ist',
    [string] $DatabaseUser = 'exitpass_ist',
    [guid] $ActorUserId,
    [guid] $CorrelationId,
    [string] $ApprovalReference
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$activeDeviceId = '7af324ce-bc3a-44ef-b3c3-5d3880b39ffe'
$unusedDraftDeviceId = '874a7af5-add6-4736-9372-0f2a0886ba67'
$terminalId = 'PITX-APT-01'
$terminalSerial = 'ACE4_2E00_653A_D952'

function Invoke-ReadOnlyQuery([string] $Sql) {
    $Sql | & docker.exe exec -i $DatabaseContainer psql -X -Atq -v ON_ERROR_STOP=1 -U $DatabaseUser -d $DatabaseName
    if ($LASTEXITCODE -ne 0) { throw 'The Central PMS database query failed.' }
}

$inspection = Invoke-ReadOnlyQuery @"
SELECT json_build_object(
  'active_device', (SELECT to_jsonb(d) FROM identity.devices d WHERE d.device_id='$activeDeviceId'),
  'unused_draft_device', (SELECT to_jsonb(d) FROM identity.devices d WHERE d.device_id='$unusedDraftDeviceId'),
  'draft_current_credentials', (SELECT count(*) FROM identity.device_credential_history WHERE device_id='$unusedDraftDeviceId' AND credential_status='ACTIVE'),
  'draft_issued_provisioning', (SELECT count(*) FROM identity.device_provisioning_credentials WHERE device_id='$unusedDraftDeviceId' AND provisioning_status='ISSUED'),
  'target_identifier_conflicts', (SELECT count(*) FROM identity.devices WHERE device_id NOT IN ('$activeDeviceId','$unusedDraftDeviceId') AND lifecycle_status NOT IN ('REVOKED','REPLACED') AND btrim(terminal_identifier)='$terminalId'),
  'target_serial_conflicts', (SELECT count(*) FROM identity.devices WHERE device_id NOT IN ('$activeDeviceId','$unusedDraftDeviceId') AND lifecycle_status NOT IN ('REVOKED','REPLACED') AND btrim(terminal_serial_number)='$terminalSerial')
)::text;
"@
$inspection
if ($Mode -eq 'Inspect') { return }

if ($ActorUserId -eq [guid]::Empty -or $CorrelationId -eq [guid]::Empty -or [string]::IsNullOrWhiteSpace($ApprovalReference)) {
    throw 'Apply requires the authorized actor, correlation ID, and approval evidence reference.'
}
if (-not $PSCmdlet.ShouldProcess("$DatabaseContainer/$DatabaseName", 'Revoke the unused draft APT registration and correct the active APT identity in place')) { return }

$sql = @"
BEGIN;
DO `$`$
DECLARE
  active_service uuid;
  draft_service uuid;
  draft_assignment uuid;
  active_site uuid;
  active_group uuid;
BEGIN
  IF NOT EXISTS (SELECT 1 FROM identity.users WHERE user_id='$ActorUserId' AND user_status='ACTIVE') THEN
    RAISE EXCEPTION 'authorized actor is unavailable';
  END IF;
  SELECT service_identity_id INTO active_service FROM identity.devices
    WHERE device_id='$activeDeviceId' AND device_type='ASSISTED_PAYMENT_TERMINAL'
      AND lifecycle_status='ACTIVE' FOR UPDATE;
  IF active_service IS NULL THEN RAISE EXCEPTION 'active APT precondition failed'; END IF;
  SELECT d.service_identity_id,a.device_assignment_id,a.site_id,a.site_group_id
    INTO draft_service,draft_assignment,active_site,active_group
    FROM identity.devices d
    JOIN sites.device_assignments a ON a.service_identity_id=d.service_identity_id AND a.unassigned_at IS NULL
    JOIN identity.service_identities s ON s.service_identity_id=d.service_identity_id
    WHERE d.device_id='$unusedDraftDeviceId' AND d.device_type='ASSISTED_PAYMENT_TERMINAL'
      AND d.lifecycle_status='REGISTERED' AND s.identity_status='DRAFT'
      AND a.assignment_status='SUSPENDED' FOR UPDATE OF d,a,s;
  IF draft_service IS NULL THEN RAISE EXCEPTION 'unused draft APT precondition failed'; END IF;
  IF EXISTS (SELECT 1 FROM identity.device_credential_history WHERE device_id='$unusedDraftDeviceId') OR
     EXISTS (SELECT 1 FROM identity.device_provisioning_credentials WHERE device_id='$unusedDraftDeviceId') THEN
    RAISE EXCEPTION 'unused draft APT has provisioning or credential evidence';
  END IF;
  IF EXISTS (SELECT 1 FROM identity.devices WHERE device_id NOT IN ('$activeDeviceId','$unusedDraftDeviceId') AND lifecycle_status NOT IN ('REVOKED','REPLACED') AND (btrim(terminal_identifier)='$terminalId' OR btrim(terminal_serial_number)='$terminalSerial')) THEN
    RAISE EXCEPTION 'approved APT identity conflicts with another current device';
  END IF;

  UPDATE identity.service_identities SET identity_status='REVOKED',revoked_at=clock_timestamp(),
    revocation_reason_code='UNUSED_DRAFT_REGISTRATION',updated_at=clock_timestamp(),updated_by_user_id='$ActorUserId',row_version=row_version+1
    WHERE service_identity_id=draft_service;
  UPDATE sites.device_assignments SET assignment_status='SUPERSEDED',assignment_reason_code='UNUSED_DRAFT_REGISTRATION',
    unassigned_at=clock_timestamp(),unassigned_by_user_id='$ActorUserId',updated_at=clock_timestamp(),updated_by_user_id='$ActorUserId',row_version=row_version+1
    WHERE device_assignment_id=draft_assignment;
  UPDATE identity.devices SET lifecycle_status='REVOKED',revoked_at=clock_timestamp(),effective_to=clock_timestamp(),
    updated_at=clock_timestamp(),updated_by_user_id='$ActorUserId',row_version=row_version+1
    WHERE device_id='$unusedDraftDeviceId';

  UPDATE identity.devices SET terminal_identifier='$terminalId',terminal_serial_number='$terminalSerial',
    updated_at=clock_timestamp(),updated_by_user_id='$ActorUserId',row_version=row_version+1
    WHERE device_id='$activeDeviceId' AND service_identity_id=active_service AND lifecycle_status='ACTIVE';
  IF NOT FOUND THEN RAISE EXCEPTION 'active APT identity update did not affect exactly one record'; END IF;

  INSERT INTO identity.device_lifecycle_history(device_lifecycle_history_id,device_id,action_code,previous_status,resulting_status,
    site_id,site_group_id,actor_user_id,reason_code,correlation_id,occurred_at)
  VALUES (gen_random_uuid(),'$unusedDraftDeviceId','REVOKE','REGISTERED','REVOKED',active_site,active_group,'$ActorUserId','UNUSED_DRAFT_REGISTRATION','$CorrelationId',clock_timestamp());
  INSERT INTO audit.audit_events(audit_event_id,event_type,event_category,event_result,event_reason_code,target_entity_type,target_entity_id,
    source_schema,source_service_name,source_channel,actor_user_id,summary,occurred_at,recorded_at,correlation_id,created_at)
  VALUES
    (gen_random_uuid(),'DeviceRevoke','CONFIGURATION_CHANGE','SUCCESS','UNUSED_DRAFT_REGISTRATION','DEVICE','$unusedDraftDeviceId','identity','ExitPass.CentralPms.Api','GOVERNED_CONFIGURATION','$ActorUserId','Revoked unused PITX APT draft registration; approval=$ApprovalReference',clock_timestamp(),clock_timestamp(),'$CorrelationId',clock_timestamp()),
    (gen_random_uuid(),'DeviceIdentityCorrect','CONFIGURATION_CHANGE','SUCCESS','APPROVED_FISCAL_IDENTITY','DEVICE','$activeDeviceId','identity','ExitPass.CentralPms.Api','GOVERNED_CONFIGURATION','$ActorUserId','Corrected active PITX APT terminal identity in place; approval=$ApprovalReference',clock_timestamp(),clock_timestamp(),'$CorrelationId',clock_timestamp());
END
`$`$;
COMMIT;
"@
$sql | & docker.exe exec -i $DatabaseContainer psql -X -v ON_ERROR_STOP=1 -U $DatabaseUser -d $DatabaseName
if ($LASTEXITCODE -ne 0) { throw 'The governed APT identity correction rolled back.' }
