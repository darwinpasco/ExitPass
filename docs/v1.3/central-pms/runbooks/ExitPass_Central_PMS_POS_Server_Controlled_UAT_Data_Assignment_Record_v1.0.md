# ExitPass Central PMS POS Server Controlled UAT Data Assignment Record v1.0

## 1. Document Control

| Field | Value |
| --- | --- |
| Document | ExitPass Central PMS POS Server Controlled UAT Data Assignment Record |
| Version | v1.0 |
| Date | 2026-07-03 |
| Branch | feature/central-pms-pos-server-controlled-uat-data-assignment-fill |
| Scope | Documentation-fill only |
| Assignment posture | Development-only values for first controlled UAT planning |
| Default assignment decision | ready_for_readiness_review |
| Completion required before | Refreshed first-run readiness review |

This fill does not execute UAT. It does not prove POS Server is currently running. It does not prove the development fiscal identity, fiscal sequence policy, or fiscal sequence state rows exist. The next readiness refresh must verify runtime/config evidence before execution.

## 2. Purpose and Scope

This record captures the assigned development values, consolidated small-organization ownership, and approval references required before the first controlled Central PMS to POS Server fiscal issuance diagnostic run can be reviewed again.

This record closes the preparation gap identified by the earlier data assignment review. It moves the assignment record from `incomplete` to `ready_for_readiness_review` using non-production values only.

This record does not authorize execution by itself. After this fill, a refreshed first-run readiness review must determine whether the project can proceed to execution dry-run checklist preparation.

## 3. Current Implementation Baseline

The current implementation and documentation baseline has:

- controlled UAT operator runbook
- controlled UAT evidence template
- controlled UAT harness planning
- controlled UAT manual-save procedure
- controlled UAT approved test data plan
- controlled UAT first-run readiness review
- controlled UAT data assignment record
- controlled UAT data assignment review
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

This fill does not:

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

## 6. Data Assignment Summary

Status values:

- `not_started`
- `assigned_pending_approval`
- `approved`
- `rejected`
- `deferred`
- `not_applicable`

| Assignment area | Required owner | Assigned value | Status | Approval reference | Notes |
| --- | --- | --- | --- | --- | --- |
| Owner and approvals | Darwin Pasco | Small-org consolidated ownership | approved | DEV-UAT-CPS-POS-001 | Development assignment attestation. |
| Environment | Darwin Pasco | DEV-CONTROLLED-UAT-LOCAL | approved | DEV-UAT-CPS-POS-001 | Non-production only. |
| Site / Site POS Server | Darwin Pasco | DEV-SITE-ATC-001 / DEV-POS-SERVER-ATC-001 | approved | DEV-UAT-CPS-POS-001 | Development symbolic refs. |
| POS Server fiscal configuration | Darwin Pasco | DEV-FISCAL-IDENTITY-ATC-001 / DEV-SI-SEQUENCE-POLICY-ATC-001 / DEV-SI-SEQUENCE-STATE-ATC-001 | approved | DEV-UAT-CPS-POS-001 | Runtime existence must be verified in readiness refresh. |
| Central PMS configuration | Darwin Pasco | Controlled diagnostic flags intended for approved window; payment/exit guards false | approved | DEV-UAT-CPS-POS-001 | Config must be verified before execution. |
| Test transaction references | Darwin Pasco | DEV-PARKING/PAYMENT/PAYABLE refs assigned | approved | DEV-UAT-CPS-POS-001 | Development symbolic refs. |
| Upstream finality reference | Darwin Pasco | CPS-POS-UAT:CPS-POS-UAT-20260703-DEV-ATC-001:newly_created:001 | approved | DEV-UAT-CPS-POS-001 | One semantic request confirmed. |
| Fiscal request facts | Darwin Pasco | sales_invoice / PHP 10000 / 2026-07-03 | approved | DEV-UAT-CPS-POS-001 | Development test facts. |
| Line / tender / tax / totals | Darwin Pasco | 1 line, 1 tender, total 10000, tax 0 | approved | DEV-UAT-CPS-POS-001 | Totals match payable basis. |
| Evidence save assignment | Darwin Pasco | Mode B temporary controlled location | approved | DEV-UAT-CPS-POS-001 | Folder must be created before execution. |
| Sensitive-data exclusion | Darwin Pasco | All exclusions confirmed yes | approved | DEV-UAT-CPS-POS-001 | Development data only. |
| Scenario assignment | Darwin Pasco | SCN-NEWLY-CREATED-001 / newly_created only | approved | DEV-UAT-CPS-POS-001 | First run only. |
| Replay assignment | Darwin Pasco | Not included | not_applicable | DEV-UAT-CPS-POS-001 | Not applicable for first run. |
| Conflict/failure/unknown assignment | Darwin Pasco | Not included | not_applicable | DEV-UAT-CPS-POS-001 | Deferred beyond first run. |
| Pre-run validation | Darwin Pasco | Assigned for readiness refresh | approved | DEV-UAT-CPS-POS-001 | Runtime verification remains required. |
| Abort owners | Darwin Pasco | Darwin Pasco for all abort categories | approved | DEV-UAT-CPS-POS-001 | Small-org consolidated ownership. |
| Reviewer/signoff | Darwin Pasco | Darwin Pasco consolidated signoff | approved | DEV-UAT-CPS-POS-001 | Development planning attestation. |

