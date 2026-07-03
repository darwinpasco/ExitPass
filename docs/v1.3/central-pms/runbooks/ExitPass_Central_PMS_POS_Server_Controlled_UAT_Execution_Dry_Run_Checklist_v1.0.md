# ExitPass Central PMS POS Server Controlled UAT Execution Dry-Run Checklist v1.0

## 1. Document control

| Field | Value |
| --- | --- |
| Document | ExitPass Central PMS POS Server Controlled UAT Execution Dry-Run Checklist |
| Version | v1.0 |
| Status | Checklist created; not approval to execute |
| Date | 2026-07-03 |
| Branch | feature/central-pms-pos-server-controlled-uat-execution-dry-run-checklist |
| Owner | ExitPass platform implementation/orchestration |
| Scope | Development-only controlled UAT pre-execution checklist for Central PMS to POS Server fiscal issuance diagnostic |

## 2. Purpose and scope

This checklist converts the remaining readiness blockers into concrete verification items that must pass before Darwin Pasco explicitly approves the first controlled Central PMS to POS Server fiscal issuance diagnostic call.

This document is a dry-run checklist only. It does not execute UAT, call POS Server, create a fiscal document, expose an invocation endpoint, add CLI/tooling, or change runtime behavior.

## 3. Authority boundaries

- Central PMS owns payment finality.
- Central PMS owns fiscal reference recording.
- Central PMS owns normal ExitAuthorization.
- POS Server owns fiscal issuance and numbering only.
- POS Server response is fiscal issuance evidence only.
- POS Server does not approve payment, exit, gate opening, entitlement, manual release, or continuity.
- Manual release is not normal ExitAuthorization.
- UAT readiness evidence and test data are audit artifacts only and do not create operational authority.

## 4. Non-goals

This checklist does not:

- execute UAT;
- execute a live POS Server call;
- create a fiscal document;
- add an API endpoint;
- add CLI/tooling;
- add file-writing code;
- wire payment confirmation to POS Server;
- wire ExitAuthorization to POS Server;
- enable fiscal gating enforcement;
- add a retry scheduler;
- add a GET readback worker;
- implement Operator Console queues;
- implement Management Dashboard projections;
- modify source code;
- modify SQL;
- modify generated artifacts;
- modify DOCX files;
- modify the POS Server runtime repository.

## 5. Source readiness decision

| Decision source | Decision | Meaning |
| --- | --- | --- |
| First-run readiness refresh | ready_for_dry_run_checklist | Planning values are assigned and a dry-run checklist may be prepared. |
| Execution posture | not_ready_for_execution | Actual diagnostic execution remains blocked until runtime checks pass and explicit execution approval is captured. |

## 6. Dry-run checklist decision boundary

This document may decide:

- checklist_created;
- ready_to_run_pre_execution_checks;
- dry_run_checklist_created_but_execution_invocation_blocked.

This document must not decide:

- ready_for_execution.

Final checklist decision for this version:

`dry_run_checklist_created_but_execution_invocation_blocked`

Reason: the Central PMS application contains an application-level controlled UAT harness and diagnostic seam, but repository inspection did not find a safe runtime invocation method exposed by endpoint, CLI, hosted service, or operator tool. Actual execution remains blocked even if infrastructure checks pass.

## 7. Filled values carried forward

### Environment

| Field | Value |
| --- | --- |
| Environment name | DEV-CONTROLLED-UAT-LOCAL |
| Central PMS environment | CentralPMS-DEV-DOCKER |
| Central PMS base URL | http://localhost:8080 |
| Central PMS Docker container name | exitpass-central-pms |
| POS Server environment | PoSServer-DEV-LOCAL |
| POS Server host/browser URL | http://localhost:8091 |
| POS Server base URL reference for Central PMS | PosServerBaseUrl = http://host.docker.internal:8091 |
| Production or non-production | Non-production |

### Site / Site POS Server

| Field | Value |
| --- | --- |
| Site name | DEV Site - Alabang Town Center |
| Site ref / Site ID | DEV-SITE-ATC-001 |
| Site POS Server ref / ID | DEV-POS-SERVER-ATC-001 |
| Site POS Server environment | PoSServer-DEV-LOCAL |
| Site POS Server base URL reference | http://host.docker.internal:8091 |

### POS Server fiscal setup

| Field | Value |
| --- | --- |
| Fiscal identity ref / ID | DEV-FISCAL-IDENTITY-ATC-001 |
| Fiscal sequence policy ref / ID | DEV-SI-SEQUENCE-POLICY-ATC-001 |
| Fiscal sequence state ref / ID | DEV-SI-SEQUENCE-STATE-ATC-001 |
| Fiscal document type | sales_invoice |
| Using production fiscal sequence | No |
| Fiscal number allocation impact accepted by | Darwin Pasco |

