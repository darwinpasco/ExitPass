# ExitPass Statutory Discount Service-Channel Decision Authority Design Decision

## 1. Purpose

This report resolves the service-channel decision-authority blocker for Central PMS statutory discounts. It defines how authenticated `WEBPAY` and `ASSISTED_PAYMENT_TERMINAL` callers may initiate a statutory-discount workflow and later request payable-basis application without supplying Operator Console-only approval, reviewer, device, or shift facts.

This is a design decision only. It does not implement runtime behavior, public DTO changes, SQL, tests, Bruno scenarios, WebPay, APT, POS Server, Operator Console runtime, Management Platform, statutory rules, VAT calculations, payment finality, fiscal issuance, ExitAuthorization, or gates.

## 2. Repository and Baseline Commit

| Item | Value |
| --- | --- |
| Repository | `D:\SourceCodes\ExitPass-Discounts` |
| Base branch | `dev` |
| Design branch | `docs/statutory-discount-service-channel-decision-authority-design` |
| Baseline commit | `0b383b4e64e918233e5fc6c6804dc4141aae592f` |
| Source inspected | Current merged repository source at the baseline commit |

## 3. Current Blocker

The merged shared route family exists, but service channels cannot request an applied discounted payable basis without violating current controls.

Current blocking behavior:

| Evidence | Current behavior |
| --- | --- |
| `src/Services/CentralPms/src/ExitPass.CentralPms.Api/Endpoints/StatutoryDiscountDecisionEndpoints.cs` / `ValidateChannelFieldMatrix()` | For `WEBPAY` and `ASSISTED_PAYMENT_TERMINAL`, `ApplyPayableBasis`, `Decision`, `DecisionReasonCode`, `ReviewerUserId`, `ReviewerAttestation`, `OperatorDeviceBindingId`, and `OperatorShiftId` are treated as prohibited operator-only fields. |
| `src/Services/CentralPms/src/ExitPass.CentralPms.Application/StatutoryDiscounts/StatutoryDiscountDecisionFacadeService.cs` / `NormalizeAndValidate()` | `ApplyPayableBasis=true` requires `Decision=APPROVE`; any supplied decision requires reviewer attestation. |
| `StatutoryDiscountDecisionFacadeService.ExecuteDecisionWorkflowAsync()` | The shared workflow invokes decision persistence only when `normalized.Decision` is supplied. |
| `src/Services/CentralPms/src/ExitPass.CentralPms.Application/OperatorConsole/OperatorConsoleStatutoryDiscountDraftService.cs` | Draft creation stores `ValidationStatus = REQUESTED`; it does not approve entitlement. |
| `src/Services/CentralPms/src/ExitPass.CentralPms.Application/OperatorConsole/OperatorConsoleStatutoryDiscountDecisionService.cs` | Approval/rejection is the implemented reviewer-controlled decision path. |

Therefore the current repository has no proven Central PMS-owned automated approval path for service channels. Allowing WebPay/APT to send approval fields would move legal decision authority to the channel. Automatically deriving approval without a review path would invent a new statutory approval mechanism. Both are out of bounds.

## 4. Authority Principles

- Central PMS remains the sole canonical statutory-discount decision and payable-basis authority.
- Operator Console remains the controlled entitlement-review workflow for approval/rejection.
- WebPay and APT submit safe facts and display canonical results only.
- Service channels do not approve their own entitlement and do not supply reviewer facts.
- Source channel is server-derived attribution, not business identity or approval authority.
- POS Server receives finalized discount facts only.
- Payment initiation consumes the effective applied tariff snapshot and does not recalculate the statutory discount.
- HikCentral remains authoritative for raw parking-session lifecycle and live tariff calculation.
- No component in this design marks payment final, issues fiscal documents, issues ExitAuthorization, controls gates, calls HikCentral, or calls payment providers.

## 5. Options Assessed

| Option | Assessment | Decision |
| --- | --- | --- |
| A. Automated Central PMS decision | Not supported by current source. The repository has deterministic policy resolution and fixed calculation behavior, but no implemented automated evidence validation, reviewer equivalence, confidence threshold, or legal approval service for WebPay/APT facts. | Rejected. |
| B. Review-mediated canonical decision | Fits current authority boundaries. Service channels can create a canonical decision-v2 request in a review-needed state; Operator Console can review and complete that same canonical command; service channels can later request application after approval. | Selected. |
| C. Operator-Console-only initiation | Preserves current controls but prevents WebPay/APT from initiating statutory-discount requests and forces out-of-band operator creation. It does not satisfy the channel-integration objective. | Rejected as the target model. |
| D. Automatic application after Operator approval | Avoids a later service-channel call, but couples entitlement review to payable-basis mutation and changes the staged Operator Console workflow. Approval and application must remain separate states. | Rejected. |

## 6. Selected Option

Selected design: **Option B - Review-mediated canonical decision**.

Conceptual flow:

1. WebPay/APT submits permitted entitlement, beneficiary, masked identity, evidence-reference, attestation, and trace facts.
2. Central PMS creates or resolves the canonical decision-v2 command using business identity `statutory-discount-decision:{parkingSessionId}:{entitlementType}`.
3. The decision result remains pending review, not approved.
4. Operator Console discovers the same canonical decision through a review queue or linked draft workflow.
5. Operator Console performs evidence review and records `APPROVED` or `REJECTED` on the same canonical decision.
6. WebPay/APT polls shared readback.
7. After approval, WebPay/APT submits application intent through the shared route without decision or reviewer fields.
8. Central PMS resolves the approved decision-v2 command and creates or resolves application-v1.
9. Rejected, pending, missing, or conflicting decisions cannot create application-v1.
10. Replay and restart recovery use canonical identifiers, idempotency, semantic hash, and original-key recovery posture.

## 7. Decision Rationale

Option B is the smallest model that satisfies all boundaries:

- It preserves the current `SENIOR_CITIZEN` and `PWD` calculation scope without inventing local ordinances, residency rules, driver/passenger rules, free periods, exemptions, stacking, or multiple-beneficiary allocation.
- It uses the existing canonical decision-v2 and application-v1 identity model from `StagedStatutoryDiscountCommandModels.cs`.
- It does not require WebPay/APT to fabricate reviewer identity, reviewer attestation, device binding, shift, approval, or rejection facts.
- It does not require automatic evidence validation or unsupported legal approval logic.
- It keeps approval and payable-basis mutation as distinct stages.
- It supports APT restart recovery with durable canonical IDs and statuses.
- It keeps WebPay user experience explicit: pending review is a real state, not a hidden processing state.

## 8. End-to-End State Flow

```text
Service channel request
  -> canonical decision-v2 RECEIVED
  -> canonical decision-v2 AWAITING_REVIEW / NOT_DECIDED
  -> Operator Console review
      -> APPROVED
          -> service channel application intent
          -> application-v1 RECEIVED
          -> application-v1 PROCESSING
          -> existing payable-basis mutation
          -> application-v1 APPLIED
          -> payment initiation consumes applied tariff snapshot
      -> REJECTED
          -> no application-v1
```

There must be no transaction spanning human review. Each stage persists durable state and can be replayed or read back independently.

## 9. Decision-v2 Lifecycle

Existing command states are `RECEIVED`, `PROCESSING`, `COMPLETED`, `FAILED_RETRYABLE`, and `FAILED_NON_RETRYABLE` in `StagedStatutoryDiscountCommandModels.cs`.

Existing result states are `APPROVED`, `REJECTED`, and `NOT_DECIDED`.

The design requires an explicit long-lived review state. Do not overload `PROCESSING`, because human review can last longer than a transient command execution and should not imply the caller should immediately retry.

Preferred vocabulary:

| Concept | Preferred representation | Reason |
| --- | --- | --- |
| Command accepted and awaiting human review | Add command state `AWAITING_REVIEW` or add review status `AWAITING_REVIEW` while command status is terminal for intake | Makes long-lived review explicit. |
| Decision result before review | Keep `NOT_DECIDED`; optionally add client-facing `PENDING_REVIEW` result/status | Avoids treating pending review as approval or failure. |
| Client recovery | `READ_CANONICAL_RESULT` / `WAIT_AND_REFRESH` or equivalent | The caller should poll/readback, not resubmit a different request. |
| Terminal approval | `COMPLETED` + `APPROVED` | Existing model fits. |
| Terminal rejection | `COMPLETED` + `REJECTED` | Existing model fits. |
| Retryable technical failure | `FAILED_RETRYABLE` | Existing model fits. |
| Non-retryable validation failure | `FAILED_NON_RETRYABLE` | Existing model fits. |

Recommended implementation detail: add an explicit review-status field rather than making `PROCESSING` long-lived. If schema or repository constraints make a separate field impractical, add `AWAITING_REVIEW` as a decision-v2 command state with corresponding check constraints and readback mapping. The implementation slice must choose one and update tests accordingly.

## 10. Application-v1 Lifecycle

Application-v1 remains unchanged conceptually:

| Stage | Behavior |
| --- | --- |
| Not requested | Decision may be pending, approved, rejected, or failed; no application command exists. |
| Request received | Service channel or shared route requests application for an approved decision. |
| Processing | Central PMS invokes the existing durable payable-basis mutation path. |
| Applied | Application-v1 becomes `APPLIED` only after the durable mutation succeeds. |
| Rejected as not approved | Pending or rejected decisions return `DECISION_NOT_APPROVED` or equivalent; no mutation occurs. |
| Missing decision | Returns `DECISION_NOT_FOUND` or equivalent; no mutation occurs. |
| Conflict | Changed material application facts return semantic conflict; existing payable basis is not altered. |

Application identity remains `statutory-discount-payable-basis-application:{statutoryDiscountDecisionCommandId}` with semantic source version `statutory-discount-payable-basis-application:sha256:v1`.

## 11. Canonical Identity and Semantic-Hash Posture

Preserve:

- Decision business identity: `statutory-discount-decision:{parkingSessionId}:{entitlementType}`
- Decision semantic source version: `statutory-discount-decision:sha256:v2`
- Application business identity: `statutory-discount-payable-basis-application:{statutoryDiscountDecisionCommandId}`
- Application semantic source version: `statutory-discount-payable-basis-application:sha256:v1`

Decision-v2 semantic boundary should remain valid for service-channel intake if service-channel review metadata is classified correctly:

| Fact | Classification |
| --- | --- |
| Parking session and entitlement | Semantic decision fact |
| Site and Site Group where known | Semantic decision fact |
| Ticket and plate references where submitted | Semantic decision fact |
| Beneficiary metadata and beneficiary count within fixed scope | Semantic decision fact |
| Masked statutory identity metadata | Semantic decision fact |
| Evidence references and verification outcomes | Semantic decision fact |
| Requester attestation facts | Semantic decision fact |
| Source channel | Audit-only attribution, not business identity |
| Request reference | Traceability-only |
| Correlation ID | Transport-only |
| Idempotency key | Transport/recovery, not semantic |
| Operator reviewer identity and reviewer attestation | Review-workflow-only; semantic when completing the decision through Operator Console, prohibited from service-channel intake |
| Operator device and shift | Review-workflow-only; prohibited from service-channel intake |
| `applyPayableBasis` | Application intent only, not decision-v2 semantic material |
| Review assignment, queue owner, SLA timer | Review-workflow-only |
| Expiration timestamp | Workflow/status fact; semantic only if it changes decision eligibility |

Do not include source channel in business identity. Source-channel changes must not create a second entitlement.

## 12. WebPay Request-Field Matrix

| Field | Classification | Source of value |
| --- | --- | --- |
| `parkingSessionId` | REQUIRED | WebPay submits Central PMS session reference. |
| `siteId` | OPTIONAL / SERVER_DERIVED where available | Prefer Central PMS session/site lookup. |
| `siteGroupId` | OPTIONAL / SERVER_DERIVED where available | Prefer Central PMS site context. |
| `ticketReference` | OPTIONAL | WebPay display/session context. |
| `plateNumber` | OPTIONAL | WebPay display/session context. |
| `entitlementType` | REQUIRED | `SENIOR_CITIZEN` or `PWD` only. |
| Beneficiary metadata | OPTIONAL within current DTO limits | Safe reference only; no raw identity. |
| `idDocumentType` | REQUIRED under current DTO | Submitted metadata; validated by Central PMS. |
| `issuingAuthority` | REQUIRED under current DTO | Submitted metadata; validated by Central PMS. |
| `expiryDate` | OPTIONAL | Submitted metadata. |
| `maskedIdReference` | REQUIRED | Masked or hashed only. |
| `evidenceReferences` | OPTIONAL / REQUIRED when policy requires evidence | References only; no raw payload. |
| Evidence verification outcome | OPTIONAL / SERVER_DERIVED after review | WebPay must not fabricate legal approval. |
| `actorUserId` | SERVER_DERIVED | Authenticated WebPay service identity. |
| `operatorDeviceBindingId` | PROHIBITED | Operator Console-only. |
| `operatorShiftId` | PROHIBITED | Operator Console-only. |
| `requesterAttestation` | REQUIRED | User/channel attestation fact, not approval. |
| `attestationNotes` | OPTIONAL | Safe notes only. |
| `reasonCode` | OPTIONAL | Safe request reason. |
| `decision` | PROHIBITED | Operator Console review outcome only. |
| `decisionReasonCode` | PROHIBITED | Operator Console review outcome only. |
| `reviewerUserId` | PROHIBITED | Operator Console review outcome only. |
| `reviewerAttestation` | PROHIBITED | Operator Console review outcome only. |
| `applyPayableBasis` on initial intake | Must be false or ignored until approval | Do not combine review creation with application. |
| `applyPayableBasis` after approval | OPTIONAL application intent | May request application only after approved canonical readback. |
| `originalTariffSnapshotId` | OPTIONAL | Used for basis integrity/staleness where known. |
| `requestReference` | TRACEABILITY_ONLY | Caller reference; not uniqueness. |
| `Idempotency-Key` | TRANSPORT_ONLY / RECOVERY | Required header. |
| `X-Correlation-Id` | TRANSPORT_ONLY | Required header. |
| `sourceChannel` | SERVER_DERIVED with compatibility check | Authenticated permission determines effective channel. |

## 13. APT Request-Field Matrix