## 7. Owner and Approval Assignment

| Role / approval | Assigned person or group | Decision | Approval reference | Date/time | Notes |
| --- | --- | --- | --- | --- | --- |
| UAT lead | Darwin Pasco | approved | DEV-UAT-CPS-POS-001 | 2026-07-03 | Small-org consolidated owner. |
| Engineering lead | Darwin Pasco | approved | DEV-UAT-CPS-POS-001 | 2026-07-03 | Small-org consolidated owner. |
| POS Server owner | Darwin Pasco | approved | DEV-UAT-CPS-POS-001 | 2026-07-03 | Development fiscal owner. |
| Central PMS owner | Darwin Pasco | approved | DEV-UAT-CPS-POS-001 | 2026-07-03 | Development Central PMS owner. |
| Site owner | Darwin Pasco | approved | DEV-UAT-CPS-POS-001 | 2026-07-03 | Development Site owner. |
| Operations lead | Darwin Pasco | approved | DEV-UAT-CPS-POS-001 | 2026-07-03 | Development operations owner. |
| Rollback/support owner | Darwin Pasco | approved | DEV-UAT-CPS-POS-001 | 2026-07-03 | Must be available during approved run window. |
| Evidence owner | Darwin Pasco | approved | DEV-UAT-CPS-POS-001 | 2026-07-03 | Owns manual-save package and traceability. |
| Compliance/accounting observer, if fiscal number may be allocated | Darwin Pasco | approved | DEV-UAT-CPS-POS-001 | 2026-07-03 | Non-production sequence; consolidated small-org observer. |
| Run approval reference | DEV-UAT-CPS-POS-001 | approved | DEV-UAT-CPS-POS-001 | 2026-07-03 | Development planning reference. |
| Evidence save approval reference | DEV-UAT-CPS-POS-001 | approved | DEV-UAT-CPS-POS-001 | 2026-07-03 | Mode B temporary controlled location. |
| Fiscal number allocation approval, if applicable | Non-production allocation impact accepted by Darwin Pasco | approved | DEV-UAT-CPS-POS-001 | 2026-07-03 | Using production fiscal sequence: No. |

## 8. Environment Assignment

| Field | Assigned value | Status | Approver/signoff | Notes |
| --- | --- | --- | --- | --- |
| Environment name | DEV-CONTROLLED-UAT-LOCAL | approved | Darwin Pasco | Development-only assignment. |
| Central PMS base environment | CentralPMS-DEV-DOCKER | approved | Darwin Pasco |  |
| Central PMS base URL | http://localhost:8080 | approved | Darwin Pasco | Browser/development reference. |
| POS Server base environment | PoSServer-DEV-LOCAL | approved | Darwin Pasco |  |
| POS Server host/browser URL | http://localhost:8091 | approved | Darwin Pasco | Must be running before actual UAT. |
| Database/environment reference | DEV-CONTROLLED-UAT-LOCAL development data context | approved | Darwin Pasco | Runtime availability must be verified later. |
| Production or non-production | Non-production | approved | Darwin Pasco | Production fiscal sequence not used. |
| POS Server Base URL reference | CentralPMS config: PosServerBaseUrl = http://host.docker.internal:8091 | approved | Darwin Pasco | Reference only; no secrets. |
| Diagnostic config enabled window start | 2026-07-03 14:00 PHT | approved | Darwin Pasco | Intended window only. |
| Diagnostic config enabled window end | 2026-07-03 16:00 PHT | approved | Darwin Pasco | Intended window only. |
| Evidence save mode | Mode B temporary controlled location | approved | Darwin Pasco | Official repository still not required for this development assignment. |
| Rollback/support owner | Darwin Pasco | approved | Darwin Pasco | Must be available if execution is later approved. |
| Run approval reference | DEV-UAT-CPS-POS-001 | approved | Darwin Pasco |  |
| Assignment status | ready_for_readiness_review | approved | Darwin Pasco | Runtime proof still required before execution. |

## 9. Site / Site POS Server Assignment