### Run identity and upstream finality

| Field | Value |
| --- | --- |
| Run ID | CPS-POS-UAT-20260703-DEV-ATC-001 |
| Correlation ID | 00000000-0000-4000-8000-000000000101 |
| Expected run type | newly_created |
| Upstream finality ref | CPS-POS-UAT:CPS-POS-UAT-20260703-DEV-ATC-001:newly_created:001 |
| One semantic request confirmation | Yes |
| Conflict bypass prohibition acknowledgement | Yes |
| Replay ref reuse confirmation | Not applicable for first run |

### Test transaction refs

| Field | Value |
| --- | --- |
| Parking session ref | DEV-PARKING-SESSION-ATC-001 |
| Payment attempt ref | DEV-PAYMENT-ATTEMPT-ATC-001 |
| Payment confirmation ref | DEV-PAYMENT-CONFIRMATION-ATC-001 |
| Payable basis ref | DEV-PAYABLE-BASIS-ATC-001 |
| Business day date | 2026-07-03 |
| Currency | PHP |
| Amount minor units | 10000 |

### Fiscal request facts

| Field | Value |
| --- | --- |
| Fiscal document type | sales_invoice |
| Business day date | 2026-07-03 |
| Line summary | Parking fee - controlled UAT development test |
| Line count | 1 |
| Line amount total | 10000 |
| Tender summary | Controlled UAT test tender - non-production |
| Tender count | 1 |
| Tender amount total | 10000 |
| Tax detail summary | DEV VAT/tax facts aligned to payable basis |
| Tax detail present | Yes |
| Tax amount total | 0 |
| Totals present | Yes |
| Grand total | 10000 |
| Totals match payable basis | Yes |

### Evidence

| Field | Value |
| --- | --- |
| Evidence save mode | Mode B temporary controlled location |
| Evidence save folder/reference | D:\ExitPass-UAT-Evidence\DEV-CONTROLLED-UAT-LOCAL\DEV-SITE-ATC-001\2026-07-03\CPS-POS-UAT-20260703-DEV-ATC-001 |
| Evidence owner | Darwin Pasco |
| Ticket/change/reference | DEV-UAT-CPS-POS-001 |
| Hash required | Yes |
| Hash command | Get-FileHash -Algorithm SHA256 "\<path-to-evidence.json\>" |

## 8. Blocker resolution matrix

| # | Blocker | Status before checklist | Checklist action | Pass criteria | Failure action | Evidence to save | Execution impact |
| --- | --- | --- | --- | --- | --- | --- | --- |
| 1 | POS Server running on http://localhost:8091 | unresolved | Run host port check and POS Server startup command. | Intended POS Server process listens on 8091. | Stop, free port or start POS Server correctly. | Port check output and startup log reference. | Block execution if failed. |
| 2 | Central PMS Docker connectivity to host.docker.internal:8091 | unresolved | Determine Docker network and run temporary curl container. | Container network can reach POS Server host URL. | Stop, fix Docker networking or host binding. | `docker inspect` and curl output. | Block execution if failed. |
| 3 | Central PMS config set to POS Server base URL | unresolved | Inspect Central PMS container env/config. | `PosServerBaseUrl` equivalent is `http://host.docker.internal:8091`. | Stop, update approved config and restart container. | Config/env inspection output. | Block execution if failed. |
| 4 | dev fiscal identity exists and is active/effective | unresolved | Query POS Server DB tables. | Assigned fiscal identity exists, active true, current effective assignment exists. | Stop, seed or correct dev fiscal config through approved process. | SQL output. | Block execution if failed. |
| 5 | dev fiscal sequence policy exists and is active/effective | unresolved | Query POS Server DB tables. | Assigned policy exists, effective now, linked to Site POS Server and document type if configured. | Stop, seed or correct dev fiscal config through approved process. | SQL output. | Block execution if failed. |
| 6 | dev fiscal sequence state exists/configured | unresolved | Query POS Server DB tables. | Assigned sequence state exists for assigned policy and values are non-negative. | Stop, seed or correct dev fiscal config through approved process. | SQL output. | Block execution if failed. |
| 7 | controlled UAT flags enabled only for approved window | unresolved | Inspect env/config before the run window. | Live call and controlled diagnostic flags are true only for the approved window. | Stop, correct config and capture approval. | Env/config output and approval ref. | Block execution if failed. |
| 8 | payment-flow guard false | unresolved | Inspect `EnableLiveFiscalIssuanceFromPaymentFlow`. | False or absent/default false. | Stop, set false and restart container. | Env/config output. | Block execution if true. |
| 9 | exit-flow guard false | unresolved | Inspect `EnableLiveFiscalIssuanceFromExitFlow`. | False or absent/default false. | Stop, set false and restart container. | Env/config output. | Block execution if true. |
| 10 | fiscal gating enforcement false | unresolved | Inspect `EnableFiscalBeforeExitAuthorizationEnforcement`. | False or absent/default false. | Stop, set false and restart container. | Env/config output. | Block execution if true. |
| 11 | evidence folder exists | unresolved | Create/check assigned folder. | `Test-Path` returns true. | Stop, create approved folder or choose approved controlled location. | Folder path and command output. | Block execution if failed. |
| 12 | no endpoint/CLI/tooling introduced | unresolved | Inspect changed files and source routes. | No new endpoint, CLI, hosted service, or tooling files are present. | Stop, remove unauthorized implementation or restart approved task. | `git status`, `git diff --name-only`, source search output. | Block execution if failed. |
| 13 | no payment/exit production wiring | unresolved | Inspect source changes and known flow guards. | No new payment/exit dependencies on POS Server or harness. | Stop and revert unauthorized wiring through approved process. | Source search output. | Block execution if failed. |
| 14 | no gate behavior | unresolved | Inspect source changes and UAT evidence confirmations. | No gate command/event/execution path is introduced. | Stop and remove unauthorized gate behavior. | Source search output. | Block execution if failed. |
| 15 | safe invocation method exists or is explicitly blocked | unresolved | Inspect Central PMS implementation and tests. | Safe invocation exists, or execution is explicitly blocked. | If absent, do not execute; create next invocation-surface task. | Search output and conclusion. | Currently blocks execution. |
| 16 | dry-run checklist passes | unresolved | Complete every item in this checklist. | All required pass criteria are captured. | Stop, remediate failed items and repeat checklist. | Completed checklist package. | Block execution if incomplete. |
| 17 | Darwin explicit execution approval captured | unresolved | Capture explicit approval after checklist passes. | Approval reference recorded after pass evidence is reviewed. | Stop, do not execute. | Approval record. | Block execution if missing. |

