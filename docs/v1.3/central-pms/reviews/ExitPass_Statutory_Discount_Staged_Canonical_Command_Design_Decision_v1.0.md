# ExitPass Statutory Discount Staged Canonical Command Design Decision v1.0

## 1. Purpose

This report resolves the design decision required before converging the Central PMS Operator Console statutory-discount workflow onto the shared statutory-discount facade.

This is a documentation and design-verification artifact only. It does not modify runtime code, API DTOs, SQL, tests, Bruno files, Operator Console UI, WebPay, APT, POS Server, Management Platform, statutory rules, VAT treatment, payment finality, ExitAuthorization, fiscal issuance, or gate behavior.

## 2. Baseline Commit

- Repository: `D:\SourceCodes\ExitPass-Discounts`
- Branch: `docs/central-pms-statutory-discount-staged-canonical-command-design`
- Base branch: `dev`
- `origin/dev` commit incorporated: `e79ae984140356ae8cd3508c1a55ded6f6b54166`
- Origin: `https://github.com/darwinpasco/ExitPass.git`

Baseline material verified directly:

- `docs/v1.3/operator-console/reviews/ExitPass_Statutory_Discount_System_Wide_Baseline_Audit_v1.0.md`
- `docs/v1.3/central-pms/reviews/ExitPass_Statutory_Discount_Integration_Readiness_and_Thread_Handoff_v1.0.md`
- `docs/v1.3/central-pms/implementation-slices/ExitPass_Central_PMS_Shared_Statutory_Discount_Decision_Facade_Implementation_Note_v1.0.md`
- `docs/v1.3/central-pms/implementation-slices/ExitPass_Central_PMS_Statutory_Discount_Channel_Contract_Readiness_Implementation_Note_v1.0.md`
- Shared facade source, contracts, repository, SQL patch, Operator Console legacy route family, payable-basis readback, fiscal mapper, tests, and Bruno scenarios listed in the evidence appendix.

## 3. Current-State Inventory

Shared Central PMS statutory-discount route family:

- `POST /v1/statutory-discounts/decisions`
  - Endpoint: `StatutoryDiscountDecisionEndpoints.SubmitAsync`
  - Application service: `StatutoryDiscountDecisionFacadeService.SubmitAsync`
  - Contract: `StatutoryDiscountDecisionRequest`
  - Result: `StatutoryDiscountDecisionResponse`
- `GET /v1/statutory-discounts/decisions/{statutoryDiscountDecisionCommandId}`
  - Endpoint: `StatutoryDiscountDecisionEndpoints.ReadAsync`
  - Application service: `StatutoryDiscountDecisionFacadeService.GetAsync`

Current shared facade orchestration:

1. Build `StatutoryDiscountDecisionCommand`.
2. Compute `StatutoryDiscountDecisionSemanticHash`.
3. Begin or replay a row in `discounts.statutory_discount_decision_commands`.
4. Create an Operator Console validation draft through `IOperatorConsoleStatutoryDiscountDraftService`.
5. Capture metadata-only evidence through `IOperatorConsoleStatutoryDiscountEvidenceService`.
6. Persist the decision through `IOperatorConsoleStatutoryDiscountDecisionService` when a decision is supplied.
7. Apply payable basis through `IOperatorConsoleStatutoryDiscountApplyPayableBasisService` when `ApplyPayableBasis` is true and the decision is approved.
8. Complete the shared command record and expose durable readback.

Current shared decision identity:

```text
statutory-discount-decision:{parkingSessionId}:{entitlementType}
```

Current shared semantic hash version:

```text
statutory-discount-decision:sha256:v1
```

Current shared semantic inputs include parking session, Site/Site Group, ticket/plate, entitlement type, ID document type, issuing authority, expiry date, masked ID reference, evidence metadata, actor/reviewer/device/shift facts, attestation facts, decision facts, `applyPayableBasis`, and `originalTariffSnapshotId`.

Legacy Operator Console route family:

- Preparatory workflow:
  - `GET /v1/ops/operator-console/statutory-discounts/drafts`
  - `GET /v1/ops/operator-console/statutory-discounts/drafts/{draftId}`
  - `POST /v1/ops/operator-console/statutory-discounts/draft`
  - `POST /v1/ops/operator-console/statutory-discounts/{draftId}/evidence`
  - `GET /v1/ops/operator-console/statutory-discounts/{draftId}/evidence`
  - `POST /v1/ops/operator-console/statutory-discounts/resolve-policy`
  - `GET /v1/ops/operator-console/audit/statutory-discounts`
- Authoritative mutation workflow:
  - `POST /v1/ops/operator-console/statutory-discounts/{draftId}/decision`
  - `POST /v1/ops/operator-console/statutory-discounts/{validationId}/apply-payable-basis`

Current Operator Console staged behavior:

