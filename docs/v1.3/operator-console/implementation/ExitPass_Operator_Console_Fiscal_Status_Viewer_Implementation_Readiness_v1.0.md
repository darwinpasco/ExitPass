# ExitPass Operator Console Fiscal Status Viewer Implementation Readiness v1.0

## 1. Document Control

| Field | Value |
| --- | --- |
| Document | ExitPass Operator Console Fiscal Status Viewer Implementation Readiness |
| Version | v1.0 |
| Date | 2026-07-08 |
| Branch | `docs/operator-console-fiscal-status-viewer-implementation-readiness` |
| Scope | Readiness note for a future read-only Operator Console fiscal issuance status viewer |
| Source contract | `docs/v1.3/operator-console/contracts/ExitPass_Operator_Console_Fiscal_Issuance_Status_Visibility_Contract_v1.0.md` |
| Source endpoint | `GET /v1/fiscal-issuance/references/{fiscalIssuanceReferenceId}` |
| Required permission | `FiscalIssuanceStatusRead` |

This readiness note is documentation-only. It does not modify source code, schema, tests, UI behavior, runtime configuration, Central PMS runtime state, POS Server state, payment state, fiscal state, ExitAuthorization state, or gate state.

No Central PMS or POS Server runtime endpoints were called while preparing this note.

## 2. Purpose

This note documents the smallest safe implementation path for adding a read-only Operator Console fiscal issuance status viewer.

The viewer should let authorized internal/operator/support/audit users inspect Central PMS fiscal issuance status by `fiscalIssuanceReferenceId` while preserving the fiscal visibility contract:

- read-only status display only;
- no POS Server call from Operator Console;
- no fiscal retry or readback/writeback;
- no payment confirmation;
- no ExitAuthorization;
- no gate opening;
- no customer PII, raw payload, secret, stack trace, or statutory evidence payload display.

## 3. Existing Repo/App Structure

The repository already contains an Operator Console UI app:

| Path | Current Relevance |
| --- | --- |
| `src/Services/OperatorConsoleUi/package.json` | Vite/React app with `dev`, `build`, and `test` scripts. |
| `src/Services/OperatorConsoleUi/src/App.tsx` | Route-driven Operator Console shell and page composition. |
| `src/Services/OperatorConsoleUi/src/apiClient.ts` | Shared typed HTTP client, mock client, operator headers, permission header, and error mapping. |
| `src/Services/OperatorConsoleUi/src/types.ts` | UI/client TypeScript domain types. |
| `src/Services/OperatorConsoleUi/src/App.test.tsx` | Vitest/Testing Library coverage for shell, routes, data states, mocked client behavior, and HTTP client behavior. |
| `src/Services/OperatorConsoleUi/src/styles.css` | Existing visual styling for panels, state messages, detail grids, status pills, diagnostics, and module pages. |

The Central PMS backend already contains Operator Console support services and endpoint patterns:

| Path | Current Relevance |
| --- | --- |
| `src/Services/CentralPms/src/ExitPass.CentralPms.Api/Endpoints/OperatorConsoleAccessReadinessEndpoints.cs` | Existing access readiness API and logging pattern. |
| `src/Services/CentralPms/src/ExitPass.CentralPms.Api/Endpoints/OperatorConsoleSessionLookupEndpoints.cs` | Existing read-only access-gated Operator Console endpoint pattern. |
| `src/Services/CentralPms/src/ExitPass.CentralPms.Api/Endpoints/OperatorConsoleIdentityContext.cs` | Centralized Operator Console identity/header parsing. |
| `src/Services/CentralPms/src/ExitPass.CentralPms.Application/OperatorConsole/OperatorConsoleSessionLookupService.cs` | Existing access-evaluation plus persisted-audit read-only service pattern. |
| `src/Services/CentralPms/src/ExitPass.CentralPms.Infrastructure/OperatorConsole/OperatorConsoleAccessEvaluationWriter.cs` | Existing persistence of Operator Console action log/audit evidence. |
| `src/Services/CentralPms/src/ExitPass.CentralPms.Application/OperatorConsole/OperatorConsoleActionCodes.cs` | Controlled Operator Console action code catalog. No fiscal status view action exists yet. |

