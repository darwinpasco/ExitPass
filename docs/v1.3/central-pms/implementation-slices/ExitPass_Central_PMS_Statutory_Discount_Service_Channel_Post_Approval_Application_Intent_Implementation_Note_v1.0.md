# ExitPass Central PMS Statutory Discount Service Channel Post Approval Application Intent Implementation Note

## Purpose

This slice enables the shared Central PMS statutory-discount route to treat `applyPayableBasis=true` from authenticated `WEBPAY` and `ASSISTED_PAYMENT_TERMINAL` service identities as post-approval application intent, not as legal approval. Service channels still cannot approve or reject an entitlement, submit reviewer facts, submit Operator Console device or shift facts, or calculate payable-basis amounts.

## Governing Model

The implementation follows the review-mediated authority model from `docs/v1.3/central-pms/reviews/ExitPass_Statutory_Discount_Service_Channel_Decision_Authority_Design_Decision_v1.0.md`.

The retained public routes are unchanged:

- `POST /v1/statutory-discounts/decisions`
- `GET /v1/statutory-discounts/decisions/{statutoryDiscountDecisionCommandId}`

No WebPay client, APT desktop, Operator Console UI, POS Server, payment-finality, fiscal-issuance, ExitAuthorization, gate, statutory-calculation, or VAT behavior was changed.

## Approved Decision Prerequisite

For service-channel requests where `applyPayableBasis=true` and `Decision` is omitted, the facade now resolves the existing canonical decision-v2 by business identity before any application work. It does not create a new decision in this path.

Decision handling is:

- Missing decision: returns `STATUTORY_DISCOUNT_DECISION_NOT_FOUND` and creates no decision or application.
- `AWAITING_REVIEW` / `NOT_DECIDED`: returns the durable awaiting-review decision, creates no application, and does not mutate payable basis.
- `COMPLETED` / `APPROVED`: creates or resolves canonical application-v1 and invokes the existing payable-basis apply path only after application processing begins.
- `COMPLETED` / `REJECTED`: returns `STATUTORY_DISCOUNT_DECISION_NOT_APPROVED` and creates no application.
- Semantic mismatch: returns deterministic canonical semantic conflict and does not mutate the existing decision or payable basis.

The service channel does not approve itself. Operator Console review remains the only implemented approval and rejection workflow for service-channel-originated pending decisions.

## Field Matrix

For WebPay and APT post-approval application intent, the following request fields remain permitted where already supported:

- `parkingSessionId`
- `siteId`
- `siteGroupId`
- `ticketReference`
- `plateNumber`
- `entitlementType`
- `idDocumentType`
- `issuingAuthority`
- `expiryDate`
- `maskedIdReference`
- `evidenceCaptureRequested`
- `evidenceReferences`
- `requesterAttestation`
- `attestationNotes`
- `reasonCode`
- `applyPayableBasis=true`
- `originalTariffSnapshotId`
- `requestReference`
- `sourceChannel` compatibility value
- `Idempotency-Key`
- `X-Correlation-Id`

The following remain prohibited for service channels:

- `Decision`
- `DecisionReasonCode`
- `ReviewerUserId`
- `ReviewerAttestation`
- `OperatorDeviceBindingId`
- `OperatorShiftId`
- raw evidence
- Base64 evidence
- full statutory ID values
- caller-supplied approval result
- caller-supplied payable-basis amount
- caller-supplied applied tariff snapshot
- caller-supplied application status

`sourceChannel` remains server-derived from the authenticated permission set. Request-supplied source channel is verified against the authenticated channel and is not trusted as authority.

## Canonical Identities

The decision identity is unchanged:

`statutory-discount-decision:{parkingSessionId}:{entitlementType}`

The decision semantic source remains:

`statutory-discount-decision:sha256:v2`

The application identity is unchanged:

`statutory-discount-payable-basis-application:{statutoryDiscountDecisionCommandId}`

The application semantic source remains:

`statutory-discount-payable-basis-application:sha256:v1`

Source channel, request reference, correlation ID, transport idempotency key, generated identifiers, and generated timestamps remain outside business identity.

## Application Workflow

The shared facade now separates service-channel pending-review intake from service-channel post-approval application intent:

1. Normalize and validate the request.
2. Detect service-channel `applyPayableBasis=true` with omitted decision facts.
3. Resolve the existing canonical decision-v2 by business identity.
4. Compare the stored semantic source version and hash against the submitted decision facts.
5. Require `COMPLETED` / `APPROVED` before application.
6. Create or resolve canonical application-v1.
7. Mark application `PROCESSING`.
8. Invoke the existing Operator Console payable-basis apply service as the current authoritative mutation path.
9. Mark application `APPLIED` only after the durable payable-basis mutation succeeds.
10. Return the shared one-shot response from the durable canonical state.

The repository now exposes a read-only decision lookup by business identity so application intent can reject missing decisions without creating a new pending decision.

## Idempotency and Replay