1. Draft route creates a privacy-minimized `discounts.statutory_discount_validations` row.
2. Evidence route stores metadata-only `discounts.discount_evidence_references` rows.
3. Decision route transitions validation status to approved or rejected.
4. Apply-payable-basis route creates and applies `discounts.statutory_discount_payable_basis_applications`, superseding the original tariff snapshot with an applied tariff snapshot.

Current payment initiation consumes the effective applied tariff snapshot through `TariffSnapshotReadRepository.GetEffectiveAppliedTariffSnapshotAsync`; it does not recalculate statutory discounts.

Current POS fiscal mapping carries finalized discount facts through Central PMS-held fiscal DTOs and `PosServerFiscalDocumentRequestMapper`; POS Server remains a finalized-fact consumer.

## 4. Exact Blocker

The current shared facade is one-shot, but Operator Console is staged. A direct adapter is unsafe because:

- `applyPayableBasis` is a material input in `StatutoryDiscountDecisionSemanticHash.Compute`.
- Operator Console approval and payable-basis application are separate HTTP calls.
- Mapping approval to a shared command with `ApplyPayableBasis=false`, then applying later with `ApplyPayableBasis=true`, changes the semantic hash for the same business identity and correctly conflicts under the current model.
- Applying payable basis during Operator Console approval would change UI-visible behavior and collapse the current staged review/apply workflow.
- Invoking the one-shot facade only from the legacy apply route leaves rejection decisions outside the canonical shared path.
- Existing legacy draft persistence does not durably store all shared semantic inputs from the original draft command, including `IdDocumentType`, `IssuingAuthority`, `MaskedIdReference`, attestation notes, and entitlement fingerprint.
- Existing evidence list readback does not return every shared evidence semantic input, including masked reference number.

The design must therefore define a canonical staged model rather than forcing the legacy staged workflow into the current one-shot semantic boundary.

## 5. Design Options

Option A: Keep the current single canonical command and add enough legacy persistence to reconstruct the full one-shot request.

Option B: Change the existing semantic hash or semantic source version so `applyPayableBasis` is no longer material.

Option C: Introduce a canonical staged model with separate statutory-decision and payable-basis-application operations.

Option D: Define another narrowly scoped model if repository evidence disproves A through C.

## 6. Decision

Selected option: C.

Central PMS must use a canonical staged command model:

- Canonical statutory-decision command owns entitlement, beneficiary/claimant metadata, evidence references, attestation, reviewer, policy, tariff-basis context, and approval/rejection decision facts.
- Canonical payable-basis-application command references one approved canonical statutory decision and owns payable-basis application intent, tariff snapshot input, calculated/applied payable-basis output, application status, and exactly-once application evidence.
- The existing shared one-shot route remains as a backward-compatible orchestration facade over both canonical staged operations.
- Operator Console decision route maps to the canonical statutory-decision operation.
- Operator Console apply-payable-basis route maps to the canonical payable-basis-application operation.
- Future WebPay and APT one-shot workflows may call the shared route after their channel-specific eligibility/review contracts are approved; they must not calculate or approve statutory discounts locally.

Decision marker: `STATUTORY_DISCOUNT_STAGED_CANONICAL_DESIGN_APPROVED`

## 7. Canonical Operation Model

There must be two canonical commands.

Canonical statutory-decision command:

- Purpose: establish the authoritative statutory-discount decision for one parking session and entitlement type.
- Authoritative inputs: parking session, Site/Site Group context where known, entitlement type, beneficiary/claimant metadata permitted by privacy model, metadata-only evidence references and verification posture, request/attestation facts, reviewer facts where required, policy-resolution facts, decision facts, and safe reason codes.
- Server-derived inputs: effective source channel, actor identity, RBAC outcome, operator device/shift context where applicable, created timestamps, durable command ID, and correlation/audit metadata.
- Semantic inputs: business parking-session/entitlement facts, permitted beneficiary and ID metadata, evidence references/outcomes, attestation/reviewer facts, policy-selection inputs, decision and reason facts, and decision-stage tariff-basis reference when already required.
- Non-semantic transport inputs: idempotency key, request reference, correlation ID, generated IDs, timestamps, HTTP route, and response-only fields.
- Durable identifier: `statutoryDiscountDecisionCommandId`.
- Business identity: `statutory-discount-decision:{parkingSessionId}:{entitlementType}`.
- Idempotency scope: same as business identity.
- Command states: `PROCESSING`, `COMPLETED`, `FAILED`, `CANCELLED`, `EXPIRED`.
- Result states: `REQUESTED`, `PENDING_OPERATOR_REVIEW`, `APPROVED`, `REJECTED`, `NON_APPROVED`, `VALIDATION_FAILED`.
- Retryability: in-progress original-key retry is recoverable; completed replay reads the durable result; semantic conflicts are non-retryable until facts are corrected.
- Recovery rules: same idempotency key may resume a processing command; different key for the same processing business identity receives deterministic in-progress conflict.
- Terminal states: `APPROVED`, `REJECTED`, `FAILED`, `CANCELLED`, `EXPIRED`.
- Conflict rules: same business identity and different material decision facts conflict; source channel, request reference, correlation ID, generated IDs, and timestamps do not create another decision.

