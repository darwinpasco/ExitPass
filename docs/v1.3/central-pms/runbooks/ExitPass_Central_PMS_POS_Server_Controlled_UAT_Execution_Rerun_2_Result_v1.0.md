# ExitPass Central PMS POS Server Controlled UAT Execution Rerun 2 Result v1.0

## 1. Execution Timestamp

| Field | Value |
| --- | --- |
| Execution started | `2026-07-09T16:27:07.1313655+08:00` |
| Execution completed | `2026-07-09T16:27:26.5315381+08:00` |
| Approved gate | `docs/v1.3/central-pms/runbooks/ExitPass_Central_PMS_POS_Server_Controlled_UAT_Execution_Gate_Go_No_Go_v1.0.md` |
| Prior rerun result | `docs/v1.3/central-pms/runbooks/ExitPass_Central_PMS_POS_Server_Controlled_UAT_Execution_Rerun_Result_v1.0.md` |
| Final result | `failed` |

The approved rerun-2 request was submitted exactly once. Central PMS accepted the scoped request far enough to reach controlled fixture preparation, then failed safely with `controlled_uat_fixture_prepare_failed`. The failure occurred before diagnostic invocation and before any POS Server fiscal document call.

No retry was attempted after the failed controlled response.

## 2. Commands / Procedure Used

Procedure summary:

1. Confirmed the approved ports `56065` and `5000` were not already listening before startup.
2. Confirmed and reused the approved evidence folder:
   - `D:\ExitPass-UAT-Evidence\CPS-POS-UAT-20260709-DEV-ATC-001`
3. Reused the approved rerun request facts from:
   - `D:\ExitPass-UAT-Evidence\CPS-POS-UAT-20260709-DEV-ATC-001\controlled-posserver-fiscal-uat-CPS-POS-UAT-20260709-DEV-ATC-001-rerun-request.json`
4. Built Central PMS:
   - `dotnet build src\Services\CentralPms\src\ExitPass.CentralPms.Api\ExitPass.CentralPms.Api.csproj`
5. Built POS Server:
   - `dotnet build D:\SourceCodes\ExitPass-PoSServer\src\ExitPass.PosServer.Api\ExitPass.PosServer.Api.csproj`
6. Started local POS Server using the approved URL:
   - `dotnet run --no-build --project D:\SourceCodes\ExitPass-PoSServer\src\ExitPass.PosServer.Api\ExitPass.PosServer.Api.csproj --urls http://localhost:5000`
7. Started local Central PMS using the approved URL:
   - `dotnet run --no-build --project D:\SourceCodes\ExitPass\src\Services\CentralPms\src\ExitPass.CentralPms.Api\ExitPass.CentralPms.Api.csproj --urls http://localhost:56065`
8. Set Central PMS process environment for the controlled diagnostic path:
   - `FiscalIssuance__PosServerIntegration__EnableControlledUatDiagnosticPath=true`
   - `FiscalIssuance__PosServerIntegration__EnablePosServerFiscalIssuanceLiveCall=true`
   - `FiscalIssuance__PosServerIntegration__PosServerBaseUrl=http://localhost:5000`
   - `FiscalIssuance__PosServerIntegration__EnableLiveFiscalIssuanceFromPaymentFlow=false`
   - `FiscalIssuance__PosServerIntegration__EnableLiveFiscalIssuanceFromExitFlow=false`
   - `FiscalIssuance__ExitAuthorizationGating__EnableFiscalBeforeExitAuthorizationEnforcement=false`
   - `InternalSecurity__Mtls__Enabled=false`
9. Called the approved controlled UAT endpoint exactly once:
   - `POST http://localhost:56065/internal/controlled-uat/fiscal-issuance/run`
10. Saved request, response, execution summary, build logs, runtime captures, and SHA-256 hashes under the approved evidence path.
11. Stopped the Central PMS and POS Server processes started for this rerun.

No preflight endpoint was called. No replay, conflict, failure, or unknown scenario was run.

## 3. Central PMS Startup Result

| Field | Value |
| --- | --- |
| Build result | Succeeded with existing compiler warnings |
| Startup result | Started |
| Process id | `6664` |
| URL/port | `http://localhost:56065` |
| Environment | `Development` |
| Readiness observation | TCP listener opened on approved port before the single `/run` request |
| Stopped after run | Yes |
| Build evidence | `D:\ExitPass-UAT-Evidence\CPS-POS-UAT-20260709-DEV-ATC-001\central-pms-rerun-2-build.log` |
| Runtime stdout capture | `D:\ExitPass-UAT-Evidence\CPS-POS-UAT-20260709-DEV-ATC-001\central-pms-rerun-2-stdout.log` |
| Runtime stderr capture | `D:\ExitPass-UAT-Evidence\CPS-POS-UAT-20260709-DEV-ATC-001\central-pms-rerun-2-stderr.log` |

Central PMS returned HTTP `409` with a controlled JSON response. The response did not expose stack traces, raw payloads, secrets, customer PII, payment provider payloads, statutory evidence payloads, or local environment values.