The Central PMS fiscal status endpoint already exists:

| Path | Current Relevance |
| --- | --- |
| `src/Services/CentralPms/src/ExitPass.CentralPms.Api/Endpoints/FiscalIssuanceStatusEndpoints.cs` | Protected read-only `GET /v1/fiscal-issuance/references/{fiscalIssuanceReferenceId}` endpoint and response DTO. |
| `src/Services/CentralPms/src/ExitPass.CentralPms.Application/FiscalIssuance/FiscalIssuanceStatusReadService.cs` | Read service and read model used by the status endpoint. |
| `src/Services/CentralPms/src/ExitPass.CentralPms.Application/Security/CentralPmsRbacPolicyCatalog.cs` | Maps `FiscalIssuanceStatusRead` to `fiscal-issuance.status.read`, `reconciliation.view`, and `reconciliation.manage`. |
| `src/Services/CentralPms/tests/ExitPass.CentralPms.IntegrationTests/Api/FiscalIssuanceStatusApiAccessPolicyIntegrationTests.cs` | Existing backend tests for 401, 403, 200, 404, GET-only, and policy metadata. |

## 4. Operator Console UI/App Shell Status

An Operator Console UI/app shell currently exists.

Observed shell capabilities:

- top-level route map in `App.tsx`;
- navigation rail under `/operator-console`;
- existing modules for overview, ticket lookup, statutory discounts, audit/reporting, vendor acknowledgments, projection health, and policy import review;
- injected `OperatorConsoleApiClient` for tests and runtime;
- existing `LoadState<T>` pattern for loading, loaded, empty, not-found, access-denied, and error UI states;
- existing `StateMessage`, panel, detail grid, status pill, and diagnostics panel patterns;
- existing mock API client for deterministic UI tests.

Recommendation: proceed with implementation. Creating an app shell first is not necessary.

## 5. Existing API Client/Service Patterns

The UI app should follow `src/Services/OperatorConsoleUi/src/apiClient.ts`:

- Add a typed `getFiscalIssuanceStatus(fiscalIssuanceReferenceId: string)` method to `OperatorConsoleApiClient`.
- Add response DTO/type mapping for the Central PMS fiscal status response fields.
- Use `newCorrelationId()` and `operatorConsoleHeaders(correlationId)`.
- Use `encodeURIComponent(fiscalIssuanceReferenceId)` in the URL.
- Use the existing `parseResponse<T>()` behavior so:
  - `404` maps to `not-found`;
  - `401` and `403` map to `access-denied`;
  - other non-success responses map to `error`.
- Keep the method GET-only and side-effect free.

Direct endpoint call shape:

```text
GET /v1/fiscal-issuance/references/{fiscalIssuanceReferenceId}
```

The direct-call approach is the smallest UI-only implementation path because the fiscal status endpoint already exists and carries RBAC metadata. However, it does not by itself add Operator Console access-evaluation persistence for the view action.

If durable Operator Console view-audit logging is required in the first implementation slice, the smallest safe implementation becomes a thin Central PMS Operator Console facade endpoint that:

- resolves Operator Console identity using `OperatorConsoleIdentityContext`;
- evaluates/persists a read action using the existing access evaluation writer;
- requires or forwards the `FiscalIssuanceStatusRead` RBAC policy;
- delegates to `IFiscalIssuanceStatusReadService`;
- returns the same safe fiscal status response shape;
- does not call POS Server and does not mutate fiscal/payment/exit/gate state.

## 6. Authorization/RBAC Patterns

Current fiscal status endpoint authorization:

- `FiscalIssuanceStatusEndpoints.cs` applies `ReconciliationPolicyMetadata("FiscalIssuanceStatusRead")`.
- `CentralPmsRbacPolicyCatalog.cs` maps `FiscalIssuanceStatusRead` to:
  - `fiscal-issuance.status.read`
  - `reconciliation.view`
  - `reconciliation.manage`
- `CentralPmsRbacMiddleware` enforces unauthenticated `401` and unauthorized `403` behavior when RBAC is enabled.
- Local/dev header permissions flow through `X-ExitPass-Permissions` when `CentralPms:Rbac:AllowPermissionHeader` is enabled.

