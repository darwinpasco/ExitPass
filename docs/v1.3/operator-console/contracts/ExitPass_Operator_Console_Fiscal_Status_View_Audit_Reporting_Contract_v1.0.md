# ExitPass Operator Console Fiscal Status View-Audit Reporting Contract v1.0

## 1. Document Control

| Field | Value |
| --- | --- |
| Document | ExitPass Operator Console Fiscal Status View-Audit Reporting Contract |
| Version | v1.0 |
| Date | 2026-07-08 |
| Branch | `docs/operator-console-fiscal-status-view-audit-reporting-contract` |
| Scope | Read-only reporting contract for Operator Console fiscal status view-audit/action-log entries |
| Source action | `VIEW_FISCAL_ISSUANCE_STATUS` |
| Source viewer route | `/operator-console/fiscal-issuance-status` |
| Source facade endpoint | `GET /v1/ops/operator-console/fiscal-issuance/references/{fiscalIssuanceReferenceId}` |
| Required permission | `FiscalIssuanceStatusRead` |

This is a documentation-only contract. It does not implement source code, schema, tests, UI features, runtime configuration, UAT scenarios, Central PMS runtime calls, POS Server runtime calls, fiscal mutation behavior, payment behavior, ExitAuthorization behavior, gate behavior, refund/reversal behavior, or document rendering.

## 2. Purpose And Scope

This contract defines how Operator Console supervisors, compliance auditors, and administrator/support users should review fiscal status view-audit/action-log entries safely.

The report is intended to answer who viewed fiscal issuance status through the Operator Console fiscal status viewer, what fiscal issuance reference was viewed, when the view occurred, and how the read completed. It is a governance and support visibility surface for view events created by the merged read-only fiscal status viewer.

Reference documents:

- `docs/v1.3/operator-console/contracts/ExitPass_Operator_Console_Fiscal_Issuance_Status_Visibility_Contract_v1.0.md`
- `docs/v1.3/operator-console/implementation/ExitPass_Operator_Console_Fiscal_Status_Viewer_Implementation_Readiness_v1.0.md`
- `docs/v1.3/operator-console/implementation/ExitPass_Operator_Console_Fiscal_Status_Viewer_Implementation_Note_v1.0.md`

This report must preserve the same authority boundaries as the viewer:

- Central PMS remains the source for Operator Console action-log/view-audit posture.
- POS Server remains the fiscal issuance and fiscal numbering authority.
- Payment finality remains separate from fiscal status viewing.
- ExitAuthorization and gate opening remain separate from fiscal status viewing.
- Operator Console reporting is observational only and must not become a mutation, retry, payment, exit, gate, refund/reversal, or document rendering surface.

## 3. Source Action

The source action for fiscal status view-audit reporting is:

```text
VIEW_FISCAL_ISSUANCE_STATUS
```

This action represents an Operator Console attempt to view fiscal issuance status for a `fiscalIssuanceReferenceId` through the read-only fiscal status viewer/facade.

The action log entry records the view attempt and result posture. It does not create fiscal issuance, confirm payment, issue ExitAuthorization, authorize exit, open a gate, retry fiscal issuance, perform POS Server readback/writeback, or render fiscal documents.

## 4. Intended Users

| User | Intended Use | Access Posture |
| --- | --- | --- |
| Site Supervisor | Review fiscal status view history for site operations, escalation handling, and operator/support accountability. | Read-only, scoped to the user's allowed site/site-group context where supported. |
| Compliance Auditor | Review who accessed fiscal status visibility, when, for which reference, and with which result class. | Read-only, audit-scoped, minimized to safe metadata and correlation references. |
| Administrator/support | Investigate support cases, access denials, not-found views, service failures, and correlation between UI/API requests and action-log entries. | Read-only support/audit detail access, subject to `FiscalIssuanceStatusRead` and broader operational authorization. |

This contract does not grant fiscal status view permission by itself. It defines the safe reporting posture for users who are already authorized to review the relevant Operator Console action-log entries.

