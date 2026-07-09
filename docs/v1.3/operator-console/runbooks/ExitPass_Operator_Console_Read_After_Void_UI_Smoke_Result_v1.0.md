# ExitPass Operator Console Read-After-Void UI Smoke Result v1.0

## 1. Purpose

Record the Operator Console UI browser smoke for fiscal status visibility after the approved non-production POS Server fiscal document was voided.

## 2. Execution Timestamp

- Executed at: 2026-07-10 02:26:48 +08:00
- Branch: `feature/operator-console-read-after-void-ui-smoke`

## 3. UI Route Tested

- Operator Console UI route: `http://localhost:5175/operator-console/fiscal-issuance-status`
- Central PMS API: `http://localhost:56065`
- POS Server API: `http://localhost:5000`
- Lookup value: `14479d9a-844f-4dba-9578-e863ece93fbf`

## 4. Backend And Facade Precheck Summary

Read-only prechecks passed before the UI smoke:

| Surface | Result |
| --- | --- |
| POS Server direct fiscal document read | HTTP 200 |
| Central PMS fiscal issuance status read | HTTP 200 |
| Operator Console facade read | HTTP 200 |

Observed backend/facade facts:

| Field | Value |
| --- | --- |
| Fiscal issuance reference id | `14479d9a-844f-4dba-9578-e863ece93fbf` |
| POS Server fiscal document id | `9bdf2948-dadd-450b-8776-be688b579395` |
| Fiscal document number | `SI-00000002-UAT` |
| Fiscal sequence value | `2` |
| POS Server document read status | `AVAILABLE` |
| Fiscal document status/posture | `voided` |
| POS Server void status | `recorded` |
| POS Server void reason code | `operator_error` |

## 5. UI Smoke Procedure

The in-app browser surface was unavailable in this Codex session, so the smoke used local Microsoft Edge headless automation against the Vite UI.

Procedure:

1. Started POS Server locally on `http://localhost:5000` against the disposable non-production smoke database.
2. Started Central PMS locally on `http://localhost:56065`.
3. Started Operator Console UI locally on `http://localhost:5175`.
4. Opened `/operator-console/fiscal-issuance-status`.
5. Searched for fiscal issuance reference `14479d9a-844f-4dba-9578-e863ece93fbf`.
6. Verified the page loaded without crash and displayed the expected read-after-void fiscal status.

## 6. UI-Visible Result

| UI-visible fact | Result |
| --- | --- |
| Fiscal document number visible | `SI-00000002-UAT` |
| Fiscal document status/posture visible | `Voided` |
| POS Server document read status visible | `Available` |
| Void status visible | `Recorded` |
| Fiscal sequence value | Present in support/audit detail data; not expanded in the screenshot |
| Unsafe action exposed | No retry, reissue, replacement, payment, gate, refund, HikCentral, or rendering action was exposed |

## 7. Evidence

Evidence path:

`D:\ExitPass-UAT-Evidence\CPS-POS-UAT-20260709-DEV-ATC-001`

Evidence prefix:

`controlled-posserver-fiscal-uat-CPS-POS-UAT-20260709-DEV-ATC-001-operator-console-ui-read-after-void`

Saved evidence includes:

- UI screenshot
- Browser smoke summary JSON
- POS Server direct read response
- Central PMS fiscal issuance status response
- Operator Console facade response
- Backend/facade precheck JSON
- POS Server, Central PMS, and UI runtime logs
- SHA-256 evidence manifest

## 8. Side-Effect Boundary Statement

This UI smoke was read-only. It did not call the POS Server void endpoint again, did not create a fiscal issuance, did not allocate a new fiscal number, did not mutate payment finality, did not issue ExitAuthorization, did not trigger gate behavior, did not create refund/reversal, did not call or write HikCentral, did not call payment providers, did not generate PDF/HTML/QR, and did not introduce final BIR statutory wording.

## 9. Validation Result

| Command | Result |
| --- | --- |
| `npm.cmd --prefix src\Services\OperatorConsoleUi test -- --run src/App.test.tsx` | Passed: 65 tests |
| `npm.cmd --prefix src\Services\OperatorConsoleUi run build` | Passed |
| `git diff --check` | Passed |

## 10. Known Limitations

- In-app browser automation was unavailable in this Codex session; local Edge headless automation was used instead.
- The support/audit details section remains collapsed by default. The UI smoke verified the main visible void status fields and no unsafe action controls.
- No POS Server or Central PMS source changes were made for this UI smoke.

## 11. Final Result

`passed`
