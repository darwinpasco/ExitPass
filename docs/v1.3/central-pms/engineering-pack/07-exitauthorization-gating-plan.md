# Central PMS Fiscal Issuance Engineering Pack Detail Plan 07: ExitAuthorization Gating Plan

## Document Control

| Field | Value |
| --- | --- |
| Document | ExitAuthorization Gating Plan |
| Parent pack | ExitPass Central PMS Fiscal Issuance Engineering Pack Detail v1.0 |
| Scope | Central PMS planning only |
| Status | Detail design plan |

This plan defines the Central PMS normal ExitAuthorization gating update. It does not implement source code, SQL, API changes, gate integration, UAT scripts, or runbooks.

## Purpose

Central PMS must not issue normal ExitAuthorization until payment finality, POS Server fiscal issuance evidence, fiscal number assignment, and durable Central PMS fiscal reference recording are all complete.

## Gating Rule

Normal ExitAuthorization must remain blocked until:

1. Central PMS verifies payment finality.
2. POS Server fiscal issuance succeeds or replays successfully.
3. POS Server returns `fiscalIssuanceEvidenceStatus = fiscal_document_number_assigned`.
4. POS Server returns `fiscalNumberAssignmentState = assigned`.
5. Central PMS durably records the fiscal issuance reference.

If any condition is missing, Central PMS must not issue normal ExitAuthorization.

## Location in Central PMS Flow

The gating check should sit after payment finality and before ExitAuthorization issuance. It should also be applied in retry/replay paths so recovered fiscal evidence can release the normal gate only after Central PMS reference persistence succeeds.

Final service boundary and call location remain deferred to engineering implementation.

## Fail-Closed Behavior

Fail closed when:

- fiscal issuance has not been requested.
- fiscal issuance request is pending.
- fiscal issuance response is unknown.
- fiscal evidence is incomplete.
- fiscal number assignment state is not assigned.
- Central PMS fiscal reference recording fails.
- idempotency conflict exists.
- readback or replay mismatch exists.
- manual review is required.

## Candidate Blocked Authorization Reasons

Candidate reason labels:

- fiscal issuance pending
- fiscal issuance unknown
- fiscal issuance failed request
- fiscal issuance failed configuration
- fiscal issuance failed service
- fiscal issuance evidence incomplete
- fiscal reference recording failed
- fiscal issuance conflict
- fiscal readback mismatch
- manual review required

Final names and API exposure remain deferred.

## Manual Release / Exception Release Boundary

Manual release is not normal ExitAuthorization. If later policy allows manual release after fiscal issuance failure, it must be:

- separately approved
- supervisor-controlled where required
- incident-tagged
- audit-tagged
- reconciliation-tagged
- reason-coded
- visible to Operator Console and reconciliation workflows

Manual release must not silently convert into payment finality or fiscal issuance success.

## Incident and Reconciliation Tagging

Exception release planning should link:

- payment confirmation
- fiscal issuance state and error reason
- Site and Site POS Server
- operator/supervisor approval
- incident or BCP reference where applicable
- reconciliation status
- post-review closure

## Existing Flow Impact

Potential impact areas:

- current payment success to exit flow
- WebPay exit eligibility messaging
- Assisted Payment Terminal status display
- Continuity Terminal restricted operation
- Operator Console review queues
- Management Dashboard fiscal exception visibility
- gate/exit system expectations

Existing behavior must be regression-tested to prevent payment success from bypassing fiscal reference requirements.

## Regression Risks

- Payment success may be incorrectly treated as sufficient for exit.
- Idempotent replay may accidentally trigger duplicate ExitAuthorization.
- Fiscal reference persistence failure may be overlooked after POS Server success.
- Manual release may be conflated with normal ExitAuthorization.
- Dashboard or Operator Console visibility may lag behind gating state.

## Test / UAT Scenarios

Future tests should include:

- normal success unlocks ExitAuthorization only after fiscal reference persistence.
- payment finality without fiscal issuance blocks ExitAuthorization.
- POS Server success but Central PMS persistence failure blocks ExitAuthorization.
- idempotent replay does not create duplicate ExitAuthorization.
- conflict blocks ExitAuthorization.
- unknown outcome blocks ExitAuthorization.
- manual release exception remains separate and tagged.
- existing non-fiscal flows use `not_required` only under approved policy.

## Authority Boundary

Central PMS owns normal ExitAuthorization. POS Server does not issue ExitAuthorization, and fiscal success is not a gate command.
