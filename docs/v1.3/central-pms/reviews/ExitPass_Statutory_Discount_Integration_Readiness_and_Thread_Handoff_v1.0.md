# ExitPass Statutory Discount Integration Readiness and Thread Handoff v1.0

## 1. Purpose

This audit determines whether the merged statutory-discount workstream in `ExitPass-Discounts` is ready to hand one bounded next implementation task to another thread. It is audit and handoff evidence only. It does not implement Operator Console convergence, change the shared facade, start WebPay/APT integration, change POS Server behavior, add statutory rules, or activate ordinance configuration.

## 2. Repository and Baseline Commit

- Repository: `D:\SourceCodes\ExitPass-Discounts`
- Branch examined: `docs/statutory-discount-integration-readiness-handoff`
- Base branch: `dev`
- origin/dev baseline commit incorporated: `683378b61d8a7b844394847056501925bf86263c`
- Origin: `https://github.com/darwinpasco/ExitPass.git`
- Baseline material read directly from the merged repository:
  - `docs/v1.3/operator-console/reviews/ExitPass_Statutory_Discount_System_Wide_Baseline_Audit_v1.0.md`
  - `docs/v1.3/central-pms/implementation-slices/ExitPass_Central_PMS_Shared_Statutory_Discount_Decision_Facade_Implementation_Note_v1.0.md`
  - Shared facade source, contracts, persistence, RBAC, tests, Bruno scenarios, database patch and validation listed in the evidence appendix.

## 3. Executive Readiness Verdict

The workstream is ready to hand off one bounded backend prerequisite task, but it is not ready for WebPay or APT channel integration.

The merged shared facade exists and provides the intended Central PMS route family:

- `POST /v1/statutory-discounts/decisions`
- `GET /v1/statutory-discounts/decisions/{statutoryDiscountDecisionCommandId}`

However, integration readiness is incomplete because the legacy Operator Console authoritative decision and payable-basis application routes remain separate from the shared facade, the shared request still exposes operator/reviewer-shaped fields as required business facts, the status vocabulary is string-based and not yet a complete client contract for WebPay/APT, POS fiscal handoff does not carry every canonical shared-decision fact, and the database patch remains app-local rather than part of canonical migration baseline.

Final authorization is therefore limited to the exact next task in section 20:

`Central PMS statutory-discount channel-integration prerequisite: stabilize the client-consumable shared contract, status vocabulary, and fiscal/readback linkage without changing calculation behavior or starting channel UI integration.`

## 4. Prerequisite Status Matrix

| # | Prerequisite | Status | Evidence |
|---|---|---|---|
| 1 | Canonical decision facade | NOT_YET_ACHIEVED | Shared routes exist in `src/Services/CentralPms/src/ExitPass.CentralPms.Api/Endpoints/StatutoryDiscountDecisionEndpoints.cs`, but legacy Operator Console decision and apply routes remain in `OperatorConsoleStatutoryDiscountDraftEndpoints.cs`. |
| 2 | Canonical status and result vocabulary | NOT_YET_ACHIEVED | DTO/result strings exist in `StatutoryDiscountDecisionDtos.cs`, `StatutoryDiscountDecisionFacadeModels.cs`, and endpoint HTTP mappings, but no complete client enum/retryability vocabulary exists for recoverable/unavailable states. |
| 3 | Payable-basis consumption contract | ACHIEVED | `CreateOrReusePaymentAttemptHandler.cs` and `TariffSnapshotReadRepository.cs` consume the effective applied tariff snapshot without recalculating the discount. |
| 4 | Channel submission contract | NOT_YET_ACHIEVED | `StatutoryDiscountDecisionRequest` currently requires operator-shaped facts such as `actorUserId`, `maskedIdReference`, and attestation/reviewer fields for decision paths. |
| 5 | Evidence and privacy contract | ACHIEVED | Shared contract accepts metadata references only and rejects full ID-like values; retention remains unresolved and must not be inferred. |
| 6 | Legal and policy scope freeze | ACHIEVED | Current implementation is fixed to `SENIOR_CITIZEN` and `PWD`; local ordinance and expanded eligibility behavior remain unsupported/research-only. |
| 7 | POS Server fiscal handoff | NOT_YET_ACHIEVED | Fiscal DTOs carry discount references and privilege details, but not the complete canonical command/source-channel/timestamp vocabulary. |
| 8 | WebPay integration ownership decision | ACHIEVED | Next task category is B: backend prerequisite before WebPay. |
| 9 | WebPay readiness | NOT_YET_ACHIEVED | Payment initiation can consume discounted basis, but submit/status/POS contract gaps remain. |
| 10 | APT readiness | NOT_YET_ACHIEVED | APT is documented as submit/display only, but the shared contract is not yet terminal-ready. |
| 11 | Management Platform readiness | NOT_REQUIRED | Not required for current fixed Senior/PWD scope; required before governed local-ordinance support. |
| 12 | Database baseline | NOT_YET_ACHIEVED | `ExitPass_StatutoryDiscountDecisionFacade_v1.3.sql` is an app-local patch under `infra/db/patches`, not a canonical migration. |
| 13 | Bruno/manual verification posture | ACHIEVED | Scenarios 139-143 exist structurally; execution should be a separate environment-validation task. |
| 14 | Active branch and overlap check | ACHIEVED | Active statutory-discount branches are inventoried; no active branch should receive WebPay/APT work before backend prerequisite closure. |