| Field | Assigned value | Status | Approval reference | Notes |
| --- | --- | --- | --- | --- |
| Site id/ref | DEV-SITE-ATC-001 | approved | DEV-UAT-CPS-POS-001 | Development symbolic Site ref. |
| Site name | DEV Site - Alabang Town Center | approved | DEV-UAT-CPS-POS-001 | Development-only Site. |
| Site group, if applicable | Not applicable for fiscal authority | approved | DEV-UAT-CPS-POS-001 | Site Group is reporting only. |
| Site POS Server id/ref | DEV-POS-SERVER-ATC-001 | approved | DEV-UAT-CPS-POS-001 | Development symbolic Site POS Server ref. |
| Site POS Server environment | PoSServer-DEV-LOCAL | approved | DEV-UAT-CPS-POS-001 |  |
| Site POS Server base URL reference | http://host.docker.internal:8091 | approved | DEV-UAT-CPS-POS-001 | Reference only; no secrets. |
| Expected fiscal identity | DEV-FISCAL-IDENTITY-ATC-001 | approved | DEV-UAT-CPS-POS-001 | Runtime row availability must be verified. |
| Expected fiscal sequence policy | DEV-SI-SEQUENCE-POLICY-ATC-001 | approved | DEV-UAT-CPS-POS-001 | Runtime row availability must be verified. |
| Expected fiscal sequence state | DEV-SI-SEQUENCE-STATE-ATC-001 | approved | DEV-UAT-CPS-POS-001 | Runtime row availability must be verified. |
| Site owner approval | Darwin Pasco | approved | DEV-UAT-CPS-POS-001 | Small-org consolidated owner. |
| POS Server owner approval | Darwin Pasco | approved | DEV-UAT-CPS-POS-001 | Small-org consolidated owner. |
| Engineering lead approval | Darwin Pasco | approved | DEV-UAT-CPS-POS-001 | Small-org consolidated owner. |
| Assignment status | ready_for_readiness_review | approved | DEV-UAT-CPS-POS-001 |  |

## 10. POS Server Fiscal Configuration Assignment

| Field | Assigned value / decision | Status | POS Server owner signoff | Notes |
| --- | --- | --- | --- | --- |
| Fiscal identity id/ref | DEV-FISCAL-IDENTITY-ATC-001 | approved | Darwin Pasco | Development assignment value; verify row before execution. |
| Fiscal identity active/effective confirmation | Assigned for readiness refresh, not runtime-proven | approved | Darwin Pasco | Refresh must verify. |
| Fiscal sequence policy id/ref | DEV-SI-SEQUENCE-POLICY-ATC-001 | approved | Darwin Pasco | Development assignment value; verify row before execution. |
| Fiscal sequence policy active/effective confirmation | Assigned for readiness refresh, not runtime-proven | approved | Darwin Pasco | Refresh must verify. |
| Fiscal sequence state id/ref | DEV-SI-SEQUENCE-STATE-ATC-001 | approved | Darwin Pasco | Development assignment value; verify row before execution. |
| Fiscal sequence state configured confirmation | Assigned for readiness refresh, not runtime-proven | approved | Darwin Pasco | Refresh must verify. |
| Fiscal document type | sales_invoice | approved | Darwin Pasco |  |
| Fiscal numbering consequence accepted | Yes, non-production allocation impact accepted by Darwin Pasco | approved | Darwin Pasco | Using production fiscal sequence: No. |
| Idempotency behavior understood | Yes | approved | Darwin Pasco | Upstream finality ref is stable. |
| Replay behavior understood | Yes | approved | Darwin Pasco | Replay not included in first run. |
| Conflict behavior understood | Yes | approved | Darwin Pasco | Conflict not included in first run. |
| GET readback available | To be verified in readiness refresh if needed | assigned_pending_approval | Darwin Pasco | No automatic readback worker involved. |
| Test/non-production sequence used | Yes | approved | Darwin Pasco |  |
| Production sequence approval reference, if applicable | Not applicable - production fiscal sequence not used | not_applicable | Darwin Pasco |  |
| POS Server owner final signoff | Darwin Pasco | approved | Darwin Pasco | Development assignment signoff. |

## 11. Central PMS Configuration Assignment