| Field | Classification | Source of value |
| --- | --- | --- |
| `parkingSessionId` | REQUIRED | APT submits Central PMS session reference. |
| `siteId` | OPTIONAL / SERVER_DERIVED where available | Prefer terminal/site authentication context or session lookup. |
| `siteGroupId` | OPTIONAL / SERVER_DERIVED where available | Prefer Central PMS context. |
| `ticketReference` | OPTIONAL | Terminal/session scan context. |
| `plateNumber` | OPTIONAL | Terminal/session context. |
| `entitlementType` | REQUIRED | `SENIOR_CITIZEN` or `PWD` only. |
| Beneficiary metadata | OPTIONAL within current DTO limits | Safe reference only. |
| `idDocumentType` | REQUIRED under current DTO | Submitted metadata. |
| `issuingAuthority` | REQUIRED under current DTO | Submitted metadata. |
| `expiryDate` | OPTIONAL | Submitted metadata. |
| `maskedIdReference` | REQUIRED | Masked or hashed only. |
| `evidenceReferences` | OPTIONAL / REQUIRED when policy requires evidence | Safe references only. |
| Evidence verification outcome | OPTIONAL / SERVER_DERIVED after review | APT must not fabricate legal approval. |
| `actorUserId` | SERVER_DERIVED | Authenticated APT service or terminal identity. |
| Cashier reference | Deferred / traceability-only | Do not overload Operator Console reviewer or shift fields. |
| Terminal reference | Deferred / server-derived or traceability-only | Should come from authenticated terminal context if needed. |
| Terminal shift reference | Deferred / traceability-only | Do not reuse Operator Console shift authority. |
| `operatorDeviceBindingId` | PROHIBITED | Operator Console-only. |
| `operatorShiftId` | PROHIBITED | Operator Console-only. |
| `requesterAttestation` | REQUIRED | Customer/terminal attestation fact, not approval. |
| `attestationNotes` | OPTIONAL | Safe notes only. |
| `reasonCode` | OPTIONAL | Safe request reason. |
| `decision` | PROHIBITED | Operator Console review outcome only. |
| `decisionReasonCode` | PROHIBITED | Operator Console review outcome only. |
| `reviewerUserId` | PROHIBITED | Operator Console review outcome only. |
| `reviewerAttestation` | PROHIBITED | Operator Console review outcome only. |
| `applyPayableBasis` on initial intake | Must be false or ignored until approval | Do not combine review creation with application. |
| `applyPayableBasis` after approval | OPTIONAL application intent | May request application only after approved canonical readback. |
| `originalTariffSnapshotId` | OPTIONAL | Used for basis integrity/staleness where known. |
| `requestReference` | TRACEABILITY_ONLY | Safe workflow reference. |
| `Idempotency-Key` | TRANSPORT_ONLY / RECOVERY | Required header. |
| `X-Correlation-Id` | TRANSPORT_ONLY | Required header. |
| `sourceChannel` | SERVER_DERIVED with compatibility check | Authenticated permission determines effective channel. |

APT must not treat SQLite, terminal-local state, cashier input, or terminal shift state as statutory decision authority.

## 14. Operator Console Review Linkage

The next implementation must add a way for Operator Console to locate and complete service-channel-originated canonical decisions without creating a competing decision authority.

Required linkage:

| Linkage | Purpose |
| --- | --- |
| Canonical decision command to service-channel submission facts | Allows review of the exact submitted facts. |
| Canonical decision command to evidence references | Allows metadata-only evidence review without raw payloads. |
| Canonical decision command to Operator Console draft or review work item | Allows the existing controlled workflow to review and decide. |
| Operator Console validation record to canonical decision command | Allows legacy readback and apply convergence to find the same decision. |
| Policy-resolution reference to canonical decision command | Preserves deterministic decision facts. |
| Reviewer decision record to canonical decision command | Preserves reviewer audit and prevents separate authoritative state. |

Existing Operator Console decision convergence already maps the legacy route to decision-v2 through `OperatorConsoleStatutoryDiscountDecisionService`, but service-channel-originated requests need a review intake and linkage path so Operator Console can review the submitted canonical decision rather than create a separate draft with approximated facts.

## 15. Service-Channel Post-Approval Application Intent

Preferred behavior after approval:

- Retain `POST /v1/statutory-discounts/decisions`.
- WebPay/APT submits the same decision-stage facts plus `applyPayableBasis=true`.
- WebPay/APT still omits `decision`, `decisionReasonCode`, `reviewerUserId`, `reviewerAttestation`, `operatorDeviceBindingId`, and `operatorShiftId`.
- Central PMS resolves the existing canonical decision-v2 by business identity and semantic hash.
- If decision is `APPROVED`, Central PMS creates or resolves application-v1.
- If decision is `NOT_DECIDED` / pending review, return pending/non-application status.
- If decision is `REJECTED`, return non-approved status and do not create application.
- If facts conflict, return semantic conflict and do not mutate payable basis.
- If the application already exists, return the durable application result and do not mutate again.

This can be implemented without changing decision-v2 semantics because `applyPayableBasis` is not part of decision-v2 semantic equality in the staged model.

## 16. Shared Readback Contract

The shared POST response and GET readback must be channel-safe during every stage.

Required fields:

| Category | Required fields |
| --- | --- |
| Decision | `statutoryDiscountDecisionCommandId`, command status, decision result, review status, decision retryability, decision recovery classification/action, decision timestamp, safe error code |
| Application | `statutoryDiscountPayableBasisApplicationCommandId`, application requested, application command status, application result, application retryability, application recovery classification/action, application timestamp |
| Payable basis | original tariff snapshot, applied tariff snapshot, original amount, discount amount, final payable amount, currency, VAT treatment facts currently supported |
| Orchestration | overall classification, one-shot complete flag, correlation ID |
| Privacy | safe masked/reference fields only; no raw evidence, restricted evidence, full statutory IDs, or unmasked identity values |

Current response DTOs expose many of these fields through `StatutoryDiscountDecisionResponse`, including decision/application statuses, retryability, recovery fields, canonical IDs, amounts, tariff snapshots, and correlation ID. The implementation must add or map explicit pending-review/review-status semantics rather than forcing clients to infer that `NOT_DECIDED` plus `PROCESSING` means human review.

## 17. APT Restart and Cash-Readiness Posture

APT may persist only safe workflow references:

- `parkingSessionId`
- `entitlementType`
- `requestReference`
- original idempotency/recovery reference under the approved posture
- `statutoryDiscountDecisionCommandId`
- `statutoryDiscountPayableBasisApplicationCommandId`
- `originalTariffSnapshotId`
- `appliedTariffSnapshotId`
- decision status/result/retryability/recovery action
- application status/result/retryability/recovery action
- final payable amount
- currency

