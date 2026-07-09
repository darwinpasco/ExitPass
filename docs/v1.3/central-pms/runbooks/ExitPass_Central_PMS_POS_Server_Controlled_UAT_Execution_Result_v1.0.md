# ExitPass Central PMS POS Server Controlled UAT Execution Result v1.0

## 1. Execution Timestamp

| Field | Value |
| --- | --- |
| Execution started | `2026-07-09T13:14:36.1291009+08:00` |
| Execution completed | `2026-07-09T13:14:50.4479112+08:00` |
| Approved gate | `docs/v1.3/central-pms/runbooks/ExitPass_Central_PMS_POS_Server_Controlled_UAT_Execution_Gate_Go_No_Go_v1.0.md` |
| Final result | `failed` |

The single approved run was attempted exactly once. It was rejected by Central PMS before diagnostic invocation, fiscal issuance reference preparation, or POS Server fiscal document creation.

## 2. Commands / Procedure Used

Procedure summary:

1. Created/confirmed evidence folder `D:\ExitPass-UAT-Evidence\CPS-POS-UAT-20260709-DEV-ATC-001`.
2. Started local POS Server using `dotnet run --no-build --project D:\SourceCodes\ExitPass-PoSServer\src\ExitPass.PosServer.Api\ExitPass.PosServer.Api.csproj --urls http://localhost:5000`.
3. Started local Central PMS using `dotnet run --no-build --project src\Services\CentralPms\src\ExitPass.CentralPms.Api\ExitPass.CentralPms.Api.csproj`.
4. Set Central PMS process environment for the controlled diagnostic path:
   - `FiscalIssuance__PosServerIntegration__EnableControlledUatDiagnosticPath=true`
   - `FiscalIssuance__PosServerIntegration__EnablePosServerFiscalIssuanceLiveCall=true`
   - `FiscalIssuance__PosServerIntegration__PosServerBaseUrl=http://localhost:5000`
   - `FiscalIssuance__PosServerIntegration__EnableLiveFiscalIssuanceFromPaymentFlow=false`
   - `FiscalIssuance__PosServerIntegration__EnableLiveFiscalIssuanceFromExitFlow=false`
   - `FiscalIssuance__ExitAuthorizationGating__EnableFiscalBeforeExitAuthorizationEnforcement=false`
5. Called the approved controlled UAT endpoint exactly once:
   - `POST http://localhost:56065/internal/controlled-uat/fiscal-issuance/run`
6. Saved request, response, summary, service logs, and SHA-256 hashes under the approved evidence path.
7. Stopped the Central PMS and POS Server processes started for this run.

No preflight endpoint was called. No retry was attempted.

## 3. Central PMS Startup Result

| Field | Value |
| --- | --- |
| Startup result | Started |
| Process id | `1468` |
| URL observed in log | `http://localhost:56065` |
| Environment | `Development` |
| Stopped after run | Yes |
| Startup evidence | `D:\ExitPass-UAT-Evidence\CPS-POS-UAT-20260709-DEV-ATC-001\central-pms-stdout.log` |

Central PMS accepted the single `/run` request and returned HTTP 400 with a controlled JSON response.

## 4. POS Server Startup Result

| Field | Value |
| --- | --- |
| Startup result | Started |
| Process id | `44036` |
| URL observed in log | `http://localhost:5000` |
| Environment | `Development` |
| Stopped after run | Yes |
| Startup evidence | `D:\ExitPass-UAT-Evidence\CPS-POS-UAT-20260709-DEV-ATC-001\pos-server-stdout.log` |

The run was rejected before Central PMS attempted a POS Server call.

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

## 6. Fiscal Issuance Result

| Field | Value |
| --- | --- |
| HTTP status | `400` |
| Response status | `run_rejected` |
| Accepted | `false` |
| Validation passed | `false` |
| Diagnostic invoked | `false` |
| POS Server call attempted | `false` |
| Error code | `run_id_not_approved_for_first_run` |
| Errors | `run_id_not_approved_for_first_run`; `correlation_id_not_approved_for_first_run`; `upstream_finality_ref_not_approved_for_first_run`; `business_day_date_not_approved_for_first_run` |