| Field | Assigned value / decision | Status | Engineering signoff | Notes |
| --- | --- | --- | --- | --- |
| Fiscal reference persistence patch confirmed | Assigned for readiness refresh, not re-verified by this fill | assigned_pending_approval | Darwin Pasco | Refresh must verify runtime/config evidence. |
| Repository/harness tests evidence reference | To be captured during refreshed readiness review | assigned_pending_approval | Darwin Pasco | No tests run by this docs-only fill. |
| Controlled UAT harness available | Yes, application-level harness baseline | approved | Darwin Pasco | No endpoint/CLI/tooling used. |
| Evidence exporter available | Yes, application-level exporter baseline | approved | Darwin Pasco | No file-writing added. |
| Manual-save procedure available | Yes | approved | Darwin Pasco | Mode B temporary controlled location selected. |
| `EnablePosServerFiscalIssuanceLiveCall` intended value | true during approved diagnostic window only | approved | Darwin Pasco | Must be verified before execution. |
| `EnableControlledUatDiagnosticPath` intended value | true during approved diagnostic window only | approved | Darwin Pasco | Must be verified before execution. |
| Diagnostic config window | 2026-07-03 14:00-16:00 PHT | approved | Darwin Pasco | Intended window only. |
| Payment-flow guard false confirmation | Yes | approved | Darwin Pasco | Must remain false. |
| Exit-flow guard false confirmation | Yes | approved | Darwin Pasco | Must remain false. |
| Fiscal gating enforcement false confirmation | Yes | approved | Darwin Pasco | Must remain false. |
| No retry/readback worker confirmation | Yes | approved | Darwin Pasco | No retry/readback worker involved. |
| No endpoint/CLI/tooling confirmation | Yes | approved | Darwin Pasco | Application-level seam only. |
| No gate behavior involved | Yes | approved | Darwin Pasco | No gate behavior. |
| No ExitAuthorization issued | Yes | approved | Darwin Pasco | Diagnostic assignment must not issue ExitAuthorization. |
| Engineering lead final signoff | Darwin Pasco | approved | Darwin Pasco | Development assignment signoff. |

## 12. Test Transaction Reference Assignment

| Field | Assigned value | Status | Approval reference | Notes |
| --- | --- | --- | --- | --- |
| Run id | CPS-POS-UAT-20260703-DEV-ATC-001 | approved | DEV-UAT-CPS-POS-001 | Development run id. |
| Correlation id | 00000000-0000-4000-8000-000000000101 | approved | DEV-UAT-CPS-POS-001 |  |
| Environment name | DEV-CONTROLLED-UAT-LOCAL | approved | DEV-UAT-CPS-POS-001 |  |
| Evidence owner | Darwin Pasco | approved | DEV-UAT-CPS-POS-001 |  |
| Approval reference | DEV-UAT-CPS-POS-001 | approved | DEV-UAT-CPS-POS-001 |  |
| Site ref | DEV-SITE-ATC-001 | approved | DEV-UAT-CPS-POS-001 |  |
| Site POS Server ref | DEV-POS-SERVER-ATC-001 | approved | DEV-UAT-CPS-POS-001 |  |
| Parking session ref | DEV-PARKING-SESSION-ATC-001 | approved | DEV-UAT-CPS-POS-001 | Development symbolic ref. |
| Payment attempt ref | DEV-PAYMENT-ATTEMPT-ATC-001 | approved | DEV-UAT-CPS-POS-001 | Development symbolic ref. |
| Payment confirmation ref | DEV-PAYMENT-CONFIRMATION-ATC-001 | approved | DEV-UAT-CPS-POS-001 | Development symbolic ref. |
| Payable basis ref | DEV-PAYABLE-BASIS-ATC-001 | approved | DEV-UAT-CPS-POS-001 | Development symbolic ref. |
| Business day date | 2026-07-03 | approved | DEV-UAT-CPS-POS-001 |  |
| Currency code | PHP | approved | DEV-UAT-CPS-POS-001 |  |
| Amount minor units | 10000 | approved | DEV-UAT-CPS-POS-001 |  |
| Expected run type | newly_created | approved | DEV-UAT-CPS-POS-001 | First controlled run recommendation. |
| Assignment status | ready_for_readiness_review | approved | DEV-UAT-CPS-POS-001 |  |

## 13. Upstream Finality Reference Assignment

Suggested pattern:

```text
CPS-POS-UAT:<run-id>:<scenario>:<sequence>
```

| Field | Assigned value / decision | Status | Approval reference | Notes |
| --- | --- | --- | --- | --- |
| Upstream finality ref | CPS-POS-UAT:CPS-POS-UAT-20260703-DEV-ATC-001:newly_created:001 | approved | DEV-UAT-CPS-POS-001 | Stable development idempotency source. |
| Pattern used | CPS-POS-UAT:<run-id>:<scenario>:<sequence> | approved | DEV-UAT-CPS-POS-001 |  |
| One semantic request confirmation | Yes | approved | DEV-UAT-CPS-POS-001 |  |
| Replay ref reuse confirmation | Not applicable for first run | not_applicable | DEV-UAT-CPS-POS-001 | Replay not included. |
| Conflict bypass prohibition acknowledgement | Yes | approved | DEV-UAT-CPS-POS-001 | Do not create a new ref to bypass conflict. |
| Assigned by | Darwin Pasco | approved | DEV-UAT-CPS-POS-001 |  |
| Approved by | Darwin Pasco | approved | DEV-UAT-CPS-POS-001 |  |

## 14. Fiscal Request Facts Assignment

