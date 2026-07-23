# ExitPass Central PMS Operator Console Statutory Discount Apply Convergence Implementation Note

## Purpose

This slice converges the retained Operator Console apply-payable-basis route onto the canonical staged payable-basis-application-v1 command while preserving the existing staged Operator Console workflow.

Retained legacy route:

- `POST /v1/ops/operator-console/statutory-discounts/{validationId}/apply-payable-basis`

The slice does not change the Operator Console UI, statutory calculation, VAT treatment, payment finality, fiscal issuance, ExitAuthorization, gate behavior, WebPay, APT, or POS Server runtime behavior.

## Canonical Decision Prerequisite

The route requires the validation to be approved and linked to a canonical decision-v2 command. The linked decision must exist and must be `COMPLETED` with decision result `APPROVED`.

Safe deterministic failures:

- missing validation: `STATUTORY_DISCOUNT_VALIDATION_NOT_FOUND`
- validation not approved: `STATUTORY_DISCOUNT_NOT_APPROVED`
- missing canonical decision linkage or missing command: `STATUTORY_DISCOUNT_DECISION_NOT_FOUND`
- non-approved canonical decision: `STATUTORY_DISCOUNT_DECISION_NOT_APPROVED`

The apply route does not create, approve, reject, or mutate canonical decision-v2 facts.

## Canonical Application-v1 Mapping

Successful legacy apply creates or resolves the same canonical application-v1 identity used by the shared staged one-shot facade:

- business identity: `statutory-discount-payable-basis-application:{statutoryDiscountDecisionCommandId}`
- semantic source version: `statutory-discount-payable-basis-application:sha256:v1`
- source channel: server-derived `OPERATOR_CONSOLE`

The existing Operator Console payable-basis writer remains the authoritative calculation and durable payable-basis mutation path. The canonical application command is created or completed from the writer's durable applied result so this slice does not duplicate the statutory calculation.

## Idempotency, Replay, and Conflict

The legacy route derives a deterministic application-stage idempotency key from the caller's legacy idempotency key and canonical decision command ID:

- prefix: `operator-console-payable-basis-application-v1:sha256:`
- input boundary: legacy idempotency key plus `statutoryDiscountDecisionCommandId`
- excluded from semantic equality: request reference, correlation ID, transport headers, generated legacy identifiers, generated canonical identifiers, and timestamps

Replay behavior:

- exact legacy replay returns the existing applied payable-basis result
- a pre-existing canonical application for the same decision is returned without invoking the legacy writer again
- shared-route replay after legacy apply resolves the same application command through the canonical decision identity
- legacy replay after shared apply resolves the same application command when it is already present
- changed material application facts produce canonical semantic conflict and do not mutate the payable basis again

## Exactly-once Payable-basis Boundary

The existing writer still enforces the locked payable-basis mutation and finds existing applied rows by validation or parking session. The convergence layer:

1. resolves the approved canonical decision
2. checks for an existing canonical application by decision
3. invokes the existing durable payable-basis writer with the derived stage key when no canonical application exists
4. creates or resolves application-v1 from the writer's applied facts
5. marks application-v1 `PROCESSING`
6. marks application-v1 `APPLIED` only after the durable payable-basis mutation has succeeded

If the writer reports a non-accepted guardrail result, no application-v1 success is created. If a canonical application already exists, the writer is not called.

## Readback and Legacy Response Compatibility

Legacy apply responses preserve existing fields for validation ID, payable-basis application ID, application status, original and applied tariff snapshots, gross amount, VAT amount, VAT-exclusive amount, statutory discount amount, final payable amount, currency, reason/error codes, timestamps, and correlation ID.

The response adds safe canonical linkage:

- `statutoryDiscountDecisionCommandId`
- `statutoryDiscountPayableBasisApplicationCommandId`

Operator Console draft detail readback now includes the canonical application command ID where linked. Shared canonical readback resolves the same application state by the canonical decision command ID.

Historical unlinked legacy records remain readable through the legacy readback posture and are not silently converted.

## Transaction and Recovery Posture

This slice does not put the entire workflow in one long transaction. It preserves separate durable boundaries for access evaluation, legacy payable-basis mutation, canonical application command creation, processing state, and applied completion.

Recovery posture:

- failure before payable-basis mutation is safely retryable through the legacy route
- failure after mutation but before canonical application completion is reconciled on replay because the writer returns the durable existing application
- completed canonical applications survive legacy response-adaptation failure
- ordinary retry does not require manual database repair

## RBAC and Source Channel

Existing Operator Console access checks remain enforced for authenticated user, Site, device, shift, role, reviewer, and controlled action.

The route derives `OPERATOR_CONSOLE` server-side. A caller cannot use this legacy route to impersonate `WEBPAY` or `ASSISTED_PAYMENT_TERMINAL`, and this slice does not broaden shared facade permissions.

## Privacy and Authority Boundaries

The route does not accept, store, hash, log, or expose raw ID images, Base64 evidence, raw evidence payloads, full statutory ID values, unmasked identity data, or restricted evidence in ordinary readback.

Central PMS remains the statutory-decision and payable-basis authority. Vendor PMS/HikCentral remains the raw parking-session and live-tariff authority. POS Server remains a finalized-fact fiscal consumer. This slice does not mark payment final, issue ExitAuthorization, trigger fiscal issuance, call HikCentral, call a payment provider, or control a gate.

## Deferred Work

Still deferred:

- WebPay statutory-discount integration
- APT statutory-discount integration
- channel authorization readiness proof
- canonical database promotion where required by deployment ownership
- POS fiscal-linkage completion
- privacy retention policy finalization

WebPay integration remains not authorized by this slice. APT integration remains not authorized by this slice.
