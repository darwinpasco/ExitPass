# ExitPass Operator Console Fiscal Status View-Audit Report UI Implementation Note v1.0

## 1. Document Control

| Field | Value |
| --- | --- |
| Document | ExitPass Operator Console Fiscal Status View-Audit Report UI Implementation Note |
| Version | v1.0 |
| Date | 2026-07-09 |
| Branch | `docs/operator-console-fiscal-status-view-audit-report-ui-note` |
| Implemented feature branch | `feature/operator-console-fiscal-status-view-audit-report-ui` |
| Scope | Implementation note and post-merge validation record for the read-only Operator Console fiscal status view-audit report UI |
| Source contract | `docs/v1.3/operator-console/contracts/ExitPass_Operator_Console_Fiscal_Status_View_Audit_Reporting_Contract_v1.0.md` |
| Readiness note | `docs/v1.3/operator-console/implementation/ExitPass_Operator_Console_Fiscal_Status_View_Audit_Report_Implementation_Readiness_v1.0.md` |
| UI route | `/operator-console/audit/fiscal-status-views` |
| Client endpoint | `GET /v1/ops/operator-console/audit/fiscal-status-views` |
| Source action displayed | `VIEW_FISCAL_ISSUANCE_STATUS` |

This note is documentation-only. It records the merged Operator Console UI implementation scope and focused post-merge validation posture. It does not modify source code, schema, tests, runtime configuration, Central PMS runtime state, POS Server state, fiscal issuance state, payment state, ExitAuthorization state, gate state, refund/reversal state, rendering behavior, UAT evidence, or UAT runbooks.

No UAT scenarios were run for this note. No Central PMS or POS Server runtime endpoints were called while preparing this note.

## 2. Purpose

This note records the completed UI implementation for the Operator Console fiscal status view-audit report.

The report lets authorized Operator Console supervisors, auditors, and administrator/support users review safe metadata for `VIEW_FISCAL_ISSUANCE_STATUS` action-log entries produced by the fiscal status viewer. The UI is read-only and is intended for governance, support, and audit review of view events only.

The page does not prove payment, fiscal issuance, exit authorization, gate behavior, refund/reversal, or statutory document finality. It surfaces only the safe metadata returned by the merged Central PMS read model.

## 3. Implemented Scope

Implemented UI scope:

- Operator Console route for fiscal status view-audit reporting.
- Navigation entry consistent with the existing Operator Console audit/reporting navigation.
- Typed API client method for the backend fiscal status view-audit report endpoint.
- Query, response, item, and result-class UI types.
- Filter form for supported backend query parameters.
- Offset/limit pagination controls.
- Safe main report table fields.
- Collapsed support/audit detail section per row.
- Read-only guardrail wording.
- Loading, empty, generic error, and 401/403 access-denied states.
- Focused UI/client tests for route loading, guardrails, endpoint query behavior, filters, pagination, access denial, collapsed details, safe display, and absence of mutation controls.

Out of scope:

- Backend changes.
- Schema changes.
- POS Server changes.
- Fiscal retry, readback, or writeback.
- Payment confirmation.
- ExitAuthorization.
- Gate behavior.
- Refund/reversal behavior.
- PDF, HTML, or QR generation.
- Final BIR statutory wording.
- UAT scenario or runbook changes.

## 4. UI Route And Page

The implemented Operator Console UI route is:

```text
/operator-console/audit/fiscal-status-views
```

The route renders the fiscal status view-audit report page inside the existing Operator Console shell. The page is placed under the audit/reporting area and is distinct from the fiscal status viewer route:

```text
/operator-console/fiscal-issuance-status
```

The report page displays a filter panel, read-only boundary panel, report results table, and pagination controls. It does not provide any action controls that could mutate fiscal, payment, exit, gate, refund, reversal, or rendering state.

## 5. Client And Type Behavior

The UI client exposes a typed method for:

```text
GET /v1/ops/operator-console/audit/fiscal-status-views
```

Client behavior:

- uses `GET`;
- sends the existing Operator Console identity and correlation headers;
- uses query-string parameters only;
- does not send a request body;
- encodes `fiscalIssuanceReferenceId` and all other query-string values through `URLSearchParams`;
- maps the backend safe response into UI report item types;
- maps `401` and `403` through existing Operator Console error handling into access-denied UI posture;
- does not call POS Server or runtime mutation endpoints.

Typed query fields:

- `from`
- `to`
- `siteId`
- `siteGroupId`
- `operatorUserId`
- `fiscalIssuanceReferenceId`
- `resultClass`
- `correlationId`
- `limit`
- `offset`

Typed response fields:

- `items`
- `totalCount`
- `limit`
- `offset`
- `correlationId`

Typed item fields:

- `actionLogEntryId`
- `actionTimestamp`
- `actionCode`
- `resultClass`
- `operatorUserId`
- `siteId`
- `siteGroupId`
- `fiscalIssuanceReferenceId`
- `correlationId`
- `safeDenialOrErrorPosture`
- `sourceModule`

Supported result-class labels:

| Result Class | UI Label | Meaning |
| --- | --- | --- |
| `SUCCEEDED` | Succeeded | Fiscal status was viewed by an authorized user. |
| `DENIED` | Denied | The view was denied or unauthorized. |
| `NOT_FOUND` | Not found | The requested fiscal issuance reference was not available through the read path. |
| `FAILED_SAFELY` | Failed safely | The view did not complete and failed without exposing unsafe details. |

## 6. Filter And Pagination Behavior

The UI exposes filters for:

| Filter | Behavior |
| --- | --- |
| Date from | Sent as `from` when supplied. |
| Date to | Sent as `to` when supplied. |
| Site ID | Sent as `siteId` when supplied. |
| Site group ID | Sent as `siteGroupId` when supplied. |
| Operator/support user ID | Sent as `operatorUserId` when supplied. |
| Fiscal issuance reference ID | Sent as `fiscalIssuanceReferenceId` when supplied and encoded in the query string. |
| Result class | Sent as `resultClass`; supported UI choices are `SUCCEEDED`, `DENIED`, `NOT_FOUND`, and `FAILED_SAFELY`. |
| Correlation ID | Sent as `correlationId` when supplied. |
| Limit | Sent as `limit`; UI choices align with the backend limit posture. |
| Offset | Maintained through pagination controls and sent as `offset`. |

Submitting filters resets `offset` to `0`.

Pagination behavior:

- default limit is `25`;
- the UI offers `25`, `50`, `100`, and `200`;
- Previous page decrements offset by the current page limit and clamps at zero;
- Next page increments offset by the current page limit when `offset + limit < totalCount`;
- the report displays total row count and current offset.

## 7. Safe Report Display Fields

The main report table displays only safe metadata returned by the read model:

| Main Field | Display Use |
| --- | --- |
| Action timestamp | When the view-audit/action-log entry occurred. |
| Action code | Source action, expected as `VIEW_FISCAL_ISSUANCE_STATUS`. |
| Result class | Safe result class label. |
| Operator/support user ID | Safe actor identifier. |
| Site ID | Site context when present. |
| Site group ID | Site-group context when present. |
| Fiscal issuance reference ID | Viewed fiscal status reference. |
| Correlation ID | Request/log correlation. |
| Safe posture | Safe denial/error posture when present. |
| Source module | Source module or screen when present. |

The table represents view events only. A row is not fiscal evidence, payment evidence, exit authorization evidence, or gate evidence.

## 8. Support/Audit Detail Posture

Each row includes a collapsed support/audit details section. The details are collapsed by default and contain only safe metadata such as:

- action-log entry id;
- result meaning;
- action code;
- fiscal issuance reference id;
- correlation id;
- safe denial/error posture;
- source module/screen.

The detail section is for support and audit correlation only. It does not expose raw evidence, raw request or response bodies, secrets, stack traces, customer PII, payment provider raw payloads, statutory evidence payloads, or local credentials.

## 9. Guardrail Wording

The UI displays the following read-only guardrail wording:

- View logs are observational only.
- View logs do not prove payment.
- View logs do not prove fiscal issuance.
- View logs do not authorize exit.
- View logs do not imply gate action.