Canonical payable-basis-application command:

- Purpose: apply one approved canonical statutory decision to payable basis exactly once.
- Authoritative inputs: canonical decision command ID, statutory validation ID where persisted, original tariff snapshot ID, apply intent, actor/service identity, approved policy reference, approved decision status, and current payable-basis prerequisites.
- Server-derived inputs: effective source channel, RBAC outcome, application command ID, application timestamps, advisory-lock key, generated applied tariff snapshot ID, payable-basis application ID, and audit metadata.
- Semantic inputs: canonical decision command ID, statutory validation ID, parking session, entitlement type, original tariff snapshot ID, approved policy reference, approved discount amounts where available, and apply intent.
- Non-semantic transport inputs: idempotency key, request reference, correlation ID, generated IDs, timestamps, and response-only fields.
- Durable identifier: `statutoryDiscountPayableBasisApplicationCommandId`.
- Business identity: `statutory-discount-payable-basis-application:{statutoryDiscountDecisionCommandId}`.
- Idempotency scope: same as application business identity.
- Command states: `PROCESSING`, `COMPLETED`, `FAILED`, `CANCELLED`.
- Result states: `NOT_REQUESTED`, `REQUESTED`, `APPLIED`, `FAILED`, `REJECTED`.
- Retryability: in-progress original-key retry is recoverable; completed replay returns the durable application result.
- Recovery rules: replay must never call the lower-level payable-basis apply routine twice after an applied result is durable.
- Terminal states: `APPLIED`, `FAILED`, `CANCELLED`, `REJECTED`.
- Conflict rules: a second application for the same canonical decision conflicts unless it is an exact replay; a changed original tariff snapshot or changed decision reference conflicts.

## 8. Decision Command Identity and Semantics

The canonical decision identity remains:

```text
statutory-discount-decision:{parkingSessionId}:{entitlementType}
```

Entitlement type remains part of business identity. The current scope supports only one `SENIOR_CITIZEN` or `PWD` decision for a parking session. This report does not add multiple-beneficiary, group transaction, multiple entitlement, or stacking behavior.

Decision semantic hash:

- New staged source version required: `statutory-discount-decision:sha256:v2`.
- Reason: current `statutory-discount-decision:sha256:v1` includes `applyPayableBasis`; staged decision semantics must not silently change the meaning of v1.
- Inputs: parking session, Site/Site Group, ticket/plate references, entitlement type, permitted ID metadata, masked ID reference, evidence metadata/outcomes, actor/reviewer/device/shift facts where applicable, requester/reviewer attestations, decision, decision reason, policy-resolution inputs, and decision-stage tariff-basis reference where applicable.
- Exclusions: source channel, request reference, idempotency key, correlation ID, generated IDs, timestamps, response-only fields, raw evidence, raw image bytes, Base64 payloads, and full statutory ID numbers.

## 9. Payable-Basis Application Identity and Semantics

Recommended application identity:

```text
statutory-discount-payable-basis-application:{statutoryDiscountDecisionCommandId}
```

Rationale:

- The approved canonical decision is the authority that permits application.
- Existing payable-basis persistence already enforces one active application by validation and parking session through `discounts.statutory_discount_payable_basis_applications`.
- The application command needs its own idempotency/recovery boundary because Operator Console applies after approval, while one-shot shared route applies immediately after approval.

One approved decision may be applied only once. Exact replay returns the durable application result. A changed original tariff snapshot or changed canonical decision reference conflicts. A processing application command can be retried only with the original idempotency key. The application result links to:

- `statutoryDiscountDecisionCommandId`
- `statutoryDiscountPayableBasisApplicationCommandId`
- `statutoryDiscountValidationId`
- `statutoryDiscountPayableBasisApplicationId`
- `originalTariffSnapshotId`
- `appliedTariffSnapshotId`
- effective payable-basis readback fields

Application semantic hash:

- New source version: `statutory-discount-payable-basis-application:sha256:v1`.
- Inputs: canonical decision command ID, statutory validation ID, parking session, entitlement type, original tariff snapshot ID, approved policy reference, approved decision status, and apply intent.
- Exclusions: source channel, request reference, idempotency key, correlation ID, generated IDs, timestamps, raw evidence, and response-only values.

## 10. One-Shot Shared Facade Behavior

The existing shared route must remain:

- `POST /v1/statutory-discounts/decisions`
- `GET /v1/statutory-discounts/decisions/{statutoryDiscountDecisionCommandId}`

After implementation of this design:

- `POST` remains one-shot for callers that submit all required facts in one request.
- `applyPayableBasis` remains accepted for source channels that are authorized to request immediate application.
- `applyPayableBasis=false` supports decision completion without application.
- `applyPayableBasis=true` orchestrates the canonical decision command first, then the canonical payable-basis-application command.
- The response exposes decision status and application status separately.
- The response includes both decision and application identifiers when application is requested or completed.
- Replay after decision completion but before application completion resumes or reads the application command under its own idempotency boundary.
- Readback by decision command ID exposes decision state and the latest linked application command/application state when present.

