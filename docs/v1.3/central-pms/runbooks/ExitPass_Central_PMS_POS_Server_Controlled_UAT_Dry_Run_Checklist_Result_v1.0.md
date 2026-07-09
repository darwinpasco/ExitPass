# ExitPass Central PMS POS Server Controlled UAT Dry-Run Checklist Result v1.0

## 1. Purpose

This record documents the safe dry-run checklist checks performed for the ExitPass Central PMS and POS Server controlled UAT preparation.

The checks were limited to build/start/reachability, local evidence folder write/checksum readiness, redacted configuration inspection, and static review of safe baseline definitions. This record does not authorize UAT execution.

## 2. Execution Context

| Field | Value |
| --- | --- |
| Repository | `D:\SourceCodes\ExitPass` |
| Branch | `docs/controlled-uat-dry-run-checklist-result` |
| Input checklist | `docs/v1.3/central-pms/runbooks/ExitPass_Central_PMS_POS_Server_Controlled_UAT_Dry_Run_Checklist_v1.0.md` |
| Result record | `docs/v1.3/central-pms/runbooks/ExitPass_Central_PMS_POS_Server_Controlled_UAT_Dry_Run_Checklist_Result_v1.0.md` |
| Check timestamp | `2026-07-09T12:18:36.2308143+08:00` |
| dotnet version | `8.0.421` |
| node version | `v24.16.0` |
| npm version | `11.13.0` via `npm.cmd --version` |
| Central PMS URL checked | `http://localhost:56065/` |
| POS Server URL checked | `http://localhost:5000/` |
| Evidence path | `D:\ExitPass-UAT-Evidence\CPS-POS-UAT-20260709-DEV-ATC-001` |

The persona note path `D:\Codex Personas\Codez_G.txt` was requested earlier in the track but was not present on this machine during this run.

## 3. Commands Used

All runtime endpoint calls were safe root GET reachability checks only. No fiscal issuance creation endpoint, mutation endpoint, payment endpoint, HikCentral endpoint, ExitAuthorization endpoint, gate endpoint, refund/reversal endpoint, or rendering endpoint was called.

```powershell
git status --short --untracked-files=all
dotnet --version
node --version
npm.cmd --version
git branch --show-current
Get-Date -Format o
dotnet build src\Services\CentralPms\src\ExitPass.CentralPms.Api\ExitPass.CentralPms.Api.csproj
dotnet build D:\SourceCodes\ExitPass-PoSServer\src\ExitPass.PosServer.Api\ExitPass.PosServer.Api.csproj
dotnet run --no-build --project src\Services\CentralPms\src\ExitPass.CentralPms.Api\ExitPass.CentralPms.Api.csproj
Invoke-WebRequest http://localhost:56065/ -UseBasicParsing -TimeoutSec 5
dotnet run --no-build --project D:\SourceCodes\ExitPass-PoSServer\src\ExitPass.PosServer.Api\ExitPass.PosServer.Api.csproj --urls http://localhost:5000
Invoke-WebRequest http://localhost:5000/ -UseBasicParsing -TimeoutSec 5
Test-Path 'D:\ExitPass-UAT-Evidence\CPS-POS-UAT-20260709-DEV-ATC-001'
Set-Content '.dry-run-write-test.txt'
Get-FileHash -Algorithm SHA256 '.dry-run-write-test.txt'
Remove-Item '.dry-run-write-test.txt'
rg "EnablePosServerFiscalIssuanceLiveCall|EnableControlledUatDiagnosticPath|EnableLiveFiscalIssuanceFromPaymentFlow|EnableLiveFiscalIssuanceFromExitFlow|FiscalIssuance:ExitAuthorization|EnableFiscalBeforeExitAuthorizationEnforcement|FiscalExceptionRetry|Readback" src\Services\CentralPms\src\ExitPass.CentralPms.Api\appsettings.json src\Services\CentralPms\src\ExitPass.CentralPms.Api\appsettings.Development.json src\Services\CentralPms\src\ExitPass.CentralPms.Api\Properties\launchSettings.json -n
git diff --check
```

The first sandboxed `dotnet build` attempts failed because the sandbox could not read the user NuGet configuration. The same build checks were rerun through the approved elevated path and completed.

## 4. Checks Performed

