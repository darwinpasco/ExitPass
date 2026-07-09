# ExitPass Central PMS + POS Server Read-After-Void Verification Result v1.0

## Purpose

Record controlled non-production read-after-void verification for the already-voided POS Server fiscal document across POS Server and Central PMS read surfaces.

## Execution Timestamp

- Executed at: 2026-07-09T17:30:54Z
- Local time: 2026-07-10 01:30 Asia/Manila
- Facade completion rerun at: 2026-07-09T17:49:45Z
- Facade completion local time: 2026-07-10 01:49 Asia/Manila

## Endpoints Read

- POS Server direct read: `GET http://localhost:5000/v1/fiscal-documents/9bdf2948-dadd-450b-8776-be688b579395`
- Central PMS fiscal issuance status read: `GET http://localhost:56065/v1/fiscal-issuance/references/14479d9a-844f-4dba-9578-e863ece93fbf`
- Operator Console facade read: `GET http://localhost:56065/v1/ops/operator-console/fiscal-issuance/references/14479d9a-844f-4dba-9578-e863ece93fbf`
- Operator Console UI route was not browser-tested in this run.

## DB Precheck

- POS Server database: `posserver_api_smoke_validation_local`
- Non-production/disposable DB name check: passed.
- Approved fixture document existed: passed.
- Fiscal document number before reads: `SI-00000002-UAT`
- Fiscal document status posture before reads: `voided`
- POS Server void status before reads: `recorded`
- Fiscal sequence value before reads: `2`
- Document count for `SI-00000002-UAT` before reads: `1`

## POS Server Read Result

- HTTP result: `200`
- Fiscal document id: `9bdf2948-dadd-450b-8776-be688b579395`
- Fiscal document number: `SI-00000002-UAT`
- Fiscal sequence value: `2`
- Fiscal document status posture: `voided`
- Void status: `recorded`
- Void reason code: `operator_error`
- Voided at: `2026-07-09T16:06:07.499917+00:00`

## Central PMS Read Result

- HTTP result: `200`
- Fiscal issuance reference id: `14479d9a-844f-4dba-9578-e863ece93fbf`
- POS Server fiscal document id: `9bdf2948-dadd-450b-8776-be688b579395`
- Fiscal document number: `SI-00000002-UAT`
- Fiscal sequence value: `2`
- POS Server document read status: `AVAILABLE`
- POS Server document status posture: `voided`
- POS Server void status: `recorded`
- POS Server void reason code: `operator_error`
- POS Server voided at: `2026-07-09T16:06:07.499917+00:00`

## Operator Console Facade Result

- Local identity fixture blocker: resolved by inserting the repo-standard manual-test operator identity `77000000-0000-0000-0000-000000000010` into only the disposable `centralpms_feq_retry_uat_local` database.
- HTTP result: `200`
- Fiscal issuance reference id: `14479d9a-844f-4dba-9578-e863ece93fbf`
- POS Server fiscal document id: `9bdf2948-dadd-450b-8776-be688b579395`
- Fiscal document number: `SI-00000002-UAT`
- Fiscal sequence value: `2`
- POS Server document read status: `AVAILABLE`
- POS Server document status posture: `voided`
- POS Server void status: `recorded`
- POS Server void reason code: `operator_error`
- POS Server voided at: `2026-07-09T16:06:07.499917+00:00`
- View-audit/action-log persistence: passed.

## Postcheck

- POS fiscal document still exists: passed.
- Fiscal document number remained `SI-00000002-UAT`.
- Fiscal sequence value remained `2`.
- Document count for `SI-00000002-UAT` remained `1`.
- New fiscal number allocated: `false`.
- ExitAuthorization count for the approved payment confirmation: `0`.
- Gate consumption count for the approved payment confirmation: `0`.
- Refund/reversal count: `0`.
- Operator fiscal status view action-log success count for the approved correlation/reference: `2`.

## Evidence

