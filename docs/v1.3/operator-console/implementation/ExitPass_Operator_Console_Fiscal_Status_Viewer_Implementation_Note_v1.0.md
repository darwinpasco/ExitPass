# ExitPass Operator Console Fiscal Status Viewer Implementation Note v1.0

## 1. Document Control

| Field | Value |
| --- | --- |
| Document | ExitPass Operator Console Fiscal Status Viewer Implementation Note |
| Version | v1.0 |
| Date | 2026-07-08 |
| Branch | `docs/operator-console-fiscal-status-viewer-implementation-note` |
| Implemented feature branch | `feature/operator-console-fiscal-issuance-status-viewer` |
| Scope | Implementation note and post-merge validation record for the read-only Operator Console fiscal issuance status viewer |
| Source contract | `docs/v1.3/operator-console/contracts/ExitPass_Operator_Console_Fiscal_Issuance_Status_Visibility_Contract_v1.0.md` |
| Readiness note | `docs/v1.3/operator-console/implementation/ExitPass_Operator_Console_Fiscal_Status_Viewer_Implementation_Readiness_v1.0.md` |
| Facade endpoint | `GET /v1/ops/operator-console/fiscal-issuance/references/{fiscalIssuanceReferenceId}` |
| UI route | `/operator-console/fiscal-issuance-status` |
| Required permission | `FiscalIssuanceStatusRead` |
| Operator Console action | `VIEW_FISCAL_ISSUANCE_STATUS` |

This note is documentation-only. It records the merged implementation scope and the focused post-merge validation results. It does not modify source code, schema, tests, runtime configuration, POS Server behavior, fiscal issuance mutation behavior, payment behavior, ExitAuthorization behavior, gate behavior, UAT evidence, or UAT runbooks.

No UAT scenarios were run for this note. No POS Server runtime endpoint was called.

## 2. Purpose

This note records the completed implementation of the read-only Operator Console fiscal issuance status viewer and confirms that the merged feature preserves the visibility contract.

The viewer lets authorized operator, support, and audit users look up a fiscal issuance reference and view safe fiscal status details without creating a fiscal action surface. The implementation keeps fiscal status visibility separate from payment confirmation, ExitAuthorization, gate authorization, POS Server mutation, retry, readback/writeback, refund/reversal, and document rendering workflows.

## 3. Implemented Scope

Implemented scope:

- Central PMS Operator Console facade endpoint for fiscal status viewing.
- `FiscalIssuanceStatusRead` authorization on the facade.
- Operator Console identity/header context parsing on the facade.
- Durable Operator Console access evaluation/action-log posture using `VIEW_FISCAL_ISSUANCE_STATUS`.
- Delegation to the existing fiscal issuance status read service.
- Safe response shape equivalent to the existing fiscal status read model/response.
- Operator Console UI route and navigation entry for fiscal status lookup.
- Typed UI API client and fiscal issuance status type.
- State/error display mapping aligned to the visibility contract.
- Collapsed support/audit detail section.
- Focused backend service/API tests and UI/client tests.

The implementation intentionally did not add fiscal retry, readback, writeback, POS Server mutation, payment confirmation, ExitAuthorization, gate opening, refund/reversal, PDF/HTML/QR generation, final BIR statutory wording, schema changes, seed data, UAT evidence changes, or UAT runbook changes.

## 4. Backend Facade Route

The Central PMS facade route added for Operator Console fiscal status viewing is:

```text
GET /v1/ops/operator-console/fiscal-issuance/references/{fiscalIssuanceReferenceId}
```

Route behavior:

| Condition | Expected Result |
| --- | --- |
| Authenticated and authorized caller, reference exists, Operator Console access allowed | `200` with safe fiscal status response |
| Authenticated and authorized caller, reference missing | `404` with safe not-found response |
| Unauthenticated caller when RBAC is enabled | `401` |
| Authenticated caller missing `FiscalIssuanceStatusRead` when RBAC is enabled | `403` |
| Operator Console access evaluation denies the view | `403` without fiscal details |
| Non-GET method | Not supported by the route |

The facade delegates to the existing fiscal issuance status read path. It does not call POS Server and does not mutate fiscal issuance, payment, ExitAuthorization, gate, retry, readback/writeback, refund/reversal, or document rendering state.

## 5. Authorization Policy Used

The facade is protected by:

```text
FiscalIssuanceStatusRead
```

This preserves the same fiscal status read authorization posture as the Central PMS source fiscal status endpoint. The implementation does not infer access from generic Operator Console presence alone and does not weaken production RBAC.

The UI client includes the fiscal status read permission in the existing operator-console local/dev permission header path so local focused tests and development flows can exercise the read endpoint without changing production authorization behavior.

## 6. View-Audit And Action-Log Behavior

The implementation adds an Operator Console action code:

```text
VIEW_FISCAL_ISSUANCE_STATUS
```

