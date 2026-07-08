# ExitPass Operator Console Fiscal Status View-Audit Report Implementation Readiness v1.0

## 1. Document Control

| Field | Value |
| --- | --- |
| Document | ExitPass Operator Console Fiscal Status View-Audit Report Implementation Readiness |
| Version | v1.0 |
| Date | 2026-07-08 |
| Branch | `docs/operator-console-fiscal-status-view-audit-report-readiness` |
| Scope | Readiness note for a future read-only Operator Console fiscal status view-audit report |
| Source contract | `docs/v1.3/operator-console/contracts/ExitPass_Operator_Console_Fiscal_Status_View_Audit_Reporting_Contract_v1.0.md` |
| Source action | `VIEW_FISCAL_ISSUANCE_STATUS` |
| Source viewer route | `/operator-console/fiscal-issuance-status` |
| Source facade endpoint | `GET /v1/ops/operator-console/fiscal-issuance/references/{fiscalIssuanceReferenceId}` |

This readiness note is documentation-only. It does not modify source code, schema, tests, UI behavior, runtime configuration, Central PMS runtime state, POS Server state, payment state, fiscal state, ExitAuthorization state, gate state, refund/reversal state, or document rendering behavior.

No UAT scenarios were run. No Central PMS or POS Server runtime endpoints were called while preparing this note.

## 2. Purpose

This note documents the smallest safe implementation path for a read-only Operator Console report over fiscal status view-audit/action-log entries.

The intended report should list Operator Console action-log entries for:

```text
VIEW_FISCAL_ISSUANCE_STATUS
```

The report should help authorized supervisors, auditors, and administrator/support users review who viewed fiscal issuance status, which `fiscalIssuanceReferenceId` was viewed, when the view occurred, site/site-group context when available, result class, and correlation id.

The report must remain observational only. It must not prove payment, prove fiscal issuance, authorize exit, imply gate action, call POS Server, retry fiscal issuance, perform readback/writeback, confirm payment, issue ExitAuthorization, open a gate, refund/reverse, or render fiscal documents.

## 3. Existing Operator Console UI And Reporting Structure

The Operator Console UI app exists under:

| Path | Current Relevance |
| --- | --- |
| `src/Services/OperatorConsoleUi/src/App.tsx` | Route shell, navigation rail, fiscal status viewer route, statutory discount audit report page, filters, table, and read-only guardrail patterns. |
| `src/Services/OperatorConsoleUi/src/apiClient.ts` | Typed client methods including `getFiscalIssuanceStatus` and existing `listAuditReport` for statutory discount audit reporting. |
| `src/Services/OperatorConsoleUi/src/types.ts` | UI types for `FiscalIssuanceStatus`, `AuditReportQuery`, `AuditReportResponse`, and report items. |
| `src/Services/OperatorConsoleUi/src/App.test.tsx` | Existing UI/client tests for fiscal status viewer safe display and broad Operator Console behavior. |
| `src/Services/OperatorConsoleUi/src/styles.css` | Existing panel, table, filter grid, status pill, guardrail, and collapsed detail styling. |

Existing UI routes include:

| Route | Current Use |
| --- | --- |
| `/operator-console/fiscal-issuance-status` | Read-only fiscal status viewer for a known `fiscalIssuanceReferenceId`. |
| `/operator-console/audit` | Existing statutory discount audit/reporting page. |

The existing statutory discount audit page provides useful UI patterns:

- `LoadState<T>` loading/loaded/empty/error handling;
- `auditGuardrail` read-only boundary panel;
- `auditFilterGrid` filter form;
- table-based report results;
- correlation id display;
- no raw evidence display;
- no mutation controls in the audit report page.

There is no existing fiscal status view-audit report page or UI client method dedicated to `VIEW_FISCAL_ISSUANCE_STATUS` entries.

## 4. Existing Central PMS Action-Log And Audit Persistence Structure

The merged fiscal status viewer uses:

| File | Current Relevance |
| --- | --- |
| `src/Services/CentralPms/src/ExitPass.CentralPms.Api/Endpoints/OperatorConsoleFiscalIssuanceStatusEndpoints.cs` | Operator Console fiscal status facade route protected by `FiscalIssuanceStatusRead`. |
| `src/Services/CentralPms/src/ExitPass.CentralPms.Application/OperatorConsole/OperatorConsoleFiscalIssuanceStatusService.cs` | Evaluates and persists Operator Console access before reading fiscal status. |
| `src/Services/CentralPms/src/ExitPass.CentralPms.Application/OperatorConsole/OperatorConsoleActionCodes.cs` | Defines `FiscalIssuanceStatusVisibilityWorkflow` and `ViewFiscalIssuanceStatus`. |
| `src/Services/CentralPms/src/ExitPass.CentralPms.Infrastructure/OperatorConsole/OperatorConsoleAccessEvaluationWriter.cs` | Persists access evaluation/action-log entries. |

Current fiscal status view persistence behavior:

- Workflow code: `FISCAL_ISSUANCE_STATUS_VISIBILITY`.
- Controlled action code: `VIEW_FISCAL_ISSUANCE_STATUS`.
- Persistence target table in current source: `operations.operator_action_logs`.
- `action_type` is written as `CONTROLLED_RECHECK`.
- `action_reason_code` stores the requested action code.
- `action_status` stores `SUCCESS` when access evaluation is allowed and `DENIED` when access evaluation is denied.
- `correlation_id`, `operator_user_id`, `site_id`, `performed_at`, and a JSON decision snapshot are written.

Important readiness gap:

- The fiscal status service currently evaluates access with `ParkingSessionId: null`.
- `OperatorConsoleAccessEvaluationService` currently sets `target_entity_type` and `target_entity_id` only from `ParkingSessionId`.
- Therefore the current persisted action-log entry does not store `fiscalIssuanceReferenceId` as `target_entity_id`.
- The current action-log entry records access allowed/denied posture, but it does not separately persist post-read result classes such as reference not found or read failed safely.

This means a report that must filter by exact `fiscalIssuanceReferenceId` and distinguish success, denied, not found, and failed safely cannot be fully implemented from the existing action-log shape alone without either enriching view-audit persistence or relying on unsafe/fragile side channels.

## 5. Existing Read And Report Endpoint Patterns

Existing Central PMS Operator Console read/report patterns:

| Endpoint | File | Pattern |
| --- | --- | --- |
| `GET /v1/ops/operator-console/statutory-discounts/drafts` | `OperatorConsoleStatutoryDiscountDraftEndpoints.cs` | Read-only list endpoint over stored validation data. |
| `GET /v1/ops/operator-console/statutory-discounts/drafts/{draftId}` | `OperatorConsoleStatutoryDiscountDraftEndpoints.cs` | Read-only detail endpoint. |
| `GET /v1/ops/operator-console/audit/statutory-discounts` | `OperatorConsoleStatutoryDiscountDraftEndpoints.cs` | Existing read-only audit/report endpoint with filters and safe DTOs. |
| `GET /v1/ops/operator-console/fiscal-issuance/references/{fiscalIssuanceReferenceId}` | `OperatorConsoleFiscalIssuanceStatusEndpoints.cs` | Read-only fiscal status viewer facade with view-audit persistence. |

The statutory discount audit report pattern currently uses:

- endpoint-level query parameters;
- access evaluation and persistence using `VIEW_AUDIT_REPORT`;
- application query/result records;
- repository query with `COUNT(*) OVER()` for total count;
- `limit` and `offset`;
- safe contract DTOs;
- no raw evidence payloads;
- no mutation behavior.

There is no existing generic Operator Console action-log report endpoint that can safely query `operations.operator_action_logs` for arbitrary action codes and expose only approved fields.

## 6. Existing Authorization And RBAC Patterns

Relevant authorization/RBAC pieces:

| File | Current Relevance |
| --- | --- |
| `src/Services/CentralPms/src/ExitPass.CentralPms.Api/Security/ReconciliationPolicyMetadata.cs` | Endpoint metadata used by Central PMS RBAC middleware. |
| `src/Services/CentralPms/src/ExitPass.CentralPms.Application/Security/CentralPmsRbacPolicyCatalog.cs` | Maps policy names to permissions. |
| `src/Services/CentralPms/src/ExitPass.CentralPms.Api/Security/CentralPmsRbacMiddleware.cs` | Enforces `401`/`403` behavior when RBAC is enabled. |
| `src/Services/CentralPms/src/ExitPass.CentralPms.Application/OperatorConsole/OperatorConsoleAccessEvaluationService.cs` | Evaluates Operator Console device, shift, user, site, and action readiness. |
| `src/Services/CentralPms/src/ExitPass.CentralPms.Application/OperatorConsole/OperatorConsoleActionCatalog.cs` | Contains metadata for `VIEW_AUDIT_REPORT`. |

