# Permanent HikCentral Session Projection Runbook

## Purpose and authority

HikCentral passageway projection is a permanent Central PMS hosted background capability. When the scheduler is explicitly enabled and PostgreSQL contains enabled targets owned by the configured Vendor System, Central PMS polls every due target, resumes after restart, locks each target with PostgreSQL advisory locking, and commits each normalized page set atomically.

ExitPass retains one logical Central PMS. Where scale or multiple HikCentral credential boundaries require separate process configuration, those processes are scheduler-enabled replicas or workers of that same Central PMS. They are not independent Central PMS deployments, and target ownership plus PostgreSQL locking preserves their shared logical boundary.

Projection is operational continuity data only. It is not tariff, payable amount, statutory-discount, payment, fiscal, exit, or gate authority. This runbook never calls `parkingfee/calculate`, `parkingfee/confirm`, or gate APIs. Degraded resolution remains disabled.

The current local mapping is synthetic TEST SITE, not actual PITX:

| Field | Value |
| --- | --- |
| Database | `exitpass_hikcentral_local_uat` |
| Site Group | `ce000000-0000-0000-0000-000000000001` / `HIKCENTRAL_TEST_SITE_UAT_GROUP` |
| Site | `c9000000-0000-0000-0000-000000000001` / `TEST_SITE` |
| Vendor System | `31bde78a-5dfc-45c3-a1f3-e48abaf90927` / `HIKCENTRAL` / `UAT` |
| Projection target | `abe7da56-1198-4d51-901f-87e8fb7cd40d` |
| Parking lot | `1` / `TEST SITE` |
| Endpoint | `http://127.0.0.1:9019` |
| Request timezone | `Asia/Manila` |
| Poll interval | `60` seconds |

Secrets below are placeholders. Set them only in each dedicated process or the approved deployment secret store; never write them to this repository or print them.

## Terminal 1: infrastructure

Working directory:

```powershell
Set-Location D:\SourceCodes\ExitPass
```

Start the already provisioned local infrastructure and platform containers:

```powershell
docker start exitpass-postgres exitpass-rabbitmq exitpass-otel-collector exitpass-jaeger exitpass-prometheus exitpass-grafana exitpass-mock-payment-provider exitpass-session-service exitpass-payment-orchestrator exitpass-audit-event-service
docker ps --format "table {{.Names}}\t{{.Status}}\t{{.Ports}}"
```

Expected ports: PostgreSQL `5433`, RabbitMQ `5672/15672`, OTLP `4317/4318`, Jaeger `16686`, Prometheus `9090`, Grafana `3000`, Session Service `8081`, Payment Orchestrator `8082`, Audit Event Service `8083`, and the mock payment provider `8084`.

Verify:

```powershell
docker inspect --format '{{.State.Health.Status}}' exitpass-postgres
docker inspect --format '{{.State.Health.Status}}' exitpass-rabbitmq
Invoke-WebRequest -UseBasicParsing http://127.0.0.1:8081/health/ready
Invoke-WebRequest -UseBasicParsing http://127.0.0.1:8082/health/ready
Invoke-WebRequest -UseBasicParsing http://127.0.0.1:8083/health/ready
Invoke-WebRequest -UseBasicParsing http://127.0.0.1:13133/
```

Expected: `healthy` for PostgreSQL/RabbitMQ and HTTP `200` for each URL.

## Terminal 2: schema and target

Working directory:

```powershell
Set-Location D:\SourceCodes\ExitPass
$env:PGUSER = '<approved-local-database-user>'
$env:PGPASSWORD = '<approved-local-database-password>'
```

Apply and validate the idempotent projection schema, then apply the guarded TEST SITE seed:

