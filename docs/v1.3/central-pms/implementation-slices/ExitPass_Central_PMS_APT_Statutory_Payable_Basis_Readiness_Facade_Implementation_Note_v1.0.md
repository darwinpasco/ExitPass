# ExitPass Central PMS APT Statutory Payable-Basis Readiness Facade Implementation Note

## Scope

This slice extends the existing APT-facing payable-basis resolve and revalidate facade:

- `POST /v1/terminal-cash-payments/payable-basis/resolve`
- `POST /v1/terminal-cash-payments/payable-basis/revalidate`

The facade remains a thin Central PMS surface over the shared vendor parking, tariff, terminal-cash eligibility, Sales Invoice readiness, fiscal readiness, and statutory-discount readback services. It does not add an APT-specific statutory decision route, a statutory calculation path, a HikCentral client, payment mutation, fiscal issuance, receipt retrieval, ExitAuthorization mutation, or gate behavior.

The `TerminalCashPayableBasisRead` policy continues to require only `terminal-cash.payable-basis.read`. That permission is read-only and does not authorize APT application access (`apt.access`), shift operation (`cashier-shifts.operate`), custody operation (`cash-custody.operate`), cash receipt (`terminal-cash.receive`), supervisor handover, or `CASH_RECEIVED`. The response field `readyForCashAcceptance` is authoritative readiness information, not human cash-receipt authorization.

## Authority Boundary

Operator Console remains the human entitlement-review surface. The shared statutory-discount backend remains authoritative for canonical decision state, payable-basis application state, applied tariff snapshot identity, final statutory payable amount, VAT facts, discount facts, retryability, and recovery posture.

APT callers may pass an optional `statutoryDiscountDecisionCommandId` as a readback anchor. The facade uses `IStatutoryDiscountDecisionFacadeService.GetAsync` only. It never calls `SubmitAsync`, never approves or rejects a decision, and never submits payable-basis application intent.

## Statutory Readiness Dimension

Resolve and revalidate responses now include `statutoryDiscountReadiness` as a separate readiness dimension. It remains separate from parking-session readiness, tariff readiness, terminal-cash availability, Sales Invoice configuration readiness, and fiscal readiness.

The dimension exposes safe APT-readable facts:

- applicability and ready/blocked state
- decision and application command identities
- decision status/result
- application status/result
- `payableBasisReady`
- `payableBasisReadinessStatus`
- `payableBasisReadinessAction`
- original and applied tariff snapshot identities
- original amount, VAT-exclusive amount, VAT amount, VAT treatment, discount amount, final payable amount, and currency
- retryability, recovery classification/action, safe error code, blocking reason, and safe message

It does not expose reviewer identity, reviewer notes, Operator Console device or shift identity, raw statutory ID values, raw evidence, Base64 payloads, credentials, SQL, stack traces, or downstream exception details.

## Resolve Behavior

When no statutory workflow is active, existing non-statutory behavior is preserved and no statutory blocker is introduced.

When a statutory workflow is active but pending, rejected, retryable, terminal, inconsistent, or missing required facts, resolve returns the original vendor parking payable basis for display continuity while setting `readyForCashAcceptance=false` and adding a statutory blocker.

When the canonical statutory application is `APPLIED` and all required facts are complete, the applied tariff snapshot becomes the current authoritative tariff snapshot and the final statutory payable amount becomes the current authoritative amount for the APT response.

## Revalidate Behavior

Revalidate validates the currently applicable authoritative basis. For an applied statutory basis, comparison uses the applied tariff snapshot, final statutory amount, and authoritative currency returned by Central PMS.

`PASSED_UNCHANGED` is returned only when the applied statutory basis remains unchanged and all readiness dimensions pass.

`AMOUNT_CHANGED` is returned when the authoritative current amount or tariff snapshot differs from the caller's expected basis, including when statutory application changes the payable amount from the original basis.

Pending or blocked statutory state returns `STATUTORY_DISCOUNT_BLOCKED`, not `AMOUNT_CHANGED`.

## Readiness Composition

For statutory workflows, `readyForCashAcceptance` may be true only when:

- statutory `payableBasisReady=true`
- application status is `APPLIED`
- applied tariff snapshot exists
- final payable amount exists
- currency exists
- parking-session and Site scope match
- existing parking-session, tariff, terminal-cash, Sales Invoice, and fiscal readiness dimensions also pass

Local Windows desktop prerequisites remain outside this Central PMS calculation and may only restrict the result later.

## Safe Blocker Codes

The facade maps statutory readback to APT-safe blocker codes:

- `STATUTORY_DISCOUNT_AWAITING_REVIEW`
- `STATUTORY_DISCOUNT_APPLICATION_NOT_REQUESTED`
- `STATUTORY_DISCOUNT_APPLICATION_PROCESSING`
- `STATUTORY_DISCOUNT_DECISION_REJECTED`
- `STATUTORY_DISCOUNT_RETRYABLE_FAILURE`
- `STATUTORY_DISCOUNT_TERMINAL_FAILURE`
- `STATUTORY_DISCOUNT_REQUIRED_FACTS_UNAVAILABLE`
- `STATUTORY_DISCOUNT_STATE_INCONSISTENT`

Null statutory amount, currency, VAT, or applied snapshot facts remain null in the response. The facade does not convert missing facts to zero or infer replacement values.

## Validation

Focused integration and contract tests cover:

- non-statutory compatibility
- awaiting review
- approved decision with application not requested
- application processing
- rejected decision
- retryable and terminal statutory failures
- missing applied facts
- complete applied statutory basis
- parking-session and Site mismatch
- applied-basis `PASSED_UNCHANGED`
- applied-basis `AMOUNT_CHANGED`
- pending statutory state not reported as amount changed
- no statutory mutation through the payable-basis facade

The proof script `scripts/Invoke-CentralPmsAptPayableBasisReadinessProof.ps1` runs the focused APT payable-basis readiness test set, including statutory-readiness coverage.

## Deferred

Windows APT statutory desktop orchestration remains deferred. Statutory `CASH_RECEIVED`, controlled UAT, cash controlled UAT, and production rollout remain unauthorized until the desktop workflow consumes this facade and passes its own validation.
