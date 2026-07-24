# ExitPass Statutory Discount WebPay APT Integration Readiness Authorization v1.0

## 1. Purpose

This audit determines whether WebPay and Assisted Payment Terminal (APT) statutory-discount source integration may begin against the merged Central PMS shared statutory-discount contract. It is evidence-only and does not authorize runtime, SQL, DTO, RBAC, WebPay, APT, POS Server, Operator Console, Management Platform, legal-rule, VAT, payment-finality, ExitAuthorization, fiscal-issuance, or gate changes.

## 2. Repository and Baseline Commit

| Item | Evidence |
| --- | --- |
| Repository | `D:\SourceCodes\ExitPass-Discounts` |
| Audit branch | `docs/statutory-discount-webpay-apt-integration-readiness-authorization` |
| Base branch | `dev` |
| HEAD | `d016e29e35d66e9d822a23346b5440f4632d49ba` |
| `origin/dev` | `d016e29e35d66e9d822a23346b5440f4632d49ba` |
| Merged baseline evidence | `docs/v1.3/central-pms/implementation-slices/ExitPass_Central_PMS_Statutory_Discount_Staged_Facade_Orchestration_Implementation_Note_v1.0.md`; `docs/v1.3/central-pms/implementation-slices/ExitPass_Central_PMS_Operator_Console_Statutory_Discount_Decision_Convergence_Implementation_Note_v1.0.md`; `docs/v1.3/central-pms/implementation-slices/ExitPass_Central_PMS_Operator_Console_Statutory_Discount_Apply_Convergence_Implementation_Note_v1.0.md` |

## 3. Executive Verdict

WebPay source integration is **not authorized yet**.

APT source integration is **not authorized yet**.

The highest-priority blocker is still the service-channel request contract for a discounted payable basis. Runtime code grants authenticated `WEBPAY` and `ASSISTED_PAYMENT_TERMINAL` submit permissions, but `ValidateChannelFieldMatrix()` rejects non-operator requests that include `applyPayableBasis`, `decision`, reviewer, device, or shift fields. The shared facade only creates `statutory-discount-payable-basis-application:sha256:v1` when `ApplyPayableBasis` is true and the decision is approved. Therefore neither WebPay nor APT can begin implementation against a stable contract for applied payable basis without fabricating Operator Console facts or receiving only a decision-only result.

Operator Console convergence no longer blocks source-integration authorization; both retained legacy decision and apply routes have merged convergence notes and canonical linkage evidence. Database promotion, POS fiscal refinement, live Bruno execution, and privacy retention are later UAT or rollout prerequisites, but the immediate source-integration blocker is the channel-safe shared contract.

## 4. Completed Canonical Milestones

| Milestone | Status | Evidence |
| --- | --- | --- |
| Shared public routes exist | ACHIEVED | `StatutoryDiscountDecisionEndpoints.MapStatutoryDiscountDecisionEndpoints()` in `src/Services/CentralPms/src/ExitPass.CentralPms.Api/Endpoints/StatutoryDiscountDecisionEndpoints.cs` maps `POST /v1/statutory-discounts/decisions` and `GET /v1/statutory-discounts/decisions/{statutoryDiscountDecisionCommandId}`. |
| Supported channel constants exist | ACHIEVED | `StatutoryDiscountSourceChannels` defines `OPERATOR_CONSOLE`, `WEBPAY`, and `ASSISTED_PAYMENT_TERMINAL` in `src/Services/CentralPms/src/ExitPass.CentralPms.Application/StatutoryDiscounts/StatutoryDiscountDecisionFacadeModels.cs`. |
| Staged decision-v2 command exists | ACHIEVED | `IStatutoryDiscountStagedCommandService` and `StatutoryDiscountStagedCommandService` use decision-v2 commands in `src/Services/CentralPms/src/ExitPass.CentralPms.Application/StatutoryDiscounts`. |
| Staged application-v1 command exists | ACHIEVED | `StatutoryDiscountPayableBasisApplicationV1SemanticHash` and application repository tests exist in `src/Services/CentralPms/src/ExitPass.CentralPms.Application/StatutoryDiscounts/StagedStatutoryDiscountCommandModels.cs` and `src/Services/CentralPms/tests/ExitPass.CentralPms.IntegrationTests/Persistence/StatutoryDiscountStagedCommandRepositoryTests.cs`. |
| Shared one-shot orchestration uses staged commands | ACHIEVED | `StatutoryDiscountDecisionFacadeService.SubmitAsync()` creates/resolves decision-v2 and optional application-v1 in `src/Services/CentralPms/src/ExitPass.CentralPms.Application/StatutoryDiscounts/StatutoryDiscountDecisionFacadeService.cs`. |
| Operator Console decision convergence merged | ACHIEVED | `docs/v1.3/central-pms/implementation-slices/ExitPass_Central_PMS_Operator_Console_Statutory_Discount_Decision_Convergence_Implementation_Note_v1.0.md`. |
| Operator Console apply convergence merged | ACHIEVED | `docs/v1.3/central-pms/implementation-slices/ExitPass_Central_PMS_Operator_Console_Statutory_Discount_Apply_Convergence_Implementation_Note_v1.0.md`. |
| Payment initiation consumes effective applied snapshot | ACHIEVED | `CreateOrReusePaymentAttemptHandler` calls `GetEffectiveAppliedTariffSnapshotAsync()` and rejects stale original snapshots in `src/Services/CentralPms/src/ExitPass.CentralPms.Application/PaymentAttempts/CreateOrReusePaymentAttemptHandler.cs`. |