```powershell
Get-Content -Raw .\docs\sql\HikCentralProjectionSchemaPatch.sql |
  docker exec -i -e PGPASSWORD exitpass-postgres psql -X -v ON_ERROR_STOP=1 -U $env:PGUSER -d exitpass_hikcentral_local_uat
Get-Content -Raw .\infra\db\patches\ExitPass_HikCentralProjectionSafety_v1.3.sql |
  docker exec -i -e PGPASSWORD exitpass-postgres psql -X -v ON_ERROR_STOP=1 -U $env:PGUSER -d exitpass_hikcentral_local_uat
Get-Content -Raw .\infra\db\patches\validation\Validate_HikCentralProjectionSafety_v1.3.sql |
  docker exec -i -e PGPASSWORD exitpass-postgres psql -X -v ON_ERROR_STOP=1 -U $env:PGUSER -d exitpass_hikcentral_local_uat
Get-Content -Raw .\infra\db\patches\ExitPass_MultiSiteVendorAdapterRouting_v1.3.sql |
  docker exec -i -e PGPASSWORD exitpass-postgres psql -X -v ON_ERROR_STOP=1 -U $env:PGUSER -d exitpass_hikcentral_local_uat
Get-Content -Raw .\infra\db\patches\validation\Validate_MultiSiteVendorAdapterRouting_v1.3.sql |
  docker exec -i -e PGPASSWORD exitpass-postgres psql -X -v ON_ERROR_STOP=1 -U $env:PGUSER -d exitpass_hikcentral_local_uat
Get-Content -Raw .\docs\sql\HikCentralProjectionTestSiteLocalUat.sql |
  docker exec -i -e PGPASSWORD exitpass-postgres psql -X -v ON_ERROR_STOP=1 -U $env:PGUSER -d exitpass_hikcentral_local_uat
Get-Content -Raw .\docs\sql\ValidateHikCentralProjectionTestSiteLocalUat.sql |
  docker exec -i -e PGPASSWORD exitpass-postgres psql -X -v ON_ERROR_STOP=1 -U $env:PGUSER -d exitpass_hikcentral_local_uat
```

Expected: all transactions commit except validators, which intentionally end with `ROLLBACK`; the target exists with interval `60` and no more than one target is enabled. Replay the seed once to prove idempotency.

List enabled targets:

```powershell
docker exec -e PGPASSWORD exitpass-postgres psql -X -U $env:PGUSER -d exitpass_hikcentral_local_uat -c "SELECT projection_sync_target_id,site_group_id,site_id,vendor_system_id,parking_lot_index_code,enabled_flag,poll_interval_seconds,health_status,last_attempt_at,last_success_at FROM sessions.vendor_session_projection_sync_targets ORDER BY projection_sync_target_id;"
```

Enable only the approved target after endpoint and credential checks pass:

```powershell
docker exec -e PGPASSWORD exitpass-postgres psql -X -v ON_ERROR_STOP=1 -U $env:PGUSER -d exitpass_hikcentral_local_uat -c "BEGIN; DO `$`$ BEGIN IF (SELECT count(*) FROM sessions.vendor_session_projection_sync_targets WHERE enabled_flag AND projection_sync_target_id <> 'abe7da56-1198-4d51-901f-87e8fb7cd40d') <> 0 THEN RAISE EXCEPTION 'Unexpected enabled target'; END IF; END `$`$; UPDATE sessions.vendor_session_projection_sync_targets SET enabled_flag=true,health_status=CASE WHEN health_status='DISABLED' THEN 'UNKNOWN' ELSE health_status END,updated_at=clock_timestamp(),row_version=row_version+1 WHERE projection_sync_target_id='abe7da56-1198-4d51-901f-87e8fb7cd40d' AND site_group_id='ce000000-0000-0000-0000-000000000001' AND site_id='c9000000-0000-0000-0000-000000000001' AND vendor_system_id='31bde78a-5dfc-45c3-a1f3-e48abaf90927' AND parking_lot_index_code='1' AND poll_interval_seconds=60; COMMIT;"
```

## Terminal 3: permanent Central PMS scheduler worker

Working directory:

```powershell
Set-Location D:\SourceCodes\ExitPass
```