APT restart states:

| Readback state | APT posture |
| --- | --- |
| Awaiting review | Resume polling/readback; do not accept cash. |
| Approved, not applied | Submit application intent or resume original application request. |
| Rejected | Show non-approved result; do not accept discounted cash amount. |
| Application processing | Wait/retry according to recovery action; do not accept cash. |
| Application applied | Cash-ready if final payable amount, currency, and applied tariff snapshot are present. |
| Retryable failure | Retry original key or wait according to recovery action; do not accept cash. |
| Terminal failure | Do not retry automatically; do not accept discounted cash amount. |
| Semantic conflict | Do not retry with changed facts; operator/support resolution required. |

APT must not accept cash until the decision is approved, application is applied, applied tariff snapshot is authoritative, final payable amount and currency are known, and no retry/conflict remains.

## 18. WebPay Pending-Review Posture

WebPay should treat pending review as a first-class user experience:

- Initial submission can return a canonical decision command with pending-review status.
- WebPay can poll or refresh shared readback.
- Duplicate submission replays or reads the same canonical decision when semantic facts match.
- Abandoned sessions can resume with canonical references and idempotency/recovery posture.
- Rejection is a terminal non-approved result.
- Payment initiation must wait until application-v1 is `APPLIED`.
- WebPay must not calculate the discount locally and must not initiate payment against a stale original tariff snapshot.

Timeout and expiration behavior must be visible to WebPay through safe status and recovery fields when implemented.

## 19. Timeout, Expiration, and Tariff-Staleness Posture

The design needs operational expiry, but not legal retention rules.

Required future distinctions:

| Concern | Posture |
| --- | --- |
| Review deadline | Needed before UAT to avoid unbounded pending requests; operational, not legal retention. |
| Request expiration | Needed before UAT; should transition pending review to a terminal or recoverable status. |
| Evidence expiration | Needs policy decision; do not invent retention. |
| Tariff snapshot staleness | Must be checked before application; payment initiation already rejects stale applied/original mismatch through `CreateOrReusePaymentAttemptHandler`. |
| Revalidation before application | Required if original tariff or policy basis can become stale while awaiting review. |
| Abandoned request | Should be represented explicitly for WebPay/APT readback. |

Do not define evidence retention periods in this design.

## 20. RBAC

Current RBAC evidence:

- `CentralPmsRbacPolicyCatalog` grants `CentralPmsStatutoryDiscountDecisionSubmit` through `statutory-discounts.decision.submit.operator-console`, `statutory-discounts.decision.submit.webpay`, and `statutory-discounts.decision.submit.assisted-payment-terminal`.
- `CentralPmsStatutoryDiscountDecisionRead` includes `statutory-discounts.decision.read`.
- `StatutoryDiscountDecisionEndpoints.TryResolveAuthenticatedSourceChannel()` derives effective source channel from permissions.
- Existing API access tests cover source-channel mismatch and non-operator prohibited fields in `StatutoryDiscountDecisionApiAccessPolicyIntegrationTests`.

Required RBAC design:

| Action | Required authority |
| --- | --- |
| WebPay submit pending-review request | Authenticated WebPay service identity with submit permission. |
| APT submit pending-review request | Authenticated APT service identity with submit permission. |
| Shared readback | Authenticated read permission scoped to the caller/channel/session context. |
| Operator Console review queue/read | Authenticated Operator Console user with review permission and Site/device/shift context where required. |
| Operator Console approval/rejection | Existing Operator Console reviewer/supervisor controls. |
| Service-channel application intent | Authenticated service-channel identity with submit/application-intent permission or current submit permission if kept unified. |

Ambiguous multiple-channel permission sets must be rejected or resolved deterministically. Request body `sourceChannel` remains a compatibility value checked against the authenticated channel; it must not grant authority.

## 21. Security and Privacy

Preserve:

- Evidence references only.
- Masked or hashed statutory identity metadata only.
- No raw ID images.
- No Base64 evidence.
- No raw evidence bytes.
- No full statutory ID values.
- No sensitive beneficiary data in errors.
- No restricted evidence in general readback.
- Safe reason/error codes.

The semantic hash helpers already reject unsafe masked identity/evidence forms in staged command models. Formal evidence retention remains unresolved and must not be invented here.

## 22. Database and Canonical-Promotion Impact

Existing persistence evidence:

- `infra/db/patches/ExitPass_StatutoryDiscountDecisionFacade_v1.3.sql` creates `discounts.statutory_discount_decision_commands` for v1 facade commands.
- `infra/db/patches/ExitPass_StatutoryDiscountStagedCanonicalCommands_v1.3.sql` extends decision commands with `business_identity`, supports v2 semantic source version, and creates `discounts.statutory_discount_payable_basis_application_commands`.
- `infra/db/patches/ExitPass_OperatorConsoleStatutoryDiscountDecisionConvergence_v1.3.sql` adds safe legacy fact persistence for canonical decision-v2 reconstruction.
- `infra/db/patches/ExitPass_AppLocal_Db_Patch_Retirement_Manifest_v1.0.md` states aligned/canonical validation starts from generated `exitpassdb_v1.2` SQL and distinguishes app-local patches from canonical promotion.