## 9. Host and Docker baseline check

### 9.1 Host port pre-check

Command:

```powershell
Get-NetTCPConnection -LocalPort 8091 -ErrorAction SilentlyContinue
```

Expected pass result:

- Before starting POS Server: no output, or output is explainable and not a conflicting process.
- After starting POS Server: output confirms the intended POS Server listener on local port 8091.

Failure result:

- Port 8091 is bound by an unintended process.

Corrective action:

- Stop the conflicting process or select a newly approved POS Server port and update every assigned reference.

Stop/abort rule:

- Abort if port ownership is unclear.

Evidence to capture:

- PowerShell output before and after POS Server startup.

### 9.2 Central PMS container status

Command:

```powershell
docker ps --filter "name=exitpass-central-pms"
```

Expected pass result:

- Container `exitpass-central-pms` appears and is running.

Failure result:

- No matching container appears, or the container is exited/unhealthy.

Corrective action:

- Start or recreate Central PMS through the approved development compose workflow. Do not change source code.

Stop/abort rule:

- Abort if the container cannot be started or identified.

Evidence to capture:

- `docker ps` output.

## 10. POS Server startup check

Commands:

```powershell
cd D:\SourceCodes\ExitPass-PoSServer
dotnet run --project src\ExitPass.PosServer.Api\ExitPass.PosServer.Api.csproj --urls "http://localhost:8091"
```

Expected pass result:

- POS Server starts on `http://localhost:8091`.
- No live fiscal document POST is executed during startup.

Failure result:

- Build/startup fails.
- Port binding fails.
- Persistence configuration fails in a way that prevents API startup.

Corrective action:

- Resolve POS Server startup/configuration in the POS Server repo under a separate approved task if changes are needed. This checklist is read-only for the POS Server runtime repository.

Stop/abort rule:

- Abort if POS Server is not running on the approved URL.

Evidence to capture:

- Terminal startup log showing bound URL and any persistence warnings.

## 11. POS Server host health/connectivity check

Repository inspection found no POS Server health endpoint. `Program.cs` maps only the fiscal document endpoint group, and `FiscalDocumentEndpointRouteBuilderExtensions.cs` maps:

- `POST /v1/fiscal-documents/`
- `GET /v1/fiscal-documents/{fiscalDocumentId:guid}`

Do not use `POST /v1/fiscal-documents/` for a dry-run connectivity check.

Baseline command:

```powershell
Invoke-WebRequest -Uri "http://localhost:8091" -UseBasicParsing
```

PowerShell-friendly reachability command when root returns a non-2xx status:

```powershell
try {
    $response = Invoke-WebRequest -Uri "http://localhost:8091" -UseBasicParsing -ErrorAction Stop
    "reachable_http_status=$($response.StatusCode)"
} catch {
    if ($_.Exception.Response) {
        "reachable_http_status=$([int]$_.Exception.Response.StatusCode)"
    } else {
        throw
    }
}
```

Expected pass result:

- Host receives any HTTP response from `localhost:8091`, including a safe 404/405 from root.

Failure result:

- Connection refused, timeout, DNS failure, or TLS/protocol mismatch.

Corrective action:

- Confirm POS Server startup, host binding, firewall, and port.

Stop/abort rule:

- Abort if the host cannot reach POS Server.

Evidence to capture:

- HTTP status output and POS Server log timestamp.

## 12. Central PMS container-to-host connectivity check

Determine the Central PMS Docker network:

```powershell
$centralPmsContainer = "exitpass-central-pms"
$centralPmsNetwork = docker inspect $centralPmsContainer --format '{{range $name, $net := .NetworkSettings.Networks}}{{println $name}}{{end}}' | Select-Object -First 1
$centralPmsNetwork
```

Run a temporary curl container on that network:

```powershell
docker run --rm --network $centralPmsNetwork curlimages/curl:8.8.0 -fsS http://host.docker.internal:8091
```

If root returns non-2xx because there is no health endpoint, use the status-only non-mutating variant:

```powershell
docker run --rm --network $centralPmsNetwork curlimages/curl:8.8.0 -sS -o /dev/null -w "%{http_code}" http://host.docker.internal:8091
```

Expected pass result:

- Temporary container can resolve and reach `http://host.docker.internal:8091`.
- Status-only variant returns an HTTP status, even if root is 404.

Failure result:

- DNS failure, connection refused, timeout, or no Docker network found.

Corrective action:

- Fix Docker Desktop host gateway support, container network selection, or POS Server binding before any execution approval.

Stop/abort rule:

- Abort if Central PMS network cannot reach POS Server host URL.

Evidence to capture:

- Docker network name and curl output.

## 13. Central PMS config verification check

Repository inspection found the actual Central PMS configuration binding:

| Area | Actual section/key | Environment variable form |
| --- | --- | --- |
| POS Server integration section | `FiscalIssuance:PosServerIntegration` | `FiscalIssuance__PosServerIntegration__*` |
| Live-call seam | `EnablePosServerFiscalIssuanceLiveCall` | `FiscalIssuance__PosServerIntegration__EnablePosServerFiscalIssuanceLiveCall` |
| Controlled UAT diagnostic guard | `EnableControlledUatDiagnosticPath` | `FiscalIssuance__PosServerIntegration__EnableControlledUatDiagnosticPath` |
| POS Server base URL | `PosServerBaseUrl` | `FiscalIssuance__PosServerIntegration__PosServerBaseUrl` |
| Timeout | `TimeoutSeconds` | `FiscalIssuance__PosServerIntegration__TimeoutSeconds` |
| Payment-flow live-call guard | `EnableLiveFiscalIssuanceFromPaymentFlow` | `FiscalIssuance__PosServerIntegration__EnableLiveFiscalIssuanceFromPaymentFlow` |
| Exit-flow live-call guard | `EnableLiveFiscalIssuanceFromExitFlow` | `FiscalIssuance__PosServerIntegration__EnableLiveFiscalIssuanceFromExitFlow` |
| ExitAuthorization gating section | `FiscalIssuance:ExitAuthorizationGating` | `FiscalIssuance__ExitAuthorizationGating__*` |
| Fiscal gating enforcement | `EnableFiscalBeforeExitAuthorizationEnforcement` | `FiscalIssuance__ExitAuthorizationGating__EnableFiscalBeforeExitAuthorizationEnforcement` |
| Shadow evaluation | `EnableShadowEvaluation` | `FiscalIssuance__ExitAuthorizationGating__EnableShadowEvaluation` |
| Gating readiness mode | `ReadinessMode` | `FiscalIssuance__ExitAuthorizationGating__ReadinessMode` |

Inspect container environment:

```powershell
docker inspect exitpass-central-pms --format '{{json .Config.Env}}'
```

PowerShell filtering:

```powershell
$envJson = docker inspect exitpass-central-pms --format '{{json .Config.Env}}'
$envJson | ConvertFrom-Json | Where-Object {
    $_ -like 'FiscalIssuance__PosServerIntegration__*' -or
    $_ -like 'FiscalIssuance__ExitAuthorizationGating__*'
}
```

Expected pass result:

- `FiscalIssuance__PosServerIntegration__PosServerBaseUrl=http://host.docker.internal:8091`
- `FiscalIssuance__PosServerIntegration__TimeoutSeconds` is absent/default 10 or positive.

