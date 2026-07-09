# ExitPass Central PMS POS Server Controlled UAT Execution Rerun Result v1.0

## 1. Execution Timestamp

| Field | Value |
| --- | --- |
| Execution started | `2026-07-09T14:52:15.8432651+08:00` |
| Execution completed | `2026-07-09T14:52:18.5342705+08:00` |
| Approved gate | `docs/v1.3/central-pms/runbooks/ExitPass_Central_PMS_POS_Server_Controlled_UAT_Execution_Gate_Go_No_Go_v1.0.md` |
| Prior failed execution result | `docs/v1.3/central-pms/runbooks/ExitPass_Central_PMS_POS_Server_Controlled_UAT_Execution_Result_v1.0.md` |
| Final result | `failed` |

The approved rerun was submitted exactly once. Central PMS accepted the July 9 first-run constants, but failed safely while preparing the fiscal issuance reference. The failure occurred before diagnostic invocation and before any POS Server fiscal document call.

A preparatory local script attempt at `2026-07-09T14:49:24.6809055+08:00` stopped before sending any `/run` request because the local PowerShell script could not load `System.Net.Http.HttpClient`. That attempt had `requestSent=false`, did not call the controlled UAT endpoint, and was not treated as the approved rerun.

## 2. Commands / Procedure Used

Procedure summary:

1. Confirmed the approved evidence folder existed:
   - `D:\ExitPass-UAT-Evidence\CPS-POS-UAT-20260709-DEV-ATC-001`
2. Reused the approved request facts from the execution gate and prior failed execution record.
3. Built Central PMS:
   - `dotnet build src\Services\CentralPms\src\ExitPass.CentralPms.Api\ExitPass.CentralPms.Api.csproj`
4. Built POS Server:
   - `dotnet build D:\SourceCodes\ExitPass-PoSServer\src\ExitPass.PosServer.Api\ExitPass.PosServer.Api.csproj`
5. Started local POS Server using the approved URL:
   - `dotnet run --no-build --project D:\SourceCodes\ExitPass-PoSServer\src\ExitPass.PosServer.Api\ExitPass.PosServer.Api.csproj --urls http://localhost:5000`
6. Started local Central PMS using the approved URL:
   - `dotnet run --no-build --project D:\SourceCodes\ExitPass\src\Services\CentralPms\src\ExitPass.CentralPms.Api\ExitPass.CentralPms.Api.csproj --urls http://localhost:56065`
7. Set Central PMS process environment for the controlled diagnostic path:
   - `FiscalIssuance__PosServerIntegration__EnableControlledUatDiagnosticPath=true`
   - `FiscalIssuance__PosServerIntegration__EnablePosServerFiscalIssuanceLiveCall=true`
   - `FiscalIssuance__PosServerIntegration__PosServerBaseUrl=http://localhost:5000`
   - `FiscalIssuance__PosServerIntegration__EnableLiveFiscalIssuanceFromPaymentFlow=false`
   - `FiscalIssuance__PosServerIntegration__EnableLiveFiscalIssuanceFromExitFlow=false`
   - `FiscalIssuance__ExitAuthorizationGating__EnableFiscalBeforeExitAuthorizationEnforcement=false`
8. Called the approved controlled UAT endpoint exactly once:
   - `POST http://localhost:56065/internal/controlled-uat/fiscal-issuance/run`
9. Saved request, response, execution summary, build logs, runtime capture files, and SHA-256 hashes under the approved evidence path.
10. Stopped the Central PMS and POS Server processes started for this rerun.

No preflight endpoint was called. No replay, conflict, failure, or unknown scenario was run. No retry was attempted after the failed response.

## 3. Central PMS Startup Result

