# ExitPass Central PMS POS Server Controlled UAT First Run Readiness Refresh v1.0

## 1. Document Control

| Field | Value |
| --- | --- |
| Document | ExitPass Central PMS POS Server Controlled UAT First Run Readiness Refresh |
| Version | v1.0 |
| Date | 2026-07-03 |
| Branch | feature/central-pms-pos-server-controlled-uat-first-run-readiness-refresh |
| Scope | Documentation/readiness-refresh only |
| Source of truth | Filled Controlled UAT Data Assignment Record v1.0 |
| Refreshed readiness decision | ready_for_dry_run_checklist |
| Execution decision | not_ready_for_execution |

## 2. Purpose and Scope

This refresh reviews the first controlled Central PMS to POS Server fiscal issuance diagnostic run readiness using the filled small-organization development data assignment values.

The previous readiness review was `not_ready_for_execution` because environment, Site/Site POS Server, fiscal configuration, transaction references, upstream finality reference, evidence save location, owners, and approvals were missing.

Those planning values are now filled for development-only UAT preparation. This refresh decides whether the project can move to execution dry-run checklist preparation.

This refresh does not execute UAT, call POS Server, create a fiscal document, approve execution, or change runtime behavior.

## 3. Current Implementation Baseline

The current baseline has:

- controlled UAT operator runbook
- controlled UAT evidence template
- controlled UAT harness planning
- controlled UAT manual-save procedure
- controlled UAT approved test data plan
- controlled UAT first-run readiness review
- controlled UAT data assignment record
- controlled UAT data assignment review
- controlled UAT data assignment fill
- application-level controlled UAT harness
- safe evidence JSON exporter
- disabled/default-safe POS Server live-call seam
- controlled diagnostic seam
- no API endpoint for controlled UAT invocation
- no CLI or operator tooling for controlled UAT invocation
- no automatic evidence file-writing
- no payment confirmation wiring
- no ExitAuthorization wiring
- no fiscal gating enforcement
- no retry scheduler
- no GET readback worker

## 4. Authority Boundaries

- Central PMS owns payment finality.
- Central PMS owns fiscal reference recording.
- Central PMS owns normal ExitAuthorization.
- POS Server owns fiscal issuance and numbering only.
- POS Server response is fiscal issuance evidence only.
- POS Server does not approve payment, exit, gate opening, entitlement, manual release, or continuity.
- Manual release is not normal ExitAuthorization.
- UAT readiness evidence and test data are audit artifacts only and do not create operational authority.

## 5. Non-Goals

This readiness refresh does not:

- execute UAT
- execute live POS Server calls
- create fiscal documents
- add endpoint or tooling
- implement file-writing
- enable payment/exit production flow
- issue ExitAuthorization
- enforce fiscal gating
- implement retry
- implement GET readback worker
- implement Operator Console queue
- implement Dashboard projection
- modify source code
- modify SQL
- modify POS Server runtime

## 6. Refresh Method

The filled data assignment record is the source of truth.

Each readiness area is marked with one of:

- `ready_for_dry_run_checklist`
- `partially_ready`
- `blocked`
- `deferred`
- `not_applicable`

Decision boundary:

- This refresh may decide `ready_for_dry_run_checklist` if planning values are assigned.
- This refresh must not decide `ready_for_execution`.
- Actual execution requires a later execution dry-run checklist and runtime confirmation.

No live service probes, POS Server calls, fiscal document creation, or runtime mutations were performed.

## 7. Previous Readiness Decision Summary

Previous decision: `not_ready_for_execution`

Previous reasons:

- environment values were missing
- Site/Site POS Server values were missing
- POS Server fiscal identity, policy, and sequence values were missing
- Central PMS diagnostic configuration values were missing
- test parking/payment/payable references were missing
- upstream finality reference was missing
- evidence save location was missing
- owners and approval references were missing
- scenario sequencing was not approved

The filled data assignment record now resolves these planning-data gaps for development planning only.

## 8. Filled Assignment Summary

### Environment

| Field | Filled value |
| --- | --- |
| Environment name | DEV-CONTROLLED-UAT-LOCAL |
| Central PMS environment | CentralPMS-DEV-DOCKER |
| Central PMS base URL | http://localhost:8080 |
| POS Server environment | PoSServer-DEV-LOCAL |
| POS Server host/browser URL | http://localhost:8091 |
| POS Server base URL reference for Central PMS | PosServerBaseUrl = http://host.docker.internal:8091 |
| Production or non-production | Non-production |
| Run date/time window | 2026-07-03 14:00-16:00 PHT |

### Site / Site POS Server