The current `statutory-discount-decision:sha256:v1` command records must remain readable as existing one-shot historical records. Future staged one-shot orchestration must use new source versions and must not reinterpret existing v1 hashes.

## 11. Operator Console Staged-Route Mapping

Legacy routes remain available and backward-compatible.

`POST /v1/ops/operator-console/statutory-discounts/{draftId}/decision`:

- Enforces existing Operator Console authentication, Site, device, shift, role, reviewer, and access-evaluation checks.
- Server derives `OPERATOR_CONSOLE`; it must not trust a body source-channel value.
- Reads the existing draft/evidence/policy context.
- Creates or replays a canonical statutory-decision command.
- Links the legacy `statutory_discount_validation_id` to the canonical `statutoryDiscountDecisionCommandId`.
- Adapts the canonical result into the existing `OperatorConsoleStatutoryDiscountDecisionResponse`.
- Rejection is terminal at the canonical decision layer and has no application command.
- Approval is terminal at the canonical decision layer but does not apply payable basis.

`POST /v1/ops/operator-console/statutory-discounts/{validationId}/apply-payable-basis`:

- Enforces existing Operator Console apply-payable-basis RBAC and context checks.
- Resolves the canonical decision linked to the validation.
- Requires the canonical decision to be approved.
- Creates or replays a canonical payable-basis-application command.
- Calls the existing payable-basis application path exactly once through the canonical application operation.
- Links the legacy payable-basis application record to the canonical application command.
- Adapts the canonical application result into the existing `OperatorConsoleStatutoryDiscountApplyPayableBasisResponse`.

Legacy readback:

- Draft/detail readback should include safe canonical decision/application linkage when present.
- Existing legacy identifiers remain valid.
- Restricted evidence remains excluded.
- If legacy response adaptation fails after canonical completion, replay reads the canonical durable command and retries only response adaptation.

## 12. Status and Result Vocabulary

Client-visible fields must distinguish:

- Decision command status: `PROCESSING`, `COMPLETED`, `FAILED`, `CANCELLED`, `EXPIRED`.
- Decision result status: `REQUESTED`, `PENDING_OPERATOR_REVIEW`, `APPROVED`, `REJECTED`, `NON_APPROVED`, `VALIDATION_FAILED`.
- Application command status: `NOT_REQUESTED`, `PROCESSING`, `COMPLETED`, `FAILED`, `CANCELLED`.
- Application result status: `NOT_REQUESTED`, `REQUESTED`, `APPLIED`, `FAILED`, `REJECTED`.
- One-shot orchestration status: `DECISION_ONLY_COMPLETED`, `DECISION_COMPLETED_APPLICATION_PROCESSING`, `DECISION_AND_APPLICATION_COMPLETED`, `REPLAYED`, `CONFLICTED`, `FAILED`.
- Retryable: boolean.
- Recovery classification: `NONE`, `READ_CANONICAL_RESULT`, `RETRY_ORIGINAL_IDEMPOTENCY_KEY`, `WAIT_THEN_RETRY_ORIGINAL_IDEMPOTENCY_KEY`, `CORRECT_REQUEST_REQUIRED`, `NOT_RECOVERABLE`.
- Recovery action: `READ_CANONICAL_DECISION`, `READ_CANONICAL_APPLICATION`, `RETRY_SAME_REQUEST_WITH_ORIGINAL_IDEMPOTENCY_KEY`, `SUBMIT_CORRECTED_REQUEST`, `WAIT_AND_RETRY`, `DO_NOT_RETRY`.

No UI display text is defined by this design. Clients consume status codes and safe error codes only.

## 13. Persistence and Migration Impact

Smallest required database changes:

1. Evolve `discounts.statutory_discount_decision_commands` for staged decision semantics.
   - Purpose: retain canonical decision command identity, semantic hash, decision status, validation linkage, and readback.
   - Required additions should include staged semantic source version support, decision-vs-application separation fields, and a unique nullable linkage from `statutory_discount_validation_id` where safe.
   - Application-layer mapping alone is insufficient because cross-route replay and readback need durable linkage between legacy validations and canonical decisions.

2. Add canonical payable-basis application command persistence.
   - Recommended table: `discounts.statutory_discount_payable_basis_application_commands`.
   - Purpose: durable idempotency, semantic hash, command state, result state, recovery state, source-channel attribution, and links to decision command, validation, payable-basis application, and tariff snapshots.
   - Required unique index: `(statutory_discount_decision_command_id)` for active/completed application authority.
   - Required idempotency index: `(idempotency_scope, idempotency_key)`.
   - Required request-reference index only if request reference remains a durable lookup/audit reference for the application command.
   - Application-layer mapping alone is insufficient because a process failure after decision completion but before application completion must be replayable without reapplying the payable basis.