| Check ID | Check | Result |
| --- | --- | --- |
| DR-01 | Central PMS process can start | Passed. Central PMS started with `dotnet run --no-build` and was stopped after the reachability check. |
| DR-02 | POS Server process can start | Passed. Local sibling project `D:\SourceCodes\ExitPass-PoSServer` was available, POS Server started with `--urls http://localhost:5000`, and was stopped after the reachability check. |
| DR-03 | Central PMS base URL reachable | Passed. Safe root GET to `http://localhost:56065/` returned HTTP 200. |
| DR-04 | POS Server base URL reachable | Passed. Safe root GET to `http://localhost:5000/` returned HTTP 404 from the POS Server process, proving reachability. No fiscal endpoint was called. |
| DR-05 | Central PMS diagnostic flags assigned correctly | Accepted for execution gate preparation / waived for this non-production local controlled UAT run by Darwin Pasco. Redacted config inspection did not find explicit assigned values for `EnablePosServerFiscalIssuanceLiveCall=true` and `EnableControlledUatDiagnosticPath=true` in local appsettings or launch settings. |
| DR-06 | Payment-flow guard false | Passed by absence/default posture. No explicit `EnableLiveFiscalIssuanceFromPaymentFlow=true` assignment was found in local appsettings or launch settings. |
| DR-07 | Exit-flow guard false | Passed by absence/default posture. No explicit `EnableLiveFiscalIssuanceFromExitFlow=true` assignment was found in local appsettings or launch settings. |
| DR-08 | Fiscal gating enforcement false | Passed by absence/default posture. No explicit `EnableFiscalBeforeExitAuthorizationEnforcement=true` assignment was found in local appsettings or launch settings. |
| DR-09 | No retry/readback worker enabled | Passed by static inspection. Fiscal retry/readback services are registered as scoped services; no fiscal retry/readback hosted worker registration was found in the inspected Central PMS startup path. |
| DR-10 | POS Server non-production fiscal identity exists | Accepted for execution gate preparation / waived for this non-production local controlled UAT run by Darwin Pasco. |
| DR-11 | POS Server non-production sequence policy exists | Accepted for execution gate preparation / waived for this non-production local controlled UAT run by Darwin Pasco. |
| DR-12 | POS Server non-production sequence state exists | Accepted for execution gate preparation / waived for this non-production local controlled UAT run by Darwin Pasco. |
| DR-13 | POS Server fiscal sequence is non-production | Passed/resolved. Darwin Pasco confirms this is a non-production controlled UAT environment. The assigned fiscal identity, fiscal sequence policy, and fiscal sequence state are non-production values for controlled UAT only and must not allocate production fiscal numbers. |
| DR-14 | Evidence folder exists | Passed after setup. The evidence folder did not exist before the check and was created during the allowed evidence readiness step. |
| DR-15 | Evidence folder write access works | Passed. A harmless marker file was written and removed. |
| DR-16 | Checksum command works | Passed. `Get-FileHash -Algorithm SHA256` produced a hash for the marker file. |
| DR-17 | Side-effect baseline checks are defined | Passed. The input checklist defines read-only placeholders for exit authorization count, gate event count, refund/reversal count, payment mutation outside fixture, and POS Server non-production sequence classification. These baseline queries were not executed. |
| DR-18 | Rollback/support owner contact works | Accepted for execution gate preparation / waived for this non-production local controlled UAT run by Darwin Pasco. Manual contact confirmation was not performed by Codex. |
| DR-19 | Execution window is current | Accepted for execution gate preparation / waived for this non-production local controlled UAT run by Darwin Pasco. The filled execution window was `July 9, 2026 1:00 PM-3:00 PM PHT`; the recorded check timestamp was before the window opened. |
| DR-20 | Runtime boundary reminder acknowledged | Passed for this dry-run activity. No UAT execution or forbidden action was performed. |

## 5. Build Results

| Component | Command | Result |
| --- | --- | --- |
| Central PMS | `dotnet build src\Services\CentralPms\src\ExitPass.CentralPms.Api\ExitPass.CentralPms.Api.csproj` | Passed under approved elevated build execution. Existing XML documentation warnings were emitted; no build errors were reported. |
| POS Server | `dotnet build D:\SourceCodes\ExitPass-PoSServer\src\ExitPass.PosServer.Api\ExitPass.PosServer.Api.csproj` | Passed under approved elevated build execution with `0 Warning(s)` and `0 Error(s)`. |

## 6. Runtime Results

| Component | Startup command | Reachability result | Log reference |
| --- | --- | --- | --- |
| Central PMS | `dotnet run --no-build --project src\Services\CentralPms\src\ExitPass.CentralPms.Api\ExitPass.CentralPms.Api.csproj` | HTTP 200 from `GET http://localhost:56065/` | `C:\Users\darwi\AppData\Local\Temp\exitpass-dryrun-centralpms-35820444881e4aa5992c2e4e23a54abe` |
| POS Server | `dotnet run --no-build --project D:\SourceCodes\ExitPass-PoSServer\src\ExitPass.PosServer.Api\ExitPass.PosServer.Api.csproj --urls http://localhost:5000` | HTTP 404 from `GET http://localhost:5000/`; treated as reachable because the POS Server process handled the request at root | `C:\Users\darwi\AppData\Local\Temp\exitpass-dryrun-posserver-ab494eed6b3d42cda2e2bfe40b94b1ce` |