These statements are intentionally visible on the page because the report can otherwise be misread as proof of payment, fiscal issuance, exit authorization, or gate behavior.

## 10. Access And Error Handling

The page handles:

| State | UI Behavior |
| --- | --- |
| Loading | Displays a loading state while retrieving safe fiscal status view rows. |
| Empty | Displays an empty result state when no rows match the filters. |
| Error | Displays a generic unable-to-load state using the safe mapped error message. |
| `401` / `403` | Displays access denied and hides report rows. |

Access handling preserves backend RBAC posture. The UI does not infer permission from navigation visibility and does not bypass Operator Console access evaluation.

## 11. Never-Displayed Data

The fiscal status view-audit report UI must never display:

- raw fiscal request payloads;
- raw POS Server request bodies;
- raw POS Server response bodies;
- secrets;
- stack traces;
- customer PII;
- payment provider raw payloads;
- statutory evidence payloads;
- raw payment callbacks;
- local environment variables or credentials.

The UI must not join to, search by, render, or export unsafe raw data. If unsafe values exist in lower-level operational logs, they remain outside this report surface.

## 12. Tests Added And Validated

Focused UI/client tests cover:

- page loads at `/operator-console/audit/fiscal-status-views`;
- read-only guardrail wording is visible;
- client calls `GET /v1/ops/operator-console/audit/fiscal-status-views`;
- client sends filters as query parameters;
- `fiscalIssuanceReferenceId` is encoded through query-string handling;
- report displays `VIEW_FISCAL_ISSUANCE_STATUS` rows;
- result class labels render for `SUCCEEDED`, `DENIED`, `NOT_FOUND`, and `FAILED_SAFELY`;
- filters render and submit the expected query object;
- pagination controls update offset;
- `401`/`403` access denied hides rows and shows access denied;
- support/audit details are collapsed by default;
- unsafe fields are not displayed;
- mutation/action controls are not present.

## 13. Post-Merge Validation Commands And Results

Focused validation executed for this documentation note:

| Command | Result |
| --- | --- |
| `git diff --check` | Passed. |
| `npm.cmd --prefix src\Services\OperatorConsoleUi test -- --run src/App.test.tsx` | Passed. `src/App.test.tsx` reported 64 tests passed. |

Validation scope was intentionally limited to post-merge UI/client validation. No UAT scenarios were run and no Central PMS or POS Server runtime endpoints were called.

## 14. Boundaries Preserved

The merged UI implementation and this note preserve the following boundaries:

- no backend changes in this documentation branch;
- no schema changes;
- no POS Server changes;
- no fiscal retry;
- no fiscal readback;
- no fiscal writeback;
- no payment confirmation;
- no ExitAuthorization;
- no gate behavior;
- no refund/reversal;
- no PDF generation;
- no HTML generation;
- no QR generation;
- no final BIR statutory wording;
- no raw evidence access;
- no UAT changes.

The report remains a read-only view-audit metadata surface over `VIEW_FISCAL_ISSUANCE_STATUS` action-log entries.

## 15. Known Limitations

Known limitations:

- The UI depends on the backend read model to enforce RBAC, site/site-group scoping, result-class filtering, pagination caps, and unsafe-field exclusion.
- The page currently displays safe identifiers rather than enriched display names for operators, sites, and site groups unless those are later added to the safe backend DTO.
- There is no export function. Any future export must follow the reporting contract and include only safe metadata.
- There is no deep link from report rows to the fiscal status viewer. If added later, the viewer must independently enforce current access checks.
- The page is table-based and optimized for focused audit review, not analytics aggregation or dashboard summaries.

## 16. Recommended Next Slice

Recommended next slice:

1. Keep the report read-only and monitor real audit/support use for missing safe metadata.
2. If auditors need export, add a separate safe metadata export contract and implementation with explicit RBAC, retention, and no raw-payload guarantees.
3. If support needs operator/site display labels, extend the backend DTO with safe display labels rather than resolving them client-side from unsafe sources.
4. Add optional row-to-viewer navigation only if the fiscal status viewer performs its own fresh authorization check and the navigation does not imply payment, issuance, exit, or gate outcome.