3. Add linkage from existing payable-basis application records to the canonical application command when required.
   - A nullable `statutory_discount_payable_basis_application_command_id` on `discounts.statutory_discount_payable_basis_applications` is the narrowest direct linkage if repository conventions allow it.
   - If direct schema change is not allowed, a separate mapping table keyed by `statutory_discount_payable_basis_application_id` and application command ID is acceptable.

4. Preserve existing `discounts.statutory_discount_payable_basis_applications` as the immutable payable-basis application evidence and calculation output store.
   - Do not create a second calculation store.
   - Do not duplicate tariff snapshot application logic.

5. Durable draft/evidence facts.
   - The staged decision operation can link to existing draft/evidence records instead of reconstructing the current one-shot request from incomplete legacy rows.
   - If future semantic hash requires original draft-only facts that are not persisted today, add privacy-minimized canonical decision fact columns or a canonical command fact snapshot. Do not store raw ID images, raw evidence payloads, or full statutory ID numbers.

No destructive migration is recommended. No existing completed records should be rewritten without a controlled backfill task.

## 14. Existing-Record Compatibility

Completed legacy decisions:

- Treat as immutable legacy-only history until replay/readback needs canonical linkage.
- Link lazily on replay only if all required canonical decision facts are determinable from safe persisted records.
- Otherwise expose historical fallback and require a new controlled backfill task for canonical linkage.

Completed legacy payable-basis applications:

- Treat as immutable applied payable-basis history.
- Readback may show missing canonical application command linkage.
- Do not reapply payable basis to create linkage.

Existing shared one-shot commands:

- Keep readable using `statutory-discount-decision:sha256:v1`.
- Do not reinterpret v1 hash semantics.
- If an existing command already links decision and application, expose it as historical one-shot command readback.

In-progress shared commands:

- Recover under existing v1 original-key rules until a migration/backfill decision is made.
- Do not convert in-progress v1 commands opportunistically during unrelated route calls.

Legacy drafts without full shared semantic facts:

- Remain valid staged legacy workflow records.
- Canonical convergence should create staged v2 decision records from available safe draft/evidence facts, not try to recreate a v1 one-shot request.

Records created before canonical linkage exists:

- Supported postures are historical fallback, controlled backfill, or lazy linkage only when safe.
- Destructive migration is prohibited.

## 15. Transaction and Recovery Boundaries

Decision persistence:

- Execute under the decision business identity lock.
- Persist or replay the canonical decision command before mutating legacy validation decision state.
- Complete the canonical decision only after legacy validation decision state is durable.

Payable-basis application:

- Execute under the application business identity lock derived from `statutoryDiscountDecisionCommandId`.
- Require approved canonical decision.
- Persist or replay the application command before invoking the lower-level payable-basis application path.
- Complete the application command only after `discounts.statutory_discount_payable_basis_applications` and the applied tariff snapshot are durable.

One-shot orchestration:

- Submit decision command.
- If `applyPayableBasis=false`, return decision-only completion.
- If `applyPayableBasis=true`, submit application command after approved decision completion.
- If failure occurs after decision but before application, replay resumes application using original application idempotency semantics.
- If failure occurs after application but before orchestration response completion, replay reads the durable application result and does not reapply.

Concurrent shared and legacy submissions:

- Decision concurrency is serialized by `statutory-discount-decision:{parkingSessionId}:{entitlementType}`.
- Application concurrency is serialized by `statutory-discount-payable-basis-application:{statutoryDiscountDecisionCommandId}`.
- A concurrent changed-material decision conflicts.
- A concurrent duplicate application replays or conflicts deterministically; it must not apply the payable basis twice.

## 16. Idempotency and Concurrency Posture

Decision idempotency:

- Same decision scope, same semantic hash: return canonical decision result.
- Same decision scope, different material decision facts: deterministic semantic conflict.
- Same processing decision with original key: recover.
- Same processing decision with different key: in-progress conflict.

Application idempotency:

- Same application scope, same semantic hash: return canonical application result.
- Same application scope, changed original tariff snapshot or changed decision reference: deterministic semantic conflict.
- Same processing application with original key: recover.
- Same processing application with different key: in-progress conflict.

Cross-route replay:

- Legacy decision after shared decision returns/adapts the canonical decision result.
- Shared decision after legacy decision returns the same canonical decision result.
- Legacy apply after shared one-shot application returns/adapts the canonical application result.
- Shared one-shot after legacy decision/apply returns decision and application readback without duplicate application.

## 17. Security and Privacy Posture

The staged design preserves:

- Server-derived source channel.
- Existing Operator Console RBAC, Site, device, shift, role, reviewer, and access-evaluation checks.
- Service-channel RBAC for future WebPay/APT.
- Metadata-only evidence references.
- Safe masked/reference-only statutory ID metadata.
- No raw ID images.
- No Base64 evidence.
- No raw evidence payloads.
- No full statutory ID numbers.
- No restricted evidence in general decision/application readback.
- No sensitive beneficiary values or unsafe request bodies in logs.