Current fiscal status viewer authorization:

- Endpoint policy: `FiscalIssuanceStatusRead`.
- Permission mappings include `fiscal-issuance.status.read`, `reconciliation.view`, and `reconciliation.manage`.
- Operator Console access evaluation uses `VIEW_FISCAL_ISSUANCE_STATUS`.

Existing audit/report authorization pattern:

- The statutory discount audit report uses `VIEW_AUDIT_REPORT` as the Operator Console action for access evaluation.
- `OperatorConsoleActionCatalog` includes `VIEW_AUDIT_REPORT` as an audit read action.

Readiness note:

- A fiscal status view-audit report should use an explicit audit/reporting authorization posture.
- It should not rely only on the ability to view an individual fiscal status reference.
- A future endpoint should either use an approved existing audit/reporting RBAC policy or introduce a narrow policy for fiscal status view-audit reporting.
- Site/site-group filters must be constrained by the caller's authorized Operator Console site context.

Potential implementation check:

- The currently inspected `OperatorConsoleAccessEvaluationService` supported action set includes `VIEW_FISCAL_ISSUANCE_STATUS` but does not list `VIEW_AUDIT_REPORT` in the same set, even though `VIEW_AUDIT_REPORT` exists in `OperatorConsoleActionCodes` and `OperatorConsoleActionCatalog`.
- Before implementing another audit report using the existing audit action, verify the access evaluator admits the chosen report action or add a narrow supported report action as part of the implementation slice.

## 7. Existing Filters, Pagination, And Sorting Patterns

Existing statutory discount audit report filters:

- `siteId`;
- `siteGroupId`;
- `operatorUserId`;
- `parkingSessionId`;
- `validationStatus`;
- `evidenceStatus`;
- `accessDecision`;
- `from`;
- `to`;
- `limit`;
- `offset`;
- `correlationId`.

Existing pagination pattern:

- query accepts `limit` and `offset`;
- service defaults `limit` to `25`;
- service caps report `limit` at `200`;
- `offset` is clamped to `0` or higher;
- repository returns `totalCount`, `limit`, `offset`, and `correlationId`;
- SQL uses `COUNT(*) OVER()` and `ORDER BY ... DESC LIMIT @limit OFFSET @offset`.

Existing sorting pattern:

- The statutory discount audit report sorts by requested timestamp descending and deterministic id descending.

Recommended fiscal status view-audit report alignment:

- use `limit`/`offset`, not a new pagination model;
- default `limit` to `25`;
- cap `limit` at `200`;
- sort by `performed_at DESC, operator_action_log_id DESC`;
- return `totalCount`, `limit`, `offset`, and `correlationId`;
- filter by date range, site/site group, operator/support user, `fiscalIssuanceReferenceId`, result class, and correlation id.

## 8. Proposed Smallest Implementation Slice

Recommended slice name:

```text
Operator Console fiscal status view-audit report
```

Smallest safe path:

1. Add or complete safe action-log query support for `VIEW_FISCAL_ISSUANCE_STATUS`.
2. Ensure fiscal status view-audit persistence records the viewed `fiscalIssuanceReferenceId` as safe target metadata.
3. Ensure the report can derive or store result class: succeeded, denied, not found, failed safely.
4. Add a narrow Central PMS read-only report endpoint.
5. Add a typed Operator Console UI client method and report page.
6. Add focused tests for filtering, authorization, safe display, no raw payload exposure, and no mutation/action controls.

No database schema change appears necessary if existing `operations.operator_action_logs` columns are sufficient for:

- action code in `action_reason_code`;
- result posture in `action_status` or safe action notes;
- viewed fiscal reference in `target_entity_id`;
- site context in `site_id`;
- actor in `operator_user_id`;
- timestamp in `performed_at`;
- correlation id in `correlation_id`.

However, current fiscal status view persistence does not yet populate `target_entity_id` with `fiscalIssuanceReferenceId`, and it does not persist not-found or safe-failure result class. Those are implementation prerequisites for the full contract.