## 5. WebPay Request-Field Matrix

| Field | WebPay classification | Evidence | Authorization impact |
| --- | --- | --- | --- |
| `parkingSessionId` | REQUIRED | DTO field in `StatutoryDiscountDecisionRequest`; decision identity is `statutory-discount-decision:{parkingSessionId}:{entitlementType}`. | Suitable. |
| `siteId` | OPTIONAL / SERVER_DERIVED where available | DTO accepts nullable value. | Suitable if derived from session/site context. |
| `siteGroupId` | OPTIONAL / SERVER_DERIVED where available | DTO accepts nullable value. | Suitable if derived from site context. |
| `entitlementType` | REQUIRED | Service supports only `SENIOR_CITIZEN` and `PWD`. | Suitable within fixed scope. |
| Beneficiary metadata | REQUIRED where current validation requires identity context | Existing DTO has identity metadata fields, not a dedicated beneficiary object. | Needs exact WebPay UX-to-contract mapping before implementation. |
| Masked identity reference | REQUIRED | `MaskedIdReference` is non-nullable and unsafe full-ID values are rejected by shared validation. | Suitable only as masked/reference value. |
| `idDocumentType` | REQUIRED | Non-nullable DTO field. | Suitable if WebPay collects a supported document type. |
| `issuingAuthority` | REQUIRED | Non-nullable DTO field. | Suitable if WebPay collects safe metadata. |
| `expiryDate` | OPTIONAL | Nullable DTO field. | Suitable. |
| `evidenceReferences` | OPTIONAL / REQUIRED by policy posture | Metadata-only `StatutoryDiscountEvidenceReferenceRequest`. | Suitable if WebPay uploads evidence elsewhere and submits references only. |
| `evidence verification outcome` | OPTIONAL / SERVER_DERIVED when reviewed | `VerificationStatus` exists in evidence reference request. | WebPay must not fabricate legal approval; current semantics need stabilization. |
| `actorUserId` | SERVER_DERIVED | Endpoint rejects a non-empty value that does not match authenticated actor. | Suitable if WebPay sends empty or matching service identity. |
| `reviewerUserId` | OPERATOR_CONSOLE_ONLY | Non-operator channel rejected when present. | Suitable as prohibited. |
| Device reference | OPERATOR_CONSOLE_ONLY | `OperatorDeviceBindingId` rejected for WebPay/APT. | Suitable as prohibited. |
| Shift reference | OPERATOR_CONSOLE_ONLY | `OperatorShiftId` rejected for WebPay/APT. | Suitable as prohibited. |
| Attestation facts | REQUIRED where requester attestation remains valid | `RequesterAttestation` is non-nullable; `AttestationNotes` is optional. | Needs WebPay wording/ownership decision before client implementation. |
| Approval or rejection facts | OPERATOR_CONSOLE_ONLY / SERVER_DERIVED | `Decision`, `DecisionReasonCode`, `ReviewerUserId`, and `ReviewerAttestation` are rejected for WebPay. | Blocking if WebPay needs an approved discount through the shared route. |
| `applyPayableBasis` | CURRENTLY PROHIBITED_FOR_CHANNELS; should become channel-safe application intent or server-derived policy | `ValidateChannelFieldMatrix()` includes `body.ApplyPayableBasis` in operator-only fields for WebPay/APT. | Blocks WebPay applied-payable-basis integration. |
| `originalTariffSnapshotId` | OPTIONAL | DTO accepts nullable value; stale-basis checks exist in payment initiation. | Suitable with current session lookup. |
| `requestReference` | TRACEABILITY_ONLY | Required DTO field; excluded from semantic equality by staged design. | Suitable. |
| Correlation ID | TRANSPORT_ONLY | `X-Correlation-Id` header required. | Suitable. |
| Idempotency-Key | TRANSPORT_ONLY | `Idempotency-Key` header required. | Suitable. |
| `sourceChannel` | SERVER_DERIVED with body compatibility check | Endpoint requires body value to match authenticated channel permission. | Suitable as anti-impersonation check, but clients must not rely on body value for authority. |

