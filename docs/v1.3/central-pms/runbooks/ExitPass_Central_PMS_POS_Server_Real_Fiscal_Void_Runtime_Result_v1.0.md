# ExitPass Central PMS + POS Server Real Fiscal Void Runtime Result v1.0

## Purpose

Record the controlled non-production runtime smoke for the Central PMS real POS Server fiscal document void integration.

## Execution Timestamp

- Executed at: 2026-07-09T16:06:07.6697929Z
- Local evidence timestamp: 2026-07-10 00:06 Asia/Manila

## DB Rebuild / Update Summary

- POS Server database: `posserver_api_smoke_validation_local`
- Update approach: in-place disposable local DB update to the merged POS Server dev void schema, preserving the approved fixture document id, document number, and fiscal sequence value.
- Verified schema/posture:
  - `pos.fiscal_documents` exists.
  - Real void columns exist, including `void_status`.
  - Local non-production status codes required for the smoke were present or inserted in the disposable DB.
  - Approved fixture document existed before the final smoke.
  - Fiscal sequence value before smoke: `2`.

## Endpoints

- Central PMS endpoint called: `POST http://localhost:56065/internal/controlled-uat/fiscal-issuance/void-smoke`
- POS Server endpoint called by Central PMS: `POST http://localhost:5000/v1/fiscal-documents/9bdf2948-dadd-450b-8776-be688b579395/void`

## Approved Target

- Profile id: `CPS-POS-UAT-20260709-DEV-ATC-001`
- Fiscal issuance reference id: `14479d9a-844f-4dba-9578-e863ece93fbf`
- POS Server fiscal document id: `9bdf2948-dadd-450b-8776-be688b579395`
- Fiscal document number: `SI-00000002-UAT`
- Correlation id: `b7b4cbea-0c8c-4d06-9f6f-728a0a3fc2df`

## Result

- HTTP result: `200`
- Central PMS result status: `pos_server_void_recorded`
- POS Server result classification: `newly_voided`
- POS Server void/cancellation status: `recorded`
- Fiscal document status posture after smoke: `voided`
- Fiscal sequence value before smoke: `2`
- Fiscal sequence value after smoke: `2`
- New fiscal number allocated: `false`
- Final result: `passed`

## Side-Effect Checks

- Fiscal document number remained `SI-00000002-UAT`.
- Fiscal sequence value remained `2`.
- Document count for `SI-00000002-UAT` remained `1`.
- Payment finality changed: `false`
- ExitAuthorization issued: `false`
- Gate behavior triggered: `false`
- Refund/reversal created: `false`
- HikCentral called/written: `false`
- Payment provider called: `false`
- Rendering/PDF/HTML/QR generated: `false`

## Evidence

- Evidence path: `D:\ExitPass-UAT-Evidence\CPS-POS-UAT-20260709-DEV-ATC-001`
- Runtime evidence prefix: `controlled-posserver-fiscal-uat-CPS-POS-UAT-20260709-DEV-ATC-001-real-void-runtime-after-status-fix`
- SHA-256 manifest: `controlled-posserver-fiscal-uat-CPS-POS-UAT-20260709-DEV-ATC-001-real-void-runtime-sha256.json`

## Validation

- `dotnet test src\Services\CentralPms\tests\ExitPass.CentralPms.UnitTests\ExitPass.CentralPms.UnitTests.csproj --filter "FullyQualifiedName~FiscalIssuanceControlledUatVoidSmokeServiceTests|FullyQualifiedName~PostgresControlledUatFiscalVoidSafetyGuardTests|FullyQualifiedName~PostgresControlledUatFiscalVoidSmokeStoreTests|FullyQualifiedName~PosServerFiscalDocumentResponseParserTests" --logger "console;verbosity=minimal"`: passed, 55 tests.
- `dotnet test tests\ExitPass.PosServer.Runtime.Tests\ExitPass.PosServer.Runtime.Tests.csproj --filter "FullyQualifiedName~FiscalDocumentVoidServiceTests" --logger "console;verbosity=minimal"`: passed, 10 tests.
- `dotnet test tests\ExitPass.PosServer.Api.Tests\ExitPass.PosServer.Api.Tests.csproj --filter "FullyQualifiedName~FiscalDocumentVoidEndpointTests" --logger "console;verbosity=minimal"`: passed, 10 tests.
- `dotnet build src\Services\CentralPms\src\ExitPass.CentralPms.Api\ExitPass.CentralPms.Api.csproj`: passed.
- `dotnet build src\ExitPass.PosServer.Api\ExitPass.PosServer.Api.csproj`: passed.
- `git diff --check`: passed with Git CRLF notices only.

## Boundary Statement

This smoke used only local disposable/non-production databases and the approved non-production fixture document. It did not mutate payment finality, issue ExitAuthorization, trigger gate behavior, create refund/reversal, call HikCentral, call payment providers, generate PDF/HTML/QR, introduce final BIR statutory wording, allocate a replacement fiscal document, or allocate a new fiscal number.