Avoid implementing the report by parsing free-form logs, app log text, raw HTTP traces, stack traces, raw request payloads, or POS Server payloads.

## 9. Proposed Backend Route

Recommended backend route:

```text
GET /v1/ops/operator-console/audit/fiscal-status-views
```

Suggested query parameters:

| Parameter | Use |
| --- | --- |
| `from` | Start timestamp for `performed_at`. |
| `to` | End timestamp for `performed_at`. |
| `siteId` | Site filter, constrained by caller access. |
| `siteGroupId` | Site-group filter, constrained by caller access. |
| `operatorUserId` | Actor filter. |
| `fiscalIssuanceReferenceId` | Exact viewed reference filter. |
| `resultClass` | `SUCCEEDED`, `DENIED`, `NOT_FOUND`, or `FAILED_SAFELY`. |
| `correlationId` | Exact request/log correlation filter. |
| `limit` | Page size, default `25`, max `200`. |
| `offset` | Offset, default `0`. |

Suggested backend response shape:

- `items`;
- `totalCount`;
- `limit`;
- `offset`;
- `correlationId`.

Suggested item fields:

- action-log entry id;
- action timestamp;
- action code;
- result class;
- operator/support user id;
- site id;
- site group id;
- `fiscalIssuanceReferenceId`;
- correlation id;
- source module/screen;
- safe denial/error posture when available;
- collapsed support/audit detail fields only where authorized.

The endpoint should be read-only and should not call POS Server, fiscal mutation services, payment confirmation services, ExitAuthorization services, gate services, refund/reversal services, or document rendering services.

## 10. Proposed UI Route And Page

Recommended UI route:

```text
/operator-console/audit/fiscal-status-views
```

Recommended page behavior:

- Show a read-only guardrail panel stating that view logs are observational only.
- Provide filters for date range, site/site group, operator/support user, `fiscalIssuanceReferenceId`, result class, and correlation id.
- Load rows from the Central PMS report endpoint using a typed API client method.
- Display main report fields only in the default table.
- Put support/audit-only metadata behind a collapsed details section or row expander.
- Handle `401` and `403` by hiding report details and showing access-denied/unauthorized posture.
- Show empty state when no rows match.
- Avoid any fiscal retry, readback/writeback, POS Server, payment confirmation, ExitAuthorization, gate, refund/reversal, PDF/HTML/QR, export, or raw evidence controls.

The existing `/operator-console/audit` statutory discount report can either remain separate or become an audit landing area with links/tabs to statutory discount audit and fiscal status view-audit reporting. The smallest implementation is a distinct route to avoid mixing report semantics.

## 11. Proposed Files Likely To Change

Backend application/contracts/infrastructure:

| File | Likely Change |
| --- | --- |
| `src/Services/CentralPms/src/ExitPass.CentralPms.Application/OperatorConsole/OperatorConsoleActionCodes.cs` | Reuse `VIEW_AUDIT_REPORT` or add a narrower fiscal status view-audit report action if needed. |
| `src/Services/CentralPms/src/ExitPass.CentralPms.Application/OperatorConsole/OperatorConsoleActionCatalog.cs` | Add metadata for a narrow report action if not reusing `VIEW_AUDIT_REPORT`. |
| `src/Services/CentralPms/src/ExitPass.CentralPms.Application/OperatorConsole/OperatorConsoleAccessEvaluationService.cs` | Ensure the chosen report action is supported by access evaluation. |
| `src/Services/CentralPms/src/ExitPass.CentralPms.Application/OperatorConsole/OperatorConsoleFiscalIssuanceStatusService.cs` | If required, enrich view-audit persistence so `fiscalIssuanceReferenceId` and post-read result class are reportable. |
| `src/Services/CentralPms/src/ExitPass.CentralPms.Application/OperatorConsole/OperatorConsoleAccessEvaluationModels.cs` | If required, support generic target entity metadata rather than only `ParkingSessionId`. |
| `src/Services/CentralPms/src/ExitPass.CentralPms.Contracts/OperatorConsole/OperatorConsoleFiscalStatusViewAuditReportDtos.cs` | Add safe report response and item DTOs. |
| `src/Services/CentralPms/src/ExitPass.CentralPms.Application/OperatorConsole/OperatorConsoleFiscalStatusViewAuditReportModels.cs` | Add query, result, and item read models. |
| `src/Services/CentralPms/src/ExitPass.CentralPms.Application/OperatorConsole/IOperatorConsoleFiscalStatusViewAuditReportService.cs` | Add read-only service abstraction. |
| `src/Services/CentralPms/src/ExitPass.CentralPms.Application/OperatorConsole/IOperatorConsoleFiscalStatusViewAuditReportRepository.cs` | Add read-only repository abstraction. |
| `src/Services/CentralPms/src/ExitPass.CentralPms.Application/OperatorConsole/OperatorConsoleFiscalStatusViewAuditReportService.cs` | Validate filters, clamp pagination, and delegate to repository. |
| `src/Services/CentralPms/src/ExitPass.CentralPms.Infrastructure/OperatorConsole/OperatorConsoleFiscalStatusViewAuditReportRepository.cs` | Query `operations.operator_action_logs` for fiscal status view action rows. |
| `src/Services/CentralPms/src/ExitPass.CentralPms.Api/Endpoints/OperatorConsoleFiscalStatusViewAuditReportEndpoints.cs` | Add route and endpoint mapping. |
| `src/Services/CentralPms/src/ExitPass.CentralPms.Api/Program.cs` | Register service/repository and map endpoint. |