## 5. Canonical Route and Authority Inventory

Shared route family:

- `POST /v1/statutory-discounts/decisions`
  - Endpoint: `StatutoryDiscountDecisionEndpoints.SubmitAsync`
  - Application service: `IStatutoryDiscountDecisionFacadeService.SubmitAsync`
  - Route policy metadata: `CentralPmsStatutoryDiscountDecisionSubmit`
  - Response: `StatutoryDiscountDecisionResponse`
- `GET /v1/statutory-discounts/decisions/{statutoryDiscountDecisionCommandId}`
  - Endpoint: `StatutoryDiscountDecisionEndpoints.ReadAsync`
  - Application service: `IStatutoryDiscountDecisionFacadeService.GetAsync`
  - Route policy metadata: `CentralPmsStatutoryDiscountDecisionRead`
  - Response: `StatutoryDiscountDecisionResponse`

Operator Console preparatory/read-only routes:

- `GET /v1/ops/operator-console/statutory-discounts/drafts`
- `GET /v1/ops/operator-console/statutory-discounts/drafts/{draftId}`
- `POST /v1/ops/operator-console/statutory-discounts/draft`
- `POST /v1/ops/operator-console/statutory-discounts/{draftId}/evidence`
- `GET /v1/ops/operator-console/statutory-discounts/{draftId}/evidence`
- `POST /v1/ops/operator-console/statutory-discounts/resolve-policy`
- `GET /v1/ops/operator-console/audit/statutory-discounts`

Operator Console authoritative legacy routes still present:

- `POST /v1/ops/operator-console/statutory-discounts/{draftId}/decision`
- `POST /v1/ops/operator-console/statutory-discounts/{validationId}/apply-payable-basis`

Conclusion: the shared route family exists, but it is not yet the only authoritative decision/payable-basis path. The legacy Operator Console decision/apply path is not merely preparatory and remains a separate application route.

## 6. Canonical DTO and Status Vocabulary

DTOs:

- `StatutoryDiscountDecisionRequest`
- `StatutoryDiscountEvidenceReferenceRequest`
- `StatutoryDiscountDecisionResponse`
- `StatutoryDiscountEvidenceReferenceResponse`
- `StatutoryDiscountDecisionCommand`
- `StatutoryDiscountDecisionResult`
- `StatutoryDiscountDecisionCommandRecord`

Supported source-channel strings:

- `OPERATOR_CONSOLE`
- `WEBPAY`
- `ASSISTED_PAYMENT_TERMINAL`

Current decision/status/result strings observed:

- New command starts as `PROCESSING` in `discounts.statutory_discount_decision_commands`.
- New successful facade result returns result classification `ACCEPTED`.
- Exact replay returns `IDEMPOTENT_REPLAY`.
- Same business identity with changed material facts returns error `IDEMPOTENCY_SEMANTIC_CONFLICT`.
- Existing incomplete command returns error `STATUTORY_DISCOUNT_DECISION_IN_PROGRESS`.
- Approved/decision path uses existing Operator Console status values such as `APPROVED` and `REJECTED`.
- Payable-basis application can surface `APPLIED_PAYABLE_BASIS` from legacy Operator Console models.
- Missing readback returns `STATUTORY_DISCOUNT_DECISION_NOT_FOUND`.
- Unsafe identity input returns `UNSAFE_IDENTIFIER_REJECTED`.
- Unsupported source and entitlement return `UNSUPPORTED_SOURCE_CHANNEL` and `UNSUPPORTED_ENTITLEMENT_TYPE`.

HTTP mappings:

- New submit: `201 Created`
- Idempotent replay: `200 OK`
- Validation failure: `400 Bad Request`
- Source-channel permission failure: `403 Forbidden`
- Semantic conflict: `409 Conflict`
- In-progress conflict: `409 Conflict`
- Readback success: `200 OK`
- Missing readback: `404 Not Found`

Gap: client-consumable vocabulary is not yet stable enough for WebPay/APT. In particular, retryability is not a complete contract because the endpoint error envelope sets `Retryable = false` for all shared errors, and recoverable/unavailable orchestration states are not exposed as a first-class status family.