| Field | Assigned value | Status | Approval reference | Notes |
| --- | --- | --- | --- | --- |
| Fiscal document type | sales_invoice | approved | DEV-UAT-CPS-POS-001 |  |
| Business day date | 2026-07-03 | approved | DEV-UAT-CPS-POS-001 |  |
| Site ref | DEV-SITE-ATC-001 | approved | DEV-UAT-CPS-POS-001 |  |
| Site POS Server ref | DEV-POS-SERVER-ATC-001 | approved | DEV-UAT-CPS-POS-001 |  |
| Parking session ref | DEV-PARKING-SESSION-ATC-001 | approved | DEV-UAT-CPS-POS-001 |  |
| Payment attempt ref | DEV-PAYMENT-ATTEMPT-ATC-001 | approved | DEV-UAT-CPS-POS-001 |  |
| Payment confirmation ref | DEV-PAYMENT-CONFIRMATION-ATC-001 | approved | DEV-UAT-CPS-POS-001 |  |
| Payable basis ref | DEV-PAYABLE-BASIS-ATC-001 | approved | DEV-UAT-CPS-POS-001 |  |
| Upstream finality ref | CPS-POS-UAT:CPS-POS-UAT-20260703-DEV-ATC-001:newly_created:001 | approved | DEV-UAT-CPS-POS-001 |  |
| Currency | PHP | approved | DEV-UAT-CPS-POS-001 |  |
| Amount minor units | 10000 | approved | DEV-UAT-CPS-POS-001 |  |
| Line count | 1 | approved | DEV-UAT-CPS-POS-001 |  |
| Tender count | 1 | approved | DEV-UAT-CPS-POS-001 |  |
| Tax detail presence | Yes | approved | DEV-UAT-CPS-POS-001 |  |
| Totals presence | Yes | approved | DEV-UAT-CPS-POS-001 |  |
| Correlation id | 00000000-0000-4000-8000-000000000101 | approved | DEV-UAT-CPS-POS-001 |  |
| Assignment status | ready_for_readiness_review | approved | DEV-UAT-CPS-POS-001 |  |

## 15. Line / Tender / Tax / Totals Assignment

| Field | Assigned value | Status | Approved by | Notes |
| --- | --- | --- | --- | --- |
| Line summary | Parking fee - controlled UAT development test | approved | Darwin Pasco | Development test fact. |
| Line amount total | 10000 | approved | Darwin Pasco |  |
| Tender summary | Controlled UAT test tender - non-production | approved | Darwin Pasco | Development test fact. |
| Tender amount total | 10000 | approved | Darwin Pasco |  |
| Tax detail summary | DEV VAT/tax facts aligned to payable basis | approved | Darwin Pasco |  |
| Tax amount total | 0 | approved | Darwin Pasco |  |
| Grand total | 10000 | approved | Darwin Pasco |  |
| Totals match payable basis | Yes | approved | Darwin Pasco |  |
| Sensitive data excluded | Yes | approved | Darwin Pasco |  |

## 16. Evidence Save Assignment

Save mode values:

- Mode A official approved location
- Mode B temporary controlled location

| Field | Assigned value / decision | Status | Approval reference | Notes |
| --- | --- | --- | --- | --- |
| Save mode | Mode B temporary controlled location | approved | DEV-UAT-CPS-POS-001 | Development evidence preservation mode. |
| Target location reference | D:\ExitPass-UAT-Evidence\DEV-CONTROLLED-UAT-LOCAL\DEV-SITE-ATC-001\2026-07-03\CPS-POS-UAT-20260703-DEV-ATC-001 | approved | DEV-UAT-CPS-POS-001 | Folder should be created before execution. |
| Evidence owner | Darwin Pasco | approved | DEV-UAT-CPS-POS-001 |  |
| Hash/checksum required | Yes | approved | DEV-UAT-CPS-POS-001 |  |
| Hash/checksum command/reference | Get-FileHash -Algorithm SHA256 "<path-to-evidence.json>" | approved | DEV-UAT-CPS-POS-001 | Manual hash after evidence export. |
| Ticket/change linkage | DEV-UAT-CPS-POS-001 | approved | DEV-UAT-CPS-POS-001 |  |
| Reviewer signoff path | Darwin Pasco consolidated review/signoff | approved | DEV-UAT-CPS-POS-001 | Small-org development review. |
| Temporary local handling owner | Darwin Pasco | approved | DEV-UAT-CPS-POS-001 |  |
| Approval reference | DEV-UAT-CPS-POS-001 | approved | DEV-UAT-CPS-POS-001 |  |

## 17. Sensitive-Data Exclusion Assignment