Set process-scoped deployment configuration. `MANAGED_DEPLOYMENT` is the normal non-interactive deployment mode; do not set the `HikCentralLocal` launch marker.

```powershell
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:ASPNETCORE_URLS = 'http://127.0.0.1:8188'
$env:ConnectionStrings__MainDatabase = 'Host=127.0.0.1;Port=5433;Database=exitpass_hikcentral_local_uat;Username=<approved-local-database-user>;Password=<approved-local-database-password>'
$env:CentralPms__VendorPms__Provider = 'SITE_ADAPTER'
$env:CentralPms__VendorPms__Environment = 'Development'
$env:CentralPms__VendorPms__CentralPmsServiceIdentityId = '12000000-0000-0000-0000-000000000002'
$env:CentralPms__VendorPms__AdapterSecretMountRoot = 'D:\ExitPass.local\site-adapter-secrets'
$env:CentralPms__VendorPms__AllowTaskOwnedHttp = 'false'
$env:CentralPms__VendorSessionProjections__SchedulerEnabled = 'true'
$env:CentralPms__VendorSessionProjections__RequiredForEnvironment = 'true'
$env:CentralPms__VendorSessionProjections__ActivationMode = 'MANAGED_DEPLOYMENT'
$env:CentralPms__VendorSessionProjections__ActivationEnvironment = 'Development'
$env:CentralPms__VendorSessionProjections__ManagedDeploymentApproved = 'true'
$env:CentralPms__VendorSessionProjections__ExpectedDatabaseName = 'exitpass_hikcentral_local_uat'
$env:CentralPms__VendorSessionProjections__AllowNonLoopbackDatabase = 'false'
$env:CentralPms__VendorSessionProjections__AllowProductionEndpoint = 'false'
$env:CentralPms__VendorSessionProjections__DefaultPollIntervalSeconds = '60'
$env:CentralPms__VendorSessionProjections__NormalFreshnessTargetSeconds = '60'
$env:CentralPms__VendorSessionProjections__MaxProjectionAgeMinutes = '1'
$env:CentralPms__VendorSessionProjections__DegradedResolveFallbackEnabled = 'false'
$env:VENDOR_PMS_CONFIRM_PAYMENT_ENABLED = 'false'
dotnet run --project .\src\Services\CentralPms\src\ExitPass.CentralPms.Api\ExitPass.CentralPms.Api.csproj -c Release --no-launch-profile --urls http://127.0.0.1:8188
```

Expected listener: `http://127.0.0.1:8188`. Verify from another terminal:

```powershell
Invoke-WebRequest -UseBasicParsing http://127.0.0.1:8188/health/live
Invoke-WebRequest -UseBasicParsing http://127.0.0.1:8188/health/ready
```

Expected: HTTP `200`. Scheduler logs show only `/artemis/api/vehicle/v1/parkinglot/passageway/record`, application code `0`, and target `abe7da56-1198-4d51-901f-87e8fb7cd40d`. They must not contain secrets, signatures, authorization headers, or raw bodies.

## Terminal 4: WebPay

Working directory and command:

```powershell
Set-Location D:\SourceCodes\ExitPass\src\Services\WebPayUi
$env:VITE_WEBPAY_API_PROXY_TARGET = 'http://127.0.0.1:8082'
$env:VITE_WEBPAY_DEFAULT_SITE_GROUP_ID = 'ce000000-0000-0000-0000-000000000001'
$env:VITE_WEBPAY_DEFAULT_SITE_ID = 'c9000000-0000-0000-0000-000000000001'
$env:VITE_WEBPAY_DEFAULT_VENDOR_SYSTEM_ID = '31bde78a-5dfc-45c3-a1f3-e48abaf90927'
cmd /c npm ci
cmd /c npm run dev
```

Expected URL: `http://127.0.0.1:5174`. Verify `Invoke-WebRequest -UseBasicParsing http://127.0.0.1:5174`; expected HTTP `200`. WebPay continues using Payment Orchestrator contracts and does not read projection tables.