## 7. Payable-Basis Consumption Contract

Authoritative payable-basis application path currently used by the shared facade:

- Shared service: `StatutoryDiscountDecisionFacadeService`
- Reused application service: `IOperatorConsoleStatutoryDiscountApplyPayableBasisService`
- Reused repository/writer: `OperatorConsoleStatutoryDiscountApplyPayableBasisWriter`
- Durable validation: `discounts.statutory_discount_validations`
- Durable application: `discounts.statutory_discount_payable_basis_applications`
- Durable payable basis: `core.tariff_snapshots`
- Shared command readback: `discounts.statutory_discount_decision_commands`

Payment initiation consumption:

- `CreateOrReusePaymentAttemptHandler` reads the submitted tariff snapshot and then checks `ITariffSnapshotReadRepository.GetEffectiveAppliedTariffSnapshotAsync`.
- `TariffSnapshotReadRepository.GetEffectiveAppliedTariffSnapshotAsync` resolves the active statutory-adjusted tariff snapshot by `core.tariff_snapshots.statutory_discount_validation_id` and applied `discounts.statutory_discount_payable_basis_applications`.
- If a discounted applied snapshot exists and the caller submits the original snapshot, payment initiation rejects it as stale rather than recalculating.
- WebPay payment intent DTO has optional `TariffSnapshotId` and `ExpectedAmountMinorUnits` for the backend-approved payable basis.

Conclusion: consumption of an already-applied statutory payable basis is proven. The missing piece is not payment recalculation; it is stable channel submission/readback contract readiness.

## 8. Channel Request-Field Matrix

| Field | Classification | Current posture |
|---|---|---|
| `parkingSessionId` | REQUIRED | Required by service validation and semantic hash. |
| `siteId` | OPTIONAL | Hashed if supplied; reused by Operator Console path. |
| `siteGroupId` | OPTIONAL | Hashed if supplied; reused by Operator Console path. |
| `ticketReference` | OPTIONAL | Hashed if supplied. |
| `plateNumber` | OPTIONAL | Hashed if supplied. |
| `entitlementType` | REQUIRED | Only `SENIOR_CITIZEN` and `PWD` supported. |
| `idDocumentType` | REQUIRED | Required by current path. |
| `issuingAuthority` | REQUIRED | Required by current path. |
| `expiryDate` | OPTIONAL | Hashed if supplied. |
| `maskedIdReference` | REQUIRED | Required; full ID-like values rejected. |
| `evidenceCaptureRequested` | REQUIRED | Boolean in DTO and semantic hash. |
| `evidenceReferences` | OPTIONAL | Metadata-only references; hashed after normalization. |
| `actorUserId` | REQUIRED | Required by current service; operator-shaped for WebPay/APT. |
| `operatorDeviceBindingId` | OPTIONAL | Operator-specific and hashed if supplied. |
| `operatorShiftId` | OPTIONAL | Operator-specific and hashed if supplied. |
| `requesterAttestation` | REQUIRED | Boolean in DTO and semantic hash. |
| `attestationNotes` | OPTIONAL | Hashed if supplied. |
| `reasonCode` | OPTIONAL | Hashed if supplied. |
| `decision` | OPTIONAL | Must be `APPROVE` or `REJECT` when supplied. |
| `decisionReasonCode` | OPTIONAL | Required for rejection. |
| `reviewerUserId` | OPTIONAL | Hashed if supplied. |
| `reviewerAttestation` | REQUIRED_FOR_DECISION | Required when `decision` is supplied. |
| `applyPayableBasis` | REQUIRED | Boolean in DTO and semantic hash; requires approval. |
| `originalTariffSnapshotId` | OPTIONAL | Hashed if supplied. |
| `Idempotency-Key` header | REQUIRED | Required by endpoint/service. |
| `X-Correlation-Id` header | REQUIRED | Required by endpoint; excluded from semantic hash. |
| `requestReference` | REQUIRED | Unique durable correlation/business reference; excluded from business scope. |
| `sourceChannel` | REQUIRED | Attribution and RBAC selection; excluded from semantic hash/business scope. |
| raw ID image bytes | PROHIBITED | Not present in DTO. |
| Base64 evidence payload | PROHIBITED | Not present in DTO. |
| full statutory ID number | PROHIBITED | Rejected by unsafe numeric identifier validation. |

Conclusion: WebPay and APT should not build production submissions against this contract yet. It is structurally shared, but it still exposes operator-shaped actor/reviewer concepts as required facts and does not distinguish server-derived service identity from beneficiary/operator workflow facts.

## 9. Evidence and Privacy Contract

Permitted evidence fields:

- `evidenceType`
- `captureMethod`
- `fileName`
- `contentType`
- `sizeBytes`
- `storageReference`
- `referenceNumberMasked`
- `verificationStatus`

Prohibited fields:

- Raw ID image bytes
- Base64 evidence payloads
- Raw document payloads
- Full statutory ID numbers

Observed privacy controls:

- Shared DTOs contain metadata/reference fields, not evidence payload bytes.
- `StatutoryDiscountDecisionFacadeService.ContainsUnsafeNumericIdentifier` rejects `maskedIdReference` or `referenceNumberMasked` values containing full-ID-like numeric runs.
- `PosServerFiscalDocumentRequestMapper` blocks sensitive terms such as `entitlement_evidence_image` from mapped fiscal context.
- Readback returns canonical result fields and does not return raw evidence payloads.

Unresolved:

- No approved privacy-retention period is established for statutory-discount evidence references in the merged shared facade. Retention must remain unresolved and must not be inferred from older Operator Console documents.

## 10. Supported Legal and Policy Scope

Implemented today:

- `SENIOR_CITIZEN`
- `PWD`
- Existing fixed Operator Console computation path reused by the shared facade.
- National fallback-oriented current behavior through existing policy-resolution/documentation posture.
- VAT-exclusive calculation behavior is inherited from the existing Operator Console statutory-discount computation contract; this audit did not alter it.

Hard-coded/current behavior:

- Current service accepts only `SENIOR_CITIZEN` and `PWD`.
- No broader local-ordinance engine is active in the shared facade.

Documented or research-only:

- Philippine local parking ordinance lists and older Operator Console policy registry plans remain research/design evidence only unless separately approved.
- Production policy import/admin endpoints are dry-run/review oriented and explicitly do not import, seed, or activate production policy rows.

Unsupported or legally unresolved:

- Parking-specific local ordinances
- Residency rules
- Driver/passenger rules
- Initial free periods
- Initial-rate exemption
- Full exemption
- Capped exemption
- Overnight exclusions
- Valet exclusions
- Standalone parking-business exclusions
- Facility-type restrictions
- Coupon/statutory stacking behavior
- Multiple beneficiaries and group/shared transaction allocation
- Legal activation of ordinance-specific rules

## 11. POS Server Fiscal-Handoff Status

Current Central PMS to POS model fields include:

- `CentralPmsPayableBasisContext.PayableBasisRef`
- `CentralPmsPayableBasisContext.UpstreamFinalityRef`
- `CentralPmsPayableBasisContext.PayableAmountMinorUnits`
- `CentralPmsFiscalDiscountReferenceContext.DiscountValidationRef`
- `CentralPmsFiscalDiscountReferenceContext.Status`
- `CentralPmsFiscalDiscountReferenceContext.AppliesStatutoryDiscountTreatment`
- `CentralPmsFiscalDiscountPrivilegeDetailContext.BasisAmountMinorUnits`
- `CentralPmsFiscalDiscountPrivilegeDetailContext.DiscountAmountMinorUnits`
- `CentralPmsFiscalDiscountPrivilegeDetailContext.VatPrivilegeAmountMinorUnits`
- `CentralPmsFiscalDiscountPrivilegeDetailContext.BeneficiaryRef`
- `CentralPmsFiscalDiscountPrivilegeDetailContext.EvidenceRef`
- `CentralPmsFiscalDiscountPrivilegeDetailContext.ApprovalRef`

Current mapper/hashing behavior:

- `PosServerFiscalDocumentRequestMapper` maps payable basis, discount references, tax details, totals, tenders, and discount privilege details.
- `FiscalSemanticRequestHashCalculator` includes payable basis discount references and discount privilege details in the fiscal semantic hash.

Missing or not proven as stable finalized shared-facade facts:

- Canonical `statutoryDiscountDecisionCommandId`
- Source-channel attribution
- Shared decision timestamp
- Explicit policy/legal reference as a dedicated typed field rather than context
- Beneficiary count
- Evidence reference posture tied to the canonical shared command
- Fiscal display code/wording that directly references the shared facade result

Conclusion: POS Server remains fiscalization and Sales Invoice rendering authority only, but the handoff facts are not yet complete enough to treat WebPay/APT statutory submission as ready.

## 12. WebPay Readiness

Overall result: `NOT_READY`

Achieved:

- Shared facade is merged.
- WebPay does not contain a public authoritative statutory-discount calculator.
- WebPay/payment initiation can pass a Central PMS tariff snapshot.
- Payment initiation consumes the effective applied statutory payable basis without recalculating.

Not yet achieved:

- WebPay-ready submission DTO is not stable because current shared request still requires operator-shaped facts.
- Status vocabulary is not stable enough for client interpretation.
- POS fiscal handoff lacks complete canonical shared-decision facts.
- Legacy Operator Console authoritative path remains unconverged.
- Database patch is not canonical migration baseline.