| Exclusion check | Check status | Checked by | Checked at | Evidence/reference | Notes |
| --- | --- | --- | --- | --- | --- |
| No PAN | Yes | Darwin Pasco | 2026-07-03 | DEV-UAT-CPS-POS-001 |  |
| No CVV | Yes | Darwin Pasco | 2026-07-03 | DEV-UAT-CPS-POS-001 |  |
| No tokens | Yes | Darwin Pasco | 2026-07-03 | DEV-UAT-CPS-POS-001 |  |
| No credentials | Yes | Darwin Pasco | 2026-07-03 | DEV-UAT-CPS-POS-001 |  |
| No secrets | Yes | Darwin Pasco | 2026-07-03 | DEV-UAT-CPS-POS-001 |  |
| No raw provider callback payloads | Yes | Darwin Pasco | 2026-07-03 | DEV-UAT-CPS-POS-001 |  |
| No raw entitlement evidence | Yes | Darwin Pasco | 2026-07-03 | DEV-UAT-CPS-POS-001 |  |
| No uncontrolled images/files | Yes | Darwin Pasco | 2026-07-03 | DEV-UAT-CPS-POS-001 |  |
| No unmanaged customer personal data | Yes | Darwin Pasco | 2026-07-03 | DEV-UAT-CPS-POS-001 |  |
| No free-form sensitive blobs | Yes | Darwin Pasco | 2026-07-03 | DEV-UAT-CPS-POS-001 |  |
| No unmasked plate/ticket unless explicitly approved | Yes | Darwin Pasco | 2026-07-03 | DEV-UAT-CPS-POS-001 | No plate/ticket values included in assignment. |

## 18. Scenario Assignment

| Field | Assigned value / decision | Status | Approval reference | Notes |
| --- | --- | --- | --- | --- |
| First scenario id | SCN-NEWLY-CREATED-001 | approved | DEV-UAT-CPS-POS-001 |  |
| First run expected type | newly_created | approved | DEV-UAT-CPS-POS-001 | First controlled run recommendation. |
| Replay included | No | not_applicable | DEV-UAT-CPS-POS-001 |  |
| Conflict included | No | not_applicable | DEV-UAT-CPS-POS-001 |  |
| Failure included | No | not_applicable | DEV-UAT-CPS-POS-001 |  |
| Unknown included | No | not_applicable | DEV-UAT-CPS-POS-001 |  |
| Scenario sequencing decision | Run newly_created only for first controlled UAT diagnostic | approved | DEV-UAT-CPS-POS-001 |  |
| Scenario owner | Darwin Pasco | approved | DEV-UAT-CPS-POS-001 |  |

## 19. Replay Assignment

Replay is not included in the first controlled UAT diagnostic run.

| Field | Assigned value / decision | Status | Approval reference | Notes |
| --- | --- | --- | --- | --- |
| Replay included | No | not_applicable | DEV-UAT-CPS-POS-001 | First run only. |
| Original run id | Not applicable | not_applicable | DEV-UAT-CPS-POS-001 |  |
| Replay run id | Not applicable | not_applicable | DEV-UAT-CPS-POS-001 |  |
| Same upstream finality ref | Not applicable | not_applicable | DEV-UAT-CPS-POS-001 | Replay not included. |
| Same semantic facts confirmation | Not applicable | not_applicable | DEV-UAT-CPS-POS-001 | Replay not included. |
| Expected same fiscal document id/number | Not applicable | not_applicable | DEV-UAT-CPS-POS-001 | Replay not included. |
| No duplicate Central PMS fiscal reference expected | Not applicable | not_applicable | DEV-UAT-CPS-POS-001 | Replay not included. |
| Replay approval reference | Not applicable | not_applicable | DEV-UAT-CPS-POS-001 |  |

## 20. Conflict/Failure/Unknown Assignment

| Scenario | Included | Scenario owner | Approval reference | Expected outcome | Readback/reconciliation plan | Abort rule | Notes |
| --- | --- | --- | --- | --- | --- | --- | --- |
| Conflict | No | Darwin Pasco | DEV-UAT-CPS-POS-001 | Not applicable | Not applicable | Stop and preserve evidence if unexpected conflict occurs. | Deferred beyond first run. |
| Failure | No | Darwin Pasco | DEV-UAT-CPS-POS-001 | Not applicable | Not applicable | Stop and preserve evidence if unexpected failure occurs. | Deferred beyond first run. |
| Unknown | No | Darwin Pasco | DEV-UAT-CPS-POS-001 | Not applicable | No readback plan for first run; abort if unknown occurs. | Stop; do not infer success; preserve upstream finality ref. | Deferred beyond first run. |

## 21. Pre-Run Validation Assignment