UI:

| File | Likely Change |
| --- | --- |
| `src/Services/OperatorConsoleUi/src/types.ts` | Add fiscal status view-audit report query/response/item types. |
| `src/Services/OperatorConsoleUi/src/apiClient.ts` | Add `listFiscalStatusViewAuditReport` client method and DTO mapping. |
| `src/Services/OperatorConsoleUi/src/App.tsx` | Add route/navigation/page, filters, table, collapsed support/audit details, and access/error states. |
| `src/Services/OperatorConsoleUi/src/App.test.tsx` | Add focused UI/client tests. |
| `src/Services/OperatorConsoleUi/src/styles.css` | Add minimal styling only if existing table/filter/detail classes are insufficient. |

Backend tests:

| File Area | Likely Change |
| --- | --- |
| `src/Services/CentralPms/tests/ExitPass.CentralPms.UnitTests/Application` | Service/query validation tests and source-boundary tests. |
| `src/Services/CentralPms/tests/ExitPass.CentralPms.IntegrationTests/Api` | Endpoint route, RBAC/access handling, safe response, filters, GET-only, and no unsafe field tests. |

## 12. Proposed Tests

Backend tests:

| Scenario | Expected Assertion |
| --- | --- |
| Report loads only `VIEW_FISCAL_ISSUANCE_STATUS` entries | Rows with other `action_reason_code` values are excluded. |
| Filters by date range | `from`/`to` constrain `performed_at`; invalid range returns `400`. |
| Filters by `fiscalIssuanceReferenceId` | Only entries for the exact viewed reference are returned. |
| Filters by result class | `SUCCEEDED`, `DENIED`, `NOT_FOUND`, and `FAILED_SAFELY` filters map to persisted safe result posture. |
| `401` access handling | Unauthenticated caller receives `401`; no report details returned. |
| `403` access handling | Missing report permission or failed Operator Console access evaluation returns `403`; no report details returned. |
| Safe fields only | Response JSON does not contain raw payloads, secrets, stack traces, customer PII, payment provider raw payloads, statutory evidence payloads, or raw POS Server bodies. |
| No mutation dependencies | Service constructor does not wire POS Server, retry, readback/writeback, payment confirmation, ExitAuthorization, gate, refund/reversal, or rendering dependencies. |
| Pagination | Limit defaults/caps correctly, offset clamps to zero, total count is returned. |
| GET-only | Route supports GET only; no POST/PUT/PATCH/DELETE mutation route exists. |

UI/client tests:

| Scenario | Expected Assertion |
| --- | --- |
| Report loads only fiscal status view rows | Table labels rows as view-audit entries for `VIEW_FISCAL_ISSUANCE_STATUS`. |
| Filters by date range | Client sends `from` and `to` query parameters and UI applies selected range. |
| Filters by `fiscalIssuanceReferenceId` | Client sends encoded exact reference query parameter. |
| Filters by result class | Client sends selected result class and table shows safe result labels. |
| `401`/`403` access handling | UI hides report rows and shows access-denied/unauthorized state. |
| Support/audit detail collapsed | Detail metadata is not expanded by default. |
| Never displays unsafe fields | DOM does not contain raw payloads, secrets, stack traces, customer PII, payment provider raw payloads, statutory evidence payloads, or raw POS Server bodies. |
| No mutation/action controls | Page has no retry, readback/writeback, POS Server, payment confirmation, ExitAuthorization, gate, refund/reversal, PDF/HTML/QR, or raw evidence buttons. |