| Field | Filled value |
| --- | --- |
| Site name | DEV Site - Alabang Town Center |
| Site ref / Site ID | DEV-SITE-ATC-001 |
| Site POS Server ref / ID | DEV-POS-SERVER-ATC-001 |
| Site POS Server environment | PoSServer-DEV-LOCAL |
| Site POS Server base URL reference | http://host.docker.internal:8091 |

### POS Server Fiscal Setup

| Field | Filled value |
| --- | --- |
| Fiscal identity ref / ID | DEV-FISCAL-IDENTITY-ATC-001 |
| Fiscal sequence policy ref / ID | DEV-SI-SEQUENCE-POLICY-ATC-001 |
| Fiscal sequence state ref / ID | DEV-SI-SEQUENCE-STATE-ATC-001 |
| Fiscal document type | sales_invoice |
| Using production fiscal sequence | No |
| Fiscal number allocation impact accepted by | Darwin Pasco |

### Run Identity

| Field | Filled value |
| --- | --- |
| Run ID | CPS-POS-UAT-20260703-DEV-ATC-001 |
| Correlation ID | 00000000-0000-4000-8000-000000000101 |
| Expected run type | newly_created |

### Upstream Finality

| Field | Filled value |
| --- | --- |
| Upstream finality ref | CPS-POS-UAT:CPS-POS-UAT-20260703-DEV-ATC-001:newly_created:001 |
| One semantic request confirmation | Yes |
| Conflict bypass prohibition acknowledgement | Yes |
| Replay ref reuse confirmation | Not applicable for first run |

### Test Transaction Refs

| Field | Filled value |
| --- | --- |
| Parking session ref | DEV-PARKING-SESSION-ATC-001 |
| Payment attempt ref | DEV-PAYMENT-ATTEMPT-ATC-001 |
| Payment confirmation ref | DEV-PAYMENT-CONFIRMATION-ATC-001 |
| Payable basis ref | DEV-PAYABLE-BASIS-ATC-001 |
| Business day date | 2026-07-03 |
| Currency | PHP |
| Amount minor units | 10000 |

### Fiscal Request Facts

| Field | Filled value |
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

| Field | Filled value |
| --- | --- |
| Evidence save mode | Mode B temporary controlled location |
| Evidence save folder/reference | D:\ExitPass-UAT-Evidence\DEV-CONTROLLED-UAT-LOCAL\DEV-SITE-ATC-001\2026-07-03\CPS-POS-UAT-20260703-DEV-ATC-001 |
| Evidence owner | Darwin Pasco |
| Ticket/change/reference | DEV-UAT-CPS-POS-001 |
| Hash required | Yes |
| Hash command | Get-FileHash -Algorithm SHA256 "<path-to-evidence.json>" |

### Safety Confirmations

| Check | Filled value |
| --- | --- |
| Payment-flow guard is false | Yes |
| Exit-flow guard is false | Yes |
| Fiscal gating enforcement is false | Yes |
| No endpoint/CLI/tooling used | Yes |
| No retry/readback worker involved | Yes |
| No gate behavior involved | Yes |
| No ExitAuthorization issued | Yes |

### Sensitive Data Check

| Check | Filled value |
| --- | --- |
| No PAN | Yes |
| No CVV | Yes |
| No tokens/secrets/credentials | Yes |
| No raw provider callback payload | Yes |
| No unmanaged customer PII | Yes |
| No raw entitlement evidence | Yes |
| No uncontrolled images/files | Yes |
| No free-form sensitive blobs | Yes |
| No unmasked plate/ticket unless approved | Yes |

### Accountable Owners

| Role | Owner |
| --- | --- |
| UAT accountable owner | Darwin Pasco |
| Engineering/config owner | Darwin Pasco |
| POS Server/fiscal owner | Darwin Pasco |
| Central PMS owner | Darwin Pasco |
| Site owner | Darwin Pasco |
| Operations lead | Darwin Pasco |
| Evidence owner | Darwin Pasco |
| Final go/no-go owner | Darwin Pasco |

### Scenario Scope

| Field | Filled value |
| --- | --- |
| First scenario ID | SCN-NEWLY-CREATED-001 |
| First run expected type | newly_created |
| Replay included | No |
| Conflict included | No |
| Failure included | No |
| Unknown included | No |
| Scenario sequencing decision | Run newly_created only for first controlled UAT diagnostic |

## 9. Environment Readiness Refresh

Status: `ready_for_dry_run_checklist`

Rationale:

- development environment name is assigned
- Central PMS development environment is assigned
- POS Server development environment is assigned
- non-production posture is assigned
- intended diagnostic window is assigned
- container-to-host POS Server URL reference is assigned

Runtime caveats:

- POS Server must still be started on http://localhost:8091 before actual UAT.
- Central PMS config must be set to `PosServerBaseUrl = http://host.docker.internal:8091` before actual UAT.
- Docker container-to-host connectivity must be verified during dry-run checklist.

## 10. Site / Site POS Server Readiness Refresh