WebPay does not need to fabricate operator identity, cashier shift, operator device, reviewer identity, reviewer attestation, or manual approval facts. It also cannot currently request an applied discounted payable basis because `applyPayableBasis=true` is rejected for WebPay.

## 6. WebPay Response/Status Vocabulary

| Field or value | Status | Evidence | WebPay impact |
| --- | --- | --- | --- |
| `DecisionCommandStatus` | ACHIEVED | `StatutoryDiscountDecisionResponse` exposes `DecisionCommandStatus`. | Client-safe. |
| `DecisionResultStatus` | ACHIEVED | Response exposes nullable decision result status. | Client-safe. |
| `ApplicationCommandStatus` | ACHIEVED | Response exposes application stage status. | Client-safe. |
| `ApplicationResultClassification` | ACHIEVED | Response exposes application result classification. | Client-safe. |
| `ClientResultStatus` | ACHIEVED | Response exposes stable client status values. | Client-safe. |
| `ResultClassification` and `OverallResultClassification` | ACHIEVED | Response exposes command/result and one-shot classifications. | Client-safe but overlapping vocabulary needs implementation guidance. |
| `Retryable` and stage retryability | PARTIAL | Response exposes `Retryable`, `DecisionRetryable`, and `ApplicationRetryable`. | Needs endpoint mapping verification for every application failure path before WebPay implementation. |
| `RecoveryClassification` and actions | PARTIAL | Response exposes overall and stage recovery fields. | Needs client contract note for WebPay retry behavior. |
| `UNSAFE_IDENTIFIER_REJECTED` | ACHIEVED | Access tests cover unsafe identifier rejection without echoing input. | Client-safe. |
| Not found | ACHIEVED | GET returns `STATUTORY_DISCOUNT_DECISION_NOT_FOUND`. | Client-safe. |
| Historical v1 compatibility | ACHIEVED for readback | Shared service reads staged record then historical repository fallback. | Client-safe if documented as readback-only compatibility. |

The vocabulary is structurally present, but WebPay authorization remains blocked because the successful applied-payable-basis path is not reachable by WebPay under the current field matrix.

## 7. WebPay Payable-Basis and Payment-Initiation Proof

Central PMS exposes post-application payable-basis facts through vendor parking resolution: `StatutoryDiscountValidationId`, `StatutoryDiscountApplicationId`, `StatutoryDiscountDecisionCommandId`, `OriginalTariffSnapshotId`, `EffectiveTariffSnapshotId`, `AppliedTariffSnapshotId`, policy basis, benefit type, entitlement type, discount amount, final payable amount, and decision timestamp in `src/Services/CentralPms/src/ExitPass.CentralPms.Contracts/Public/VendorParking/ResolveVendorParkingResponse.cs`.

Payment initiation consumes the effective applied tariff snapshot without recalculating the statutory discount. `CreateOrReusePaymentAttemptHandler` reads `EffectiveTariffSnapshotResolution` and rejects a stale original tariff snapshot when `AppliedTariffSnapshotId` differs from the submitted tariff snapshot. Tests in `src/Services/CentralPms/tests/ExitPass.CentralPms.IntegrationTests/Api/VendorParkingResolutionApiIntegrationTests.cs` cover WebPay summary readback and applied-snapshot payment attempts.

Current gaps for WebPay are: vendor parking readback does not expose `StatutoryDiscountPayableBasisApplicationCommandId`, application timestamp, explicit decision/application status, retryability, recovery posture, or direct VAT treatment fields. WebPay would not need to recalculate amounts after application, but it cannot currently request application through the shared service-channel contract.

## 8. WebPay Identity and RBAC Proof

`CentralPmsRbacPolicyCatalog` defines `CentralPmsStatutoryDiscountDecisionSubmit` with `statutory-discounts.decision.submit.webpay`. `StatutoryDiscountDecisionEndpoints.TryResolveAuthenticatedSourceChannel()` maps that permission to `WEBPAY`, requires exactly one channel permission, and rejects body/authenticated channel mismatch. `StatutoryDiscountDecisionApiAccessPolicyIntegrationTests` includes WebPay channel authorization, mismatch, prohibited field, and safe readback tests.

Status: ACHIEVED for authentication and anti-impersonation. NOT_YET_ACHIEVED for WebPay applied-payable-basis submission because the same endpoint validation forbids the field needed to create application-v1.

## 9. WebPay Authorization Decision

WebPay integration is **not authorized yet**.

