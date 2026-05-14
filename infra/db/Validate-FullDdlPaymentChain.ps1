param(
    [string] $DatabaseName = "exitpass_v12_rebuild_test",
    [string] $PostgresContainer = "exitpass-postgres",
    [string] $PostgresUser = "exitpass",
    [string] $HostPort = "5433",
    [string] $ComposeFile = ".\infra\docker\docker-compose.yml",
    [string] $CentralPmsService = "central-pms",
    [switch] $SkipTests
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")
$ddlPath = Join-Path $repoRoot "ExitPass_Full_Database_Creation_DDL_v1.2.sql"
$testProject = Join-Path $repoRoot "src\Services\CentralPms\tests\ExitPass.CentralPms.IntegrationTests\ExitPass.CentralPms.IntegrationTests.csproj"
$paymentChainFilter = "FullyQualifiedName~CreatePaymentAttemptPublicApiIntegrationTests|FullyQualifiedName~CreateOrReusePaymentAttempt|FullyQualifiedName~RecordPaymentConfirmationIntegrationTests|FullyQualifiedName~FinalizePaymentAttemptIntegrationTests|FullyQualifiedName~IssueExitAuthorization|FullyQualifiedName~ConsumeExitAuthorization|FullyQualifiedName~PaymentToExitFlow"

function Invoke-PostgresSql {
    param(
        [string] $Database,
        [string] $Sql
    )

    $Sql | docker exec -i $PostgresContainer psql -v ON_ERROR_STOP=1 -U $PostgresUser -d $Database
    if ($LASTEXITCODE -ne 0) {
        throw "psql command failed for database '$Database'."
    }
}

Push-Location $repoRoot
try {
    docker exec $PostgresContainer psql -v ON_ERROR_STOP=1 -U $PostgresUser -d postgres -c "DROP DATABASE IF EXISTS $DatabaseName WITH (FORCE);"
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to drop database '$DatabaseName'."
    }

    docker exec $PostgresContainer psql -v ON_ERROR_STOP=1 -U $PostgresUser -d postgres -c "CREATE DATABASE $DatabaseName OWNER $PostgresUser;"
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to create database '$DatabaseName'."
    }

    Get-Content $ddlPath -Raw |
        docker exec -i $PostgresContainer psql -v ON_ERROR_STOP=1 -U $PostgresUser -d $DatabaseName
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to apply full DDL to '$DatabaseName'."
    }

    $routineVerificationSql = @"
SELECT n.nspname AS schema_name,
       p.proname,
       pg_get_function_identity_arguments(p.oid) AS args
  FROM pg_proc AS p
  JOIN pg_namespace AS n ON n.oid = p.pronamespace
 WHERE (n.nspname, p.proname) IN (
       ('core', 'create_or_reuse_payment_attempt'),
       ('core', 'record_payment_confirmation'),
       ('core', 'finalize_payment_attempt'),
       ('core', 'issue_exit_authorization'),
       ('core', 'consume_exit_authorization'))
 ORDER BY p.proname, args;

DO `$`$
DECLARE
    v_typed_count integer;
    v_zero_arg_count integer;
BEGIN
    SELECT COUNT(*)
      INTO v_typed_count
      FROM pg_proc AS p
      JOIN pg_namespace AS n ON n.oid = p.pronamespace
     WHERE (n.nspname, p.proname, pg_get_function_identity_arguments(p.oid)) IN (
           ('core', 'create_or_reuse_payment_attempt', 'p_parking_session_id uuid, p_tariff_snapshot_id uuid, p_payment_provider_code text, p_idempotency_key text, p_requested_by text, p_correlation_id uuid, p_now timestamp with time zone'),
           ('core', 'record_payment_confirmation', 'p_payment_attempt_id uuid, p_provider_reference text, p_provider_status text, p_requested_by text, p_correlation_id uuid, p_now timestamp with time zone'),
           ('core', 'finalize_payment_attempt', 'p_payment_attempt_id uuid, p_final_attempt_status text, p_requested_by text, p_correlation_id uuid, p_now timestamp with time zone'),
           ('core', 'issue_exit_authorization', 'p_parking_session_id uuid, p_payment_attempt_id uuid, p_requested_by uuid, p_correlation_id uuid, p_now timestamp with time zone'),
           ('core', 'consume_exit_authorization', 'p_exit_authorization_id uuid, p_requested_by uuid, p_correlation_id uuid, p_now timestamp with time zone'));

    IF v_typed_count <> 5 THEN
        RAISE EXCEPTION 'Expected 5 typed ExitPass v1.2 payment-chain routines, found %', v_typed_count;
    END IF;

    SELECT COUNT(*)
      INTO v_zero_arg_count
      FROM pg_proc AS p
      JOIN pg_namespace AS n ON n.oid = p.pronamespace
     WHERE n.nspname = 'core'
       AND p.proname IN (
           'create_or_reuse_payment_attempt',
           'record_payment_confirmation',
           'finalize_payment_attempt',
           'issue_exit_authorization',
           'consume_exit_authorization')
       AND p.pronargs = 0;

    IF v_zero_arg_count <> 0 THEN
        RAISE EXCEPTION 'Found % zero-argument payment-chain routine placeholders', v_zero_arg_count;
    END IF;
