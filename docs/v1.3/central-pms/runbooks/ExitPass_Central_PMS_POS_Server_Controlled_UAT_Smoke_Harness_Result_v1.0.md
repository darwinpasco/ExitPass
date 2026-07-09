# ExitPass Central PMS POS Server Controlled UAT Smoke Harness Result v1.0

## Purpose

Record the first local runtime smoke of the reusable internal non-production fiscal issuance smoke harness after replacing the one-off July 9 constants with an allowlisted profile model.

## Profile

| Field | Value |
| --- | --- |
| Profile id | `CPS-POS-UAT-20260709-DEV-ATC-001` |
| Run id | `CPS-POS-UAT-20260709-DEV-ATC-001` |
| Environment | `DEV-CONTROLLED-UAT-LOCAL` |
| Central PMS URL | `http://localhost:56065` |
| POS Server URL | `http://localhost:5000` |
| Evidence path | `D:\ExitPass-UAT-Evidence\CPS-POS-UAT-20260709-DEV-ATC-001` |

## Scenarios Run

| Scenario | HTTP status | Response status | POS creation attempted | Result |
| --- | ---: | --- | --- | --- |
| `replay` | `200` | `replay_recorded` | `false` | Passed |
| `conflict` | `409` | `conflict_failure_mapped` | `false` | Passed |

## Fiscal Result

| Field | Value |
| --- | --- |
| Fiscal issuance reference id | `14479d9a-844f-4dba-9578-e863ece93fbf` |
| POS Server fiscal document id | `9bdf2948-dadd-450b-8776-be688b579395` |
| Fiscal document number | `SI-00000002-UAT` |
| Fiscal sequence value | `2` |
| New fiscal number allocated | No |

Read-only DB verification showed one active Central PMS fiscal issuance reference and one active POS Server fiscal document for the approved upstream finality reference.

## Side-Effect Checks

Read-only Central PMS DB counts for correlation id `b7b4cbea-0c8c-4d06-9f6f-728a0a3fc2df`:

| Check | Count |
| --- | ---: |
| `core.exit_authorizations` | `0` |
| `gates.gate_authorization_consumptions` | `0` |
| `gates.gate_events` | `0` |
| `operations.manual_gate_logs` | `0` |

No payment provider call, HikCentral write, ExitAuthorization, gate behavior, refund/reversal, PDF/HTML/QR generation, or final BIR statutory wording was introduced.

## Evidence

| Evidence file | Purpose |
| --- | --- |
| `controlled-posserver-fiscal-uat-CPS-POS-UAT-20260709-DEV-ATC-001-smoke-harness-replay-request.json` | Replay smoke request |
| `controlled-posserver-fiscal-uat-CPS-POS-UAT-20260709-DEV-ATC-001-smoke-harness-replay-response.json` | Replay smoke response |
| `controlled-posserver-fiscal-uat-CPS-POS-UAT-20260709-DEV-ATC-001-smoke-harness-conflict-request.json` | Conflict smoke request |
| `controlled-posserver-fiscal-uat-CPS-POS-UAT-20260709-DEV-ATC-001-smoke-harness-conflict-response.json` | Conflict smoke response |
| `controlled-posserver-fiscal-uat-CPS-POS-UAT-20260709-DEV-ATC-001-smoke-harness-summary.json` | Runtime summary |
| `controlled-posserver-fiscal-uat-CPS-POS-UAT-20260709-DEV-ATC-001-smoke-harness-db-verification.json` | Read-only DB verification |
| `controlled-posserver-fiscal-uat-CPS-POS-UAT-20260709-DEV-ATC-001-smoke-harness-hash.txt` | SHA-256 hashes |

## Validation

- Focused Central PMS unit tests for controlled UAT invocation, fixture store, and orchestration behavior: passed.
- Runtime replay smoke: passed.
- Runtime conflict smoke: passed.
- Read-only DB side-effect verification: passed.
- `git diff --check`: passed with Git line-ending notices only.

Final result: `passed`.