Reason: WebPay can authenticate and can submit decision-only allowed fields, but it cannot start a complete source integration because the shared route rejects `applyPayableBasis=true` for WebPay. A WebPay implementation would either stop at a decision-only state or have to invent an out-of-contract way to request the applied payable basis. That is not a stable channel contract.

## 10. WebPay Controlled-UAT Prerequisites

| Prerequisite | Status |
| --- | --- |
| Channel-safe approval/application intent in shared route | NOT_YET_ACHIEVED |
| WebPay-specific required/prohibited field validation tests | NOT_YET_ACHIEVED |
| Readback exposing application command ID/status/retryability | NOT_YET_ACHIEVED |
| Payment initiation applied-snapshot proof | ACHIEVED |
| POS fiscal finalized-fact population proof for channel path | REQUIRED_BEFORE_CONTROLLED_UAT |
| Canonical database promotion | REQUIRED_BEFORE_CONTROLLED_UAT |
| Live authenticated Bruno/environment validation | REQUIRED_BEFORE_CONTROLLED_UAT |

## 11. WebPay Production Prerequisites

Production rollout additionally requires canonical database promotion, live environment proof, POS fiscal acceptance proof, privacy retention approval, operational monitoring/readback, and explicit confirmation that no unsupported legal-policy behavior is represented to users.

## 12. APT Request-Field Matrix

| Field | APT classification | Evidence | Authorization impact |
| --- | --- | --- | --- |
| `parkingSessionId` | REQUIRED | Shared DTO and business identity. | Suitable. |
| `siteId` / `siteGroupId` | OPTIONAL / SERVER_DERIVED | Nullable DTO fields. | Suitable if terminal resolves site context from authenticated terminal/session. |
| `entitlementType` | REQUIRED | Only `SENIOR_CITIZEN` and `PWD`. | Suitable within fixed scope. |
| Beneficiary metadata | REQUIRED where policy requires | Current DTO uses identity metadata fields. | Needs APT UX-to-contract mapping. |
| Masked identity reference | REQUIRED | Non-nullable DTO; unsafe full-ID rejection exists. | Suitable only as masked/reference value. |
| Document type, issuing authority, expiry date | REQUIRED/OPTIONAL as DTO indicates | `idDocumentType` and `issuingAuthority` are non-nullable; `expiryDate` nullable. | Suitable as safe metadata. |
| Evidence references | OPTIONAL / REQUIRED by policy posture | Metadata-only references. | Suitable if terminal uploads/stores evidence outside shared DTO. |
| Evidence verification outcome | OPTIONAL / SERVER_DERIVED when reviewed | `VerificationStatus` exists. | APT must not fabricate legal approval. |
| Actor/cashier reference | SERVER_DERIVED or channel workflow fact | Endpoint maps actor to authenticated identity and rejects mismatches. | Needs APT service identity model. |
| Terminal reference | SERVER_DERIVED / APT workflow fact | No dedicated APT terminal field exists in shared DTO. | Gap for restart/audit if required by APT. |
| Shift reference | OPERATOR_CONSOLE_ONLY currently | `OperatorShiftId` rejected for APT. | APT cannot submit cashier shift unless contract changes. |
| Requester attestation | REQUIRED where workflow requires | Non-nullable `RequesterAttestation`. | Needs APT attestation meaning. |
| Approval/reviewer facts | OPERATOR_CONSOLE_ONLY | Rejected for non-operator. | Suitable as prohibited, but server-side decision posture must be defined. |
| `applyPayableBasis` | CURRENTLY PROHIBITED_FOR_CHANNELS; should become APT-safe application intent or server-derived policy | Endpoint rejects true value for APT. | Blocks APT cash-acceptance integration. |
| `originalTariffSnapshotId` | OPTIONAL | Nullable DTO field. | Suitable if terminal receives current payable-basis snapshot. |
| `requestReference` | TRACEABILITY_ONLY | Required DTO field. | Suitable. |
| Correlation ID | TRANSPORT_ONLY | Header required. | Suitable. |
| Idempotency-Key | TRANSPORT_ONLY | Header required. | Suitable. |
| `sourceChannel` | SERVER_DERIVED with body compatibility check | APT permission maps to `ASSISTED_PAYMENT_TERMINAL`. | Suitable as anti-impersonation check. |

APT does not need to fabricate reviewer or legal-approval facts, but it also cannot currently request application-v1 or carry terminal-specific workflow facts through a stable service-channel contract.

## 13. APT Response/Status and Restart-Recovery Posture

APT can consume the same shared response DTO vocabulary as WebPay for canonical decision and application state. The response exposes decision command ID, application command ID, stage statuses, retryability, recovery classifications, recovery actions, applied tariff snapshot, and final payable amount when the shared route reaches application.