- Evidence path: `D:\ExitPass-UAT-Evidence\CPS-POS-UAT-20260709-DEV-ATC-001`
- Evidence prefix: `controlled-posserver-fiscal-uat-CPS-POS-UAT-20260709-DEV-ATC-001-read-after-void`
- Final facade evidence prefix: `controlled-posserver-fiscal-uat-CPS-POS-UAT-20260709-DEV-ATC-001-read-after-void-final`
- Runtime summary: `controlled-posserver-fiscal-uat-CPS-POS-UAT-20260709-DEV-ATC-001-read-after-void-runtime-summary.json`
- Final read summary: `controlled-posserver-fiscal-uat-CPS-POS-UAT-20260709-DEV-ATC-001-read-after-void-final-summary.json`
- Final DB postcheck: `controlled-posserver-fiscal-uat-CPS-POS-UAT-20260709-DEV-ATC-001-read-after-void-final-db-postcheck.json`
- Final SHA-256 manifest: `controlled-posserver-fiscal-uat-CPS-POS-UAT-20260709-DEV-ATC-001-read-after-void-final-sha256.json`
- SHA-256 manifest: `controlled-posserver-fiscal-uat-CPS-POS-UAT-20260709-DEV-ATC-001-read-after-void-sha256.json`

## Validation

- `dotnet test src\Services\CentralPms\tests\ExitPass.CentralPms.UnitTests\ExitPass.CentralPms.UnitTests.csproj --filter "FullyQualifiedName~FiscalIssuanceStatusReadServiceTests|FullyQualifiedName~PosServerFiscalDocumentResponseParserTests" --logger "console;verbosity=minimal"`: passed, 29 tests.
- `dotnet test tests\ExitPass.PosServer.Api.Tests\ExitPass.PosServer.Api.Tests.csproj --filter "FullyQualifiedName~FiscalDocumentReadEndpointTests|FullyQualifiedName~DigitalSalesInvoiceEndpointTests" --logger "console;verbosity=minimal"`: passed, 26 tests.
- `dotnet test tests\ExitPass.PosServer.Runtime.Tests\ExitPass.PosServer.Runtime.Tests.csproj --filter "FullyQualifiedName~FiscalDocumentReadServiceTests|FullyQualifiedName~DigitalSalesInvoiceRenderServiceTests" --logger "console;verbosity=minimal" -p:UseSharedCompilation=false`: passed, 8 tests.
- `dotnet test tests\ExitPass.PosServer.Persistence.Postgres.Tests\ExitPass.PosServer.Persistence.Postgres.Tests.csproj --filter "FullyQualifiedName~PostgresFiscalDocumentReaderTests" --logger "console;verbosity=minimal" -p:UseSharedCompilation=false`: passed, 5 tests.
- `dotnet test src\Services\CentralPms\tests\ExitPass.CentralPms.IntegrationTests\ExitPass.CentralPms.IntegrationTests.csproj --filter "FullyQualifiedName~FiscalIssuanceStatusApiAccessPolicyIntegrationTests|FullyQualifiedName~OperatorConsoleFiscalIssuanceStatusApiIntegrationTests" --logger "console;verbosity=minimal"`: passed, 13 tests.
- `dotnet build src\Services\CentralPms\src\ExitPass.CentralPms.Api\ExitPass.CentralPms.Api.csproj --no-restore`: passed.
- `dotnet build src\ExitPass.PosServer.Api\ExitPass.PosServer.Api.csproj --no-restore -p:UseSharedCompilation=false`: passed.
- Disposable local identity fixture application: passed; seeded only `centralpms_feq_retry_uat_local` with repo-standard manual-test operator identity `77000000-0000-0000-0000-000000000010`.
- Runtime Operator Console facade rerun: passed with HTTP `200`.
- `git diff --check` in `D:\SourceCodes\ExitPass`: passed with line-ending warnings only.
- `git diff --check` in `D:\SourceCodes\ExitPass-PoSServer`: passed with line-ending warnings only.

## Known Limitations

- Operator Console UI route was not browser-tested.
- POS Server direct read still includes broader fiscal document detail already present on that endpoint; Central PMS status exposes only safe void posture fields.

## Boundary Statement

This verification used local disposable/non-production databases and read-only fiscal status/read endpoints. It did not call the void endpoint again, mutate payment finality, issue ExitAuthorization, trigger gate behavior, create refund/reversal, call HikCentral, call payment providers, generate PDF/HTML/QR, introduce final BIR statutory wording, create a replacement fiscal document, or allocate a new fiscal number.

## Final Result

`passed_read_after_void_verified_across_pos_server_central_pms_and_operator_console_facade`
