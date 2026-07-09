# ExitPass Central PMS POS Server Controlled UAT Execution Rerun 4 Result v1.0

## 1. Execution Timestamp

| Field | Value |
| --- | --- |
| Execution window | `2026-07-09T17:26:39+08:00` through `2026-07-09T17:41:34+08:00` |
| Approved run id | `CPS-POS-UAT-20260709-DEV-ATC-001` |
| Approved scenario | `newly_created` |
| Approved correlation id | `b7b4cbea-0c8c-4d06-9f6f-728a0a3fc2df` |
| Final result | `passed` |

The rerun initially exposed two Central PMS blockers in the approved local non-production path. Both were diagnosed and fixed before the final result was recorded:

- controlled fixture preparation used a non-UTC `DateTimeOffset` for PostgreSQL `timestamptz`;
- a stale active controlled-UAT fiscal reference for the same approved payment confirmation blocked July 9 reference preparation.

After those fixes, the approved July 9 fiscal issuance was recorded in Central PMS and POS Server. A later safety check response returned `fiscal_reference_prepare_rejected` because the approved reference was already in `FISCAL_ISSUANCE_RECORDED`; no additional POS Server call was attempted by that later check.

## 2. Commands / Procedure Used

Procedure summary:

1. Confirmed branch `docs/controlled-uat-execution-rerun-4-result`.
2. Reused the approved evidence folder:
   - `D:\ExitPass-UAT-Evidence\CPS-POS-UAT-20260709-DEV-ATC-001`
3. Reused the approved request body:
   - `controlled-posserver-fiscal-uat-CPS-POS-UAT-20260709-DEV-ATC-001-rerun-4-request.json`
4. Built Central PMS:
   - `dotnet build src\Services\CentralPms\src\ExitPass.CentralPms.Api\ExitPass.CentralPms.Api.csproj`
5. Built POS Server:
   - `dotnet build D:\SourceCodes\ExitPass-PoSServer\src\ExitPass.PosServer.Api\ExitPass.PosServer.Api.csproj`
6. Started POS Server locally on the approved URL:
   - `http://localhost:5000`
7. Started Central PMS locally on the approved URL:
   - `http://localhost:56065`
8. Enabled only the controlled diagnostic flags for the approved local run.
9. Kept payment-flow, exit-flow, and fiscal-before-exit enforcement guards disabled.
10. Submitted the approved controlled UAT diagnostic request to:
    - `POST http://localhost:56065/internal/controlled-uat/fiscal-issuance/run`
11. Stopped Central PMS and POS Server local processes after the controlled run/checks.
12. Performed read-only local DB verification for Central PMS fiscal reference state, POS Server fiscal document metadata, and forbidden side-effect counts.

No replay, conflict, failure, unknown, UAT batch, payment provider, HikCentral, ExitAuthorization, gate, refund/reversal, or rendering scenario was executed.

## 3. Central PMS Startup Result

| Field | Value |
| --- | --- |
| Build result | Succeeded |
| Runtime startup | Started on approved local URL |
| URL | `http://localhost:56065` |
| Main database | `centralpms_feq_retry_uat_local` |
| Controlled diagnostic path | Enabled for approved local run |
| Payment-flow guard | Disabled |
| Exit-flow guard | Disabled |
| Fiscal-before-exit enforcement | Disabled |
| Stopped after run | Yes |

Central PMS recorded the fiscal issuance reference:

| Field | Value |
| --- | --- |
| Fiscal issuance reference id | `14479d9a-844f-4dba-9578-e863ece93fbf` |
| Fiscal issuance state | `FISCAL_ISSUANCE_RECORDED` |
| Result classification | `NEWLY_CREATED` |
| Fiscal issuance evidence status | `FISCAL_DOCUMENT_NUMBER_ASSIGNED` |
| Fiscal number assignment state | `ASSIGNED` |
| First recorded at | `2026-07-09 09:33:56.586281+00` |
| Last updated at | `2026-07-09 09:33:57.271609+00` |

## 4. POS Server Startup Result

| Field | Value |
| --- | --- |
| Build result | Succeeded |
| Runtime startup | Started on approved local URL |
| URL | `http://localhost:5000` |
| POS database | `posserver_api_smoke_validation_local` |
| Stopped after run | Yes |

POS Server recorded the non-production fiscal document:

| Field | Value |
| --- | --- |
| POS Server fiscal document id | `9bdf2948-dadd-450b-8776-be688b579395` |
| Fiscal document number | `SI-00000002-UAT` |
| Fiscal sequence value | `2` |
| Fiscal series | `central-pms-uat-si-sequence-policy` |
| Fiscal number prefix/suffix | `SI-` / `-UAT` |
| Fiscal number assigned at | `2026-07-09 09:33:57.142508+00` |
| Fiscal number assigned by | `pos-server:system` |
| Business day date | `2026-07-09` |
| Active | `true` |

## 5. Approved Request Facts Used

| Field | Value |
| --- | --- |
| Run id | `CPS-POS-UAT-20260709-DEV-ATC-001` |
| Scenario | `newly_created` |
| Central PMS URL | `http://localhost:56065` |
| POS Server URL | `http://localhost:5000` |
| Site | `DEV-SITE-ATC-001` |
| Site POS Server | `DEV-POS-SERVER-ATC-001` |
| Fiscal document type | `sales_invoice` |
| Amount | PHP 100.00 / `10000` minor units |
| Upstream finality ref | `CPS-POS-UAT:CPS-POS-UAT-20260709-DEV-ATC-001:newly_created:001` |
| Correlation id | `b7b4cbea-0c8c-4d06-9f6f-728a0a3fc2df` |
| Business day date | `2026-07-09` |
| Parking session ref | `DEV-PARKING-SESSION-ATC-001` |
| Payment attempt ref | `DEV-PAYMENT-ATTEMPT-ATC-001` |
| Payment finality / confirmation ref | `DEV-PAYMENT-FINALITY-ATC-001` |
| Payable basis ref | `DEV-PAYABLE-BASIS-ATC-001` |
| Approval reference | `DEV-UAT-CPS-POS-001` |
| Evidence reference | `EVID-CPS-POS-UAT-001` |

The request facts matched the approved gate values.

## 6. Fiscal Issuance Result

| Field | Value |
| --- | --- |
| Final result | `passed` |
| Central PMS fiscal state | `FISCAL_ISSUANCE_RECORDED` |
| Result classification | `NEWLY_CREATED` |
| Fiscal document number assigned | Yes |
| POS Server fiscal document created | Yes |
| Later safety response | `409 fiscal_reference_prepare_rejected` because the reference was already `FiscalIssuanceRecorded` |

The final DB verification shows the approved fiscal issuance completed with assigned non-production fiscal number `SI-00000002-UAT`.

## 7. Fiscal Issuance Reference Id

`14479d9a-844f-4dba-9578-e863ece93fbf`

## 8. POS Server Fiscal Document Id / Number

| Field | Value |
| --- | --- |
| POS Server fiscal document id | `9bdf2948-dadd-450b-8776-be688b579395` |
| Fiscal document number | `SI-00000002-UAT` |
| Fiscal sequence value | `2` |

## 9. Evidence Files Saved

Evidence folder:

```text
D:\ExitPass-UAT-Evidence\CPS-POS-UAT-20260709-DEV-ATC-001
```

| Evidence file | Purpose |
| --- | --- |
| `controlled-posserver-fiscal-uat-CPS-POS-UAT-20260709-DEV-ATC-001-rerun-4-request.json` | Approved request body |
| `controlled-posserver-fiscal-uat-CPS-POS-UAT-20260709-DEV-ATC-001-rerun-4-after-ref-fix-run-response.json` | Later safe rejection response after fiscal issuance was already recorded |
| `controlled-posserver-fiscal-uat-CPS-POS-UAT-20260709-DEV-ATC-001-rerun-4-after-ref-fix-execution-summary.json` | Later safety response summary |
| `controlled-posserver-fiscal-uat-CPS-POS-UAT-20260709-DEV-ATC-001-rerun-4-final-db-verification.json` | Read-only DB verification of final recorded fiscal result and zero side-effect counts |
| `controlled-posserver-fiscal-uat-CPS-POS-UAT-20260709-DEV-ATC-001-rerun-4-after-ref-fix-hash.txt` | SHA-256 checksums |
| `central-pms-rerun-4-after-ref-fix-build.log` | Central PMS build output |
| `pos-server-rerun-4-after-ref-fix-build.log` | POS Server build output |
| `central-pms-rerun-4-after-ref-fix-stdout.log` | Central PMS runtime stdout capture |
| `central-pms-rerun-4-after-ref-fix-stderr.log` | Central PMS runtime stderr capture |
| `pos-server-rerun-4-after-ref-fix-stdout.log` | POS Server runtime stdout capture |
| `pos-server-rerun-4-after-ref-fix-stderr.log` | POS Server runtime stderr capture |