Status: `ready_for_dry_run_checklist`

Rationale:

- development Site is assigned
- development Site POS Server is assigned
- Site POS Server environment is assigned
- Site POS Server base URL reference is assigned
- expected fiscal identity, sequence policy, and sequence state refs are assigned

Runtime caveats:

- This refresh does not prove these map to actual runtime rows.
- The dry-run checklist must verify mapping before any actual diagnostic call.

## 11. POS Server Fiscal Configuration Readiness Refresh

Status: `partially_ready`

Rationale:

- development fiscal identity ref is assigned
- development fiscal sequence policy ref is assigned
- development fiscal sequence state ref is assigned
- fiscal document type is assigned as `sales_invoice`
- production fiscal sequence is not used
- fiscal number allocation impact is accepted for development planning

Runtime caveats:

- development fiscal identity existence must be verified
- development fiscal identity active/effective state must be verified
- development fiscal sequence policy existence must be verified
- development fiscal sequence policy active/effective state must be verified
- development fiscal sequence state existence/configuration must be verified
- POS Server runtime must be running before execution

## 12. Central PMS Configuration Readiness Refresh

Status: `partially_ready`

Rationale:

- intended live-call flag value is assigned for the approved diagnostic window only
- intended controlled UAT diagnostic path flag value is assigned for the approved diagnostic window only
- payment-flow guard false is assigned
- exit-flow guard false is assigned
- fiscal gating enforcement false is assigned
- no endpoint/CLI/tooling is assigned
- no retry/readback worker is assigned

Dry-run checklist must confirm:

- `PosServerBaseUrl = http://host.docker.internal:8091`
- `EnablePosServerFiscalIssuanceLiveCall = true` only during approved diagnostic window
- `EnableControlledUatDiagnosticPath = true` only during approved diagnostic window
- payment-flow guard false
- exit-flow guard false
- fiscal gating enforcement false
- no endpoint/CLI/tooling path
- no payment/exit production wiring

## 13. Test Transaction Data Readiness Refresh

Status: `ready_for_dry_run_checklist`

Rationale:

- run id is assigned
- correlation id is assigned
- development parking session ref is assigned
- development payment attempt ref is assigned
- development payment confirmation ref is assigned
- development payable basis ref is assigned
- business day date is assigned
- currency and amount are assigned
- expected run type is `newly_created`

Runtime caveat:

- symbolic refs must be accepted by the harness/POS Server mapping or replaced by actual development IDs during the dry-run checklist.

## 14. Upstream Finality Reference Readiness Refresh

Status: `ready_for_dry_run_checklist`

Rationale:

- stable upstream finality ref is assigned
- approved pattern is followed
- one semantic request is confirmed
- conflict bypass prohibition is acknowledged
- replay is not applicable for the first run

The assigned reference is:

```text
CPS-POS-UAT:CPS-POS-UAT-20260703-DEV-ATC-001:newly_created:001
```

## 15. Fiscal Request Facts Readiness Refresh

Status: `ready_for_dry_run_checklist`

Rationale:

- fiscal document type is assigned
- business day date is assigned
- Site and Site POS Server refs are assigned
- parking/payment/payable refs are assigned
- upstream finality ref is assigned
- currency, amount, line count, tender count, tax detail, totals, and correlation id are assigned
- totals match payable basis

Runtime caveat:

- the dry-run checklist must verify the harness can use the assigned development symbolic refs or substitute approved runtime IDs without changing semantic request intent.

## 16. Evidence Manual-Save Readiness Refresh

Status: `ready_for_dry_run_checklist`

Rationale:

- Mode B temporary controlled location is assigned
- evidence owner is assigned
- ticket/change reference is assigned
- hash requirement is assigned
- hash command is assigned
- manual-save procedure exists

Runtime caveat:

- evidence folder should be created before execution.
- no automatic file writer is approved or introduced by this refresh.

## 17. Sensitive-Data Exclusion Readiness Refresh

Status: `ready_for_dry_run_checklist`

Rationale:

- no PAN confirmed
- no CVV confirmed
- no tokens/secrets/credentials confirmed
- no raw provider callback payload confirmed
- no unmanaged customer PII confirmed
- no raw entitlement evidence confirmed
- no uncontrolled images/files confirmed
- no free-form sensitive blobs confirmed
- no unmasked plate/ticket values included in assignment

The assigned development values do not include sensitive payment, provider, entitlement, credential, token, or unmanaged customer data.

## 18. Scenario Readiness Refresh

Status: `ready_for_dry_run_checklist`

Rationale:

- first scenario id is assigned
- first expected run type is `newly_created`
- scenario sequencing decision is assigned
- replay, conflict, failure, and unknown are excluded

First controlled run recommendation: `newly_created` only.