The approved July 9 request facts were submitted, but the current Central PMS controlled UAT invocation service still rejected values outside its built-in first-run constants. The rejection happened before fiscal issuance reference preparation and before any POS Server call.

## 7. Fiscal Issuance Reference Id

No fiscal issuance reference id was produced.

## 8. POS Server Fiscal Document Id / Number

No POS Server fiscal document id or fiscal document number was produced.

## 9. Evidence Files Saved

Evidence folder:

```text
D:\ExitPass-UAT-Evidence\CPS-POS-UAT-20260709-DEV-ATC-001
```

Files saved:

| Evidence file | Purpose |
| --- | --- |
| `controlled-posserver-fiscal-uat-CPS-POS-UAT-20260709-DEV-ATC-001-request.json` | Approved request body submitted to Central PMS |
| `controlled-posserver-fiscal-uat-CPS-POS-UAT-20260709-DEV-ATC-001-run-response.json` | Central PMS controlled response |
| `controlled-posserver-fiscal-uat-CPS-POS-UAT-20260709-DEV-ATC-001-execution-summary.json` | Execution summary |
| `controlled-posserver-fiscal-uat-CPS-POS-UAT-20260709-DEV-ATC-001-hash.txt` | SHA-256 checksums |
| `central-pms-stdout.log` | Central PMS startup/request log |
| `central-pms-stderr.log` | Central PMS stderr log |
| `pos-server-stdout.log` | POS Server startup log |
| `pos-server-stderr.log` | POS Server stderr log |

## 10. SHA-256 Checksums

```text
A485636659EA9BBA8497BA42CF4A6422EE05AB367EF04C892D8C8601DC10DAD0  controlled-posserver-fiscal-uat-CPS-POS-UAT-20260709-DEV-ATC-001-request.json
4DCA28AA737B8E5B982F5E9DB76061627072F48E7428B54A381F2C7B0A205607  controlled-posserver-fiscal-uat-CPS-POS-UAT-20260709-DEV-ATC-001-run-response.json
FE1599277B473D048828573FDB9B7DA7C205B36A134708920BEF3389099372C8  controlled-posserver-fiscal-uat-CPS-POS-UAT-20260709-DEV-ATC-001-execution-summary.json
401AF03F501281719367EC0E035D1EAC0FB82EFB3E7BADFD565848BE1121EDAF  central-pms-stdout.log
E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855  central-pms-stderr.log
94BF1ADF60D3596D588D05B4523A5A61652886B79E5A3AE19BD0BE187BCEEA94  pos-server-stdout.log
E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855  pos-server-stderr.log
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

No database side-effect count queries were executed after the run. Because the endpoint rejected the request during validation, the diagnostic path did not prepare a fiscal issuance reference and did not call POS Server.

## 12. Stop Criteria Outcome

Outcome: no forbidden side-effect stop criterion was observed.

The procedure stopped after the single controlled `/run` response returned `run_rejected`. No retry was attempted.

The effective execution blocker is the mismatch between the approved July 9 gate facts and the current Central PMS invocation service's built-in first-run constants.

## 13. Final Result

Final result: `failed`

Reason: Central PMS rejected the approved July 9 request facts before diagnostic invocation.

## 14. Boundary Statement

During this execution:

- no HikCentral endpoint was called or written;
- no payment provider was called;
- no payment confirmation was created;
- no ExitAuthorization was issued;
- no gate behavior was triggered;
- no refund/reversal was created;
- no PDF, HTML, or QR artifact was generated;
- no final BIR statutory wording was introduced;
- no production fiscal sequence was used;
- no replay/conflict/failure/unknown scenario was run;
- no retry was attempted after the failed response.

## 15. Validation

`git diff --check` result: passed.
