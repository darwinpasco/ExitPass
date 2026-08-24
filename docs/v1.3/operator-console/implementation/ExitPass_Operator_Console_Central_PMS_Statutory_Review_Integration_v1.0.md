# ExitPass Operator Console Central PMS Statutory Review Integration v1.0

## 1. Status

`IMPLEMENTED_NOT_ACCEPTED`

This record describes the implementation working tree for the approved Operator Console MVP task. It is automated implementation evidence, not whole-console integrated runtime or visual acceptance.

| Item | Value |
|---|---|
| Selected baseline | `origin/dev` at `a2aa6023ec182c71d4cd8d842a4269a425c76675` |
| Boundary-correction ancestor | `d1ba12cef389b470b88749a0d07a0fa171f2c6a6` |
| Branch | `feature/operator-console-central-pms-statutory-review` |
| Worktree | `D:\wt\OperatorConsoleCentralPmsStatutoryReview` |
| Implementation commit | Pending Darwin commit |
| Requirements authority | Current documents under `docs/v1.3/` only |

The selected baseline was current when the fresh worktree was created. During validation, fetched `origin/dev` advanced to `8fb3903020d60843fbd0de097e793eb8c0522e8d` through two unrelated digital-payment commits. No merge or rebase was performed because this task prohibits Codex-created commits and merges.

## 2. Architecture and reused authority

Operator Console communicates only with the same-origin Central PMS facade. The queue may contain requests originating from WebPay or APT, but the browser has no WebPay, APT, or Management Platform URL, client, callback, messaging dependency, or shared client database.

The implementation reuses these canonical Central PMS capabilities rather than creating parallel persistence or workflow:

- `src/Services/CentralPms/src/ExitPass.CentralPms.Application/OperatorConsole/OperatorConsoleServiceChannelStatutoryDiscountReviewService.cs` for queue, detail, scope evaluation, terminal decisions, idempotency, and conflict handling.
- `src/Services/CentralPms/src/ExitPass.CentralPms.Infrastructure/StatutoryDiscounts/PostgresStatutoryDiscountServiceChannelReviewRepository.cs` for canonical request records, pagination/filtering, reviewer audit fields, and read-only payable-basis application status.
- `src/Services/CentralPms/src/ExitPass.CentralPms.Application/OperatorConsole/OperatorConsoleStatutoryEvidenceReviewService.cs` for Central PMS-controlled evidence metadata and preview authorization.
- Existing statutory approve, reject, and evidence-view permissions plus the H-006 human session, CSRF, authorization epoch, expiry, revocation, and Site/Site Group scope pipeline.

No database schema or migration is added. Operator Console neither calculates statutory entitlement nor applies a payable basis.

## 3. Implemented behavior

### 3.1 Queue

`src/Services/OperatorConsoleUi/src/CanonicalStatutoryReview.tsx` replaces the active legacy statutory-draft path with the canonical Central PMS queue. It defaults to pending, uses server pagination, and supports status, authorized-Site, benefit type, originating channel, and safe request/session/ticket/plate search. List rows omit evidence content and plate data and show only selection facts. Loading, empty, stale, unavailable, denied, and failed states are distinct.

`GET /v1/ops/operator-console/statutory-discounts/reviews` is the canonical Operator Console facade. The former pending URL remains a compatibility alias to the same service; the UI does not call a channel-owned route.

### 3.2 Detail and evidence

The detail route reads the canonical request, prior decision attribution, terminal status, and payable-basis application status. Monetary values are accepted only when their currency is exactly `PHP` and are rendered with `₱`.

Evidence metadata remains Central PMS-controlled. Preview selection is sent in the CSRF-protected request body to avoid putting an evidence identifier in the browser URL. The Operator Console contract no longer returns the storage reference. The browser uses temporary object URLs only and displays a concise privacy notice before evidence access.

### 3.3 Decisions

Approve and reject operate on pending canonical requests. Rejection requires a reason, the reviewer must attest that the facts and evidence were reviewed, and an in-app confirmation precedes submission. The browser sends only decision data and an idempotency key. Central PMS derives reviewer identity, timestamp, permissions, and Site/Site Group scope from the authenticated server session; unknown or client-authored authority fields are rejected.

The canonical service preserves immutable terminal decisions, idempotent retries, atomic decision persistence, and controlled already-terminal/concurrent-review results. The UI disables repeat submission and refreshes the authoritative detail after every result or conflict. Safe queue filters remain in memory for return navigation and are cleared with the protected application state on logout/session loss.

## 4. Preserved boundaries

- PHP-only validation and `₱` presentation remain fail closed for missing, blank, or non-PHP currency.
- Fiscal status and historical void audit remain read-only; no Operator Console fiscal mutation control or route is introduced.
- No direct WebPay, APT, or Management Platform request is introduced.
- No request, evidence, decision, permission, scope, or authority data is written to browser storage.
- No evidence download/export, PDF, OCR, biometrics, automated government verification, continuity workflow, report/export expansion, or whole-console acceptance work is included.

## 5. Automated validation

Validation performed from the uncommitted working tree on 2026-08-24:

| Validation | Result |
|---|---|
| Operator Console Vitest suite | Pass: 120 active tests; 28 obsolete legacy-draft assertions skipped pending deletion after compatibility review |
| Focused Central PMS unit tests | Pass: 42/42 |
| Focused Central PMS statutory-review API/PostgreSQL integration tests | Pass: 50/50 |
| Focused Central PMS evidence facade integration tests | Pass: 22/22 |
| Hosted H-006 authentication/session integration tests | Pass: 5/5 |
| Central PMS API build | Pass; existing XML-documentation/nullability warnings only |
| Operator Console production build | Pass |
| Focused Chromium | Pass: 4/4, covering 1440×900, 768×1024, 390×844, keyboard action, CSRF decision shape, same-origin topology, PHP display, privacy notice, and fiscal-control absence |

Final encoding, control-byte, link, dependency, whitespace, and changed-file boundary checks are recorded in the completion report and the traceability matrix validation section.

## 6. Remaining acceptance boundary

This task does not establish `IMPLEMENTED_AND_ACCEPTED`. The final separate whole-console acceptance task must exercise the merged candidate with hosted Central PMS, current identities, multi-Site/Site Group data, representative WebPay- and APT-originated requests, real evidence access, concurrent reviewers, dependency failures, browser privacy inspection, and retained visual/runtime artifacts tied to an exact reachable commit.