Current Operator Console UI header pattern:

- `operatorConsoleHeaders()` sends:
  - `X-Correlation-Id`
  - `X-Operator-User-Id`
  - `X-ExitPass-User-Id`
  - `X-ExitPass-Permissions`
  - `X-Operator-Device-Binding-Id`
  - `X-Operator-Shift-Id`
- `VITE_OPERATOR_CONSOLE_PERMISSIONS` currently defaults to policy import and projection health permissions. A fiscal viewer implementation must add `fiscal-issuance.status.read` for local/dev use or document that the direct fiscal status call will return `403` without that permission.

Implementation note:

- Do not weaken RBAC.
- Do not bypass `FiscalIssuanceStatusRead`.
- Do not infer fiscal status access from generic Operator Console route access alone.

## 7. Audit/Logging Patterns

Existing backend patterns:

- `OperatorConsoleAccessReadinessEndpoints.cs` logs readiness evaluations with correlation id, requested action, decision, and allowed flag.
- `OperatorConsoleSessionLookupService.cs` evaluates access, persists an action log, and only then returns read-only session details.
- `OperatorConsoleAccessEvaluationWriter.cs` writes to `operations.operator_action_logs` using `CONTROLLED_RECHECK`, target entity metadata, action status, decision snapshot, performed timestamp, and correlation id.
- `OperatorConsoleStatutoryDiscountReadRepository.cs` contains read-only audit/reporting queries that avoid raw evidence payload exposure.

Current gap for fiscal status viewing:

- The direct fiscal status endpoint enforces RBAC but does not appear to persist an Operator Console view action by itself.
- `OperatorConsoleActionCodes.cs` has no fiscal status view action yet.

Readiness decision:

- If the implementation slice must satisfy the visibility contract's "viewing fiscal issuance status must be auditable" expectation using durable Operator Console action logs, add a small backend facade/action code first.
- If the first implementation accepts RBAC plus API/log telemetry only, the UI can call the direct endpoint, but that should be explicitly recorded as a limited audit posture and followed by an audit-hardening slice.

Recommended safe path: include the thin Operator Console facade in the implementation slice so the viewer has durable view-audit evidence from the start.

## 8. Proposed Smallest Implementation Slice

Recommended slice name:

```text
Operator Console read-only fiscal issuance status viewer
```

Scope:

1. Add a fiscal status route in `OperatorConsoleUi`, for example:

```text
/operator-console/fiscal-issuance-status
```

2. Add a form accepting `fiscalIssuanceReferenceId`.

3. Add a read-only detail panel that displays only the fields allowed by the visibility contract:

- state;
- label mapping;
- Sales Invoice/fiscal document number only when `fiscalDocumentNumber` exists;
- replay/conflict/failed-service posture;
- safe error code/posture;
- timestamps.

4. Add collapsed support/audit detail for support-only references such as:

- fiscal issuance reference id;
- upstream finality reference;
- payment confirmation id;
- payment attempt id;
- parking session id;
- site/POS Server references;
- POS Server fiscal document id;
- hash metadata.

5. Add no action buttons for retry, readback, writeback, payment confirmation, ExitAuthorization, gate opening, refund, reversal, PDF/HTML/QR, or POS Server calls.

6. Add or choose an access/audit path:

- Preferred: add a thin Central PMS Operator Console fiscal status view endpoint/facade that persists view audit and delegates to `IFiscalIssuanceStatusReadService`.
- Minimum UI-only: call the existing protected fiscal status endpoint directly and rely on Central PMS RBAC plus standard HTTP logging, with durable view-audit as a follow-up.

7. Add focused UI/client tests and, if the facade is added, backend API/service tests.

## 9. Proposed Files Likely To Change

UI-only portion:

| File | Likely Change |
| --- | --- |
| `src/Services/OperatorConsoleUi/src/types.ts` | Add `FiscalIssuanceStatus`, state/result/error posture types, and display model helpers if kept typed. |
| `src/Services/OperatorConsoleUi/src/apiClient.ts` | Add `getFiscalIssuanceStatus`, DTO mapping, mock data, mock client option hooks, and default local permission update. |
| `src/Services/OperatorConsoleUi/src/App.tsx` | Add route, navigation item, fiscal status page, form, state mapping, main panel, and support/audit details. |
| `src/Services/OperatorConsoleUi/src/App.test.tsx` | Add UI and client tests for success, state mapping, 404, 401, and 403 behavior. |
| `src/Services/OperatorConsoleUi/src/styles.css` | Add only minimal styling if existing panel/detail/status classes are insufficient. |