Smallest expected persistence changes for Option B:

| Expected change | Classification | Purpose |
| --- | --- | --- |
| Explicit pending-review or review-status field for decision-v2 | Additive pending-review status | Avoid overloading `PROCESSING` for human review. |
| Service-channel submission metadata table or columns | Additive service-channel submission metadata | Persist safe submitted facts and review discoverability without raw evidence. |
| Canonical decision to Operator Console review linkage | Additive review linkage | Let Operator Console review the same canonical decision. |
| Review queue/readback indexes | Additive readback linkage | Support operator discovery and channel polling. |
| Canonical promotion | Separate prerequisite | Needed before controlled UAT/production promotion; should remain a separate database-release task. |

No destructive migration or backfill is required by the design. Existing historical records remain historical unless a future implementation provides safe readback linkage or controlled backfill.

## 23. Transaction and Recovery Boundaries

Recommended durable boundaries:

1. Service-channel submission creates or resolves canonical decision-v2 and persists pending-review metadata atomically.
2. Operator Console review reads the canonical decision and persists review linkage before approving/rejecting.
3. Approval/rejection completes decision-v2 in a durable transaction separate from submission.
4. Service-channel readback reads canonical decision and review status without mutating state.
5. Application intent creates or resolves application-v1 only after approved decision readback.
6. Payable-basis mutation remains its own durable boundary using the existing Central PMS mutation path.
7. Application-v1 is marked `APPLIED` only after durable payable-basis mutation succeeds.

Recovery behavior:

| Failure point | Recovery |
| --- | --- |
| Before service-channel command creation | Caller retries with same idempotency key. |
| After command creation before pending-review linkage | Replay resumes linkage creation or returns retryable failure with original-key recovery. |
| During human review | Canonical readback returns pending/review status; no retry loop as processing. |
| After approval before service-channel readback | Readback returns completed decision. |
| After approval before application intent | Service channel can submit application intent later. |
| After application command creation before mutation | Original-key recovery resumes application. |
| After mutation before application completion | Reconciliation must mark application applied without mutating again. |
| Concurrent service-channel submissions | Business identity and semantic hash produce one canonical decision or deterministic conflict. |
| Concurrent application intents | Application business identity produces one application or deterministic conflict. |

## 24. Authority Boundaries

The selected design preserves:

- WebPay/APT submit facts but do not decide entitlement.
- Operator Console reviews and validates entitlement.
- Central PMS owns canonical decision persistence.
- Central PMS owns payable-basis mutation.
- POS Server consumes finalized facts only.
- Payment initiation consumes effective applied tariff snapshots.
- No channel calculates the discount.
- No channel controls payment finality, fiscal issuance, ExitAuthorization, or gates.

## 25. Recommended Implementation Slices

1. **Service-channel pending-review canonical decision intake**
   - Add explicit pending-review/review-status model and persistence.
   - Allow WebPay/APT decision-only intake without approval/reviewer facts.
   - Return pending-review readback.

2. **Operator Console review linkage for service-channel decisions**
   - Add review queue/readback for service-channel-originated canonical decisions.
   - Complete the same decision-v2 as approved or rejected.

3. **Service-channel post-approval application intent**
   - Allow WebPay/APT `applyPayableBasis=true` only after the canonical decision is approved.
   - Create/resolve application-v1 without reviewer fields.

4. **Channel-safe readback hardening**
   - Ensure shared and channel-adjacent readback expose review status, application status, retryability, recovery, applied snapshot, final payable amount, and currency.

5. **Readiness re-authorization audit**
   - Reassess WebPay and APT independently after the backend model is merged.

Keep canonical database promotion as a separate database-release task unless a slice adds required SQL that must be validated immediately.

## 26. Exact Next Bounded Implementation Task

Task name: **Central PMS statutory-discount service-channel pending-review canonical decision intake**

Scope:

- Retain `POST /v1/statutory-discounts/decisions` and `GET /v1/statutory-discounts/decisions/{statutoryDiscountDecisionCommandId}`.
- Allow authenticated `WEBPAY` and `ASSISTED_PAYMENT_TERMINAL` callers to submit decision-only requests without `decision`, reviewer, device, or shift fields.
- Persist or expose explicit pending-review/review-needed state for decision-v2.
- Preserve canonical decision identity and decision-v2 semantic source version.
- Prevent `applyPayableBasis=true` for service channels until the follow-up post-approval application-intent slice.
- Do not implement Operator Console review linkage in the same slice unless the intake cannot be validated without a minimal readback-only linkage.

Completion criteria:

- Service-channel initial submission produces one canonical decision-v2 request in pending review.
- Exact replay returns the same pending decision.
- Changed material facts conflict.
- Source channel remains server-derived and non-authoritative.
- Operator Console-only fields remain prohibited.
- No application-v1 is created.
- No payable-basis mutation occurs.
- Shared readback exposes pending-review status safely.

## 27. Repository, Persona, Base Branch, and Proposed Feature Branch

| Item | Value |
| --- | --- |
| Persona | Codex I |
| Repository | `D:\SourceCodes\ExitPass-Discounts` |
| Base branch | `dev` |
| Proposed feature branch | `feature/central-pms-statutory-discount-service-channel-pending-review-intake` |