END;
`$`$;
"@
    Invoke-PostgresSql -Database $DatabaseName -Sql $routineVerificationSql

    $seedPaymentRailsSql = @"
/*
 * ExitPass v1.2 validation seed.
 *
 * BRD:
 * - 9.9 Payment Initiation
 * - 10.7.4 One Active Payment Attempt Per Session
 *
 * SDD:
 * - 6.3 Initiate Payment Attempt
 * - 9.6 Integrity Constraints and Concurrency Rules
 *
 * System Invariant:
 * - Focused payment-chain validation needs an active PHP payment rail for GCASH/QRPh requests.
 * - The canonical full DDL is schema-first; this local seed supplies only reference data required by the guard.
 */
WITH validation_service AS (
    INSERT INTO identity.service_identities (
        service_identity_id,
        service_identity_code,
        service_identity_name,
        identity_type,
        identity_status,
        owning_service_name,
        credential_type,
        effective_from,
        created_at,
        created_by_service_identity_id,
        updated_at,
        updated_by_service_identity_id,
        row_version
    )
    VALUES (
        '00000000-0000-0000-0000-000000001201'::uuid,
        'ddl-rebuild-validation',
        'DDL Rebuild Validation',
        'DEVICE',
        'ACTIVE',
        'ExitPass.Validation',
        'NONE',
        now() - interval '1 minute',
        now(),
        '00000000-0000-0000-0000-000000001201'::uuid,
        now(),
        '00000000-0000-0000-0000-000000001201'::uuid,
        1
    )
    ON CONFLICT (service_identity_id) DO UPDATE
    SET identity_status = 'ACTIVE',
        updated_at = now(),
        updated_by_service_identity_id = EXCLUDED.updated_by_service_identity_id,
        row_version = identity.service_identities.row_version + 1
    RETURNING service_identity_id
)
INSERT INTO payments.payment_rails (
    payment_rail_id,
    rail_code,
    rail_name,
    provider_code,
    rail_type,
    supported_currency_code,
    rail_status,
    is_primary,
    is_fallback,
    provider_profile_ref,
    configuration_ref,
    effective_from,
    created_at,
    created_by_service_identity_id,
    updated_at,
    updated_by_service_identity_id,
    row_version
)
SELECT
    '00000000-0000-0000-0000-000000001202'::uuid,
    'PAYMONGO_QRPH_DEV',
    'PayMongo QRPh Development',
    'PAYMONGO',
    'QRPH',
    'PHP',
    'ACTIVE',
    true,
    false,
    'paymongo-dev',
    'payment-rail/paymongo/qrph/dev',
    now() - interval '1 minute',
    now(),
    service_identity_id,
    now(),
    service_identity_id,
    1
FROM validation_service
ON CONFLICT (payment_rail_id) DO UPDATE
SET rail_code = EXCLUDED.rail_code,
    rail_name = EXCLUDED.rail_name,
    provider_code = EXCLUDED.provider_code,
    rail_type = EXCLUDED.rail_type,
    supported_currency_code = EXCLUDED.supported_currency_code,
    rail_status = EXCLUDED.rail_status,
    is_primary = EXCLUDED.is_primary,
    is_fallback = EXCLUDED.is_fallback,
    provider_profile_ref = EXCLUDED.provider_profile_ref,
    configuration_ref = EXCLUDED.configuration_ref,
    effective_from = EXCLUDED.effective_from,
    updated_at = now(),
    updated_by_service_identity_id = EXCLUDED.updated_by_service_identity_id,
    row_version = payments.payment_rails.row_version + 1;
"@
    Invoke-PostgresSql -Database $DatabaseName -Sql $seedPaymentRailsSql

    if (-not $SkipTests) {
        $env:POSTGRES_DB = $DatabaseName
        docker compose -f $ComposeFile up -d --force-recreate --no-deps $CentralPmsService
        if ($LASTEXITCODE -ne 0) {
            throw "Failed to recreate '$CentralPmsService' for '$DatabaseName'."
        }

        $connectionString = "Host=127.0.0.1;Port=$HostPort;Database=$DatabaseName;Username=$PostgresUser;Password=change_me;Include Error Detail=true"
        $env:EXITPASS_TEST_MAIN_DB = $connectionString
        $env:EXITPASS_INTEGRATION_DB = $connectionString
        $env:EXITPASS_TEST_DB_CONNECTION_STRING = $connectionString
        $env:ConnectionStrings__MainDatabase = $connectionString

        dotnet test $testProject --filter $paymentChainFilter --logger "console;verbosity=detailed"
        if ($LASTEXITCODE -ne 0) {
            throw "Focused payment-chain integration guard failed."
        }
    }
}
finally {
    Pop-Location
}
