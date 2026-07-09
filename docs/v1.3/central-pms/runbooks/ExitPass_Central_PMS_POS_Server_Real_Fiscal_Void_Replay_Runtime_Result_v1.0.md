# ExitPass Central PMS + POS Server Real Fiscal Void Replay Runtime Result v1.0

## Purpose

Record the controlled non-production replay smoke for the Central PMS real POS Server fiscal document void integration.

## Execution Timestamp

- Executed at: 2026-07-09T16:36:26.7787502Z
- Local evidence timestamp: 2026-07-10 00:36 Asia/Manila

## Endpoints

- Central PMS endpoint called: `POST http://localhost:56065/internal/controlled-uat/fiscal-issuance/void-smoke`
- POS Server endpoint called by Central PMS: `POST http://localhost:5000/v1/fiscal-documents/9bdf2948-dadd-450b-8776-be688b579395/void`

## Approved Target

- Profile id: `CPS-POS-UAT-20260709-DEV-ATC-001`
- Fiscal issuance reference id: `14479d9a-844f-4dba-9578-e863ece93fbf`
- POS Server fiscal document id: `9bdf2948-dadd-450b-8776-be688b579395`
- Fiscal document number: `SI-00000002-UAT`
- Correlation id: `b7b4cbea-0c8c-4d06-9f6f-728a0a3fc2df`

## POS Server Disposable DB Precheck

- Database: `posserver_api_smoke_validation_local`
- Non-production/disposable name check: passed.
- `pos.fiscal_documents` exists: passed.
- `void_status` column exists: passed.
- Approved fixture existed before replay: passed.
- Approved fixture posture before replay: `voided`, `void_status=recorded`.
- Fiscal sequence value before replay: `2`.
- Document count for `SI-00000002-UAT` before replay: `1`.

## Result

- HTTP result: `200`
- Central PMS result status: `pos_server_void_idempotent_replay`
- POS Server result classification: `idempotent_replay`
- POS Server void/cancellation status: `recorded`
- Fiscal document status posture after replay: `voided`
- Fiscal sequence value before replay: `2`
- Fiscal sequence value after replay: `2`
- New fiscal number allocated: `false`
- Document count for `SI-00000002-UAT`: `1`
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
- Successful replay evidence prefix: `controlled-posserver-fiscal-uat-CPS-POS-UAT-20260709-DEV-ATC-001-real-void-replay-runtime-after-db-config-fix`
- SHA-256 manifest: `controlled-posserver-fiscal-uat-CPS-POS-UAT-20260709-DEV-ATC-001-real-void-replay-runtime-sha256.json`

## Validation

- `dotnet test src\Services\CentralPms\tests\ExitPass.CentralPms.UnitTests\ExitPass.CentralPms.UnitTests.csproj --filter "FullyQualifiedName~FiscalIssuanceControlledUatVoidSmokeServiceTests|FullyQualifiedName~PostgresControlledUatFiscalVoidSafetyGuardTests|FullyQualifiedName~PostgresControlledUatFiscalVoidSmokeStoreTests|FullyQualifiedName~PosServerFiscalDocumentResponseParserTests" --logger "console;verbosity=minimal"`: passed.
- `dotnet test tests\ExitPass.PosServer.Runtime.Tests\ExitPass.PosServer.Runtime.Tests.csproj --filter "FullyQualifiedName~FiscalDocumentVoidServiceTests" --logger "console;verbosity=minimal"`: passed, 10 tests.
- `dotnet test tests\ExitPass.PosServer.Api.Tests\ExitPass.PosServer.Api.Tests.csproj --filter "FullyQualifiedName~FiscalDocumentVoidEndpointTests" --logger "console;verbosity=minimal"`: passed, 10 tests.
- `dotnet build src\Services\CentralPms\src\ExitPass.CentralPms.Api\ExitPass.CentralPms.Api.csproj --no-restore`: passed with existing warnings.
- `dotnet build src\ExitPass.PosServer.Api\ExitPass.PosServer.Api.csproj --no-restore`: passed.
- `git diff --check`: passed.

## Boundary Statement

This replay smoke used only local disposable/non-production databases and the approved non-production fixture document. It did not mutate payment finality, issue ExitAuthorization, trigger gate behavior, create refund/reversal, call HikCentral, call payment providers, generate PDF/HTML/QR, introduce final BIR statutory wording, allocate a replacement fiscal document, or allocate a new fiscal number.