## 13. APT Readiness

Overall result: `NOT_READY`

Achieved:

- APT documents in this repository describe submit/display style authority, not local legal interpretation authority.
- The shared facade includes `ASSISTED_PAYMENT_TERMINAL` as a supported source-channel value.

Not yet achieved:

- The current shared DTO is not terminal-ready because it still requires operator-shaped actor/reviewer facts and uses Operator Console service orchestration underneath.
- APT cash acceptance can use an approved payable basis only after channel contract and fiscal handoff gaps close.
- Cross-channel replay exists at the shared facade, but legacy Operator Console remains separate.

No APT repositories were accessed.

## 14. Management Platform Prerequisite Status

Management Platform status: `NOT_REQUIRED_FOR_CURRENT_FIXED_SCOPE`

The current fixed Senior Citizen/PWD behavior does not require Management Platform statutory policy administration before a backend contract prerequisite can proceed.

Management Platform or governed policy administration is required before:

- local ordinance activation,
- production policy registry management,
- effective-dated site/jurisdiction policy administration,
- legal-source approval workflow,
- activation/suspension/retirement/supersession,
- maker/checker policy changes.

Current implemented Management Platform surfaces are read-only identity/RBAC inventory and Sales Invoice profile administration, not statutory-discount policy administration.

## 15. Database Baseline Status

Merged facade patch:

- `infra/db/patches/ExitPass_StatutoryDiscountDecisionFacade_v1.3.sql`
- validation: `infra/db/patches/validation/Validate_StatutoryDiscountDecisionFacade_v1.3.sql`

Objects:

- `discounts.statutory_discount_decision_commands`
- `ux_statutory_discount_decision_commands__idempotency`
- `ux_statutory_discount_decision_commands__business_identity`
- `ux_statutory_discount_decision_commands__request_reference`
- `ix_statutory_discount_decision_commands__parking_session`
- `ix_statutory_discount_decision_commands__validation`
- `ix_statutory_discount_decision_commands__correlation`

Key fields:

- `statutory_discount_decision_command_id`
- `request_reference`
- `idempotency_scope`
- `idempotency_key`
- `source_channel`
- `parking_session_id`
- `entitlement_type`
- `semantic_hash_source_version`
- `semantic_request_hash`
- `statutory_discount_validation_id`
- `payable_basis_application_id`
- `original_tariff_snapshot_id`
- `applied_tariff_snapshot_id`

Status:

- The patch is merged in the application repository.
- It remains an app-local patch, not a canonical executable migration under a migration directory.
- Database promotion blocks channel integration only if the receiving thread intends to run against canonical database baseline environments. It does not block the next backend prerequisite design/contract slice.

## 16. Bruno and Manual-Verification Posture

Scenarios 139-143 exist under `bruno/operator-console-statutory-discount-draft`:

- `139-shared-statutory-decision-submit.bru`
- `140-shared-statutory-decision-replay.bru`
- `141-shared-statutory-decision-semantic-conflict.bru`
- `142-shared-statutory-decision-readback.bru`
- `143-shared-statutory-decision-unsafe-id-rejected.bru`

Posture:

- Bruno structural coverage exists for submit, replay, conflict, readback, and unsafe-ID rejection.
- Authenticated integration tests are sufficient merge-level proof for code-path safety.
- Bruno execution should be a separate environment-validation task before channel UAT, so another implementation thread does not spend time repeatedly proving local Bruno CLI availability.

## 17. Active Branch and Overlap Inventory

Local statutory/discount branches visible:

- `docs/statutory-discount-integration-readiness-handoff` - current audit branch, adds this report only.
- `feature/operator-console-statutory-discount-shared-facade-convergence` - local convergence branch; no changes were incorporated by this audit.

Origin statutory/discount branches visible:

- `origin/docs/statutory-discount-system-wide-baseline-audit`
- `origin/feature/central-pms-statutory-discount-shared-contract-facade`
- `origin/feature/apt-terminal-cash-receipt-presentation-parity`

Known/likely owner:

- Codex I owns the statutory-discount/payable-basis workstream per persona notes and local task history.
- No repository metadata identifies branch owners beyond branch names and prior task context.

Overlap assessment:

- Central PMS shared facade files are the expected area for the recommended backend prerequisite.
- WebPay source files should not be touched by the next task.
- APT repositories are off limits.
- POS Server external repository is off limits; only ExitPass-held contracts may be read.
- Management Platform source should not be touched.
- The local Operator Console convergence branch indicates convergence is a known separate topic and should not be mixed into WebPay/APT integration.

## 18. Completed Milestones