## Terminal 5: Operator Console

```powershell
Set-Location D:\SourceCodes\ExitPass\src\Services\OperatorConsoleUi
$env:VITE_OPERATOR_CONSOLE_API_PROXY_TARGET = 'http://127.0.0.1:8188'
cmd /c npm ci
cmd /c npm run dev
```

Expected URL: `http://127.0.0.1:5175`. Verify `Invoke-WebRequest -UseBasicParsing http://127.0.0.1:5175`; expected HTTP `200`. Projection health is `/operator-console/vendor-session-projections/health` and requires the existing operator RBAC session.

## Terminal 6: APT services and desktop

`D:\SourceCodes\ExitPass-APT` contains an older ExitPass service tree, not a separately required APT backend executable. The current terminal product is `D:\SourceCodes\ExitPass-AssistedPaymentTerminal` and its required backends are Central PMS plus POS Server for fiscal actions. Do not start a duplicate Central PMS from `ExitPass-APT`.

Start the Vite UI:

```powershell
Set-Location D:\SourceCodes\ExitPass-AssistedPaymentTerminal
cmd /c npm ci
cmd /c npm run app:dev
```

Expected URL: `http://127.0.0.1:5173`; verify with `Invoke-WebRequest -UseBasicParsing http://127.0.0.1:5173`.

Start the WPF shell in a separate terminal after a dedicated APT service identity has been provisioned. The inspected local database has no APT service identity, so do not substitute `CENTRAL_PMS_API` or invent one.

```powershell
Set-Location D:\SourceCodes\ExitPass-AssistedPaymentTerminal
$env:APT_PROFILE = 'CASHIER_ASSISTED_TERMINAL'
$env:APT_WEB_UI_URL = 'http://127.0.0.1:5173'
$env:CENTRAL_PMS_BASE_URL = 'http://127.0.0.1:8188'
$env:APT_SITE_GROUP_ID = 'ce000000-0000-0000-0000-000000000001'
$env:APT_SITE_ID = 'c9000000-0000-0000-0000-000000000001'
$env:APT_POS_SERVER_ID = 'POS-DEV-001'
$env:APT_CENTRAL_PMS_SERVICE_IDENTITY_ID = '<provisioned-APT-service-identity-uuid>'
dotnet run --project .\src\AssistedPaymentTerminal.Desktop\AssistedPaymentTerminal.Desktop.csproj
```

Expected result: the Windows desktop opens the Vite URL. The placeholder is a required governed identity gap, not a projection dependency.

## Terminal 7: POS Server

```powershell
Set-Location D:\SourceCodes\ExitPass-PoSServer
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:ASPNETCORE_URLS = 'http://127.0.0.1:8090'
$env:ConnectionStrings__PosServer = 'Host=127.0.0.1;Port=5433;Database=<approved-POS-database>;Username=<approved-POS-user>;Password=<approved-POS-password>'
dotnet run --project .\src\ExitPass.PosServer.Api\ExitPass.PosServer.Api.csproj -c Release
```

Expected listener: `127.0.0.1:8090`. This repository currently defines no health endpoint. Verify the process without invoking fiscal commands:

```powershell
Get-NetTCPConnection -LocalPort 8090 -State Listen
```

Expected: exactly one listener. Do not use a fiscal mutation endpoint as a health probe.

## Projection verification

With Terminal 2's `PGUSER`/`PGPASSWORD` still process-scoped:

```powershell
docker exec -e PGPASSWORD exitpass-postgres psql -X -U $env:PGUSER -d exitpass_hikcentral_local_uat -c "SELECT projection_sync_target_id,enabled_flag,poll_interval_seconds,health_status,last_attempt_at,last_success_at,last_failure_at,failure_count,lock_contention_count FROM sessions.vendor_session_projection_sync_targets WHERE projection_sync_target_id='abe7da56-1198-4d51-901f-87e8fb7cd40d';"
docker exec -e PGPASSWORD exitpass-postgres psql -X -U $env:PGUSER -d exitpass_hikcentral_local_uat -c "SELECT count(*) AS projected_rows,count(card_num) AS card_rows,count(plate_license) AS usable_plate_rows,max(last_refreshed_at) AS latest_refresh FROM sessions.vendor_session_projections WHERE site_id='c9000000-0000-0000-0000-000000000001' AND site_group_id='ce000000-0000-0000-0000-000000000001' AND vendor_system_id='31bde78a-5dfc-45c3-a1f3-e48abaf90927' AND parking_lot_index_code='1';"
docker exec -e PGPASSWORD exitpass-postgres psql -X -U $env:PGUSER -d exitpass_hikcentral_local_uat -c "SELECT card_num,plate_license,projection_status,enter_time,last_refreshed_at FROM sessions.vendor_session_projections WHERE card_num='3524357074073' AND site_id='c9000000-0000-0000-0000-000000000001';"
```

Expected: target enabled, interval `60`, health `HEALTHY`; timestamps advance; known card appears once and its unusable `Unknown` plate is SQL `NULL`. Valid plates remain indexed. A duplicate `stable_identity_key` query must return zero rows.

Confirm target freshness through the read-only operations contract. In local Development only, the configured permission header may be used; ordinary environments require the normal authenticated operator session:

```powershell
$headers = @{ 'X-ExitPass-Permissions' = 'ops.vendor-session-projection-health.view' }
$target = Invoke-RestMethod -Headers $headers -Uri 'http://127.0.0.1:8188/v1/ops/vendor-session-projections/targets/abe7da56-1198-4d51-901f-87e8fb7cd40d?latestRecordLimit=1'
$target.target | Select-Object projectionSyncTargetId, healthStatus, freshnessClassification, freshnessAgeSeconds, lastAttemptAt, lastSuccessAt, pollIntervalSeconds
```

Expected immediately after a successful due cycle: target health `Healthy`, target freshness `CURRENT`, and poll interval `60`. Aggregate `HEALTHY` is not a substitute for this target-level freshness check.

## Disable, recover, and re-enable

Disable only the approved target; projection rows are preserved:

```powershell
docker exec -e PGPASSWORD exitpass-postgres psql -X -v ON_ERROR_STOP=1 -U $env:PGUSER -d exitpass_hikcentral_local_uat -c "UPDATE sessions.vendor_session_projection_sync_targets SET enabled_flag=false,health_status='DISABLED',updated_at=clock_timestamp(),row_version=row_version+1 WHERE projection_sync_target_id='abe7da56-1198-4d51-901f-87e8fb7cd40d';"
```

Correct endpoint, credential, network, mapping, or database failures without deleting targets or projections. Re-enable with Terminal 2's guarded command. A failed cycle advances `last_attempt_at`/failure fields but preserves `last_success_at` and the last committed projections. Advisory-lock contention defers the cycle and is not success.

## Clean shutdown

Stop foreground Vite/.NET terminals with `Ctrl+C`, close the WPF application, then stop only the listed local containers:

```powershell
docker stop exitpass-audit-event-service exitpass-payment-orchestrator exitpass-session-service exitpass-mock-payment-provider exitpass-grafana exitpass-prometheus exitpass-jaeger exitpass-otel-collector exitpass-rabbitmq exitpass-postgres
```

Do not delete PostgreSQL volumes or projection rows.

## Windows reboot recovery

After Docker Desktop starts, run Terminal 1. PostgreSQL, RabbitMQ, and observability containers use `unless-stopped`; API containers without restart policies need the explicit `docker start` command. Run Terminal 3 with the same managed deployment environment. Central PMS reads the enabled target from PostgreSQL and resumes it without reseeding or an interactive acknowledgement. Re-run the projection verification and confirm `last_success_at` advances while row identities remain unique.