Restart-recovery gaps remain: vendor parking readback lacks the canonical application command ID, application timestamp, explicit application status, and recovery fields. APT can persist safe IDs locally, but SQLite or desktop persistence must remain non-authoritative. APT implementation should not begin until the channel-safe applied-payable-basis request and restart-readback contract are stabilized.

## 14. APT Cash-Acceptance Posture

APT cash acceptance requires an approved decision, applied payable basis, applied tariff snapshot, final payable amount, currency, application timestamp, and terminal-safe retry status before `CASH_RECEIVED`. Central PMS has the authoritative applied snapshot and payment-initiation proof, but the shared service-channel request currently cannot create that application for APT. APT must not calculate discounts locally or treat desktop state as authoritative.

Status: NOT_YET_ACHIEVED for source integration.

## 15. APT Identity and RBAC Proof

`CentralPmsRbacPolicyCatalog` defines `statutory-discounts.decision.submit.assisted-payment-terminal`; endpoint channel resolution maps it to `ASSISTED_PAYMENT_TERMINAL`, rejects ambiguous channel permission sets, and rejects source-channel mismatches. `StatutoryDiscountDecisionApiAccessPolicyIntegrationTests` covers APT channel permission and prohibited operator-only fields.

Status: ACHIEVED for authentication and anti-impersonation. NOT_YET_ACHIEVED for complete APT source integration because application request/readback remains blocked.

## 16. APT Authorization Decision

APT integration is **not authorized yet**.

Reason: APT cannot safely begin implementation for cash-acceptance statutory discounts until the shared service-channel contract can create or read an applied payable basis without Operator Console reviewer/device/shift fields and without local calculation.

## 17. APT Controlled-UAT Prerequisites

| Prerequisite | Status |
| --- | --- |
| Channel-safe application intent | NOT_YET_ACHIEVED |
| Terminal/workflow fact classification | NOT_YET_ACHIEVED |
| Restart readback with application command ID/status/recovery | NOT_YET_ACHIEVED |
| Proof that cash submission uses approved final amount | REQUIRED_BEFORE_CONTROLLED_UAT |
| POS fiscal finalized-fact population proof | REQUIRED_BEFORE_CONTROLLED_UAT |
| Canonical database promotion | REQUIRED_BEFORE_CONTROLLED_UAT |
| Live authenticated Bruno/environment validation | REQUIRED_BEFORE_CONTROLLED_UAT |

## 18. APT Production Prerequisites

Production rollout additionally requires environment-deployed canonical DB objects, terminal identity provisioning, live authenticated endpoint proof, POS fiscal acceptance proof, privacy retention approval, operational reconciliation, and explicit unsupported-rule guardrails.

## 19. POS Fiscal-Handoff Readiness

Central PMS-held fiscal models carry several finalized statutory-discount facts:

- `StatutoryDiscountDecisionCommandRef`
- `DiscountValidationRef`
- `EntitlementType`
- `AppliedPolicyReferenceRef`
- `OriginalTariffSnapshotRef`
- `AppliedTariffSnapshotRef`
- `OriginalAmountMinorUnits`
- `VatExclusiveBasisAmountMinorUnits`
- `VatTreatment`
- `DiscountAmountMinorUnits`
- `FinalPayableAmountMinorUnits`
- `DecisionTimestamp`
- `SourceChannel`

Evidence: `CentralPmsFiscalDiscountReferenceContext` and `PosServerFiscalDiscountReferenceRequest` in `src/Services/CentralPms/src/ExitPass.CentralPms.Application/FiscalIssuance/PosServerFiscalDocumentClientModels.cs`; mapping and semantic hash inclusion in `PosServerFiscalDocumentRequestMapper` and `FiscalSemanticRequestHashCalculator`; tests in `src/Services/CentralPms/tests/ExitPass.CentralPms.UnitTests/FiscalIssuance/PosServerFiscalDocumentRequestMapperTests.cs`.

| Fact | Status |
| --- | --- |
| Canonical decision command ID | ACHIEVED |
| Statutory validation ID | ACHIEVED |
| Canonical application command ID | REQUIRED_BEFORE_CONTROLLED_UAT; not a typed fiscal field today |
| Entitlement type | ACHIEVED |
| Beneficiary reference | LEGALLY_UNRESOLVED / SAFE_TO_DEFER |
| Evidence reference | SAFE_TO_DEFER; evidence reference can remain in safe context only if legally allowed |
| Approval reference | SAFE_TO_DEFER / current context-only where present |
| Source channel | ACHIEVED |
| Decision timestamp | ACHIEVED |
| Application timestamp | REQUIRED_BEFORE_CONTROLLED_UAT |
| Original basis amount | ACHIEVED |
| VAT treatment facts | PARTIAL; VAT-exclusive basis and treatment exist, direct VAT amount is context-dependent |
| Discount amount | ACHIEVED |
| Final payable amount | ACHIEVED |
| Currency | ACHIEVED through payable-basis context |
| Legal or policy reference | ACHIEVED for applied policy reference, broader legal wording unresolved |
| Fiscal display code/wording | REQUIRED_BEFORE_PRODUCTION_ROLLOUT |