Retention remains unresolved unless an approved policy exists; this design does not introduce fixed retention periods.

## 18. Authority Boundaries

Preserved:

- Vendor PMS or HikCentral owns raw parking-session lifecycle and live tariff computation.
- Central PMS owns statutory decision, policy resolution, approved computation, payable-basis effect, and discount audit.
- Operator Console remains a staged controlled workflow.
- WebPay and APT remain future submit-and-display channels only.
- POS Server remains a finalized-fact fiscal consumer and Sales Invoice rendering authority.

The design does not move or add authority for payment finality, ExitAuthorization, fiscal issuance, gate control, payment-provider calls, HikCentral calls, or POS Server entitlement approval.

## 19. Rejected Alternatives

Rejected: persisting missing legacy fields alone.

- It would still leave approval and apply as two calls while the current v1 hash includes `applyPayableBasis`.
- It would not solve canonical recovery after decision completion but before application.

Rejected: removing `applyPayableBasis` from the current semantic material.

- It would silently change `statutory-discount-decision:sha256:v1`.
- It would weaken conflict detection for already-merged one-shot semantics.

Rejected: applying payable basis during Operator Console approval.

- It changes existing UI-visible workflow behavior.
- The current legacy decision endpoint explicitly says it does not apply the discount.

Rejected: invoking the one-shot facade only during legacy apply.

- Rejections would remain outside canonical decision authority.
- Approval decision state would still be written by a separate authoritative path.

Rejected: maintaining two separate authoritative implementations.

- It defeats cross-channel business uniqueness, replay, and exactly-once payable-basis proof.

Rejected: changing the Operator Console UI to one-shot behavior.

- The assigned convergence work is backend-compatible only.
- UI changes are explicitly off-limits.

Option D is not needed because option C is implementable and aligns with repository evidence.

## 20. Required Tests for Implementation

Implementation must add focused tests for:

- Canonical decision submit, replay, semantic conflict, in-progress recovery, and readback.
- Canonical application submit, replay, semantic conflict, in-progress recovery, and readback.
- Shared one-shot orchestration with decision only.
- Shared one-shot orchestration with decision plus application.
- Failure after decision before application, followed by replay.
- Failure after application before response adaptation, followed by replay.
- Legacy decision route creates/replays canonical decision.
- Legacy rejection creates/replays canonical decision without application.
- Legacy apply route creates/replays canonical application.
- Shared route replay after legacy decision/apply.
- Legacy route replay after shared decision/application.
- Concurrent shared and legacy decision submission.
- Concurrent shared and legacy application submission.
- Payable basis applied once.
- Payment initiation consumes the effective applied tariff snapshot without recalculation.
- POS fiscal mapper receives finalized canonical decision and application facts.
- RBAC and source-channel impersonation rejection.
- Safe readback without restricted evidence.
- No raw evidence or full statutory ID values in contracts, persistence, responses, or logs.

## 21. Manual Verification Requirements

Manual verification must cover:

- Authenticated Operator Console approval.
- Authenticated Operator Console rejection.
- Legacy decision replay.
- Legacy apply replay.
- Shared one-shot replay after legacy decision/application.
- Legacy replay after shared one-shot.
- Cross-route semantic conflict.
- Concurrent shared and legacy decision.
- Concurrent shared and legacy application.
- Canonical decision readback.
- Canonical application/readback linkage.
- Payable basis applied once.
- Payment initiation uses applied payable basis.
- POS fiscal request contains finalized facts only.
- No restricted evidence or full ID values appear in logs or responses.
- No payment finality, ExitAuthorization, fiscal issuance, or gate action occurs from decision/application commands.

## 22. Recommended Implementation Sequence

1. Add canonical staged decision/application contracts and persistence.
2. Refactor shared one-shot facade to orchestrate staged decision then optional staged application.
3. Converge Operator Console decision route onto canonical decision.
4. Converge Operator Console apply-payable-basis route onto canonical application.
5. Prove cross-route replay, semantic conflict, and concurrency.
6. Run WebPay integration readiness review.
7. Run APT integration readiness review.

Each step should be a separate bounded implementation concern.

## 23. Exact Next Bounded Implementation Task

Next task:

```text
Implement Central PMS statutory-discount staged canonical command contracts and persistence.
```

Scope:

- Introduce or evolve canonical decision command persistence for staged `statutory-discount-decision:sha256:v2` semantics.
- Introduce canonical payable-basis application command contracts/persistence with `statutory-discount-payable-basis-application:sha256:v1`.
- Add application service boundaries and repository methods for submit/replay/readback.
- Do not converge Operator Console routes in the same task.
- Do not change WebPay/APT/POS Server behavior.
- Do not change statutory calculation, VAT treatment, local ordinance support, payment finality, ExitAuthorization, fiscal issuance, or gate behavior.