Both started processes were stopped by the validation script. A follow-up process check for the started process IDs returned no running processes.

## 7. Evidence Folder And Checksum Result

| Field | Value |
| --- | --- |
| Evidence path | `D:\ExitPass-UAT-Evidence\CPS-POS-UAT-20260709-DEV-ATC-001` |
| Existed before check | `false` |
| Created during check | `true` |
| Marker write result | Passed |
| Marker removal result | Passed |
| Hash algorithm | `SHA256` |
| Marker hash | `A9DED86D2E18151E7511272FF6D1120407EA1C9605EAE1B349C2D00CA7015F2D` |
| Marker timestamp | `2026-07-09T12:22:14.2072412+08:00` |

The marker file was removed after hashing. No fiscal, payment, POS request/response, statutory, PII, or credential payload was written.

## 8. Redacted Configuration Inspection

Central PMS `launchSettings.json` confirmed the expected local application URLs:

- `https://localhost:56064`
- `http://localhost:56065`

The local Central PMS appsettings files were inspected only for approved flag names. No secrets or raw connection strings are reproduced in this record.

Findings:

- `EnablePosServerFiscalIssuanceLiveCall` was not explicitly assigned in inspected local appsettings or launch settings.
- `EnableControlledUatDiagnosticPath` was not explicitly assigned in inspected local appsettings or launch settings.
- `EnableLiveFiscalIssuanceFromPaymentFlow` was not explicitly assigned in inspected local appsettings or launch settings.
- `EnableLiveFiscalIssuanceFromExitFlow` was not explicitly assigned in inspected local appsettings or launch settings.
- `EnableFiscalBeforeExitAuthorizationEnforcement` was not explicitly assigned in inspected local appsettings or launch settings.

This posture is accepted by Darwin Pasco for execution gate preparation only on this non-production local controlled UAT path.

## 9. Accepted Assumptions / Waivers For Non-Production Local Controlled UAT

Darwin Pasco confirms this is a non-production controlled UAT environment and accepts the remaining dry-run assumptions for purposes of creating the execution gate/go-no-go record.

| Check ID | Status | Accepted assumption / waiver |
| --- | --- | --- |
| DR-05 | Accepted for execution gate preparation / waived | Accepted only for this non-production local controlled UAT path. Central PMS controlled UAT diagnostic/live-call flags were not explicitly assigned in inspected local config. |
| DR-10 | Accepted for execution gate preparation / waived | Accepted only for this non-production local controlled UAT path. POS Server fiscal identity existence was not independently verified by Codex. |
| DR-11 | Accepted for execution gate preparation / waived | Accepted only for this non-production local controlled UAT path. POS Server fiscal sequence policy existence was not independently verified by Codex. |
| DR-12 | Accepted for execution gate preparation / waived | Accepted only for this non-production local controlled UAT path. POS Server fiscal sequence state existence was not independently verified by Codex. |
| DR-13 | Passed/resolved | Resolved by Darwin Pasco's non-production assertion for this controlled UAT environment. The assigned fiscal identity, fiscal sequence policy, and fiscal sequence state are non-production values for controlled UAT only and must not allocate production fiscal numbers. |
| DR-18 | Accepted for execution gate preparation / waived | Accepted only for this non-production local controlled UAT path. Rollback/support owner contact was not manually confirmed by Codex. |
| DR-19 | Accepted for execution gate preparation / waived | Accepted only for this non-production local controlled UAT path. The assigned execution window was not open at the recorded check timestamp. |

## 10. Final Classification

Final classification: `dry_run_checklist_passed`

Central PMS and POS Server local runtime reachability passed, evidence write/checksum readiness passed, DR-13 was resolved by Darwin Pasco's non-production assertion, and Darwin Pasco accepted the remaining dry-run assumptions for execution gate preparation.

Darwin Pasco confirms this is a non-production controlled UAT environment and accepts the remaining dry-run assumptions for purposes of creating the execution gate/go-no-go record.

This dry-run pass does not authorize UAT execution.

## 11. Boundary Statement

UAT execution remains not authorized.

During this dry-run result preparation:

- no UAT scenario was executed during this dry-run result preparation;
- no fiscal issuance was created during this dry-run result preparation;
- no fiscal issuance creation endpoint was called;
- no payment was confirmed;
- no POS Server mutation endpoint was called;
- no HikCentral endpoint was called or written;
- no ExitAuthorization or gate behavior was triggered;
- no refund/reversal was created;
- no PDF, HTML, or QR artifact was generated;
- no final BIR statutory wording was defined;
- no raw fiscal request payload, raw POS Server request/response body, payment provider payload, statutory evidence payload, customer PII, stack trace, credential, or local environment dump was recorded.

## 12. Recommended Next Step

Create the Controlled UAT execution gate/go-no-go record.

## 13. Validation

`git diff --check` result: passed.