## 5. Explicit Read-Only Boundary

Allowed:

- Query and display `VIEW_FISCAL_ISSUANCE_STATUS` action-log/view-audit entries.
- Filter entries by safe metadata such as date range, site/site group, user, fiscal issuance reference, result class, and correlation id.
- Display safe view result posture such as succeeded, denied, not found, or failed safely.
- Export safe report metadata only if a future export implementation follows this contract.
- Link from a report entry to the fiscal status viewer only when the viewer independently enforces `FiscalIssuanceStatusRead` and current access scope.

Not allowed:

- Modify fiscal issuance state.
- Trigger fiscal retry, readback, writeback, closure, refund, reversal, or POS Server mutation.
- Confirm payment or infer payment finality from a view log.
- Issue or infer ExitAuthorization.
- Open or imply gate action.
- Display raw fiscal request payloads, raw POS Server bodies, secrets, stack traces, customer PII, payment provider raw payloads, or statutory evidence payloads.
- Treat action-log entries as statutory fiscal evidence.

## 6. What The Report Answers

The report should answer:

| Question | Required Reporting Meaning |
| --- | --- |
| Who viewed fiscal status? | Authenticated operator/support/audit identity recorded for the view attempt, using safe display identifiers. |
| Which reference was viewed? | The `fiscalIssuanceReferenceId` supplied to the fiscal status viewer/facade. |
| When was it viewed? | Timestamp of the action-log/view-audit entry. |
| Which site/site-group context applied? | Site and site-group context recorded or inferred by the authorized Operator Console action-log path, when available. |
| Did the view succeed? | Result class should distinguish successful view from denied, not found, or safe failure. |
| Was it denied? | Access-denied/forbidden/unauthorized posture should be visible without exposing fiscal details. |
| Was it not found? | Not-found result should mean only that the requested reference was unavailable through the status read path. |
| Did it fail safely? | Service/error posture should show safe error classification without stack traces or raw payloads. |
| What correlation id applies? | Correlation id should allow support/audit users to connect the UI/API request to platform logs without exposing unsafe payloads. |

The report does not answer whether the customer paid, whether fiscal issuance legally completed, whether exit is authorized, whether the gate opened, whether a refund or reversal occurred, or whether a statutory document is final.

## 7. Suggested Filters

Suggested report filters:

| Filter | Notes |
| --- | --- |
| Date range | Required for operational usability and retention-aware review. Support absolute start/end timestamps and local display timezone. |
| Site/site group | Scope review to allowed operational context. Do not allow this filter to bypass site authorization. |
| Operator/support user | Filter by safe user id, display name, or support identity where available. |
| `fiscalIssuanceReferenceId` | Exact reference lookup for incident/support investigation. |
| Result class | Suggested values: succeeded, denied, not found, failed safely. |
| Correlation id | Exact correlation lookup for UI/API request tracing. |

Filters must not expose raw request payload search, raw POS Server body search, payment provider payload search, customer PII search, stack trace search, or statutory evidence payload search.

## 8. Main Report Fields Allowed For Display

These fields may appear in the main report grid/list when available and authorized:

| Field | Display Use |
| --- | --- |
| Action timestamp | Primary report ordering and audit time. |
| Action code | Must be `VIEW_FISCAL_ISSUANCE_STATUS`. |
| Result class | Succeeded, denied, not found, or failed safely. |
| Operator/support user id | Safe actor identity for accountability. |
| Operator/support display name | Optional safe actor label if available. |
| Role or permission context | Safe summary of role/permission used for the view. |
| Site id | Site context when available. |
| Site display name | Safe site label when available. |
| Site group id | Site-group context when available. |
| Site group display name | Safe site-group label when available. |
| `fiscalIssuanceReferenceId` | Viewed fiscal status reference. |
| Correlation id | Request/log correlation. |
| Source module/screen | Operator Console fiscal status viewer. |

Main report display must avoid customer-facing statutory wording. The row represents a view event, not a fiscal issuance result.