| Validation item | Assigned status | Owner | Approval/reference | Notes |
| --- | --- | --- | --- | --- |
| Test data approved | approved | Darwin Pasco | DEV-UAT-CPS-POS-001 | Development symbolic refs assigned. |
| Environment approved | approved | Darwin Pasco | DEV-UAT-CPS-POS-001 | Runtime availability still requires readiness refresh. |
| Site/Site POS Server mapping approved | approved | Darwin Pasco | DEV-UAT-CPS-POS-001 | Development symbolic refs assigned. |
| POS Server fiscal config confirmed | assigned_pending_approval | Darwin Pasco | DEV-UAT-CPS-POS-001 | Runtime rows must be verified in readiness refresh. |
| Central PMS config confirmed | assigned_pending_approval | Darwin Pasco | DEV-UAT-CPS-POS-001 | Config must be verified in readiness refresh. |
| Evidence save path ready | assigned_pending_approval | Darwin Pasco | DEV-UAT-CPS-POS-001 | Folder should be created before execution. |
| Run id assigned | approved | Darwin Pasco | DEV-UAT-CPS-POS-001 |  |
| Upstream finality ref assigned | approved | Darwin Pasco | DEV-UAT-CPS-POS-001 |  |
| Sensitive-data check passed | approved | Darwin Pasco | DEV-UAT-CPS-POS-001 |  |
| Payment-flow guard false | approved | Darwin Pasco | DEV-UAT-CPS-POS-001 | Must be verified before execution. |
| Exit-flow guard false | approved | Darwin Pasco | DEV-UAT-CPS-POS-001 | Must be verified before execution. |
| Fiscal gating enforcement false | approved | Darwin Pasco | DEV-UAT-CPS-POS-001 | Must be verified before execution. |
| No retry/readback worker | approved | Darwin Pasco | DEV-UAT-CPS-POS-001 | No retry/readback worker involved. |
| No endpoint/CLI/tooling used | approved | Darwin Pasco | DEV-UAT-CPS-POS-001 | Application-level seam only. |
| No gate behavior involved | approved | Darwin Pasco | DEV-UAT-CPS-POS-001 |  |
| No ExitAuthorization issued | approved | Darwin Pasco | DEV-UAT-CPS-POS-001 |  |
| Rollback owner online | assigned_pending_approval | Darwin Pasco | DEV-UAT-CPS-POS-001 | Must be confirmed before execution. |
| Approval reference attached | approved | Darwin Pasco | DEV-UAT-CPS-POS-001 |  |

## 22. Abort Owner Assignment

| Abort condition | Assigned owner | Backup owner | Required action | Status | Notes |
| --- | --- | --- | --- | --- | --- |
| Sensitive data detected | Darwin Pasco | Darwin Pasco | Stop, restrict access, and notify evidence/redaction owner. | approved |  |
| Wrong Site/Site POS Server | Darwin Pasco | Darwin Pasco | Stop and do not execute diagnostic. | approved |  |
| Fiscal config missing | Darwin Pasco | Darwin Pasco | Stop and return to POS Server owner. | approved |  |
| Upstream finality unstable | Darwin Pasco | Darwin Pasco | Stop and assign stable reference. | approved |  |
| Amount/tax/totals mismatch | Darwin Pasco | Darwin Pasco | Stop and correct approved test data. | approved |  |
| Evidence location unavailable | Darwin Pasco | Darwin Pasco | Stop unless temporary controlled location is approved. | approved |  |
| Payment/exit flow mutation observed | Darwin Pasco | Darwin Pasco | Stop, preserve evidence, and escalate incident. | approved |  |
| ExitAuthorization issued | Darwin Pasco | Darwin Pasco | Stop, preserve evidence, and escalate incident. | approved |  |
| Gate behavior triggered | Darwin Pasco | Darwin Pasco | Stop, preserve evidence, and escalate incident. | approved |  |
| POS Server unknown outcome without readback plan | Darwin Pasco | Darwin Pasco | Stop; do not infer success; preserve upstream finality ref. | approved |  |

## 23. Reviewer/Signoff Assignment

| Reviewer | Name | Role | Decision | Date/time | Approval reference | Notes |
| --- | --- | --- | --- | --- | --- | --- |
| UAT lead | Darwin Pasco | UAT lead | approved | 2026-07-03 | DEV-UAT-CPS-POS-001 | Small-org consolidated signoff. |
| Engineering lead | Darwin Pasco | Engineering lead | approved | 2026-07-03 | DEV-UAT-CPS-POS-001 | Small-org consolidated signoff. |
| POS Server owner | Darwin Pasco | POS Server owner | approved | 2026-07-03 | DEV-UAT-CPS-POS-001 | Small-org consolidated signoff. |
| Central PMS owner | Darwin Pasco | Central PMS owner | approved | 2026-07-03 | DEV-UAT-CPS-POS-001 | Small-org consolidated signoff. |
| Site owner | Darwin Pasco | Site owner | approved | 2026-07-03 | DEV-UAT-CPS-POS-001 | Small-org consolidated signoff. |
| Operations lead | Darwin Pasco | Operations lead | approved | 2026-07-03 | DEV-UAT-CPS-POS-001 | Small-org consolidated signoff. |
| Evidence owner | Darwin Pasco | Evidence owner | approved | 2026-07-03 | DEV-UAT-CPS-POS-001 | Small-org consolidated signoff. |
| Compliance/accounting observer, if fiscal number allocated | Darwin Pasco | Observer | approved | 2026-07-03 | DEV-UAT-CPS-POS-001 | Non-production only; consolidated small-org observer. |