POS Server remains a finalized-fact consumer in repository evidence. No ExitPass-held code moves eligibility or calculation authority to POS Server.

## 20. Database Promotion Posture

Statutory-discount SQL patches present under `infra/db/patches` include:

- `ExitPass_StatutoryDiscountDecisionFacade_v1.3.sql`
- `ExitPass_StatutoryDiscountStagedCanonicalCommands_v1.3.sql`
- `ExitPass_OperatorConsoleStatutoryDiscountDecisionConvergence_v1.3.sql`
- `ExitPass_StatutoryDiscountPayableBasisApplicationSchema_v1.2.sql`
- `ExitPass_StatutoryDiscountAppliedTariffSnapshotLifecycle_v1.2.sql`
- `ExitPass_StatutoryDiscountPolicyRegistrySchema_v1.2.sql`

`infra/db/migrations` contains only `.gitkeep`. The app-local patch retirement manifest states that v1.2 payable-basis application and applied tariff lifecycle patches are `RETIRED_CANONICAL_SUPERSEDED`, while new object changes should be made in `exitpassdb_v1.2` object source first. The newer v1.3 shared facade, staged command, and Operator Console convergence patches are not listed as canonical-superseeded in the manifest evidence inspected for this audit.

Database promotion is not the smallest source-integration blocker, because the shared service-channel contract still cannot produce an applied basis for WebPay/APT. It is required before controlled UAT/environment rollout.

## 21. Privacy-Retention Posture

Current shared DTOs use evidence references and masked identity metadata, not raw image bytes or Base64 evidence payloads. Unsafe full-ID-like values are rejected without echoing submitted values by shared endpoint/service tests. Readback DTOs do not include raw evidence payload fields.

Retention policy remains unresolved. This does not block the next backend contract slice if evidence remains reference-only and no retention period is invented. It does block production rollout.

## 22. Bruno and Authenticated Environment-Validation Posture

Bruno scenarios exist in `bruno/operator-console-statutory-discount-draft`, including shared scenarios `139` through `149` for submit, replay, semantic conflict, readback, unsafe ID rejection, application replay/readback, source-channel mismatch, and legacy canonical readback.

The local `bru` CLI was not available during this audit. Structural inventory is sufficient for this report; live authenticated Bruno execution remains required before controlled UAT, not before the next backend contract-readiness implementation.

## 23. Active Branch and Overlap Inventory

Local branches matching statutory/discount/channel terms:

- `docs/statutory-discount-webpay-apt-integration-readiness-authorization` - current audit branch.
- `feature/central-pms-operator-console-statutory-discount-apply-convergence` - local branch name remains, but apply convergence evidence is merged in `dev`.
- `feature/operator-console-statutory-discount-shared-facade-convergence` - older local convergence branch; do not reuse for channel work.

Remote branches matching statutory/discount/channel terms:

- `origin/feature/apt-terminal-cash-receipt-presentation-parity` - APT cash receipt parity branch name; no merged evidence in this audit that it changes shared statutory-discount contracts.

No active origin WebPay statutory-discount branch or shared DTO branch was found. The local Operator Console branch names should not be used as implementation bases; start from `dev`.

## 24. Legal and Policy Scope

Current supported production behavior remains limited to:

- `SENIOR_CITIZEN`
- `PWD`
- existing fixed national fallback behavior
- existing VAT-exclusive statutory calculation behavior

Unsupported or unresolved for this authorization:

- local ordinance activation
- residency rules
- driver/passenger rules
- initial free parking periods
- full or capped exemptions
- stacking or coupon interaction
- overnight, valet, or standalone-parking exclusions
- multiple beneficiaries
- group transactions
- new fiscal legal wording

Channel authorization must not imply support for unsupported rules.

## 25. Known Limitations

- WebPay/APT cannot currently request `applyPayableBasis=true`.
- WebPay/APT cannot submit approval/reviewer facts, which is correct, but Central PMS has not exposed an alternate service-channel-safe approval/application intent.
- Vendor parking readback lacks canonical application command ID, application timestamp, application status, retryability, and recovery posture.
- POS fiscal handoff lacks a typed canonical application command field and application timestamp.
- Canonical database promotion for newer v1.3 staged/facade/convergence objects is not proven.
- Live authenticated Bruno execution is pending.
- Formal privacy retention is unresolved.

## 26. Exact Recommended Next Task for WebPay, When Authorized