Expected file areas:

- `src/Services/CentralPms/src/ExitPass.CentralPms.Application/StatutoryDiscounts`
- `src/Services/CentralPms/src/ExitPass.CentralPms.Api/Endpoints/StatutoryDiscountDecisionEndpoints.cs`
- `src/Services/CentralPms/src/ExitPass.CentralPms.Contracts/StatutoryDiscounts`
- `src/Services/CentralPms/src/ExitPass.CentralPms.Infrastructure/Persistence`
- `infra/db/patches` only if explicit pending-review persistence requires SQL
- focused Central PMS unit/integration tests
- existing statutory-discount Bruno collection only if public behavior changes need scenario proof

## 28. Off-Limits Repositories and Behavior

Off-limits:

- `D:\SourceCodes\ExitPass`
- `D:\SourceCodes\ExitPass-APT`
- `D:\SourceCodes\ExitPass-AssistedPaymentTerminal`
- POS Server repositories
- Separate WebPay repositories
- WebPay UI/runtime integration
- APT desktop/runtime integration
- Operator Console UI
- Management Platform
- statutory rule expansion
- VAT calculation changes
- payment finality
- payment-provider behavior
- fiscal issuance authority
- ExitAuthorization
- gate behavior
- raw evidence or full statutory identity storage

## 29. Validation Requirements

For this design branch:

- cited-path and symbol verification
- Central PMS API build
- focused staged-command tests
- shared facade tests
- Operator Console decision/apply tests
- RBAC tests
- `git diff --check`
- Markdown trailing-whitespace check
- complete file inventory verification

For the next implementation slice:

- service-channel pending-review API tests
- RBAC/anti-impersonation tests for WebPay and APT
- semantic hash and idempotency tests
- repository/PostgreSQL validation if SQL changes
- readback contract tests
- existing staged-command, shared facade, Operator Console decision/apply, payable-basis, payment-initiation, POS fiscal hash, and WebPay-adjacent regression tests

## 30. Known Limitations

- The selected design is not implemented yet.
- WebPay and APT remain unauthorized for source integration until a follow-up readiness audit verifies the implemented backend contract.
- No automated Central PMS legal approval/evidence-validation mechanism exists in the current repository.
- Operator Console review linkage for service-channel-originated decisions is not implemented.
- Service-channel post-approval application intent is not implemented.
- Review timeout, request expiration, tariff revalidation timing, and abandoned-request classification require implementation decisions.
- Privacy retention remains unresolved; this design does not define retention periods.
- Canonical database promotion remains a separate release-readiness concern.

## 31. Authorization Status

This report authorizes a bounded backend implementation task only: service-channel pending-review canonical decision intake.

It does not authorize WebPay or APT integration.

WebPay integration: not authorized yet

APT integration: not authorized yet

## 32. Evidence Appendix

### Source Evidence

- `src/Services/CentralPms/src/ExitPass.CentralPms.Api/Endpoints/StatutoryDiscountDecisionEndpoints.cs`
  - `MapStatutoryDiscountDecisionEndpoints()`
  - `TryResolveAuthenticatedSourceChannel()`
  - `ValidateChannelFieldMatrix()`
  - `CENTRAL_PMS_SOURCE_CHANNEL_MISMATCH`
  - `STATUTORY_DISCOUNT_CHANNEL_FIELD_PROHIBITED`
- `src/Services/CentralPms/src/ExitPass.CentralPms.Application/StatutoryDiscounts/StatutoryDiscountDecisionFacadeService.cs`
  - `SubmitAsync()`
  - `GetAsync()`
  - `ExecuteDecisionWorkflowAsync()`
  - `CreateOrResolveApplicationStageAsync()`
  - `ResolveApplicationStageAsync()`
  - `NormalizeAndValidate()`
  - `APPROVAL_REQUIRED_FOR_PAYABLE_BASIS`
  - `REVIEWER_ATTESTATION_REQUIRED`
- `src/Services/CentralPms/src/ExitPass.CentralPms.Application/StatutoryDiscounts/StagedStatutoryDiscountCommandModels.cs`
  - `StatutoryDiscountDecisionV2CommandStates`
  - `StatutoryDiscountDecisionV2ResultStates`
  - `StatutoryDiscountPayableBasisApplicationV1CommandStates`
  - `StatutoryDiscountPayableBasisApplicationV1ResultClassifications`
  - `StatutoryDiscountDecisionV2SemanticHash`
  - `StatutoryDiscountPayableBasisApplicationV1SemanticHash`
- `src/Services/CentralPms/src/ExitPass.CentralPms.Application/StatutoryDiscounts/StatutoryDiscountDecisionFacadeModels.cs`
  - `StatutoryDiscountSourceChannels`
  - `StatutoryDiscountDecisionCommandStatuses`
  - `StatutoryDiscountOneShotResultClassifications`
  - `StatutoryDiscountApplicationStageStatuses`
  - `StatutoryDiscountDecisionClientResultStatuses`
  - `StatutoryDiscountDecisionRecoveryClassifications`
  - `StatutoryDiscountDecisionRecoveryActions`
- `src/Services/CentralPms/src/ExitPass.CentralPms.Application/OperatorConsole/OperatorConsoleStatutoryDiscountDraftService.cs`
  - `DraftAsync()`
  - `ValidationStatus = REQUESTED`