## 9. Support/Audit-Only Fields

These fields may be shown only in a detail drawer, expanded audit panel, administrator/support diagnostic view, or governed audit export:

| Field | Support/Audit Use |
| --- | --- |
| Action-log entry id | Durable view-audit entry correlation. |
| Access decision snapshot | Review why a view was allowed or denied, if safely recorded. |
| Denial reason code | Safe access-denial classification. |
| HTTP status class | Distinguish success, unauthorized, forbidden, not found, and safe failure. |
| Request path template | Confirm the Operator Console fiscal status facade was used. |
| Operator device binding id | Support/operator accountability context. |
| Operator shift id | Shift-level operational correlation. |
| Target entity type | Fiscal issuance status reference target classification. |
| Target entity id/reference | The viewed `fiscalIssuanceReferenceId` or normalized target reference. |
| Site/site-group authorization scope | Support/audit access review context. |
| Safe error code | Non-sensitive failure classification. |
| Safe error posture | Non-sensitive support posture, such as failed safely. |
| Recorded source service | Central PMS/Operator Console action-log source. |
| Created/recorded timestamp in UTC | Durable audit timestamp for reconciliation with platform logs. |

Support/audit-only detail must remain metadata-only. It must not expose raw fiscal evidence, POS Server bodies, raw payment provider evidence, secrets, stack traces, customer PII, or statutory evidence payloads.

## 10. Fields Never Displayed

The report, detail drawer, export, logs surfaced to users, and support/audit UI must never display:

- raw fiscal request payloads;
- raw POS Server request bodies;
- raw POS Server response bodies;
- secrets;
- stack traces;
- customer PII;
- payment provider raw payloads;
- statutory evidence payloads;
- raw payment callbacks;
- raw entitlement evidence;
- local environment variables or credentials;
- database connection strings;
- webhook secrets or API keys.

If any unsafe value exists in lower-level operational logs, this report must not join, enrich, or display it.

## 11. UX And Display Rules

Fiscal status view-audit reports must use observational wording.

Required rules:

- A view log is observational only.
- A view log does not prove payment.
- A view log does not prove fiscal issuance.
- A view log does not authorize exit.
- A view log does not imply gate action.
- A successful view means only that an authorized user received a fiscal status response from the read-only viewer/facade.
- A not-found view means only that the requested `fiscalIssuanceReferenceId` was not available through the read path at that time.
- A denied view means only that access was not allowed or authentication/authorization was missing.
- A failed-safe view means only that the view did not complete and failed without exposing unsafe details.

Recommended result labels:

| Result Class | Recommended Label | Display Meaning |
| --- | --- | --- |
| Succeeded | Fiscal status viewed | The read-only fiscal status viewer returned safe status details to an authorized user. |
| Denied | View denied | The user was unauthenticated, unauthorized, or denied by Operator Console access posture. |
| Not found | Fiscal reference not found | The requested reference was not available through the status read path. |
| Failed safely | View failed safely | The read did not complete; safe support review may be needed. |

The report must not use success color or payment/exit/gate wording in a way that implies business finality. If color badges are used, they should represent report result posture only, not fiscal issuance outcome.

## 12. Access And RBAC Expectations

Access expectations:

- Report access must be protected by an explicit Operator Console/audit reporting permission or an approved existing audit/reporting permission.
- Viewing an individual fiscal status reference through the viewer remains protected by `FiscalIssuanceStatusRead`.
- Report access must not bypass site/site-group scoping.
- Report filters must be constrained to the caller's authorized operational scope.
- Support/audit-only fields require the same or stronger authorization than the main report.
- Export, if added later, must require explicit export authorization and must be audit logged.

RBAC must not be weakened for convenience. Local/dev permission headers may support development flows only where existing local/dev RBAC posture allows them; production access must rely on the normal RBAC and Operator Console access evaluation model.

## 13. Export Rules If Export Is Later Implemented

This contract does not implement export. If export is later added, it must follow these rules:

- Export only the main report fields and approved support/audit-only metadata.
- Require explicit report/export authorization.
- Record a separate export audit/action-log event.
- Include date range, filter criteria summary, exporting user, export timestamp, and correlation id.
- Preserve site/site-group scoping in exported rows.
- Use clear labels stating that rows are view-audit entries only.
- Exclude every field listed in Section 10.
- Exclude raw payloads even for administrator/support users.
- Avoid customer PII unless a separately approved compliance contract explicitly authorizes a minimized field.
- Avoid statutory wording that implies the export is fiscal evidence, a receipt register, or a BIR statutory report.

Exports must not become a backdoor for raw evidence access, fiscal issuance status mutation, payment confirmation, ExitAuthorization, gate action, refund/reversal, or POS Server diagnostics.

## 14. Audit Retention Notes

Retention policy is not defined by this contract. The report should be designed so a future retention policy can be applied consistently.

Retention notes:

- Retain view-audit/action-log entries according to the approved Operator Console audit retention policy when one exists.
- Keep retention separate from fiscal evidence retention, payment record retention, and statutory document retention.
- Do not retain raw fiscal request payloads, POS Server bodies, secrets, stack traces, customer PII, payment provider raw payloads, or statutory evidence payloads in this report.
- If action-log entries are archived, archive metadata-only view events with enough context for audit review: action code, actor, target reference, result class, timestamp, site/site-group context, and correlation id.
- Redaction or deletion policies must not be bypassed through report export caches.

## 15. Explicit Non-Goals

This contract does not define or authorize:

- fiscal retry;
- fiscal readback/writeback;
- POS Server mutation;
- direct POS Server calls from Operator Console reporting;
- payment confirmation;
- ExitAuthorization;
- gate opening;
- refund or reversal;
- PDF generation;
- HTML generation;
- QR generation;
- final BIR statutory wording;
- raw evidence access;
- fiscal exception closure;
- customer-facing receipt display;
- payment provider reconciliation;
- UAT scenario execution.

## 16. Recommended Next Implementation Slice

Recommended next slice:

```text
Implement Operator Console fiscal status view-audit report
```

Suggested scope:

- Add a read-only report over Operator Console action-log entries where action code is `VIEW_FISCAL_ISSUANCE_STATUS`.
- Enforce explicit audit/reporting authorization plus site/site-group scoping.
- Provide filters for date range, site/site group, operator/support user, `fiscalIssuanceReferenceId`, result class, and correlation id.
- Display only the main report fields allowed by this contract.
- Place support/audit-only metadata in a collapsed detail view.
- Add tests proving read-only behavior, RBAC/scoping, safe field display, result-class mapping, and absence of raw payload/secret/PII/statutory evidence fields.
- Keep export out of scope unless an explicit export authorization and export audit contract are included in the slice.

Out of scope for that slice:

- fiscal retry;
- fiscal readback/writeback;
- POS Server mutation;
- payment confirmation;
- ExitAuthorization;
- gate opening;
- refund/reversal;
- PDF/HTML/QR generation;
- final BIR statutory wording;
- raw evidence access;
- UAT execution.

## 17. Completion Checklist

| Requirement | Status |
| --- | --- |
| Purpose and scope defined | Covered in Section 2 |
| Source action documented | Covered in Section 3 |
| Intended users documented | Covered in Section 4 |
| Read-only boundary documented | Covered in Section 5 |
| Report questions documented | Covered in Section 6 |
| Suggested filters documented | Covered in Section 7 |
| Main report fields documented | Covered in Section 8 |
| Support/audit-only fields documented | Covered in Section 9 |
| Never-displayed fields documented | Covered in Section 10 |
| UX/display rules documented | Covered in Section 11 |
| Access/RBAC expectations documented | Covered in Section 12 |
| Export rules documented | Covered in Section 13 |
| Audit retention notes documented | Covered in Section 14 |
| Explicit non-goals documented | Covered in Section 15 |
| Recommended next implementation slice documented | Covered in Section 16 |
