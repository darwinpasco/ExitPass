# Central PMS Fiscal Issuance Engineering Pack Detail Plan 05: Failure / ErrorPosture Plan

## Document Control

| Field | Value |
| --- | --- |
| Document | Failure / ErrorPosture Plan |
| Parent pack | ExitPass Central PMS Fiscal Issuance Engineering Pack Detail v1.0 |
| Scope | Central PMS planning only |
| Status | Detail design plan |

This plan defines Central PMS handling for POS Server conflict, request, configuration, service, and fiscal evidence failures. It does not implement code, SQL, endpoint contracts, retry jobs, or runbook procedures.

## Purpose

Central PMS must fail closed when POS Server fiscal issuance does not return complete fiscal evidence. This plan maps failure categories and `errorPosture` values to candidate fiscal issuance exception states, review queues, and retry boundaries.

## 409 Idempotency Conflict Handling

When POS Server returns `409 fiscal_document_idempotency_conflict`, Central PMS should treat it as:

- same upstream finality reference with semantically different request facts
- fail-closed fiscal issuance state
- no automatic retry with changed payload
- no new upstream finality reference to bypass conflict unless a future supervised correction policy explicitly allows it
- no normal ExitAuthorization
- mandatory review/reconciliation event

Candidate state: `fiscal_issuance_conflict`.

## 400 Request Correction Handling

Request correction failures should include:

- missing payable basis
- missing upstream finality reference
- unapproved discount reference
- unsupported or invalid fiscal request facts
- sensitive payload rejection
- invalid tenders, tax, totals, discount, or privilege details

Central PMS should:

- fail closed.
- record attempt and error details.
- mark request correction required.
- block normal ExitAuthorization.
- route to review if payment finality exists and customer exit is pending.

Candidate state: `fiscal_issuance_failed_request`.

## 400 Fiscal Configuration Correction Handling

Configuration correction failures should include:

- fiscal identity not found, ambiguous, inactive, or not effective
- fiscal sequence policy not found, ambiguous, inactive, or not effective
- fiscal sequence state missing, inactive, unsafe, or not effective
- allocation or format failure when classified as unsafe configuration

Central PMS should:

- fail closed.
- preserve same upstream finality reference.
- avoid automatic retry until configuration is corrected.
- block normal ExitAuthorization.
- notify operational review surfaces.

Candidate state: `fiscal_issuance_failed_configuration`.

## 503 Persistence / Service Handling

Service recovery failures should include:

- persistence not configured
- invalid persistence configuration
- persistence write failed
- service unavailable
- fiscal numbering evidence failure
- `fiscal_number_assignment_incomplete`

Central PMS should:

- not record fiscal issuance evidence.
- preserve the same upstream finality reference for future safe retry if request semantics remain unchanged.
- perform GET readback only when a fiscal document id is available and safe to use.
- block normal ExitAuthorization.
- escalate if outcome is unknown or customer impact exists.

Candidate states: `fiscal_issuance_failed_service` or `fiscal_issuance_unknown`.

## `fiscal_number_assignment_incomplete`

If POS Server reports `fiscal_number_assignment_incomplete`, Central PMS must:

- not treat the response as fiscal issuance evidence.
- not record normal fiscal reference completion.
- not issue normal ExitAuthorization.
- classify as service or fiscal evidence failure.
- route to recovery/manual review.

This response exists to prevent misleading success when complete numbering evidence is missing.

## ErrorPosture Mapping

| `errorPosture` | Central PMS planning response |
| --- | --- |
| `do_not_retry_without_request_change` | Do not retry automatically. Correct request or investigate semantic conflict first. |
| `retry_after_configuration_correction` | Do not retry until Site POS Server fiscal identity, policy, sequence state, or related configuration is corrected. |
| `retry_after_service_recovery` | Retry only after service or persistence recovery. Preserve same upstream finality reference if request semantics are unchanged. |

`errorPosture` guides handling. It is not a full retry scheduler.

## Exception State Mapping

Candidate mappings:

- idempotency conflict -> `fiscal_issuance_conflict`
- request correction -> `fiscal_issuance_failed_request`
- configuration correction -> `fiscal_issuance_failed_configuration`
- service recovery -> `fiscal_issuance_failed_service`
- incomplete evidence -> `fiscal_issuance_failed_service` or `fiscal_issuance_manual_review`
- unknown outcome -> `fiscal_issuance_unknown`

Final names and transition constraints remain deferred.

## Manual Review Escalation

Manual review should be required when:

- idempotency conflict occurs.
- fiscal evidence is incomplete.
- configuration cannot be corrected quickly.
- retry/readback result is inconclusive.
- Central PMS reference mismatches POS Server readback.
- manual release is requested after fiscal issuance failure.

Operator Console supports review/governance only; it must not issue Sales Invoices, collect payment, issue ExitAuthorization, or open gates.

## Retry Blocking Rules

Central PMS should block:

- retry with changed payload under the same upstream finality reference.
- retry with a new upstream finality reference solely to bypass a conflict.
- automatic retry before configuration correction when `retry_after_configuration_correction` is returned.
- automatic retry before service recovery when `retry_after_service_recovery` is returned.
- normal ExitAuthorization while fiscal issuance state is unresolved.

## Audit and Reporting

Record:

- response status and error code
- `errorPosture`
- upstream finality reference
- payment confirmation reference
- Site and Site POS Server
- retry eligibility decision
- operator/manual review reference when created

## Risks and Open Questions

- Exact retry scheduler ownership remains deferred.
- Exact error code taxonomy may evolve with future POS Server API updates.
- Manual release policy is separate and must be approved before operational use.

## Authority Boundary

Failure handling must not convert payment finality into fiscal success or exit authority. POS Server remains fiscal authority only; Central PMS owns normal ExitAuthorization and must fail closed on unresolved fiscal issuance failures.