| Field | Value |
| --- | --- |
| Startup result | Started |
| Process id | `51580` |
| URL/port | `http://localhost:56065` |
| Environment | `Development` |
| Readiness observation | TCP listener opened on approved port before the single `/run` request |
| Stopped after run | Yes |
| Build evidence | `D:\ExitPass-UAT-Evidence\CPS-POS-UAT-20260709-DEV-ATC-001\central-pms-rerun-build.log` |
| Runtime stdout capture | `D:\ExitPass-UAT-Evidence\CPS-POS-UAT-20260709-DEV-ATC-001\central-pms-rerun-stdout.log` |
| Runtime stderr capture | `D:\ExitPass-UAT-Evidence\CPS-POS-UAT-20260709-DEV-ATC-001\central-pms-rerun-stderr.log` |

Central PMS returned HTTP `409` with a controlled JSON response. The response did not expose stack traces, raw payloads, secrets, customer PII, payment provider payloads, statutory evidence payloads, or local environment values.

## 4. POS Server Startup Result

| Field | Value |
| --- | --- |
| Startup result | Started |
| Process id | `59480` |
| URL/port | `http://localhost:5000` |
| Environment | `Development` |
| Readiness observation | TCP listener opened on approved port before the single `/run` request |
| Stopped after run | Yes |
| Build evidence | `D:\ExitPass-UAT-Evidence\CPS-POS-UAT-20260709-DEV-ATC-001\pos-server-rerun-build.log` |
| Runtime stdout capture | `D:\ExitPass-UAT-Evidence\CPS-POS-UAT-20260709-DEV-ATC-001\pos-server-rerun-stdout.log` |
| Runtime stderr capture | `D:\ExitPass-UAT-Evidence\CPS-POS-UAT-20260709-DEV-ATC-001\pos-server-rerun-stderr.log` |

The Central PMS response reported `posServerCallAttempted=false`, so POS Server was not called for fiscal document creation during the approved rerun.

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
| Payment finality / confirmation ref supplied to endpoint | `DEV-PAYMENT-FINALITY-ATC-001` |
| Payable basis ref | `DEV-PAYABLE-BASIS-ATC-001` |
| Approval reference | `DEV-UAT-CPS-POS-001` |
| Evidence reference | `EVID-CPS-POS-UAT-001` |

The request facts matched the approved gate. No stop criterion for mismatched URL, run id, amount, site, Site POS Server, finality ref, or correlation id was triggered before submission.

## 6. Fiscal Issuance Result

| Field | Value |
| --- | --- |
| HTTP status | `409` |
| Response status | `fiscal_reference_prepare_failed` |
| Accepted | `false` |
| Validation passed | `false` |
| Readiness status | `enabled_ready` |
| Diagnostic invoked | `false` |
| POS Server call attempted | `false` |
| Error code | `fiscal_reference_prepare_failed` |
| Errors | `fiscal_reference_prepare_failed` |
| Sensitive data excluded | `true` |

The first-run constant mismatch from the prior failed execution was no longer present. Central PMS proceeded past first-run constant validation and then failed safely while preparing the fiscal issuance reference.

Source-level review shows `fiscal_reference_prepare_failed` is returned when Central PMS catches a non-cancellation exception during fiscal issuance reference lookup or pending-reference preparation. The controlled response intentionally redacts the underlying exception details.

## 7. Fiscal Issuance Reference Id

No fiscal issuance reference id was produced in the controlled response.

## 8. POS Server Fiscal Document Id / Number

No POS Server fiscal document id or fiscal document number was produced.

The response reported:

- `fiscalDocumentId=null`
- `fiscalDocumentNumber=null`
- `fiscalIssuanceEvidenceStatus=null`
- `fiscalNumberAssignmentState=null`
- `centralPmsFiscalState=null`

## 9. Evidence Files Saved

Evidence folder:

```text
D:\ExitPass-UAT-Evidence\CPS-POS-UAT-20260709-DEV-ATC-001
```

Files saved:

| Evidence file | Purpose |
| --- | --- |
| `controlled-posserver-fiscal-uat-CPS-POS-UAT-20260709-DEV-ATC-001-rerun-request.json` | Approved request body submitted to Central PMS |
| `controlled-posserver-fiscal-uat-CPS-POS-UAT-20260709-DEV-ATC-001-rerun-run-response.json` | Central PMS controlled response |
| `controlled-posserver-fiscal-uat-CPS-POS-UAT-20260709-DEV-ATC-001-rerun-execution-summary.json` | Execution summary |
| `controlled-posserver-fiscal-uat-CPS-POS-UAT-20260709-DEV-ATC-001-rerun-hash.txt` | SHA-256 checksums |
| `central-pms-rerun-build.log` | Central PMS build output |
| `pos-server-rerun-build.log` | POS Server build output |
| `central-pms-rerun-stdout.log` | Central PMS runtime stdout capture |
| `central-pms-rerun-stderr.log` | Central PMS runtime stderr capture |
| `pos-server-rerun-stdout.log` | POS Server runtime stdout capture |
| `pos-server-rerun-stderr.log` | POS Server runtime stderr capture |

## 10. SHA-256 Checksums

```text
A485636659EA9BBA8497BA42CF4A6422EE05AB367EF04C892D8C8601DC10DAD0  controlled-posserver-fiscal-uat-CPS-POS-UAT-20260709-DEV-ATC-001-rerun-request.json
447043B01F04E2BB29CA6964F9C43E81817A4D9609D52C6DCEF104686ADF6166  controlled-posserver-fiscal-uat-CPS-POS-UAT-20260709-DEV-ATC-001-rerun-run-response.json
90CF95AA0E5FCA55345A7FED693B4031DBA4620A77C2B6602F7C08E38E77FCCA  controlled-posserver-fiscal-uat-CPS-POS-UAT-20260709-DEV-ATC-001-rerun-execution-summary.json
037045BEE49082E5E0E1ACCEC79427879AF5701FD1E38FF9ABEA0181DDE20846  central-pms-rerun-build.log
87967979561297E0CB95CCFF0B2608831E9C236FE242BF1CE3AD215EE5255543  pos-server-rerun-build.log
F01A374E9C81E3DB89B3A42940C4D6A5447684986A1296E42BF13F196EED6295  central-pms-rerun-stdout.log
F01A374E9C81E3DB89B3A42940C4D6A5447684986A1296E42BF13F196EED6295  central-pms-rerun-stderr.log
F01A374E9C81E3DB89B3A42940C4D6A5447684986A1296E42BF13F196EED6295  pos-server-rerun-stdout.log
F01A374E9C81E3DB89B3A42940C4D6A5447684986A1296E42BF13F196EED6295  pos-server-rerun-stderr.log
```

## 11. Side-Effect Checks

Side-effect posture from the controlled response:

| Check | Result |
| --- | --- |
| Payment finality changed | `false` |
| ExitAuthorization issued | `false` |
| Gate behavior triggered | `false` |
| Fiscal gating enforcement enabled | `false` |
| Evidence file written by application | `false` |
| Sensitive data excluded | `true` |
| Diagnostic invoked | `false` |
| POS Server call attempted | `false` |

No additional runtime endpoint was called for side-effect checking after the single approved `/run` response. The response itself shows the run stopped before diagnostic invocation and before POS Server fiscal document creation.

## 12. Stop Criteria Outcome

Outcome: no forbidden side-effect stop criterion was observed.

The run stopped after the single controlled `/run` response returned `fiscal_reference_prepare_failed`. No retry was attempted.

The effective execution blocker is now fiscal issuance reference preparation in Central PMS, not the first-run approved constants. Because the approved rerun request was already sent once, further execution requires a new approval or follow-up diagnostic authorization.

## 13. Final Result

Final result: `failed`

Reason: Central PMS returned HTTP `409` with status `fiscal_reference_prepare_failed` before diagnostic invocation and before POS Server call attempt.

## 14. Boundary Statement

During this rerun:

- no HikCentral endpoint was called or written;
- no payment provider was called;
- no payment confirmation was created;
- no ExitAuthorization was issued;
- no gate behavior was triggered;
- no refund/reversal was created;
- no PDF, HTML, or QR artifact was generated;
- no final BIR statutory wording was introduced;
- no production fiscal sequence was used;
- no replay, conflict, failure, or unknown scenario was run;
- no retry was attempted after the failed response.

## 15. Validation

`git diff --check` result: passed.