- `src/Services/CentralPms/src/ExitPass.CentralPms.Application/OperatorConsole/OperatorConsoleStatutoryDiscountDecisionService.cs`
  - `DecideAsync()`
  - `CanonicalDecisionAlreadyHandled`
  - `PrecheckDecisionability()`
  - `CompleteCanonicalDecisionAsync()`
  - `REQUESTER_CANNOT_APPROVE_OWN_DISCOUNT`
  - `EVIDENCE_REQUIRED_NOT_CAPTURED`
- `src/Services/CentralPms/src/ExitPass.CentralPms.Application/OperatorConsole/OperatorConsoleStatutoryDiscountApplyPayableBasisService.cs`
  - approval prerequisite and payable-basis boundary
- `src/Services/CentralPms/src/ExitPass.CentralPms.Application/Security/CentralPmsRbacPolicyCatalog.cs`
  - `CentralPmsStatutoryDiscountDecisionSubmit`
  - `CentralPmsStatutoryDiscountDecisionRead`
- `src/Services/CentralPms/src/ExitPass.CentralPms.Application/PaymentAttempts/CreateOrReusePaymentAttemptHandler.cs`
  - effective applied tariff snapshot consumption and stale snapshot rejection
- `src/Services/CentralPms/src/ExitPass.CentralPms.Infrastructure/PaymentAttempts/TariffSnapshotReadRepository.cs`
  - `GetEffectiveAppliedTariffSnapshotAsync()`
- `src/Services/CentralPms/src/ExitPass.CentralPms.Contracts/StatutoryDiscounts/StatutoryDiscountDecisionDtos.cs`
  - `StatutoryDiscountDecisionRequest`
  - `StatutoryDiscountDecisionResponse`
- `src/Services/CentralPms/src/ExitPass.CentralPms.Contracts/Public/VendorParking/ResolveVendorParkingResponse.cs`
  - WebPay-adjacent payable-basis readback fields

### SQL Evidence

- `infra/db/patches/ExitPass_StatutoryDiscountDecisionFacade_v1.3.sql`
- `infra/db/patches/ExitPass_StatutoryDiscountStagedCanonicalCommands_v1.3.sql`
- `infra/db/patches/ExitPass_OperatorConsoleStatutoryDiscountDecisionConvergence_v1.3.sql`
- `infra/db/patches/validation/Validate_StatutoryDiscountDecisionFacade_v1.3.sql`
- `infra/db/patches/validation/Validate_StatutoryDiscountStagedCanonicalCommands_v1.3.sql`
- `infra/db/patches/validation/Validate_OperatorConsoleStatutoryDiscountDecisionConvergence_v1.3.sql`
- `infra/db/patches/ExitPass_AppLocal_Db_Patch_Retirement_Manifest_v1.0.md`

### Test and Bruno Evidence

- `src/Services/CentralPms/tests/ExitPass.CentralPms.UnitTests/Application/StatutoryDiscountDecisionFacadeServiceTests.cs`
- `src/Services/CentralPms/tests/ExitPass.CentralPms.UnitTests/Application/StatutoryDiscountStagedCommandServiceTests.cs`
- `src/Services/CentralPms/tests/ExitPass.CentralPms.IntegrationTests/Api/StatutoryDiscountDecisionApiAccessPolicyIntegrationTests.cs`
- `src/Services/CentralPms/tests/ExitPass.CentralPms.IntegrationTests/Api/OperatorConsoleStatutoryDiscountDecisionApiIntegrationTests.cs`
- `src/Services/CentralPms/tests/ExitPass.CentralPms.IntegrationTests/Api/OperatorConsoleStatutoryDiscountApplyPayableBasisApiIntegrationTests.cs`
- `src/Services/CentralPms/tests/ExitPass.CentralPms.IntegrationTests/Api/VendorParkingResolutionApiIntegrationTests.cs`
- `src/Services/CentralPms/tests/ExitPass.CentralPms.IntegrationTests/Persistence/StatutoryDiscountStagedCommandRepositoryTests.cs`
- `bruno/operator-console-statutory-discount-draft/139-shared-statutory-decision-submit.bru`
- `bruno/operator-console-statutory-discount-draft/140-shared-statutory-decision-replay.bru`
- `bruno/operator-console-statutory-discount-draft/141-shared-statutory-decision-semantic-conflict.bru`
- `bruno/operator-console-statutory-discount-draft/142-shared-statutory-decision-readback.bru`
- `bruno/operator-console-statutory-discount-draft/143-shared-statutory-decision-unsafe-id-rejected.bru`
- `bruno/operator-console-statutory-discount-draft/144-shared-statutory-decision-apply-later.bru`
- `bruno/operator-console-statutory-discount-draft/145-shared-statutory-decision-application-replay.bru`
- `bruno/operator-console-statutory-discount-draft/146-shared-statutory-decision-readback-with-application.bru`
- `bruno/operator-console-statutory-discount-draft/147-shared-statutory-decision-source-channel-mismatch-rejected.bru`
- `bruno/operator-console-statutory-discount-draft/148-legacy-decision-canonical-readback.bru`
- `bruno/operator-console-statutory-discount-draft/149-legacy-apply-canonical-readback.bru`