Not authorized yet. The prerequisite before WebPay implementation is:

Task name: **Central PMS statutory-discount service-channel apply/readback contract enablement**

Persona: Codex I

Repository: `D:\SourceCodes\ExitPass-Discounts`

Base branch: `dev`

Proposed branch: `feature/central-pms-statutory-discount-service-channel-apply-readback-contract`

Scope:

- Keep shared routes unchanged.
- Keep WebPay implementation out of scope.
- Make the shared contract allow WebPay service identity to request Central PMS-owned decision/application without Operator Console-only facts.
- Preserve server-derived source channel.
- Keep `decision`, reviewer, device, and shift fields prohibited for WebPay.
- Expose application command ID/status/timestamp/retryability/recovery in channel readback.
- Add WebPay RBAC/contract tests proving no impersonation and no local calculation.

## 27. Exact Recommended Next Task for APT, When Authorized

Not authorized yet. The same backend prerequisite blocks APT before terminal source implementation:

Task name: **Central PMS statutory-discount service-channel apply/readback contract enablement**

Persona: Codex I

Repository: `D:\SourceCodes\ExitPass-Discounts`

Base branch: `dev`

Proposed branch: `feature/central-pms-statutory-discount-service-channel-apply-readback-contract`

APT-specific scope:

- Keep APT repository work out of scope.
- Define which terminal/cashier fields are allowed, prohibited, or server-derived.
- Do not require reviewer/legal-approval facts from APT.
- Expose restart-safe canonical decision/application IDs and status in readback.
- Prove one applied payable basis for APT service-channel requests.

## 28. Owning Persona for Each Authorized Task

No WebPay or APT channel implementation task is authorized by this report. The prerequisite backend contract task should be owned by Codex I.

## 29. Repository, Base Branch, and Proposed Feature Branch

| Task | Repository | Base | Proposed branch |
| --- | --- | --- | --- |
| Backend prerequisite | `D:\SourceCodes\ExitPass-Discounts` | `dev` | `feature/central-pms-statutory-discount-service-channel-apply-readback-contract` |

## 30. Off-Limits Repositories and Files

Off-limits for the next prerequisite:

- `D:\SourceCodes\ExitPass`
- APT repositories
- POS Server repositories
- WebPay UI/runtime behavior
- Operator Console UI
- Management Platform policy implementation
- local ordinance rules or seed data
- VAT calculation behavior
- payment finality
- ExitAuthorization
- fiscal issuance authority
- gates

## 31. Required Validations

For the next prerequisite:

- Central PMS API build.
- Central PMS unit and integration test builds.
- Shared facade WebPay/APT endpoint and RBAC tests.
- Staged-command tests.
- Payable-basis and payment-initiation tests.
- Vendor parking readback contract tests.
- POS fiscal mapper and semantic-hash tests if fiscal fields change.
- Repository/PostgreSQL tests if persistence changes.
- Bruno structural validation, with live authenticated Bruno required before controlled UAT.
- `git diff --check`.

## 32. Final Authorization Lines

WebPay integration: not authorized yet

APT integration: not authorized yet

## 33. Evidence Appendix

### Code Evidence

- `src/Services/CentralPms/src/ExitPass.CentralPms.Api/Endpoints/StatutoryDiscountDecisionEndpoints.cs`: shared route mapping, idempotency/correlation headers, source-channel permission resolution, body/auth channel mismatch rejection, channel field matrix.
- `src/Services/CentralPms/src/ExitPass.CentralPms.Contracts/StatutoryDiscounts/StatutoryDiscountDecisionDtos.cs`: request and response DTO fields.
- `src/Services/CentralPms/src/ExitPass.CentralPms.Application/StatutoryDiscounts/StatutoryDiscountDecisionFacadeModels.cs`: channel constants, status vocabulary, recovery vocabulary, command/result models.
- `src/Services/CentralPms/src/ExitPass.CentralPms.Application/StatutoryDiscounts/StatutoryDiscountDecisionFacadeService.cs`: staged one-shot orchestration and `SENIOR_CITIZEN`/`PWD` support.
- `src/Services/CentralPms/src/ExitPass.CentralPms.Application/StatutoryDiscounts/StatutoryDiscountStagedCommandService.cs`: decision-v2 and application-v1 create/resolve semantics.
- `src/Services/CentralPms/src/ExitPass.CentralPms.Application/PaymentAttempts/CreateOrReusePaymentAttemptHandler.cs`: effective applied snapshot lookup and stale original snapshot rejection.
- `src/Services/CentralPms/src/ExitPass.CentralPms.Infrastructure/PaymentAttempts/TariffSnapshotReadRepository.cs`: applied tariff snapshot readback query.
- `src/Services/CentralPms/src/ExitPass.CentralPms.Contracts/Public/VendorParking/ResolveVendorParkingResponse.cs`: WebPay-adjacent payable-basis response fields.
- `src/Services/CentralPms/src/ExitPass.CentralPms.Application/FiscalIssuance/PosServerFiscalDocumentClientModels.cs`: Central PMS fiscal discount reference model.
- `src/Services/CentralPms/src/ExitPass.CentralPms.Application/FiscalIssuance/PosServerFiscalDocumentRequestMapper.cs`: finalized fact mapping.
- `src/Services/CentralPms/src/ExitPass.CentralPms.Application/FiscalIssuance/FiscalSemanticRequestHashCalculator.cs`: fiscal semantic hash inclusion.
- `src/Services/CentralPms/src/ExitPass.CentralPms.Application/Security/CentralPmsRbacPolicyCatalog.cs`: submit/read permissions.