## 13. Risks Or Blockers

| Risk / Blocker | Impact | Recommended Handling |
| --- | --- | --- |
| No dedicated fiscal status view-audit report query path exists. | UI cannot safely load the report yet. | Add a narrow read-only repository/service/endpoint over approved action-log metadata. |
| Current fiscal status view action log does not store `fiscalIssuanceReferenceId` as target metadata. | Exact reference filtering and display cannot be reliably implemented from `operations.operator_action_logs` alone. | Enrich view-audit persistence to store `target_entity_type = FISCAL_ISSUANCE_REFERENCE` and `target_entity_id = fiscalIssuanceReferenceId`, or another approved safe metadata field, before building the full report. |
| Current action-log status distinguishes access `SUCCESS`/`DENIED`, not post-read `NOT_FOUND` or `FAILED_SAFELY`. | Required result-class filter cannot be fully satisfied. | Persist a safe post-read result class or add a governed view-audit read model that records it without raw payloads. |
| Existing access-evaluation model derives target entity only from `ParkingSessionId`. | Reusing it for fiscal references would require a narrow model extension. | Add generic target entity metadata rather than overloading parking session fields. |
| `VIEW_AUDIT_REPORT` exists in the action catalog, but the inspected supported action set does not list it. | A report endpoint using that action may be denied by access evaluation. | Verify and align supported actions before implementation, or add a specific fiscal status view-audit report action. |
| Site/site-group scoping depends on action-log context quality. | Report filters could over- or under-include rows. | Use persisted site context from the access evaluation and constrain filters by evaluated caller scope. |
| Existing action notes are JSON decision snapshots. | Parsing notes for primary report fields would be fragile. | Query typed columns first; expose action notes only as safe support metadata if explicitly approved. |
| Export is not yet specified for implementation. | Export can become an unsafe data exfiltration path. | Keep export out of the first slice unless explicit export RBAC and export audit are included. |

## 14. Explicit Non-Goals

The implementation slice must not add:

- fiscal retry;
- fiscal readback/writeback;
- POS Server mutation or POS Server calls;
- payment confirmation;
- ExitAuthorization;
- gate opening;
- refund or reversal;
- PDF generation;
- HTML generation;
- QR generation;
- final BIR statutory wording;
- raw evidence access;
- raw fiscal request payload display;
- raw POS Server request/response body display;
- payment provider raw payload display;
- customer PII display;
- stack trace display;
- UAT scenario execution.

## 15. Recommendation

Do not build the UI report directly against the current fiscal status view-audit entries until the reportable action-log shape is confirmed.

Recommended path:

```text
First add a narrow read model/query path and complete safe fiscal status view-audit metadata.
```

Specifically:

1. Ensure `VIEW_FISCAL_ISSUANCE_STATUS` action-log entries include the viewed `fiscalIssuanceReferenceId` as safe target metadata.
2. Ensure a safe result class is available for succeeded, denied, not found, and failed safely.
3. Add a read-only Central PMS report endpoint over `VIEW_FISCAL_ISSUANCE_STATUS` action-log entries.
4. Reuse existing statutory discount audit report conventions for filters, `limit`/`offset`, `totalCount`, correlation id, safe DTOs, and UI table/guardrail patterns.
5. Add the UI report page only after the backend can satisfy exact reference filtering and result-class reporting without unsafe log parsing.

If a safe action-log query path already exists by the time implementation starts, proceed with the report implementation using that path. If it does not, the first implementation slice should add the narrow query path and any required safe metadata enrichment before adding the UI.

## 16. Validation Notes

Validation for this readiness note should remain documentation-only:

- run `git diff --check`;
- do not run UI tests as part of this note unless explicitly requested;
- do not run backend tests as part of this note unless explicitly requested;
- do not call Central PMS runtime endpoints;
- do not call POS Server runtime endpoints;
- do not execute UAT scenarios.