## 24. Final Assignment Status

| Final check | Value | Status | Notes |
| --- | --- | --- | --- |
| All required values assigned | Yes - development placeholders assigned | approved | Runtime evidence still required in readiness refresh. |
| All required owners assigned | Yes - small-org consolidated ownership | approved | Darwin Pasco assigned. |
| All required approvals recorded | Yes - small-org owner attestation | approved | DEV-UAT-CPS-POS-001. |
| Sensitive-data check passed | Yes | approved | Development values only. |
| Evidence save path assigned | Yes | approved | Folder should be created before execution. |
| Ready for readiness re-review | Yes | approved | Next step is refreshed readiness review. |
| Ready for execution | No - requires refreshed readiness review first | assigned_pending_approval | This fill alone does not approve execution. |
| Final assignment decision | ready_for_readiness_review | approved | Development values assigned for first controlled UAT planning. |

Final assignment decision: `ready_for_readiness_review`

Reason: Development values assigned for first controlled UAT planning; no production data, no payment/exit wiring, no gate behavior, and no fiscal gating enforcement.

First controlled run recommendation: `newly_created` only

## 25. Conditions and Dependencies

| Condition id | Condition/dependency | Owner | Required before readiness re-review | Status | Notes |
| --- | --- | --- | --- | --- | --- |
| CDA-001 | Environment assignment approved | Darwin Pasco | yes | approved | DEV-CONTROLLED-UAT-LOCAL assigned. |
| CDA-002 | Site/Site POS Server assignment approved | Darwin Pasco | yes | approved | DEV-SITE-ATC-001 / DEV-POS-SERVER-ATC-001 assigned. |
| CDA-003 | POS Server fiscal identity/policy/sequence confirmed | Darwin Pasco | yes | assigned_pending_approval | Runtime availability must be verified in readiness refresh. |
| CDA-004 | Central PMS config and guard posture confirmed | Darwin Pasco | yes | assigned_pending_approval | Config must be verified in readiness refresh. |
| CDA-005 | Test transaction refs assigned | Darwin Pasco | yes | approved | Development symbolic refs assigned. |
| CDA-006 | Upstream finality ref assigned and approved | Darwin Pasco | yes | approved | Stable development ref assigned. |
| CDA-007 | Fiscal request facts assigned and approved | Darwin Pasco | yes | approved | Development test facts assigned. |
| CDA-008 | Evidence save mode/location approved | Darwin Pasco | yes | approved | Mode B location assigned; folder should be created. |
| CDA-009 | Sensitive-data exclusion check complete | Darwin Pasco | yes | approved | All checks marked yes. |
| CDA-010 | Abort owners assigned | Darwin Pasco | yes | approved | Consolidated abort owner assigned. |
| CDA-011 | Reviewer/signoff path assigned | Darwin Pasco | yes | approved | Small-org consolidated signoff. |
| CDA-012 | Replay/conflict/failure/unknown scope decision recorded | Darwin Pasco | yes | approved | All excluded for first run. |

Remaining blockers before actual execution:

- POS Server must be started on http://localhost:8091 before actual UAT.
- Central PMS config must be set to `PosServerBaseUrl = http://host.docker.internal:8091` before actual UAT.
- Refreshed readiness review must confirm development fiscal identity/policy/sequence availability.
- Refreshed readiness review must confirm guards/config before execution.
- Evidence folder should be created before execution.
- No execution is approved by this fill alone.

## 26. Requirements Traceability Summary

| Requirement | Trace |
| --- | --- |
| Fill data assignment record with agreed development values | Sections 6 through 24 |
| Mark assignment as ready for readiness review | Sections 1, 24 |
| Preserve ready-for-execution as no | Section 24 |
| Record consolidated small-org ownership | Sections 7, 22, 23 |
| Record environment values | Section 8 |
| Record Site/Site POS Server values | Section 9 |
| Record POS Server fiscal setup values | Section 10 |
| Record Central PMS guard/safety posture | Section 11 |
| Record test transaction refs | Section 12 |
| Record upstream finality ref | Section 13 |
| Record fiscal request facts and totals | Sections 14, 15 |
| Record evidence save assignment | Section 16 |
| Record sensitive-data exclusion | Section 17 |
| Record newly-created-only scenario scope | Sections 18 through 20 |
| Preserve authority boundaries | Section 4 |
| Preserve non-goals | Section 5 |

## Recommended Next Task

Recommended next branch:

`feature/central-pms-pos-server-controlled-uat-first-run-readiness-refresh`

Purpose:

Refresh the first-run readiness review using the filled small-organization data assignment values and decide whether the project can move to execution dry-run checklist preparation.