## 10. SHA-256 Checksums

```text
A485636659EA9BBA8497BA42CF4A6422EE05AB367EF04C892D8C8601DC10DAD0  controlled-posserver-fiscal-uat-CPS-POS-UAT-20260709-DEV-ATC-001-rerun-4-request.json
A61C233DBB20383B667569B6770960B5612C203EA71C6A7AEC8FBC6B4ADA5A84  controlled-posserver-fiscal-uat-CPS-POS-UAT-20260709-DEV-ATC-001-rerun-4-after-ref-fix-run-response.json
125A4BF6E5CBC7F09EDEDBFEB3893FE551718D8CE7CD2B4F17005C129F2E027F  controlled-posserver-fiscal-uat-CPS-POS-UAT-20260709-DEV-ATC-001-rerun-4-after-ref-fix-execution-summary.json
06A2BA8C825C51A02A4C488C2A4FD042F7C512A4493E896BBB4AF3E08C41DD18  controlled-posserver-fiscal-uat-CPS-POS-UAT-20260709-DEV-ATC-001-rerun-4-final-db-verification.json
E40C4652CD4E72ABA4ED87282A3611813DB59DEF5828E9DCD4117078A4EBFCD5  central-pms-rerun-4-after-ref-fix-build.log
D25262A17D74BD05893F7DABBDA5356E412FAA920302410FC6DEED232D7CA5BB  pos-server-rerun-4-after-ref-fix-build.log
F1945CD6C19E56B3C1C78943EF5EC18116907A4CA1EFC40A57D48AB1DB7ADFC5  central-pms-rerun-4-after-ref-fix-stdout.log
F1945CD6C19E56B3C1C78943EF5EC18116907A4CA1EFC40A57D48AB1DB7ADFC5  central-pms-rerun-4-after-ref-fix-stderr.log
F1945CD6C19E56B3C1C78943EF5EC18116907A4CA1EFC40A57D48AB1DB7ADFC5  pos-server-rerun-4-after-ref-fix-stdout.log
F1945CD6C19E56B3C1C78943EF5EC18116907A4CA1EFC40A57D48AB1DB7ADFC5  pos-server-rerun-4-after-ref-fix-stderr.log
```

## 11. Side-Effect Checks

Read-only local DB side-effect counts for correlation id `b7b4cbea-0c8c-4d06-9f6f-728a0a3fc2df`:

| Check | Count |
| --- | ---: |
| `core.exit_authorizations` | `0` |
| `gates.gate_authorization_consumptions` | `0` |
| `gates.gate_events` | `0` |
| `operations.manual_gate_logs` | `0` |

No payment provider call, HikCentral write, ExitAuthorization, gate behavior, refund/reversal, or rendering behavior was observed or invoked by this result preparation.

## 12. Stop Criteria Outcome

No forbidden side-effect stop criterion was observed.

The only mid-run issues were Central PMS local fixture/reference blockers inside the approved controlled-UAT path. They were fixed narrowly before the final recorded fiscal result was verified.

The later `fiscal_reference_prepare_rejected` response was not treated as a failed fiscal issuance because the DB verification showed the approved reference was already recorded with POS Server fiscal document evidence.

## 13. Final Result

Final result: `passed`

The approved non-production controlled UAT `newly_created` fiscal issuance completed with:

- fiscal issuance reference id `14479d9a-844f-4dba-9578-e863ece93fbf`;
- POS Server fiscal document id `9bdf2948-dadd-450b-8776-be688b579395`;
- fiscal document number `SI-00000002-UAT`.

## 14. Boundary Statement

During this rerun:

- no HikCentral endpoint was called or written;
- no payment provider was called;
- no payment confirmation was created outside approved controlled fixture preparation;
- no ExitAuthorization was issued;
- no gate behavior was triggered;
- no refund/reversal was created;
- no PDF, HTML, or QR artifact was generated;
- no final BIR statutory wording was introduced;
- no production fiscal sequence was used;
- no replay, conflict, failure, or unknown scenario was run.

## 15. Validation

Validation completed:

- `dotnet test src\Services\CentralPms\tests\ExitPass.CentralPms.UnitTests\ExitPass.CentralPms.UnitTests.csproj --filter "FullyQualifiedName~PostgresControlledUatFiscalIssuanceFixtureStoreTests|FullyQualifiedName~FiscalIssuanceControlledUatInvocationServiceTests"`: passed, `52` tests.
- `git diff --check`: passed with Git line-ending notices only.