Failure result:

- Base URL is missing, points to `localhost:8091` from inside the container, points to the wrong host, or timeout is non-positive.

Corrective action:

- Update approved Central PMS container configuration and restart the container. Do not modify source code for this dry run.

Stop/abort rule:

- Abort if Central PMS effective config cannot be verified.

Evidence to capture:

- Filtered environment output with no secrets.

## 14. Controlled UAT flag verification check

Required values for the approved diagnostic window only:

```text
FiscalIssuance__PosServerIntegration__EnablePosServerFiscalIssuanceLiveCall=true
FiscalIssuance__PosServerIntegration__EnableControlledUatDiagnosticPath=true
```

Expected pass result:

- Both flags are explicitly true during the approved run window only.
- Readiness status from code path is expected to be `enabled_ready` when base URL and flow guards are valid.

Failure result:

- Either flag is missing/false when preparing for approved execution.
- Flags are true outside the approved diagnostic window.

Corrective action:

- Correct configuration only for the approved window, restart Central PMS, and capture approval.

Stop/abort rule:

- Abort if either flag is not controlled by the approved window.

Evidence to capture:

- Filtered config output, approval reference, and timestamp.

## 15. Payment-flow guard check

Required value:

```text
FiscalIssuance__PosServerIntegration__EnableLiveFiscalIssuanceFromPaymentFlow=false
```

Expected pass result:

- Explicit false or absent/default false.

Failure result:

- Value is true.

Corrective action:

- Set false and restart Central PMS through approved config. Re-run config verification.

Stop/abort rule:

- Abort if payment-flow live-call guard is true.

Evidence to capture:

- Filtered config output showing false or absence with code-default explanation.

## 16. Exit-flow guard check

Required value:

```text
FiscalIssuance__PosServerIntegration__EnableLiveFiscalIssuanceFromExitFlow=false
```

Expected pass result:

- Explicit false or absent/default false.

Failure result:

- Value is true.

Corrective action:

- Set false and restart Central PMS through approved config. Re-run config verification.

Stop/abort rule:

- Abort if exit-flow live-call guard is true.

Evidence to capture:

- Filtered config output showing false or absence with code-default explanation.

## 17. Fiscal gating enforcement check

Required value:

```text
FiscalIssuance__ExitAuthorizationGating__EnableFiscalBeforeExitAuthorizationEnforcement=false
```

Expected pass result:

- Explicit false or absent/default false.
- `ReadinessMode` is absent/default `readiness_only` or explicitly `readiness_only`.

Failure result:

- Enforcement flag is true.

Corrective action:

- Set enforcement false and restart Central PMS through approved config. Re-run config verification.

Stop/abort rule:

- Abort if fiscal gating enforcement is true.

Evidence to capture:

- Filtered config output.

## 18. POS Server fiscal identity/policy/sequence availability check

### 18.1 Connection string discovery

POS Server code reads the database connection string from these configuration sources:

- `ConnectionStrings:PosServer`
- `POSSERVER_DB_URL`
- `PosServer:Database:ConnectionString`

Discovery commands:

```powershell
Get-ChildItem Env:POSSERVER_DB_URL
```

If the value is not available from environment, inspect the approved POS Server launch profile, local secrets, or runtime configuration. Do not print secrets into shared evidence. Record only the source and a redacted reference.

Operator-supplied variable for SQL checks:

```powershell
$posServerConnectionString = "<approved-redacted-pos-server-npgsql-connection-string>"
```

### 18.2 SQL verification guidance

Use actual table and column names from `D:\SourceCodes\ExitPass-PoSServer\db\state`.

Command pattern:

```powershell
psql "$posServerConnectionString" -c "<SQL>"
```

Site POS Server, fiscal identity, effective identity assignment, sequence policy, sequence state, and fiscal document type check:

```sql
select
    sps.site_pos_server_id,
    sps.site_pos_server_code,
    sps.central_pms_site_ref,
    sps.is_active as site_pos_server_is_active,
    fi.fiscal_identity_id,
    fi.fiscal_identity_code,
    fi.is_active as fiscal_identity_is_active,
    h.effective_start_at as identity_effective_start_at,
    h.effective_end_at as identity_effective_end_at,
    fsp.fiscal_sequence_policy_id,
    fsp.policy_code,
    fsp.effective_start_at as policy_effective_start_at,
    fsp.effective_end_at as policy_effective_end_at,
    fss.fiscal_sequence_state_id,
    fss.current_sequence_value,
    fss.last_reserved_sequence_value,
    fss.last_issued_sequence_value,
    doc_type.code_key as fiscal_document_type_code_key
from pos.site_pos_servers sps
left join pos.site_pos_server_fiscal_identity_history h
    on h.site_pos_server_id = sps.site_pos_server_id
    and h.effective_start_at <= now()
    and (h.effective_end_at is null or h.effective_end_at > now())
left join pos.fiscal_identities fi
    on fi.fiscal_identity_id = h.fiscal_identity_id
left join pos.fiscal_sequence_policies fsp
    on fsp.site_pos_server_id = sps.site_pos_server_id
    and fsp.effective_start_at <= now()
    and (fsp.effective_end_at is null or fsp.effective_end_at > now())
left join pos.fiscal_sequence_states fss
    on fss.fiscal_sequence_policy_id = fsp.fiscal_sequence_policy_id
left join pos.controlled_codes doc_type
    on doc_type.controlled_code_id = fsp.document_type_code_id
where sps.site_pos_server_code = 'DEV-POS-SERVER-ATC-001'
  and fi.fiscal_identity_code = 'DEV-FISCAL-IDENTITY-ATC-001'
  and fsp.policy_code = 'DEV-SI-SEQUENCE-POLICY-ATC-001';
```

Fiscal document type code check if policy query does not expose a document type:

```sql
select
    ccs.code_set_key,
    cc.controlled_code_id,
    cc.code_key,
    cc.display_name,
    cc.is_active,
    cc.effective_start_at,
    cc.effective_end_at
from pos.controlled_codes cc
join pos.controlled_code_sets ccs
    on ccs.controlled_code_set_id = cc.controlled_code_set_id
where cc.code_key = 'sales_invoice';
```

Expected pass result:

- Site POS Server `DEV-POS-SERVER-ATC-001` exists and `is_active = true`.
- Fiscal identity `DEV-FISCAL-IDENTITY-ATC-001` exists and `is_active = true`.
- Effective identity assignment exists in `pos.site_pos_server_fiscal_identity_history`.
- Sequence policy `DEV-SI-SEQUENCE-POLICY-ATC-001` exists, is effective at runtime, and is linked to the Site POS Server.
- Sequence state exists for the sequence policy and values are non-negative.
- `sales_invoice` controlled code exists and is active/effective if the code set uses effective dates.
- Production fiscal sequence is not used.

Failure result:

- Any required row is missing, inactive, not currently effective, not linked, or sequence state is absent.

Corrective action:

- Stop. Seed or correct dev POS Server fiscal configuration only through an approved POS Server configuration/data task.

Stop/abort rule:

- Abort if any fiscal identity/policy/sequence/type check fails or if the SQL result cannot be interpreted.

Evidence to capture:

- Redacted SQL output, runtime database reference, timestamp, and reviewer.

## 19. Test transaction ref and semantic request check

Inspection method:

- Compare the planned harness request with the filled data assignment record.
- Confirm all refs are development-only symbolic refs or approved dev IDs.

Expected pass result:

- Run ID, correlation ID, Site POS Server ref, parking session ref, payment attempt ref, payment confirmation ref, payable basis ref, upstream finality ref, business day, currency, amount, lines, tenders, taxes, and totals match the assignment record.
- No runtime value introduces real production customer/payment data.

Failure result:

- Any ref differs without approved supersession.
- Any value is production customer data or uncontrolled data.

Corrective action:

- Stop, update the data assignment record through an approved fill/review task, and rerun readiness review.

Stop/abort rule:

- Abort if semantic request stability cannot be proven.

Evidence to capture:

- Filled assignment record reference and reviewer checklist.

## 20. Upstream finality/idempotency check

Required upstream finality ref:

```text
CPS-POS-UAT:CPS-POS-UAT-20260703-DEV-ATC-001:newly_created:001
```

Expected pass result:

- One semantic request maps to this upstream finality ref.
- First run scenario is newly_created only.
- Replay is not included in this first run.
- Conflict bypass by new upstream finality ref is prohibited.

Failure result:

- Upstream finality ref changes, is reused for another semantic request, or replay/conflict is added without approval.

Corrective action:

- Stop and revise scenario assignment before execution.

Stop/abort rule:

- Abort if upstream finality is unstable.

Evidence to capture:

- Upstream finality review note.

## 21. Evidence folder creation check

Commands:

```powershell
$evidenceFolder = "D:\ExitPass-UAT-Evidence\DEV-CONTROLLED-UAT-LOCAL\DEV-SITE-ATC-001\2026-07-03\CPS-POS-UAT-20260703-DEV-ATC-001"
New-Item -ItemType Directory -Force -Path $evidenceFolder
Test-Path $evidenceFolder
```

Expected pass result:

- `Test-Path` returns `True`.
- Folder is outside the source repository.

Failure result:

- Folder cannot be created, is inaccessible, or resolves inside the source repository without an approved local dry-run exception.

Corrective action:

- Create an approved controlled evidence location and update the assignment record if path changes.

Stop/abort rule:

- Abort if evidence location is unavailable.

Evidence to capture:

- Folder path, `Test-Path` output, and access owner.

## 22. Evidence manual-save/hash check

Hash command after evidence export:

```powershell
Get-FileHash -Algorithm SHA256 "<path-to-evidence.json>"
```

Expected pass result:

- Evidence JSON is saved manually under the approved evidence folder.
- SHA-256 hash is recorded in `controlled-posserver-fiscal-uat-CPS-POS-UAT-20260703-DEV-ATC-001-hash.txt`.

Failure result:

- Evidence file is missing, overwritten, manually edited without supersession, or hash cannot be computed.

Corrective action:

- Stop and follow the manual-save procedure. Do not use an automatic writer.

Stop/abort rule:

- Abort if evidence cannot be preserved and hashed.

Evidence to capture:

- Evidence file name, hash output, hash file, and manual-save review note.

## 23. Sensitive-data preflight check

Confirm the request and evidence package contain no:

- PAN;
- CVV;
- tokens;
- credentials;
- secrets;
- raw provider callback payloads;
- raw entitlement evidence;
- uncontrolled images/files;
- unmanaged customer PII;
- free-form sensitive blobs;
- unmasked plate/ticket unless explicitly approved.

Suggested safe-text scan for the evidence folder after files exist:

```powershell
rg -n -i "pan|cvv|token|secret|credential|password|provider callback|provider_callback|raw payload|raw_payload|entitlement image|base64 image|unmanaged pii|customer_pii" "D:\ExitPass-UAT-Evidence\DEV-CONTROLLED-UAT-LOCAL\DEV-SITE-ATC-001\2026-07-03\CPS-POS-UAT-20260703-DEV-ATC-001"
```

Expected pass result:

- No prohibited sensitive markers are found.

Failure result:

- Any sensitive marker is found or suspected.

Corrective action:

- Stop, restrict access, notify the redaction owner, and create a deviation record.

Stop/abort rule:

- Abort if sensitive data is detected.

Evidence to capture:

- Sensitive-data checklist and scan output.

## 24. Invocation method verification

Repository inspection outcome:

`Outcome B: No safe existing invocation method exists.`

Evidence:

- `FiscalIssuanceControlledUatHarness` exists as an application-level service/helper with `ExecuteAsync(...)`.
- `RunPosServerFiscalIssuanceDiagnosticAsync(...)` exists on `IFiscalIssuancePosServerLiveIntegrationService`.
- Unit tests invoke the harness and diagnostic seam with mocked dependencies.
- No Central PMS API endpoint, CLI/tool, hosted service, or operator action was found that invokes the controlled UAT harness.

Execution implication:

- Actual diagnostic execution remains blocked even if all runtime pre-execution checks pass.
- Do not fake invocation through payment confirmation, ExitAuthorization, existing ops endpoints, direct database changes, or ad hoc source edits.

Corrective action:

- Create the smallest compliant next implementation task to add a controlled invocation surface, without production payment/exit wiring and without fiscal gating enforcement.

Stop/abort rule:

- Abort any execution attempt until an approved invocation surface exists and has its own tests/runbook update.

Evidence to capture:

- Source search output showing no endpoint/CLI/tooling invocation path.

## 25. Stop/abort conditions

Abort immediately if any of these occur:

- POS Server is not running on the approved URL.
- Central PMS cannot reach `host.docker.internal:8091`.
- Central PMS config cannot be verified.
- `PosServerBaseUrl` is missing or wrong.
- controlled UAT flags are not explicitly controlled by the approved window.
- payment-flow guard is true.
- exit-flow guard is true.
- fiscal gating enforcement is true.
- POS Server fiscal identity/policy/sequence/type checks fail.
- evidence folder is unavailable.
- sensitive data is detected.
- payment finality mutation is observed.
- ExitAuthorization is issued.
- gate behavior is triggered.
- endpoint/CLI/tooling is introduced without explicit scope approval.
- no safe invocation method exists.
- Darwin explicit execution approval is missing.

## 26. Dry-run checklist pass criteria

This checklist is considered passed only when:

- every required pre-execution check has pass evidence;
- all failure/corrective-action items are closed;
- no source/SQL/generated/DOCX/POS Server runtime files were changed by this checklist task;
- no live POS Server fiscal document POST was executed;
- no fiscal document was created;
- no payment finality mutation occurred;
- no ExitAuthorization was issued;
- no gate behavior occurred;
- manual-save evidence path is ready;
- Darwin reviews the checklist evidence.

Because no safe invocation method currently exists, checklist pass does not authorize execution. It only permits moving to the controlled invocation-surface task.