- System-wide statutory-discount baseline audit merged.
- Shared Central PMS statutory-discount decision/readback facade merged.
- Cross-channel business identity uses `statutory-discount-decision:{parkingSessionId}:{entitlementType}`.
- Source channels are supported as attribution values.
- Shared facade rejects full-ID-like values and avoids raw evidence payload fields.
- Shared facade database patch and validation file exist.
- Payment initiation consumes effective applied statutory payable basis without recalculation.
- Bruno scenarios 139-143 exist structurally.

## 19. Remaining Gaps

- Legacy Operator Console decision/apply routes are not converged onto the shared facade.
- Shared DTO still requires operator-shaped fields, so WebPay/APT cannot safely build production submissions.
- Status/retryability vocabulary is not complete enough for independent channel clients.
- POS fiscal handoff does not yet expose all canonical shared-decision facts.
- Shared facade patch is not canonical database migration baseline.
- Retention policy remains unresolved.
- Local ordinance/legal-source activation remains unsupported and unresolved.

## 20. Exact Recommended Next Task

Next task category: `B. Backend prerequisite before WebPay`

Exact bounded task:

`Stabilize the Central PMS shared statutory-discount channel-integration contract by defining client-consumable status/retryability vocabulary, separating server-derived channel/service identity from operator-specific workflow facts, and linking shared-decision canonical identifiers into payable-basis readback and POS fiscal handoff models where already supported, without changing calculation behavior or starting WebPay/APT UI integration.`

Required scope for that task:

- Central PMS shared statutory-discount request/response contract clarity.
- Stable status/result/error vocabulary for channel clients.
- Server-side channel identity/RBAC enforcement model for WebPay/APT service identities.
- Payable-basis readback fields that channels can consume without recalculation.
- POS fiscal context linkage for canonical command ID/source-channel/decision timestamp where existing fiscal contracts can carry them safely.
- Tests for WebPay/APT service identities at the Central PMS boundary.

## 21. Rejected Alternative Next Tasks and Reasons

- `A. WebPay integration with the shared facade` - rejected because the shared DTO/status/POS handoff are not ready for channel clients.
- `C. Management Platform policy/configuration work` - rejected because it is not required for the current fixed Senior/PWD backend prerequisite and would expand into local ordinance/governance scope.
- `D. APT integration preparation` - rejected because the same channel-neutral contract gaps block APT.
- `E. POS Server fiscal-handoff completion` - rejected as standalone next task because the missing fiscal fields should be coordinated with the shared status/readback contract rather than isolated in POS code.
- `Operator Console convergence` - rejected as next handoff task because it is a separate compatibility/convergence problem with an existing local branch and should not block the channel-contract prerequisite.

## 22. Owning Persona

Codex persona: `Codex I`

Ownership note: Codex I owns statutory-discount, legal-source caution, and payable-basis boundary work in this repository.

## 23. Repository

Use only:

- `D:\SourceCodes\ExitPass-Discounts`

Read-only only when explicitly needed:

- `D:\SourceCodes\ExitPass`

Do not access:

- `D:\SourceCodes\ExitPass-APT`
- `D:\SourceCodes\ExitPass-APT-NewVersion`
- `D:\SourceCodes\ExitPass-PoSServer` unless an exact ExitPass-held contract cannot be verified.

## 24. Base Branch

- Base branch for the next task: latest `origin/dev`

## 25. Feature Branch

Recommended feature branch for the next task:

- `feature/central-pms-statutory-discount-channel-contract-readiness`

This audit branch remains documentation-only.

## 26. Expected File Areas

Expected file areas for the next task:

- `src/Services/CentralPms/src/ExitPass.CentralPms.Contracts/StatutoryDiscounts`
- `src/Services/CentralPms/src/ExitPass.CentralPms.Application/StatutoryDiscounts`
- `src/Services/CentralPms/src/ExitPass.CentralPms.Api/Endpoints/StatutoryDiscountDecisionEndpoints.cs`
- `src/Services/CentralPms/src/ExitPass.CentralPms.Application/Security`
- `src/Services/CentralPms/src/ExitPass.CentralPms.Application/FiscalIssuance`
- `src/Services/CentralPms/tests`
- `bruno/operator-console-statutory-discount-draft`
- `docs/v1.3/central-pms/implementation-slices`

Expected exclusions:

- WebPay UI source
- APT repositories
- POS Server repository
- Management Platform statutory policy administration
- Operator Console UI
- local ordinance seed/configuration

## 27. Off-Limits Repositories and Files

Off limits for the next task unless separately assigned:

- `D:\SourceCodes\ExitPass`
- `D:\SourceCodes\ExitPass-APT`
- `D:\SourceCodes\ExitPass-APT-NewVersion`
- `D:\SourceCodes\ExitPass-PoSServer`
- Operator Console UI behavior
- WebPay UI behavior
- Management Platform policy/configuration implementation
- SQL ordinance seed data
- fiscal numbering/issuance authority changes
- payment finality
- ExitAuthorization
- gate control

## 28. Required Validations

The next task must run:

- Central PMS API build
- Central PMS unit test build
- focused shared statutory-discount facade tests
- shared endpoint/RBAC tests
- PostgreSQL repository tests for shared command persistence
- payable-basis and payment-initiation tests for discounted basis consumption
- relevant POS fiscal mapper and semantic-hash tests
- WebPay contract/readback tests only if contracts are changed
- Bruno structural validation and environment execution if available
- `git diff --check`

## 29. Final Authorization Decision

`STATUTORY_DISCOUNT_HANDOFF_READY`

Ready means the backend prerequisite task in section 20 is safe to start. It does not mean WebPay, APT, Operator Console convergence, Management Platform policy administration, local ordinance support, or the full statutory-discount program is complete.

## 30. Evidence Appendix

Baseline and implementation notes:

- `docs/v1.3/operator-console/reviews/ExitPass_Statutory_Discount_System_Wide_Baseline_Audit_v1.0.md`
- `docs/v1.3/central-pms/implementation-slices/ExitPass_Central_PMS_Shared_Statutory_Discount_Decision_Facade_Implementation_Note_v1.0.md`

Shared facade contracts and source:

- `src/Services/CentralPms/src/ExitPass.CentralPms.Contracts/StatutoryDiscounts/StatutoryDiscountDecisionDtos.cs`
- `src/Services/CentralPms/src/ExitPass.CentralPms.Application/StatutoryDiscounts/IStatutoryDiscountDecisionFacadeService.cs`
- `src/Services/CentralPms/src/ExitPass.CentralPms.Application/StatutoryDiscounts/StatutoryDiscountDecisionFacadeModels.cs`
- `src/Services/CentralPms/src/ExitPass.CentralPms.Application/StatutoryDiscounts/StatutoryDiscountDecisionFacadeService.cs`
- `src/Services/CentralPms/src/ExitPass.CentralPms.Infrastructure/StatutoryDiscounts/PostgresStatutoryDiscountDecisionFacadeRepository.cs`
- `src/Services/CentralPms/src/ExitPass.CentralPms.Api/Endpoints/StatutoryDiscountDecisionEndpoints.cs`

Database patch and validation:

- `infra/db/patches/ExitPass_StatutoryDiscountDecisionFacade_v1.3.sql`
- `infra/db/patches/validation/Validate_StatutoryDiscountDecisionFacade_v1.3.sql`

Operator Console legacy statutory-discount path:

- `src/Services/CentralPms/src/ExitPass.CentralPms.Api/Endpoints/OperatorConsoleStatutoryDiscountDraftEndpoints.cs`
- `src/Services/CentralPms/src/ExitPass.CentralPms.Api/Endpoints/OperatorConsoleStatutoryDiscountPolicyResolutionEndpoints.cs`
- `src/Services/CentralPms/src/ExitPass.CentralPms.Application/OperatorConsole/OperatorConsoleStatutoryDiscountDraftService.cs`
- `src/Services/CentralPms/src/ExitPass.CentralPms.Application/OperatorConsole/OperatorConsoleStatutoryDiscountDecisionService.cs`
- `src/Services/CentralPms/src/ExitPass.CentralPms.Application/OperatorConsole/OperatorConsoleStatutoryDiscountApplyPayableBasisService.cs`
- `src/Services/CentralPms/src/ExitPass.CentralPms.Application/OperatorConsole/OperatorConsoleStatutoryDiscountComputationContract.cs`
- `src/Services/CentralPms/src/ExitPass.CentralPms.Infrastructure/OperatorConsole/OperatorConsoleStatutoryDiscountDraftWriter.cs`
- `src/Services/CentralPms/src/ExitPass.CentralPms.Infrastructure/OperatorConsole/OperatorConsoleStatutoryDiscountDecisionWriter.cs`
- `src/Services/CentralPms/src/ExitPass.CentralPms.Infrastructure/OperatorConsole/OperatorConsoleStatutoryDiscountApplyPayableBasisWriter.cs`

RBAC:

- `src/Services/CentralPms/src/ExitPass.CentralPms.Application/Security/CentralPmsRbacPolicyCatalog.cs`
- `src/Services/CentralPms/src/ExitPass.CentralPms.Api/Endpoints/StatutoryDiscountDecisionEndpoints.cs`

Payable-basis/payment-initiation consumption:

- `src/Services/CentralPms/src/ExitPass.CentralPms.Application/PaymentAttempts/CreateOrReusePaymentAttemptHandler.cs`
- `src/Services/CentralPms/src/ExitPass.CentralPms.Infrastructure/PaymentAttempts/TariffSnapshotReadRepository.cs`
- `src/Services/PaymentOrchestrator/src/ExitPass.PaymentOrchestrator.Contracts/WebPay/WebPayPaymentIntentRequest.cs`
- `src/Services/PaymentOrchestrator/src/ExitPass.PaymentOrchestrator.Contracts/WebPay/WebPayPaymentIntentResponse.cs`
- `src/Services/PaymentOrchestrator/src/ExitPass.PaymentOrchestrator.Contracts/WebPay/WebPayParkingSessionResolveResponse.cs`

POS fiscal handoff:

- `src/Services/CentralPms/src/ExitPass.CentralPms.Application/FiscalIssuance/PosServerFiscalDocumentClientModels.cs`
- `src/Services/CentralPms/src/ExitPass.CentralPms.Application/FiscalIssuance/PosServerFiscalDocumentRequestMapper.cs`
- `src/Services/CentralPms/src/ExitPass.CentralPms.Application/FiscalIssuance/FiscalSemanticRequestHashCalculator.cs`
- `src/Services/CentralPms/tests/ExitPass.CentralPms.UnitTests/FiscalIssuance/Fixtures/pos_server_semantic_hash_sha256_v1_representative_fixture.json`

Management Platform and policy import evidence:

- `src/Services/CentralPms/src/ExitPass.CentralPms.Api/Endpoints/ManagementPlatformIdentityRbacInventoryEndpoints.cs`
- `src/Services/CentralPms/src/ExitPass.CentralPms.Api/Endpoints/ManagementPlatformSalesInvoiceProfileAdministrationEndpoints.cs`
- `src/Services/CentralPms/src/ExitPass.CentralPms.Api/Endpoints/OperatorConsoleProductionPolicyImportEndpoints.cs`
- `docs/operator-console/OperatorConsole_Production_Policy_Registry_Readiness_v1.md`
- `docs/operator-console/OperatorConsole_Production_Policy_Registry_Admin_Import_Alignment_v1.md`
- `docs/operator-console/OperatorConsole_Policy_Registry_DB_Baseline_Alignment_Plan_v1.md`

WebPay/readback evidence:

- `src/Services/WebPayUi/src/App.test.tsx`
- `src/Services/CentralPms/tests/ExitPass.CentralPms.IntegrationTests/Api/VendorParkingResolutionApiIntegrationTests.cs`

APT evidence in this repository:

- `docs/v1.3/assisted-payment-terminal/diagrams/D-03_Cashier_Assisted_Payment_with_Statutory_Discount_Validation_Flow.puml`
- `docs/v1.3/assisted-payment-terminal/system-design/diagrams/APT-SD-D06_Statutory_Discount_Capture_and_Payable_Basis_Refresh_Sequence.puml`

Bruno:

- `bruno/operator-console-statutory-discount-draft/139-shared-statutory-decision-submit.bru`
- `bruno/operator-console-statutory-discount-draft/140-shared-statutory-decision-replay.bru`
- `bruno/operator-console-statutory-discount-draft/141-shared-statutory-decision-semantic-conflict.bru`
- `bruno/operator-console-statutory-discount-draft/142-shared-statutory-decision-readback.bru`
- `bruno/operator-console-statutory-discount-draft/143-shared-statutory-decision-unsafe-id-rejected.bru`

Tests:

- `src/Services/CentralPms/tests/ExitPass.CentralPms.UnitTests/Application/StatutoryDiscountDecisionFacadeServiceTests.cs`
- `src/Services/CentralPms/tests/ExitPass.CentralPms.IntegrationTests/Api/StatutoryDiscountDecisionApiAccessPolicyIntegrationTests.cs`
- `src/Services/CentralPms/tests/ExitPass.CentralPms.IntegrationTests/Persistence/StatutoryDiscountDecisionFacadeRepositoryTests.cs`
- `src/Services/CentralPms/tests/ExitPass.CentralPms.IntegrationTests/Api/OperatorConsoleStatutoryDiscountDecisionApiIntegrationTests.cs`
- `src/Services/CentralPms/tests/ExitPass.CentralPms.IntegrationTests/Api/OperatorConsoleStatutoryDiscountApplyPayableBasisApiIntegrationTests.cs`
- `src/Services/CentralPms/tests/ExitPass.CentralPms.IntegrationTests/Api/OperatorConsoleStatutoryDiscountRbacContractIntegrationTests.cs`
- `src/Services/CentralPms/tests/ExitPass.CentralPms.IntegrationTests/Api/VendorParkingResolutionApiIntegrationTests.cs`

STATUTORY_DISCOUNT_HANDOFF_READY