Preferred backend facade/audit portion:

| File | Likely Change |
| --- | --- |
| `src/Services/CentralPms/src/ExitPass.CentralPms.Application/OperatorConsole/OperatorConsoleActionCodes.cs` | Add `VIEW_FISCAL_ISSUANCE_STATUS` or equivalent action code. |
| `src/Services/CentralPms/src/ExitPass.CentralPms.Api/Endpoints/OperatorConsoleFiscalIssuanceStatusEndpoints.cs` | Add read-only Operator Console facade endpoint if durable view audit is in first slice. |
| `src/Services/CentralPms/src/ExitPass.CentralPms.Application/OperatorConsole/OperatorConsoleFiscalIssuanceStatusService.cs` | Evaluate/persist view access and delegate fiscal status read. |
| `src/Services/CentralPms/src/ExitPass.CentralPms.Application/OperatorConsole/IOperatorConsoleFiscalIssuanceStatusService.cs` | Add service abstraction if following existing Operator Console patterns. |
| `src/Services/CentralPms/src/ExitPass.CentralPms.Contracts/OperatorConsole/OperatorConsoleFiscalIssuanceStatusDtos.cs` | Add facade contract DTOs only if not reusing endpoint-local fiscal DTOs. |
| `src/Services/CentralPms/src/ExitPass.CentralPms.Api/Program.cs` | Register service and map facade endpoint. |
| `src/Services/CentralPms/tests/ExitPass.CentralPms.UnitTests/...` | Add service tests for access denied, audit persisted before read, and no mutation. |
| `src/Services/CentralPms/tests/ExitPass.CentralPms.IntegrationTests/Api/...` | Add facade endpoint tests for RBAC, access audit, 200, 404, and GET-only behavior. |

No schema changes should be needed if the existing `operations.operator_action_logs` access-evaluation writer can represent the view action.

## 10. Proposed Tests

UI tests in `src/Services/OperatorConsoleUi/src/App.test.tsx`:

| Scenario | Expected Assertion |
| --- | --- |
| Recorded with `fiscalDocumentNumber` | Shows `Issued`, Sales Invoice/fiscal document number, timestamp, and no retry/gate/payment wording. |
| Recorded without `fiscalDocumentNumber` | Shows `Recorded - number not available`; does not show `Issued`. |
| Replayed | Shows `Existing issuance reused`; shows existing number when present; does not imply duplicate issuance. |
| Conflict | Shows `Fiscal issuance conflict`; shows escalation/non-retry guidance; no retry button. |
| Failed service | Shows `Fiscal service failed`; shows support-review guidance; no stack trace/raw payload. |
| Missing reference / `404` | Shows `Fiscal reference not found`; does not imply unpaid, unauthorized to exit, voided, or reversed. |
| Unauthenticated / `401` | Maps to access denied/unauthorized state and hides fiscal detail. |
| Forbidden / `403` | Maps to access denied state and hides fiscal detail. |

Client tests in `src/Services/OperatorConsoleUi/src/App.test.tsx` or a future `apiClient.test.ts`:

- Sends a GET to the fiscal status endpoint/facade with `encodeURIComponent(fiscalIssuanceReferenceId)`.
- Includes `X-Correlation-Id`, operator identity headers, and `X-ExitPass-Permissions`.
- Maps `404` to `not-found`.
- Maps `401` and `403` to `access-denied`.
- Does not send a request body or idempotency key for the read-only GET.

Backend tests if a facade is added:

- Authorized fiscal status view returns recorded status.
- Missing reference returns safe not-found response.
- Unauthenticated request returns `401`.
- Missing permission returns `403`.
- Access denied by Operator Console readiness returns no fiscal details.
- View access evaluation/action log is persisted before details are returned.
- No POS Server client, fiscal retry/readback/writeback, payment confirmation, ExitAuthorization, gate, refund, reversal, or document rendering behavior is wired.