## 27. Explicit execution approval gate

Actual execution requires a separate explicit approval from Darwin Pasco after:

- this checklist has been followed;
- all runtime checks pass;
- a safe controlled invocation surface exists;
- the execution method has been documented;
- evidence capture location is ready;
- non-goals and authority boundaries are reconfirmed.

Approval must record:

- run ID;
- date/time;
- approver;
- evidence package reference;
- checklist pass reference;
- invocation method reference;
- exact approval statement for the first controlled diagnostic call.

## 28. Commands appendix

### Host and POS Server commands

```powershell
Get-NetTCPConnection -LocalPort 8091 -ErrorAction SilentlyContinue

cd D:\SourceCodes\ExitPass-PoSServer
dotnet run --project src\ExitPass.PosServer.Api\ExitPass.PosServer.Api.csproj --urls "http://localhost:8091"

Invoke-WebRequest -Uri "http://localhost:8091" -UseBasicParsing
```

### Docker connectivity commands

```powershell
docker ps --filter "name=exitpass-central-pms"

$centralPmsContainer = "exitpass-central-pms"
$centralPmsNetwork = docker inspect $centralPmsContainer --format '{{range $name, $net := .NetworkSettings.Networks}}{{println $name}}{{end}}' | Select-Object -First 1
$centralPmsNetwork

docker run --rm --network $centralPmsNetwork curlimages/curl:8.8.0 -fsS http://host.docker.internal:8091
docker run --rm --network $centralPmsNetwork curlimages/curl:8.8.0 -sS -o /dev/null -w "%{http_code}" http://host.docker.internal:8091
```

### Central PMS config commands

```powershell
docker inspect exitpass-central-pms --format '{{json .Config.Env}}'

$envJson = docker inspect exitpass-central-pms --format '{{json .Config.Env}}'
$envJson | ConvertFrom-Json | Where-Object {
    $_ -like 'FiscalIssuance__PosServerIntegration__*' -or
    $_ -like 'FiscalIssuance__ExitAuthorizationGating__*'
}
```

### Evidence folder and hash commands

```powershell
$evidenceFolder = "D:\ExitPass-UAT-Evidence\DEV-CONTROLLED-UAT-LOCAL\DEV-SITE-ATC-001\2026-07-03\CPS-POS-UAT-20260703-DEV-ATC-001"
New-Item -ItemType Directory -Force -Path $evidenceFolder
Test-Path $evidenceFolder

Get-FileHash -Algorithm SHA256 "<path-to-evidence.json>"
```

### Source safety commands

```powershell
git status --short --untracked-files=all
git diff --name-only
git ls-files --others --exclude-standard
rg -n "IFiscalIssuanceControlledUatHarness|FiscalIssuanceControlledUatHarness|RunPosServerFiscalIssuanceDiagnosticAsync|MapPost|MapGroup" src\Services\CentralPms\src src\Services\CentralPms\tests
```

Pass result:

- Only approved documentation files are changed in this branch.
- No new endpoint/CLI/tooling/source/SQL/generated/DOCX/POS Server runtime file appears.

## 29. Remaining blockers after checklist creation

- No safe existing runtime invocation method exists for the application-level controlled UAT harness.
- POS Server must still be started on `http://localhost:8091` before a later approved execution attempt.
- Central PMS Docker connectivity to `http://host.docker.internal:8091` must still be verified at runtime.
- Central PMS config must still be set and verified at runtime.
- POS Server dev fiscal identity/policy/sequence/type availability must still be verified in the runtime database.
- Evidence folder must still be created before execution.
- Darwin explicit execution approval must still be captured after all checks and after an approved invocation surface exists.

## 30. Recommended next task

Recommended next branch:

`feature/central-pms-pos-server-controlled-uat-invocation-surface`

Purpose:

Add the smallest safe controlled invocation surface for the application-level UAT harness without exposing production payment/exit flows, without enabling fiscal gating enforcement, and without adding uncontrolled operator capability.

## 31. Requirements traceability summary

| Requirement | Covered by |
| --- | --- |
| Carry forward readiness decision and filled values | Sections 5, 7 |
| Convert blockers into command-based checks | Sections 8-24 |
| Document actual config keys | Sections 13-17 |
| Verify POS Server fiscal config using actual schema names | Section 18 |
| Confirm no UAT execution or fiscal document creation | Sections 2, 4, 25-27 |
| Preserve payment/exit/gate authority boundaries | Sections 3, 15-17, 25 |
| Confirm no endpoint/CLI/tooling introduced | Sections 4, 8, 24, 28 |
| Determine invocation method reality | Section 24 |
| Provide exact commands | Section 28 |
| Recommend next task based on invocation outcome | Section 30 |
