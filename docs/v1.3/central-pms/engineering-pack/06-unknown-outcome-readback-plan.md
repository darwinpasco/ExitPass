# Central PMS Fiscal Issuance Engineering Pack Detail Plan 06: Unknown Outcome / Readback Plan

## Document Control

| Field | Value |
| --- | --- |
| Document | Unknown Outcome / Readback Plan |
| Parent pack | ExitPass Central PMS Fiscal Issuance Engineering Pack Detail v1.0 |
| Scope | Central PMS planning only |
| Status | Detail design plan |

This plan defines Central PMS handling for uncertain POS Server fiscal issuance outcomes and GET readback reconciliation. It does not implement retry workers, source code, SQL, endpoint contracts, or runbooks.

## Purpose

Network and service failures can leave Central PMS uncertain whether POS Server committed a numbered fiscal document. Central PMS must recover safely by preserving the same upstream finality reference, using idempotent replay/readback where possible, and blocking normal ExitAuthorization until fiscal reference evidence is durably recorded.

## Unknown Outcome Scenarios

Planning scenarios:

- POST timeout before response.
- Network disconnect after POS Server may have committed.
- POST returns 503 with fiscal document id.
- POST returns 503 without fiscal document id.
- POS Server success response is received, but Central PMS fiscal reference recording fails.
- GET readback succeeds.
- GET readback fails.
- Replay later succeeds.
- Readback or replay returns mismatched evidence.

## POST Timeout Before Response

Central PMS should:

- mark fiscal state as unknown or requested/unknown.
- keep the same `payableBasis.upstreamFinalityRef`.
- retry with the same semantic request when safe.
- avoid constructing a changed request with the same upstream finality reference.
- block normal ExitAuthorization.

## Network Disconnect After Possible Commit

Central PMS should treat the outcome as unknown. The next safe action is usually a same-key/same-body retry to obtain an idempotent replay, or a GET readback if fiscal document id is known.

## 503 With Fiscal Document Id

If a failed response includes a fiscal document id:

- do not record fiscal issuance evidence from the failed response alone.
- schedule controlled GET readback.
- record the fiscal document id as an investigation/readback reference.
- block normal ExitAuthorization until readback confirms complete evidence and Central PMS records it.

## 503 Without Fiscal Document Id

If no fiscal document id is available:

- preserve same upstream finality reference.
- retry only after service recovery if request semantics are unchanged.
- keep fiscal issuance unresolved.
- escalate when operational impact requires review.

## Fiscal Reference Recording Failure After POS Success

If POS Server returned complete success but Central PMS failed to persist the reference:

- do not issue normal ExitAuthorization.
- preserve the POS Server response in transient logs only if safe and compliant.
- use idempotent replay or GET readback to recover the fiscal reference.
- record an audit event that Central PMS persistence failed after POS Server success.

## Safe Retry Rule

Retry must use:

- same upstream finality reference
- same semantic request facts
- same Site POS Server and fiscal document type scope
- no sensitive payload additions
- no payload mutation to bypass conflict

## GET Readback Decision Matrix

| Condition | Planned action |
| --- | --- |
| fiscal document id known and service available | Call GET readback and validate complete evidence. |
| fiscal document id known but GET unavailable | Keep unknown/service-failed; retry readback after recovery. |
| fiscal document id unknown and request unchanged | Retry POST with same upstream finality reference after recovery. |
| replay returns complete matching evidence | Record/reconcile fiscal reference. |
| readback returns mismatch | Fail closed and route to manual review. |
| readback returns not found after uncertain POST | Keep unresolved; retry or review according to policy. |

GET readback is persisted fiscal document visibility only. It is not payment finality or ExitAuthorization.

## Reconciliation State Transitions

Candidate transitions:

- `fiscal_issuance_requested` to `fiscal_issuance_unknown` on timeout/disconnect.
- `fiscal_issuance_unknown` to `fiscal_issuance_replayed` on successful replay.
- `fiscal_issuance_unknown` to `fiscal_issuance_recorded` on successful readback and durable reference recording.
- `fiscal_issuance_unknown` to `fiscal_issuance_manual_review` on mismatch or inconclusive readback.
- `fiscal_issuance_manual_review` to `fiscal_issuance_reconciled` after approved closure.

## Mismatch Handling

Mismatch examples:

- fiscal document id differs from Central PMS recorded reference.
- fiscal document number differs from recorded reference.
- Site POS Server differs from expected route.
- upstream finality reference does not match expected payment finality.
- evidence status or assignment state is incomplete.

Mismatch must fail closed, block normal ExitAuthorization, and route to manual review.

## Operator Review Escalation

Operator Console should surface:

- unknown fiscal outcome
- readback pending
- readback failed
- replay recovered
- mismatch requiring review
- fiscal reference recording failure
- manual release request after fiscal failure

Operator Console remains governance only.

## Audit Events

Candidate events:

- `FiscalIssuanceUnknownOutcome`
- `FiscalIssuanceReadbackRequested`
- `FiscalIssuanceReplayed`
- `FiscalIssuanceRecorded`
- `FiscalIssuanceManualReviewRequired`
- `FiscalIssuanceReconciled`

## Test Scenarios

Future tests should cover:

- POST timeout then replay success.
- POST timeout then conflict.
- 503 with fiscal document id then GET success.
- 503 with fiscal document id then GET not found.
- 503 without fiscal document id then same-request retry.
- Central PMS persistence failure after POS Server success.
- readback mismatch.
- ExitAuthorization remains blocked until Central PMS reference is durable.

## Risks and Open Questions

- Final retry/readback scheduler ownership remains deferred.
- Whether POS Server returns fiscal document id on all relevant 503 cases may vary by runtime behavior.
- Correlation and durable outbox strategy are future engineering decisions.

## Authority Boundary

Unknown outcome handling must not invent fiscal success. Central PMS may proceed toward normal ExitAuthorization only after confirmed fiscal evidence and durable Central PMS fiscal reference recording.