Existing backend coverage already present:

- `FiscalIssuanceStatusApiAccessPolicyIntegrationTests.cs` covers the direct fiscal endpoint for 401, 403, 200, 404, GET-only behavior, and policy metadata.

## 11. Explicit Non-Goals

The implementation slice must not add:

- fiscal retry
- fiscal readback/writeback
- POS Server mutation
- direct POS Server calls from Operator Console
- payment confirmation
- ExitAuthorization
- gate opening
- refund or reversal
- PDF generation
- HTML generation
- QR generation
- final BIR statutory wording
- schema changes
- UAT scenario execution
- payment provider interaction
- raw request payload display
- secrets, stack traces, customer PII, raw payment provider payloads, or statutory evidence payload display

## 12. Risks Or Blockers

| Risk / Blocker | Impact | Recommended Handling |
| --- | --- | --- |
| Direct fiscal status endpoint has RBAC but no durable Operator Console view-audit persistence. | May not satisfy the visibility contract's audit expectation. | Prefer thin Operator Console facade or explicitly split audit-hardening into the same implementation slice. |
| No existing fiscal status view action code. | Access evaluation/action log cannot clearly identify fiscal status views yet. | Add a narrow `VIEW_FISCAL_ISSUANCE_STATUS` action code if facade path is selected. |
| UI local default permissions do not currently include `fiscal-issuance.status.read`. | Local/dev viewer calls can return `403`. | Add permission only to local/dev default header configuration or document required env var. Do not weaken production RBAC. |
| Source endpoint path is outside existing `/v1/ops/operator-console/...` namespace. | UI can call it safely, but routing/audit posture differs from existing Operator Console modules. | Prefer facade if consistency and action logging matter in the first slice. |
| Fiscal status response contains many support/audit fields. | UI could accidentally expose fields beyond the display contract. | Separate main display model from support/audit detail model and cover never-displayed fields in tests. |
| `Issued` wording depends on `fiscalDocumentNumber`, not state alone. | Incorrect UI could overstate fiscal issuance. | Test recorded-with-number and recorded-without-number separately. |
| Conflict and failed-service states could be mistaken for retryable operator actions. | Operator could retry blindly or escalate incorrectly. | Do not render retry controls; show escalation/support-review guidance only. |
| Backend facade may require choosing whether to reuse endpoint-local `FiscalIssuanceStatusResponse` or introduce a contract DTO. | Contract ownership and OpenAPI clarity decision. | Keep response shape equivalent; avoid expanding fields. |

No blocker requires creating a new UI app shell.

## 13. Recommendation

Proceed with implementation because the Operator Console UI/app shell exists.

Recommended first implementation slice:

```text
Add read-only Operator Console fiscal issuance status viewer with durable view-audit posture
```

Preferred path:

1. Add a thin Central PMS Operator Console facade for fiscal status views if durable Operator Console action logging is required in the same slice.
2. Delegate the facade to the existing `IFiscalIssuanceStatusReadService`.
3. Keep `FiscalIssuanceStatusRead` RBAC protection.
4. Add `VIEW_FISCAL_ISSUANCE_STATUS` access/audit action code.
5. Add UI route/page/client/types/tests using the existing OperatorConsoleUi patterns.
6. Keep all mutation, retry, payment, exit, gate, refund/reversal, rendering, POS Server, and statutory wording work out of scope.

Minimum path if audit persistence is explicitly deferred:

1. Add UI route/page/client/types/tests only.
2. Call `GET /v1/fiscal-issuance/references/{fiscalIssuanceReferenceId}` directly.
3. Require `fiscal-issuance.status.read`.
4. Document the temporary audit limitation and schedule a follow-up facade/audit slice before broader rollout.

The preferred path is safer because it aligns with existing Operator Console access-evaluation and action-log patterns while still remaining small and read-only.

## 14. Validation Notes

Validation for this readiness note should remain documentation-only:

- run `git diff --check`;
- do not run UI tests as part of this note unless explicitly requested;
- do not call Central PMS runtime endpoints;
- do not call POS Server runtime endpoints;
- do not execute UAT scenarios.