The backend service follows the existing Operator Console access evaluation and action-log pattern:

- resolves Operator Console identity/header context;
- evaluates access for fiscal issuance status visibility;
- persists the access/action-log posture using the existing Operator Console writer pattern;
- records the target fiscal issuance reference as the viewed entity context;
- carries the correlation id through the evaluation and facade response handling;
- delegates to the fiscal status read service only when the Operator Console access evaluation allows the view.

The action log is observational only. It records the view posture and must not be used to mutate fiscal issuance, payment, ExitAuthorization, gate, retry, readback/writeback, refund/reversal, or rendering state.

## 7. UI Route And Page

The Operator Console UI route added is:

```text
/operator-console/fiscal-issuance-status
```

The page provides:

- an input for `fiscalIssuanceReferenceId`;
- a read-only lookup action;
- loading, loaded, not-found, access-denied, and error states using existing UI patterns;
- a main fiscal status panel for safe display fields;
- a collapsed support/audit details section for subordinate diagnostic references;
- no retry, readback, writeback, POS Server, payment confirmation, ExitAuthorization, gate, refund/reversal, PDF/HTML/QR, or closure controls.

The page does not call POS Server. It calls the Central PMS Operator Console facade through the typed API client.

## 8. Client And Type Behavior

The Operator Console UI client now exposes a typed fiscal status lookup method that:

- uses `GET`;
- calls `/v1/ops/operator-console/fiscal-issuance/references/{encodedReference}`;
- applies `encodeURIComponent(fiscalIssuanceReferenceId)`;
- sends existing operator identity and correlation headers;
- sends no request body;
- maps `404` into the not-found UI posture;
- maps `401` and `403` into the access-denied/unauthorized UI posture;
- returns the safe fiscal status DTO/type used by the viewer.

The added type models safe fiscal status response fields only. It does not add raw payload, secret, customer PII, payment provider raw payload, statutory evidence payload, or raw POS Server request-body fields.

## 9. State And Error Display Mapping

The UI display mapping implements the visibility contract:

| API State / Condition | UI Display |
| --- | --- |
| `FISCAL_ISSUANCE_RECORDED` with `fiscalDocumentNumber` | `Issued` |
| `FISCAL_ISSUANCE_RECORDED` without `fiscalDocumentNumber` | `Recorded - number not available` |
| `FISCAL_ISSUANCE_REPLAYED` | `Existing issuance reused` |
| `FISCAL_ISSUANCE_CONFLICT` | `Fiscal issuance conflict` |
| `FISCAL_ISSUANCE_FAILED_SERVICE` | `Fiscal service failed` |
| Missing reference / `404` | `Fiscal reference not found` |
| Unauthenticated / `401` or forbidden / `403` | Access denied / unauthorized state with fiscal detail hidden |

Required posture details:

- `Issued` is shown only when `fiscalDocumentNumber` exists.
- Recorded status without a fiscal document number does not show `Issued`.
- Replay is displayed as existing issuance reused, not as duplicate issuance.
- Conflict includes escalation and non-blind-retry guidance.
- Failed service includes support-review guidance.
- Missing reference does not imply unpaid, unauthorized to exit, voided, reversed, or gate state.
- Access denied/unauthorized responses hide fiscal details.

## 10. Support/Audit Detail Posture

Support/audit fields are visually subordinate and placed behind a collapsed details section. The section is intended for correlation and investigation, not customer-facing statutory wording or operational authorization.

Support/audit detail may include safe references such as:

- fiscal issuance reference id;
- upstream finality reference;
- payment confirmation id;
- payment attempt id;
- parking session id;
- site id;
- site POS Server id/reference;
- POS Server fiscal document id as support/audit reference only;
- fiscal setup references;
- fiscal sequence and assignment metadata;
- semantic request hash metadata;
- correlation id.

`posServerFiscalDocumentId` is not displayed as the Sales Invoice number and is not treated as customer-facing invoice text. `fiscalDocumentNumber`, when present, is the Sales Invoice/fiscal document number shown in the main panel.

## 11. Never-Displayed Data

The viewer must not display or derive display content from:

- raw request payloads;
- secrets;
- stack traces;
- payment provider raw payloads;
- customer PII;
- statutory evidence payloads;
- raw POS Server request bodies;
- raw payment callbacks;
- local environment variables or credentials.

Focused UI tests cover the absence of unsafe raw detail in failed-service display posture. The backend facade returns the safe fiscal status response shape and does not enrich the response from unsafe logs or side channels.

## 12. Tests Added And Validated

Focused UI/client coverage includes:

- recorded with `fiscalDocumentNumber` shows `Issued` and the number;
- recorded without `fiscalDocumentNumber` does not show `Issued`;
- replayed status shows `Existing issuance reused` and does not imply duplicate issuance;
- conflict status shows escalation/non-retry guidance and no retry button;
- failed service shows support-review guidance and does not expose stack trace/raw payload;
- `404` shows `Fiscal reference not found` and does not imply unpaid, unauthorized, voided, or reversed state;
- `401` and `403` map to access denied/unauthorized posture and hide fiscal detail;
- API client uses GET, an encoded reference id, correlation/operator headers, and no request body.

Focused backend coverage includes:

- authorized facade request returns `200` for an existing reference;
- missing reference returns `404`;
- unauthenticated request returns `401` when RBAC is enabled;
- missing permission returns `403` when RBAC is enabled;
- Operator Console access denial returns `403` without fiscal details;
- view access evaluation/action-log persistence is invoked for authorized service attempts where the existing pattern supports it;
- the service constructor remains narrow and does not wire POS Server, retry, readback, payment confirmation, ExitAuthorization, gate, refund/reversal, or rendering dependencies;
- route metadata exposes `FiscalIssuanceStatusRead`;
- the facade remains GET-only.

## 13. Post-Merge Validation Commands And Results

Post-merge validation was run on 2026-07-08 from branch:

```text
docs/operator-console-fiscal-status-viewer-implementation-note
```

Commands and results:

| Command | Result |
| --- | --- |
| `npm.cmd --prefix src\Services\OperatorConsoleUi test -- --run src/App.test.tsx` | Passed: 1 test file, 58 tests. |
| `dotnet test src\Services\CentralPms\tests\ExitPass.CentralPms.UnitTests\ExitPass.CentralPms.UnitTests.csproj --filter "FullyQualifiedName~OperatorConsoleFiscalIssuanceStatus\|FullyQualifiedName~OperatorConsoleAccessEvaluationServiceTests"` | Passed after approved sandbox rerun. The first sandboxed attempt could not read the user-level NuGet config during restore. Existing XML documentation warnings were emitted. |
| `dotnet test src\Services\CentralPms\tests\ExitPass.CentralPms.IntegrationTests\ExitPass.CentralPms.IntegrationTests.csproj --filter "FullyQualifiedName~OperatorConsoleFiscalIssuanceStatus"` | Passed: 7 tests. Existing XML documentation/nullability warnings were emitted. |
| `git diff --check` | Passed with no whitespace errors. |

The dotnet commands were focused test runs. They did not call POS Server runtime endpoints and did not execute UAT scenarios.

## 14. Boundaries Preserved

The implementation preserved these boundaries:

| Boundary | Status |
| --- | --- |
| No schema or migration changes | Preserved |
| No seed data changes | Preserved |
| No POS Server code changes | Preserved |
| No direct POS Server call from Operator Console | Preserved |
| No fiscal issuance mutation behavior change | Preserved |
| No fiscal retry | Preserved |
| No fiscal readback/writeback | Preserved |
| No payment confirmation behavior change | Preserved |
| No ExitAuthorization behavior change | Preserved |
| No gate behavior change | Preserved |
| No refund/reversal behavior | Preserved |
| No PDF/HTML/QR generation | Preserved |
| No final BIR statutory wording | Preserved |
| No UAT evidence changes | Preserved |
| No UAT runbook changes | Preserved |

The viewer is a read-only visibility surface. It must not be interpreted as proof of payment, authorization to exit, or permission to open a gate.

## 15. Known Limitations

- The viewer requires a known `fiscalIssuanceReferenceId`; it does not provide search by ticket, plate, payment attempt, invoice number, or POS Server fiscal document id.
- The support/audit detail section exposes safe correlation references only; it does not provide raw fiscal evidence or statutory payload inspection.
- The implementation does not provide fiscal exception closure, retry, readback, writeback, reconciliation resolution, refund/reversal, or document rendering.
- Missing fiscal references are reported as lookup misses only; the viewer does not determine whether the transaction was paid, unpaid, voided, reversed, or authorized to exit.
- The page does not implement role-specific field hiding beyond the current shared `FiscalIssuanceStatusRead` and Operator Console access posture.
- The implementation records view posture through the existing Operator Console action-log path but does not define a separate audit export/report for fiscal status views.

## 16. Recommended Next Slice

Recommended next slice:

```text
Add Operator Console fiscal status lookup ergonomics and audit reporting
```

Suggested scope:

- Add safe lookup pivots only if backed by already-authorized, read-only Central PMS query paths, such as payment attempt or parking session correlation, without exposing raw payloads or PII.
- Add an audit/reporting view for `VIEW_FISCAL_ISSUANCE_STATUS` action-log entries if compliance users need review history.
- Keep support/audit detail collapsed and permission-protected.
- Add no POS Server mutation, fiscal retry, readback/writeback, payment confirmation, ExitAuthorization, gate, refund/reversal, or document rendering controls.

Any future fiscal retry, readback/writeback, exception closure, or statutory rendering work must be defined as a separate governed implementation slice with its own contract, authorization model, audit posture, and tests.