## 19. Replay/Conflict/Failure/Unknown Readiness Refresh

Status: `deferred`

Rationale:

- replay is not included
- conflict is not included
- failure is not included
- unknown is not included
- first diagnostic run is scoped to `newly_created` only

If any of these scenarios are later included, they require separate assignment, approval, evidence planning, and readiness review.

## 20. Runtime Verification Still Required

The following runtime checks remain required before any actual diagnostic execution:

- POS Server process running on http://localhost:8091
- Central PMS can resolve `host.docker.internal:8091` from Docker container
- Central PMS config points to `PosServerBaseUrl = http://host.docker.internal:8091`
- development fiscal identity exists and is active/effective
- development fiscal sequence policy exists and is active/effective
- development fiscal sequence state exists/configured
- controlled UAT flags enabled only for approved window
- payment-flow guard false
- exit-flow guard false
- fiscal gating enforcement false
- evidence folder exists
- no endpoint/CLI/tooling used
- no payment/exit production wiring
- no gate behavior

## 21. Final Refreshed Readiness Decision

Decision: `ready_for_dry_run_checklist`

The project may proceed to execution dry-run checklist preparation.

The project is not ready for execution.

Decision wording:

The project may proceed to execution dry-run checklist preparation, but must not execute the diagnostic call until the dry-run checklist confirms runtime availability, configuration, guard posture, evidence folder readiness, and fiscal configuration availability.

## 22. Conditions Before Execution Dry-Run Checklist

Before preparing or executing the dry-run checklist:

- merge or carry forward this readiness refresh
- keep branch state clean
- create next branch for execution dry-run checklist
- include exact commands to start POS Server on port 8091
- include exact checks for Central PMS container-to-host connectivity
- include exact config verification for `PosServerBaseUrl`
- include evidence folder creation check
- include no-payment/no-exit/no-gate safety checks
- include fiscal identity/policy/sequence runtime verification checks
- include confirmation that replay/conflict/failure/unknown remain out of scope

## 23. Conditions Before Actual Execution

Actual execution remains blocked until:

- execution dry-run checklist exists
- POS Server runtime is started
- Central PMS config is set
- development fiscal config exists
- evidence folder exists
- safety guards are verified
- dry-run checklist passes
- user explicitly approves execution

This readiness refresh alone does not authorize execution.

## 24. Risks

| Risk | Impact | Control |
| --- | --- | --- |
| POS Server not running at execution time | Diagnostic call cannot proceed | Verify runtime during dry-run checklist. |
| Central PMS container cannot resolve host URL | Diagnostic call cannot reach POS Server | Verify Docker host connectivity. |
| Fiscal identity/policy/sequence refs do not exist | POS Server fiscal issuance fails | Verify runtime rows before execution. |
| Evidence folder missing | Evidence package may be lost or delayed | Create and verify folder before execution. |
| Config guards incorrect | Payment/exit or enforcement boundaries could be violated | Verify flags and guard values before execution. |
| Symbolic refs not accepted by harness/runtime | Request mapping may fail | Dry-run checklist must confirm or substitute approved dev IDs without changing semantic intent. |
| Unknown outcome without readback plan | Ambiguous fiscal state | Keep unknown scenario out of scope and abort if encountered. |

## 25. Open Blockers

Open blockers before actual execution:

- POS Server must be started on http://localhost:8091.
- Central PMS config must be set to `PosServerBaseUrl = http://host.docker.internal:8091`.
- Development fiscal identity must be verified active/effective.
- Development fiscal sequence policy must be verified active/effective.
- Development fiscal sequence state must be verified configured.
- Controlled UAT flags and safety guards must be verified.
- Evidence folder must be created.
- Execution dry-run checklist must be created and passed.
- User must explicitly approve execution.

No blocker remains for preparing the execution dry-run checklist.

## 26. Recommended Next Branch/Task

Recommended next branch:

`feature/central-pms-pos-server-controlled-uat-execution-dry-run-checklist`

Purpose:

Create the final execution dry-run checklist with commands and verification steps for POS Server startup, Central PMS config, Docker host connectivity, fiscal config availability, evidence folder creation, and safety guard confirmation before the first controlled UAT diagnostic call.

## 27. Requirements Traceability Summary

| Requirement | Trace |
| --- | --- |
| Use filled assignment record as source of truth | Sections 6, 8 |
| Summarize previous not-ready decision | Section 7 |
| Summarize filled development values | Section 8 |
| Refresh readiness by area | Sections 9 through 19 |
| Preserve runtime verification caveats | Sections 9 through 12, 20 |
| Decide ready for dry-run checklist, not execution | Section 21 |
| Define dry-run checklist conditions | Section 22 |
| Define actual execution conditions | Section 23 |
| Preserve authority boundaries | Section 4 |
| Preserve non-goals | Section 5 |