### Test Evidence

- `src/Services/CentralPms/tests/ExitPass.CentralPms.UnitTests/Application/StatutoryDiscountDecisionFacadeServiceTests.cs`
- `src/Services/CentralPms/tests/ExitPass.CentralPms.UnitTests/Application/StatutoryDiscountStagedCommandServiceTests.cs`
- `src/Services/CentralPms/tests/ExitPass.CentralPms.IntegrationTests/Api/StatutoryDiscountDecisionApiAccessPolicyIntegrationTests.cs`
- `src/Services/CentralPms/tests/ExitPass.CentralPms.IntegrationTests/Api/OperatorConsoleStatutoryDiscountDecisionApiIntegrationTests.cs`
- `src/Services/CentralPms/tests/ExitPass.CentralPms.IntegrationTests/Api/OperatorConsoleStatutoryDiscountApplyPayableBasisApiIntegrationTests.cs`
- `src/Services/CentralPms/tests/ExitPass.CentralPms.IntegrationTests/Api/VendorParkingResolutionApiIntegrationTests.cs`
- `src/Services/CentralPms/tests/ExitPass.CentralPms.IntegrationTests/Persistence/StatutoryDiscountStagedCommandRepositoryTests.cs`
- `src/Services/CentralPms/tests/ExitPass.CentralPms.UnitTests/FiscalIssuance/PosServerFiscalDocumentRequestMapperTests.cs`
- `src/Services/CentralPms/tests/ExitPass.CentralPms.UnitTests/FiscalIssuance/FiscalSemanticRequestHashCalculatorTests.cs`

### SQL and Documentation Evidence

- `infra/db/patches/ExitPass_StatutoryDiscountDecisionFacade_v1.3.sql`
- `infra/db/patches/ExitPass_StatutoryDiscountStagedCanonicalCommands_v1.3.sql`
- `infra/db/patches/ExitPass_OperatorConsoleStatutoryDiscountDecisionConvergence_v1.3.sql`
- `infra/db/patches/validation/Validate_StatutoryDiscountDecisionFacade_v1.3.sql`
- `infra/db/patches/validation/Validate_StatutoryDiscountStagedCanonicalCommands_v1.3.sql`
- `infra/db/patches/validation/Validate_OperatorConsoleStatutoryDiscountDecisionConvergence_v1.3.sql`
- `infra/db/patches/ExitPass_AppLocal_Db_Patch_Retirement_Manifest_v1.0.md`
- `docs/v1.3/central-pms/reviews/ExitPass_Statutory_Discount_Integration_Readiness_and_Thread_Handoff_v1.0.md`
- `docs/v1.3/central-pms/reviews/ExitPass_Statutory_Discount_Staged_Canonical_Command_Design_Decision_v1.0.md`
- `docs/v1.3/central-pms/reviews/ExitPass_Central_PMS_Statutory_Discount_Channel_Contract_Readiness_Audit_v1.0.md`
- `docs/v1.3/central-pms/implementation-slices/ExitPass_Central_PMS_Statutory_Discount_Channel_Contract_Readiness_Implementation_Note_v1.0.md`
- `docs/v1.3/central-pms/implementation-slices/ExitPass_Central_PMS_Statutory_Discount_Staged_Canonical_Commands_Implementation_Note_v1.0.md`
- `docs/v1.3/central-pms/implementation-slices/ExitPass_Central_PMS_Statutory_Discount_Staged_Facade_Orchestration_Implementation_Note_v1.0.md`
- `docs/v1.3/central-pms/implementation-slices/ExitPass_Central_PMS_Operator_Console_Statutory_Discount_Decision_Convergence_Implementation_Note_v1.0.md`
- `docs/v1.3/central-pms/implementation-slices/ExitPass_Central_PMS_Operator_Console_Statutory_Discount_Apply_Convergence_Implementation_Note_v1.0.md`