The existing staged one-shot derived key model is preserved:

- decision stage: derived from original `Idempotency-Key`, `decision-v2`, and `parkingSessionId`
- application stage: derived from original `Idempotency-Key`, `payable-basis-application-v1`, and `statutoryDiscountDecisionCommandId`

Exact replay returns durable canonical state. Equivalent cross-channel application intent converges on the same decision and application business identities. Different request references and correlation IDs do not conflict. Changed material application facts conflict deterministically. Cross-key observers cannot create another application because the application business identity is unique per canonical decision.

## Exactly-Once Boundary

The slice preserves:

- one application-v1 command per approved canonical decision
- one payable-basis writer invocation for an application command that reaches mutation
- replay without reapplying the payable basis
- semantic conflict without mutating the applied basis
- response-adaptation failure without losing the completed canonical application

Operator Console review completion now creates the approved statutory validation row from the reviewed service-channel facts and links it to both the canonical decision-v2 command and the service-channel review record. The existing payable-basis apply service therefore receives the durable `statutoryDiscountValidationId` and approved payable-basis facts through the real review-mediated flow. When historical or malformed records still lack validation linkage or payable-basis facts, the facade fails safely before mutation and creates no application side effect.

## Shared Response and GET Readback

For approved application intent, the shared POST and GET routes return the canonical decision and application fields already available in the shared response model, including:

- `statutoryDiscountDecisionCommandId`
- `statutoryDiscountPayableBasisApplicationCommandId`
- `decisionCommandStatus`
- `decisionResultStatus`
- `applicationRequested`
- `applicationCommandStatus`
- `applicationResultClassification`
- `originalTariffSnapshotId`
- `appliedTariffSnapshotId`
- gross, VAT, discount, final payable, and currency fields when available
- decision and application timestamps when available
- retryability
- recovery classification
- recovery action
- `overallResultClassification`
- `oneShotComplete`

The later channel-safe readback-hardening slice remains responsible for completing any vendor-parking or channel-adjacent durable readback surface needed by WebPay and APT.

## Payment Initiation

Payment initiation remains unchanged. It continues to consume the effective applied tariff snapshot and does not recalculate statutory discounts or VAT privilege in the payment stage.

## RBAC and Anti-Impersonation

Existing service-channel submit and read permissions remain in force:

- `statutory-discounts.decision.submit.webpay`
- `statutory-discounts.decision.submit.assisted-payment-terminal`
- `statutory-discounts.decision.read`

The endpoint continues to reject:

- source-channel mismatch
- ambiguous multi-channel submit permissions
- unauthenticated submit
- unauthorized submit
- service-channel reviewer fields
- service-channel manual decision fields
- service-channel Operator Console device and shift fields

Service identities do not receive Operator Console review permission.

## Privacy

The slice does not accept, persist, hash, log, or expose raw statutory ID images, Base64 evidence, raw evidence bytes, full statutory ID values, unmasked identity values, restricted evidence, or sensitive beneficiary values in errors. Evidence remains reference-only.

No retention period, deletion schedule, or archive policy is introduced.

## Database Posture

The slice adds one focused app-local SQL patch:

- `infra/db/patches/ExitPass_StatutoryDiscountServiceChannelPostApprovalApplicationIntent_v1.3.sql`
- `infra/db/patches/validation/Validate_StatutoryDiscountServiceChannelPostApprovalApplicationIntent_v1.3.sql`

The patch adds nullable durable validation linkage to `operator_console.statutory_discount_service_channel_reviews`:

- `statutory_discount_validation_id`
- `fk_stat_disc_svc_reviews__validation`
- `ux_stat_disc_svc_reviews__validation`
- `ix_stat_disc_svc_reviews__decision_validation`

The slice reuses:

- canonical decision-v2 persistence
- canonical application-v1 persistence
- Operator Console review linkage
- existing payable-basis mutation and tariff snapshot structures

Corrected disposable SQL validation starts from the canonical database repository at `D:\SourceCodes\exitpassdb_v1.2`, using `build/generated/exitpass-full-object.generated.sql` plus the active app-local statutory predecessor patches that are not yet in canonical object source. The corrected disposable validation also applies and reapplies this branch patch, runs the branch validation SQL, and proves the real WebPay/APT review-mediated post-approval application workflows against the rebuilt database.

## Transaction and Recovery Boundaries

The workflow preserves separate durable boundaries:

1. request validation
2. canonical decision resolution
3. application create or resolve
4. application `PROCESSING`
5. payable-basis mutation
6. mutation reconciliation
7. application `APPLIED`
8. response adaptation
9. shared readback

No transaction spans the full one-shot operation or human review time.

## Deferred Behavior

This slice deliberately defers:

- channel-safe readback hardening
- WebPay client integration
- APT desktop integration
- live Bruno execution in an authenticated controlled-UAT environment
- channel authorization review
- privacy-retention policy
- POS Server contract changes

WebPay and APT integration remain not authorized by this implementation task.
