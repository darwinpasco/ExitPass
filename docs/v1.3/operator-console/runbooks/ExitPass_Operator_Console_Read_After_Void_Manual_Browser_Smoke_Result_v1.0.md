# ExitPass Operator Console Read-After-Void Manual Browser Smoke Result v1.0

## 1. Scope

This records Darwin's manual browser verification of the Operator Console fiscal issuance status page after the real POS Server fiscal void and read-after-void integration.

This is a post-test manual browser smoke result record. It is not a planning document, readiness document, or design document.

## 2. Test Environment

| Item | Value |
| --- | --- |
| ExitPass repo | `D:\SourceCodes\ExitPass` |
| POS Server repo | `D:\SourceCodes\ExitPass-PoSServer` |
| POS Server URL | `http://localhost:5000` |
| Central PMS URL | `http://localhost:56065` |
| Operator Console UI URL | `http://localhost:5175/operator-console/fiscal-issuance-status` |
| Local DB port | `5433` |
| Local DB user | `exitpass` |
| Local DB password | `change_me` |
| Central PMS DB | `centralpms_feq_retry_uat_local` |
| POS Server DB | `posserver_api_smoke_validation_local` |
| POS Server base URL config used by Central PMS | `FiscalIssuance:PosServerIntegration:PosServerBaseUrl = http://localhost:5000` |

## 3. Test Input

| Item | Value |
| --- | --- |
| Fiscal issuance reference ID | `14479d9a-844f-4dba-9578-e863ece93fbf` |
| POS Server fiscal document ID | `9bdf2948-dadd-450b-8776-be688b579395` |
| Fiscal document number | `SI-00000002-UAT` |
| Correlation ID | `b7b4cbea-0c8c-4d06-9f6f-728a0a3fc2df` |

## 4. Backend Precheck Result

The following were manually verified before the browser check:

| Read surface | Result |
| --- | --- |
| POS Server direct fiscal document read | HTTP 200 |
| Central PMS fiscal issuance status read | HTTP 200 |
| Operator Console facade fiscal issuance status read | HTTP 200 |

Observed backend read-after-void fields:

| Field | Value |
| --- | --- |
| `posServerFiscalDocumentReadStatus` | `AVAILABLE` |
| `posServerFiscalDocumentStatusCodeKey` | `voided` |
| `posServerVoidStatus` | `recorded` |
| `posServerVoidReasonCode` | `operator_error` |

## 5. Browser Result

Final result: `passed`

Observed UI facts:

| Field | Observed value |
| --- | --- |
| Page | Fiscal issuance status |
| Result headline | Fiscal document voided |
| Badge | Voided |
| Safety banner | Stated the document is voided in POS Server and the view is observational only |
| Fiscal state | `FISCAL_ISSUANCE_RECORDED` |
| Sales invoice / fiscal document number | `SI-00000002-UAT` |
| Result classification | `NEWLY_CREATED` |
| Evidence status | `FISCAL_DOCUMENT_NUMBER_ASSIGNED` |
| Number assignment | `ASSIGNED` |
| First recorded | Jul 9, 2026, 5:33 PM |
| Last updated | Jul 9, 2026, 5:33 PM |
| POS Server document read status | Available |
| Fiscal document status | Voided |
| Void status | Recorded |

## 6. Support/Audit Details Observed

| Field | Observed value |
| --- | --- |
| Requested reference | `14479d9a-844f-4dba-9578-e863ece93fbf` |
| Fiscal issuance reference ID | `14479d9a-844f-4dba-9578-e863ece93fbf` |
| Upstream finality reference | `CPS-POS-UAT:CPS-POS-UAT-20260709-DEV-ATC-001:newly_created:001` |
| Payment confirmation ID | `00000000-0000-4000-8000-000000000301` |
| Payment attempt ID | `00000000-0000-4000-8000-000000000302` |
| Parking session ID | `00000000-0000-4000-8000-000000000303` |
| Site ID | `00000000-0000-4000-8000-000000000402` |
| Site POS Server ID | `10000000-0000-4000-8000-000000000201` |
| Site POS Server ref | `DEV-POS-SERVER-ATC-001` |
| POS Server fiscal document ID | `9bdf2948-dadd-450b-8776-be688b579395` |
| Fiscal document type | `sales_invoice` |
| Fiscal identity ID | `10000000-0000-4000-8000-000000000701` |
| Fiscal sequence policy ID | `10000000-0000-4000-8000-000000000803` |
| Fiscal sequence value | `2` |
| Fiscal series | `central-pms-uat-si-sequence-policy` |
| Fiscal number prefix | `SI-` |
| Fiscal number suffix | `-UAT` |
| Fiscal number assigned at | Jul 9, 2026, 5:33 PM |
| Fiscal number assigned by | `pos-server:system` |
| POS Server document read status | `AVAILABLE` |
| Fiscal document status | `voided` |
| Void status | `recorded` |
| Void reason code | `operator_error` |
| Voided at | Jul 10, 2026, 12:06 AM |
| Semantic request hash status | `AVAILABLE` |
| Semantic request hash algorithm | `SHA-256` |
| Semantic request hash fact count | `20` |
| Correlation ID | `b7b4cbea-0c8c-4d06-9f6f-728a0a3fc2df` |

## 7. Safety Assertions

No unsafe operator action was displayed:

- No retry
- No reissue
- No replacement
- No payment action
- No gate action
- No refund/reversal
- No HikCentral action
- No PDF/HTML/QR rendering action
- No final BIR action

## 8. Final Conclusion

The manual browser smoke passed. The Operator Console safely displays the POS Server read-after-void state for `SI-00000002-UAT` as observational evidence only.

## 9. Validation

| Check | Result |
| --- | --- |
| Result document path exists | Passed |
| `git diff --check` | Passed |
| Build | Not required; no source code changed |
