# ExitPass Central PMS Fiscal Void Command Runtime Proof Result v1.0

## Purpose

This record captures the local disposable runtime proof for the Central PMS internal fiscal void command API:

`POST /internal/fiscal-issuance/references/{fiscalIssuanceReferenceId}/void`

Final result: **passed**.

## Local Environment

- Central PMS URL: `http://localhost:56065`
- POS Server URL: `http://localhost:5000`
- Central PMS DB: `centralpms_feq_retry_uat_local`
- POS Server DB: `posserver_api_smoke_validation_local`
- DB port/user: local development `5433` / `exitpass`
- Execution timestamp: `2026-07-10T11:50:45+08:00`
- Source code changed for this proof: no
- Disposable DB fixture changed for this proof: yes, local-only

## Fresh Runtime Fixture

| Field | Value |
| --- | --- |
| Fiscal issuance reference ID | `63a530e2-e4fc-428f-a7ac-8ecfcb47d261` |
| POS Server fiscal document ID | `bf5c5693-1098-4534-bb80-543a1f47f68d` |
| Fiscal document number | `SI-VOIDCMD-0001-UAT` |
| Fiscal sequence value before void | `9001` |
| Fiscal sequence value after void/replay/conflict | `9001` |
| Correlation ID | `eafe16cd-9d4e-45cb-8d41-d81dd5f287dd` |
| Idempotency key | `central-pms-fiscal-void-command-runtime-proof:20260710:SI-VOIDCMD-0001-UAT:issued` |
| Reason code | `operator_error` |
| Requested by | `central-pms-runtime-proof` |

The fixture used local disposable Central PMS and POS Server rows only. The POS Server document was set to the existing non-production `issued` status before the proof because the POS Server void endpoint only allows `issued` or `recorded` fiscal documents to transition to voided.

## Runtime Results

| Check | Result |
| --- | --- |
| Pre-void Central PMS status read | HTTP `200`; document `SI-VOIDCMD-0001-UAT`; sequence `9001`; POS read status `AVAILABLE`; status `issued` |
| Newly voided command | HTTP `200`; Central PMS status `pos_server_void_recorded`; POS classification `newly_voided`; accepted `true` |
| Idempotent replay command | HTTP `200`; Central PMS status `pos_server_void_idempotent_replay`; POS classification `idempotent_replay`; idempotent replay `true` |
| Conflict/fail-closed command | HTTP `409`; Central PMS status `pos_server_void_conflict`; POS classification `conflict`; error `fiscal_document_void_idempotency_conflict` |
| Read-after-void | HTTP `200`; POS read status `AVAILABLE`; fiscal document status `voided`; void status `recorded`; void reason code `operator_error` |
| POS Server direct read | HTTP `200`; document `SI-VOIDCMD-0001-UAT`; sequence `9001`; status `voided`; void status `recorded` |
| Document count | `1` row for `SI-VOIDCMD-0001-UAT` |

## Side-Effect Assertions

- New fiscal number allocated: false
- Fiscal sequence changed by Central PMS: false
- Payment finality changed: false
- ExitAuthorization issued: false
- Gate behavior triggered: false
- Refund/reversal created: false
- HikCentral called: false
- Payment provider called: false
- PDF/HTML/QR rendering generated: false
- Replacement fiscal document created: false

## Validation

| Command | Result |
| --- | --- |
| `dotnet test src\Services\CentralPms\tests\ExitPass.CentralPms.UnitTests\ExitPass.CentralPms.UnitTests.csproj --filter FullyQualifiedName~FiscalIssuanceVoidCommandServiceTests` | Passed: 15 / Failed: 0 |
| `dotnet build src\Services\CentralPms\src\ExitPass.CentralPms.Api\ExitPass.CentralPms.Api.csproj` | Passed |
| `git diff --check` | Passed |

## Conclusion

The Central PMS internal fiscal void command API is runtime-proven against a fresh disposable local fiscal document fixture for newly voided, idempotent replay, semantic conflict/fail-closed, and read-after-void behavior. The fiscal document number remained `SI-VOIDCMD-0001-UAT`, the fiscal sequence remained `9001`, and no unsafe side effects were observed.

Real blocker: none.