## 24. Owning Persona

Codex persona: `Codex I`

Ownership note: Codex I owns the statutory-discount command, legal-source caution, payable-basis authority, and cross-channel boundary work in `D:\SourceCodes\ExitPass-Discounts`.

## 25. Repository and Proposed Branch

- Repository: `D:\SourceCodes\ExitPass-Discounts`
- Base branch: latest `origin/dev`
- Proposed implementation branch: `feature/central-pms-statutory-discount-staged-canonical-commands`

## 26. Expected File Areas

Expected implementation file areas:

- `src/Services/CentralPms/src/ExitPass.CentralPms.Contracts/StatutoryDiscounts`
- `src/Services/CentralPms/src/ExitPass.CentralPms.Application/StatutoryDiscounts`
- `src/Services/CentralPms/src/ExitPass.CentralPms.Infrastructure/StatutoryDiscounts`
- `src/Services/CentralPms/src/ExitPass.CentralPms.Api/Endpoints/StatutoryDiscountDecisionEndpoints.cs`
- `src/Services/CentralPms/tests/ExitPass.CentralPms.UnitTests/Application`
- `src/Services/CentralPms/tests/ExitPass.CentralPms.IntegrationTests/Persistence`
- `infra/db/patches`
- `infra/db/patches/validation`
- `docs/v1.3/central-pms/implementation-slices`

Operator Console endpoint files should be touched only in later convergence tasks.

## 27. Off-Limits Repositories and Files

Off limits for the next implementation task unless separately assigned:

- `D:\SourceCodes\ExitPass`
- `D:\SourceCodes\ExitPass-APT`
- `D:\SourceCodes\ExitPass-APT-NewVersion`
- `D:\SourceCodes\ExitPass-PoSServer`
- Operator Console UI
- WebPay UI and behavior
- APT implementation repositories
- POS Server repository
- Management Platform policy/configuration behavior
- Local ordinance rules or seed data
- VAT calculation behavior
- Payment finality
- ExitAuthorization
- Fiscal issuance authority
- Gate control

## Evidence Appendix

Reports and implementation notes:

- `docs/v1.3/operator-console/reviews/ExitPass_Statutory_Discount_System_Wide_Baseline_Audit_v1.0.md`
- `docs/v1.3/central-pms/reviews/ExitPass_Statutory_Discount_Integration_Readiness_and_Thread_Handoff_v1.0.md`
- `docs/v1.3/central-pms/implementation-slices/ExitPass_Central_PMS_Shared_Statutory_Discount_Decision_Facade_Implementation_Note_v1.0.md`
- `docs/v1.3/central-pms/implementation-slices/ExitPass_Central_PMS_Statutory_Discount_Channel_Contract_Readiness_Implementation_Note_v1.0.md`

Shared facade source and contracts:

- `src/Services/CentralPms/src/ExitPass.CentralPms.Api/Endpoints/StatutoryDiscountDecisionEndpoints.cs`
- `src/Services/CentralPms/src/ExitPass.CentralPms.Contracts/StatutoryDiscounts/StatutoryDiscountDecisionDtos.cs`
- `src/Services/CentralPms/src/ExitPass.CentralPms.Application/StatutoryDiscounts/IStatutoryDiscountDecisionFacadeService.cs`
- `src/Services/CentralPms/src/ExitPass.CentralPms.Application/StatutoryDiscounts/StatutoryDiscountDecisionFacadeModels.cs`
- `src/Services/CentralPms/src/ExitPass.CentralPms.Application/StatutoryDiscounts/StatutoryDiscountDecisionFacadeService.cs`
- `src/Services/CentralPms/src/ExitPass.CentralPms.Infrastructure/StatutoryDiscounts/PostgresStatutoryDiscountDecisionFacadeRepository.cs`

Shared facade database:

- `infra/db/patches/ExitPass_StatutoryDiscountDecisionFacade_v1.3.sql`
- `infra/db/patches/validation/Validate_StatutoryDiscountDecisionFacade_v1.3.sql`
- Table: `discounts.statutory_discount_decision_commands`
- Unique indexes: `ux_statutory_discount_decision_commands__idempotency`, `ux_statutory_discount_decision_commands__business_identity`, `ux_statutory_discount_decision_commands__request_reference`

Operator Console staged workflow:

- `src/Services/CentralPms/src/ExitPass.CentralPms.Api/Endpoints/OperatorConsoleStatutoryDiscountDraftEndpoints.cs`
- `src/Services/CentralPms/src/ExitPass.CentralPms.Contracts/OperatorConsole/OperatorConsoleStatutoryDiscountDraftDtos.cs`
- `src/Services/CentralPms/src/ExitPass.CentralPms.Contracts/OperatorConsole/OperatorConsoleStatutoryDiscountDecisionDtos.cs`
- `src/Services/CentralPms/src/ExitPass.CentralPms.Contracts/OperatorConsole/OperatorConsoleStatutoryDiscountApplyPayableBasisDtos.cs`
- `src/Services/CentralPms/src/ExitPass.CentralPms.Application/OperatorConsole/OperatorConsoleStatutoryDiscountDraftModels.cs`
- `src/Services/CentralPms/src/ExitPass.CentralPms.Application/OperatorConsole/OperatorConsoleStatutoryDiscountEvidenceModels.cs`
- `src/Services/CentralPms/src/ExitPass.CentralPms.Application/OperatorConsole/OperatorConsoleStatutoryDiscountDecisionModels.cs`
- `src/Services/CentralPms/src/ExitPass.CentralPms.Application/OperatorConsole/OperatorConsoleStatutoryDiscountApplyPayableBasisModels.cs`
- `src/Services/CentralPms/src/ExitPass.CentralPms.Infrastructure/OperatorConsole/OperatorConsoleStatutoryDiscountDraftWriter.cs`
- `src/Services/CentralPms/src/ExitPass.CentralPms.Infrastructure/OperatorConsole/OperatorConsoleStatutoryDiscountEvidenceRepository.cs`
- `src/Services/CentralPms/src/ExitPass.CentralPms.Infrastructure/OperatorConsole/OperatorConsoleStatutoryDiscountDecisionWriter.cs`
- `src/Services/CentralPms/src/ExitPass.CentralPms.Infrastructure/OperatorConsole/OperatorConsoleStatutoryDiscountApplyPayableBasisWriter.cs`

Payable-basis and payment consumption:

- `infra/db/patches/ExitPass_StatutoryDiscountPayableBasisApplicationSchema_v1.2.sql`
- `infra/db/patches/ExitPass_StatutoryDiscountAppliedTariffSnapshotLifecycle_v1.2.sql`
- `src/Services/CentralPms/src/ExitPass.CentralPms.Infrastructure/PaymentAttempts/TariffSnapshotReadRepository.cs`
- `src/Services/CentralPms/src/ExitPass.CentralPms.Application/PaymentAttempts/CreateOrReusePaymentAttemptHandler.cs`

Fiscal handoff:

- `src/Services/CentralPms/src/ExitPass.CentralPms.Application/FiscalIssuance/PosServerFiscalDocumentRequestMapper.cs`
- `src/Services/CentralPms/src/ExitPass.CentralPms.Application/FiscalIssuance/PosServerFiscalDocumentClientModels.cs`
- `src/Services/CentralPms/tests/ExitPass.CentralPms.UnitTests/FiscalIssuance/PosServerFiscalDocumentRequestMapperTests.cs`
- `src/Services/CentralPms/tests/ExitPass.CentralPms.UnitTests/FiscalIssuance/FiscalSemanticRequestHashCalculatorTests.cs`

Tests and Bruno inventory:

- `src/Services/CentralPms/tests/ExitPass.CentralPms.UnitTests/Application/StatutoryDiscountDecisionFacadeServiceTests.cs`
- `src/Services/CentralPms/tests/ExitPass.CentralPms.IntegrationTests/Persistence/StatutoryDiscountDecisionFacadeRepositoryTests.cs`
- `src/Services/CentralPms/tests/ExitPass.CentralPms.IntegrationTests/Api/StatutoryDiscountDecisionApiAccessPolicyIntegrationTests.cs`
- `src/Services/CentralPms/tests/ExitPass.CentralPms.IntegrationTests/Api/OperatorConsoleStatutoryDiscountDecisionApiIntegrationTests.cs`
- `src/Services/CentralPms/tests/ExitPass.CentralPms.IntegrationTests/Api/OperatorConsoleStatutoryDiscountApplyPayableBasisApiIntegrationTests.cs`
- `src/Services/CentralPms/tests/ExitPass.CentralPms.IntegrationTests/Api/OperatorConsoleStatutoryDiscountE2EIntegrationTests.cs`
- `src/Services/CentralPms/tests/ExitPass.CentralPms.ContractTests/Public/VendorParkingResolutionContractTests.cs`
- `src/Services/CentralPms/tests/ExitPass.CentralPms.IntegrationTests/Api/VendorParkingResolutionApiIntegrationTests.cs`
- `src/Services/CentralPms/tests/ExitPass.CentralPms.IntegrationTests/Api/CreatePaymentAttemptPublicApiIntegrationTests.cs`
- `bruno/operator-console-statutory-discount-draft/139-shared-statutory-decision-submit.bru`
- `bruno/operator-console-statutory-discount-draft/140-shared-statutory-decision-replay.bru`
- `bruno/operator-console-statutory-discount-draft/141-shared-statutory-decision-semantic-conflict.bru`
- `bruno/operator-console-statutory-discount-draft/142-shared-statutory-decision-readback.bru`
- `bruno/operator-console-statutory-discount-draft/143-shared-statutory-decision-unsafe-id-rejected.bru`

## 28. Decision Marker

STATUTORY_DISCOUNT_STAGED_CANONICAL_DESIGN_APPROVED