## 4. POS Server Startup Result

| Field | Value |
| --- | --- |
| Build result | Succeeded with `0` warnings and `0` errors |
| Startup result | Started |
| Process id | `57196` |
| URL/port | `http://localhost:5000` |
| Environment | `Development` |
| Readiness observation | TCP listener opened on approved port before the single `/run` request |
| Stopped after run | Yes |
| Build evidence | `D:\ExitPass-UAT-Evidence\CPS-POS-UAT-20260709-DEV-ATC-001\pos-server-rerun-2-build.log` |
| Runtime stdout capture | `D:\ExitPass-UAT-Evidence\CPS-POS-UAT-20260709-DEV-ATC-001\pos-server-rerun-2-stdout.log` |
| Runtime stderr capture | `D:\ExitPass-UAT-Evidence\CPS-POS-UAT-20260709-DEV-ATC-001\pos-server-rerun-2-stderr.log` |

The Central PMS controlled response reported `posServerCallAttempted=false`, so POS Server was not called for fiscal document creation during rerun-2.

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
| Response status | `controlled_uat_fixture_prepare_failed` |
| Accepted | `false` |
| Validation passed | `false` |
| Readiness status | `enabled_ready` |
| Diagnostic invoked | `false` |
| POS Server call attempted | `false` |
| Error code | `controlled_uat_fixture_prepare_failed` |
| Errors | `controlled_uat_fixture_prepare_failed` |
| Sensitive data excluded | `true` |

The prior `fiscal_reference_prepare_failed` result was not reached in rerun-2. Central PMS failed safely earlier while preparing the approved controlled UAT fixture. The response intentionally redacted underlying exception details.

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
| `controlled-posserver-fiscal-uat-CPS-POS-UAT-20260709-DEV-ATC-001-rerun-2-request.json` | Approved request body submitted to Central PMS |
| `controlled-posserver-fiscal-uat-CPS-POS-UAT-20260709-DEV-ATC-001-rerun-2-run-response.json` | Central PMS controlled response |
| `controlled-posserver-fiscal-uat-CPS-POS-UAT-20260709-DEV-ATC-001-rerun-2-execution-summary.json` | Execution summary |
| `controlled-posserver-fiscal-uat-CPS-POS-UAT-20260709-DEV-ATC-001-rerun-2-hash.txt` | SHA-256 checksums |
| `central-pms-rerun-2-build.log` | Central PMS build output |
| `pos-server-rerun-2-build.log` | POS Server build output |
| `central-pms-rerun-2-stdout.log` | Central PMS runtime stdout capture |
| `central-pms-rerun-2-stderr.log` | Central PMS runtime stderr capture |
| `pos-server-rerun-2-stdout.log` | POS Server runtime stdout capture |
| `pos-server-rerun-2-stderr.log` | POS Server runtime stderr capture |

## 10. SHA-256 Checksums

```text
A485636659EA9BBA8497BA42CF4A6422EE05AB367EF04C892D8C8601DC10DAD0  controlled-posserver-fiscal-uat-CPS-POS-UAT-20260709-DEV-ATC-001-rerun-2-request.json
20741A5D0F873750E602E3F16BD369A75C957FB4F48F4C5A0DBBB5DD39F87D1F  controlled-posserver-fiscal-uat-CPS-POS-UAT-20260709-DEV-ATC-001-rerun-2-run-response.json
BBB569817763DA36D2AA1A0B74851B242DF8DF2F8C5807F89F37F9BE3A35F484  controlled-posserver-fiscal-uat-CPS-POS-UAT-20260709-DEV-ATC-001-rerun-2-execution-summary.json
2BFF3A366CEE40E4837FC0820CE6732C1435EE8980151E4259ADFED6A93EF35D  central-pms-rerun-2-build.log
25A98AA1DD3A8481A6E856739BFEDA384D10F8CAB2D46198CF8B59C2A93FD31C  pos-server-rerun-2-build.log
93AFD8D6EDD2C950EC3FFDF27C69C5148A85845B7C6AF0D607D5FD3DB6993430  central-pms-rerun-2-stdout.log
E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855  central-pms-rerun-2-stderr.log
94BF1ADF60D3596D588D05B4523A5A61652886B79E5A3AE19BD0BE187BCEEA94  pos-server-rerun-2-stdout.log
E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855  pos-server-rerun-2-stderr.log
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

The run stopped after the single controlled `/run` response returned `controlled_uat_fixture_prepare_failed`. No retry was attempted.

The effective execution blocker is now controlled UAT fixture preparation in Central PMS. Because the approved rerun-2 request was already sent once, further execution requires a new approval or follow-up diagnostic authorization.

## 13. Final Result

Final result: `failed`

Reason: Central PMS returned HTTP `409` with status `controlled_uat_fixture_prepare_failed` before diagnostic invocation and before POS Server call attempt.

## 14. Boundary Statement

During this rerun:

- no HikCentral endpoint was called or written;
- no payment provider was called;
- no payment confirmation was created outside approved fixture preparation;
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
