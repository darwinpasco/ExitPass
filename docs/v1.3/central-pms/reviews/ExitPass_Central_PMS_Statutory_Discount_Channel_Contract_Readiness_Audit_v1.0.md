# ExitPass Central PMS Statutory Discount Channel Contract Readiness Audit

## 1. Executive Verdict

Verdict: **READY_WITH_BOUNDED_PREREQUISITES**

WebPay integration may start: **No**. Assisted Payment Terminal integration may start: **No**.

Operator Console convergence must happen first: **No**, not before the next backend contract prerequisite. Legacy Operator Console routes remain separately authoritative, so convergence remains required before mixed-channel production rollout, but it is not the smallest blocker to WebPay/APT contract readiness.

Database promotion must happen first: **No**, not before the next backend contract prerequisite. The app-local staged patches are executable and tested, but canonical database promotion remains required before environment/UAT channel enablement.

POS fiscal linkage must happen first: **No**, not before request-contract stabilization. Central PMS fiscal models already contain most safe finalized discount facts, but payable-basis application command linkage and end-to-end population are not fully proven.

Highest-priority blocker: the shared request contract still couples final approval and payable-basis application to Operator Console workflow fields. `WEBPAY` and `ASSISTED_PAYMENT_TERMINAL` identities can authenticate, but the endpoint prohibits operator-only fields including `decision`, reviewer/device/shift fields, and `applyPayableBasis`; without a non-operator-safe server-derived approval/application contract, WebPay/APT cannot produce an approved discounted payable basis through the shared route.

Final authorization: **CHANNEL_CONTRACT_IMPLEMENTATION_AUTHORIZED** for a bounded Central PMS request/status/channel-contract stabilization slice only.

## 2. Repository and Branch Evidence

Repository: `D:\SourceCodes\ExitPass-Discounts`

Branch: `docs/central-pms-statutory-discount-channel-contract-readiness-audit`

HEAD: `db359b7130036750ba137f42ab53cf7cd0a1ce04`

`origin/dev`: `db359b7130036750ba137f42ab53cf7cd0a1ce04`

Merged baseline commit required by task: `683378b61d8a7b844394847056501925bf86263c` is an ancestor of HEAD.

Working tree before report creation: clean.

Active related branches:

| Branch | Scope inferred from name | Unique file overlap with `origin/dev` |
|---|---|---|
| `docs/central-pms-statutory-discount-channel-contract-readiness-audit` | This audit | This report only |
| `feature/central-pms-statutory-discount-staged-facade-orchestration` | Staged facade orchestration | No unique diff; merged into `origin/dev` |
| `feature/operator-console-statutory-discount-shared-facade-convergence` | Operator Console convergence | No unique diff locally |
| `origin/feature/central-pms-statutory-discount-staged-facade-orchestration` | Staged facade orchestration | No unique diff; merged into `origin/dev` |
| `origin/feature/apt-terminal-cash-receipt-presentation-parity` | APT cash receipt parity | No unique diff against `origin/dev` in this clone |

No active branch with unique Central PMS shared statutory-discount request/status/readback/fiscal-linkage changes was found.

## 3. Current Shared Facade Inventory

Routes:

- `POST /v1/statutory-discounts/decisions` in `src/Services/CentralPms/src/ExitPass.CentralPms.Api/Endpoints/StatutoryDiscountDecisionEndpoints.cs:29`
- `GET /v1/statutory-discounts/decisions/{statutoryDiscountDecisionCommandId}` in `src/Services/CentralPms/src/ExitPass.CentralPms.Api/Endpoints/StatutoryDiscountDecisionEndpoints.cs:37`

Contracts:

- `StatutoryDiscountDecisionRequest` in `src/Services/CentralPms/src/ExitPass.CentralPms.Contracts/StatutoryDiscounts/StatutoryDiscountDecisionDtos.cs:6`
- `StatutoryDiscountEvidenceReferenceRequest` in the same file at line 34
- `StatutoryDiscountDecisionResponse` in the same file at line 48

Application services:

- `StatutoryDiscountDecisionFacadeService` in `src/Services/CentralPms/src/ExitPass.CentralPms.Application/StatutoryDiscounts/StatutoryDiscountDecisionFacadeService.cs:10`
- `IStatutoryDiscountStagedCommandService` in `src/Services/CentralPms/src/ExitPass.CentralPms.Application/StatutoryDiscounts/IStatutoryDiscountStagedCommandService.cs:6`
- `StatutoryDiscountStagedCommandService` in `src/Services/CentralPms/src/ExitPass.CentralPms.Application/StatutoryDiscounts/StatutoryDiscountStagedCommandService.cs:6`

Repositories and database objects:

- `PostgresStatutoryDiscountDecisionFacadeRepository` in `src/Services/CentralPms/src/ExitPass.CentralPms.Infrastructure/StatutoryDiscounts/PostgresStatutoryDiscountDecisionFacadeRepository.cs`
- `PostgresStatutoryDiscountStagedCommandRepository` in `src/Services/CentralPms/src/ExitPass.CentralPms.Infrastructure/StatutoryDiscounts/PostgresStatutoryDiscountStagedCommandRepository.cs`
- `discounts.statutory_discount_decision_commands` from `infra/db/patches/ExitPass_StatutoryDiscountDecisionFacade_v1.3.sql:14`
- `discounts.statutory_discount_payable_basis_application_commands` from `infra/db/patches/ExitPass_StatutoryDiscountStagedCanonicalCommands_v1.3.sql:108`

RBAC:

- `CentralPmsStatutoryDiscountDecisionSubmit` maps to operator-console, webpay, assisted-payment-terminal submit permissions in `src/Services/CentralPms/src/ExitPass.CentralPms.Application/Security/CentralPmsRbacPolicyCatalog.cs:141`
- `CentralPmsStatutoryDiscountDecisionRead` maps to `statutory-discounts.decision.read` in the same file at line 150

Tests:

- Shared API/RBAC tests: `src/Services/CentralPms/tests/ExitPass.CentralPms.IntegrationTests/Api/StatutoryDiscountDecisionApiAccessPolicyIntegrationTests.cs`
- Facade orchestration tests: `src/Services/CentralPms/tests/ExitPass.CentralPms.UnitTests/Application/StatutoryDiscountDecisionFacadeServiceTests.cs`
- Staged command service tests: `src/Services/CentralPms/tests/ExitPass.CentralPms.UnitTests/Application/StatutoryDiscountStagedCommandServiceTests.cs`
- Staged repository tests: `src/Services/CentralPms/tests/ExitPass.CentralPms.IntegrationTests/Persistence/StatutoryDiscountStagedCommandRepositoryTests.cs`

Bruno:

- Shared scenarios 139-147 under `bruno/operator-console-statutory-discount-draft/`.

## 4. Authority and Source-of-Truth Assessment

The shared facade owns the canonical shared business identity through decision-v2:

```text
statutory-discount-decision:{parkingSessionId}:{entitlementType}
```

Evidence:

- `StatutoryDiscountDecisionV2SemanticHash.BuildBusinessIdentity()` at `src/Services/CentralPms/src/ExitPass.CentralPms.Application/StatutoryDiscounts/StagedStatutoryDiscountCommandModels.cs:272`
- staged service uses that identity for create/resolve at `src/Services/CentralPms/src/ExitPass.CentralPms.Application/StatutoryDiscounts/StatutoryDiscountStagedCommandService.cs:24`
- repository detects semantic conflict on existing identity in `src/Services/CentralPms/src/ExitPass.CentralPms.Infrastructure/StatutoryDiscounts/PostgresStatutoryDiscountStagedCommandRepository.cs:55`

Different source channels cannot create duplicate shared decisions because source channel is not part of the business identity. Request reference does not create another entitlement because it is not part of decision-v2 business identity or semantic equality. Replay avoids duplicate application through the application-v1 identity:

```text
statutory-discount-payable-basis-application:{statutoryDiscountDecisionCommandId}
```

Evidence: `StatutoryDiscountPayableBasisApplicationV1SemanticHash.BuildBusinessIdentity()` at `StagedStatutoryDiscountCommandModels.cs:436`; application table unique indexes at `infra/db/patches/ExitPass_StatutoryDiscountStagedCanonicalCommands_v1.3.sql:222` and line 225.

Legacy Operator Console decision and apply routes still exist and are separately authoritative:

- decision route maps at `OperatorConsoleStatutoryDiscountDraftEndpoints.cs:90`
- apply route maps at `OperatorConsoleStatutoryDiscountDraftEndpoints.cs:126`
- decision service writes validation state through `OperatorConsoleStatutoryDiscountDecisionService.cs:63`

This does not block the next backend channel-contract prerequisite. It does block a later mixed-channel rollout unless convergence is completed before WebPay/APT production activation.

## 5. Request Field Classification Matrix

| Field | Current requirement | Correct future classification | Source of value | WebPay suitability | APT suitability | Security concern | Recommended action |
|---|---|---|---|---|---|---|---|
| `sourceChannel` | Required in body and must match authenticated permission | SERVER_DERIVED plus body echo/compatibility | Authenticated permission, not caller assertion | Partial | Partial | Body mismatch already rejected at endpoint lines 92-101 | Prefer server-derived canonical value; keep body only as checked compatibility input |
| `requestReference` | Required DTO field | TRACEABILITY_ONLY | Channel/caller reference | Suitable | Suitable | Must not affect uniqueness | Keep transport-only |
| `parkingSessionId` | Required by service validation | REQUIRED_FOR_ALL_CHANNELS | Channel submits resolved Central PMS session | Suitable | Suitable | Must be canonical session reference | Keep required |
| `siteId` | Optional DTO, passed to workflow | OPTIONAL or SERVER_DERIVED when auth/site scope exists | Server/session/site context where possible | Partial | Partial | Caller-supplied site spoofing risk if not cross-checked | Validate against session/site scope |
| `siteGroupId` | Optional DTO, passed to workflow | OPTIONAL or SERVER_DERIVED | Server/site context | Partial | Partial | Same as site | Validate or derive |
| `ticketReference` | Optional DTO | OPTIONAL | Session lookup context | Suitable | Suitable | Display/reference only if session ID is canonical | Keep optional |
| `plateNumber` | Optional DTO | OPTIONAL | Session lookup context | Suitable | Suitable | PII handling/log safety | Keep optional, avoid semantic identity when session ID exists |
| `entitlementType` | Required; only `SENIOR_CITIZEN`/`PWD` | REQUIRED_FOR_ALL_CHANNELS | Channel-selected supported type | Suitable | Suitable | Unsupported values rejected at service line 642 | Keep required |
| `idDocumentType` | Required DTO; no endpoint matrix difference | REQUIRED_FOR_APPROVAL | Channel/operator collected fact | Suitable if evidence policy allows | Suitable if evidence policy allows | Must not imply raw ID storage | Keep but document allowed values |
| `issuingAuthority` | Required DTO | REQUIRED_FOR_APPROVAL | Channel/operator collected fact | Suitable | Suitable | Legal interpretation risk if overused | Keep metadata-only |
| `expiryDate` | Optional | OPTIONAL | Channel/operator collected fact | Suitable | Suitable | None if safe | Keep optional |
| `maskedIdReference` | Required by service line 628 | REQUIRED_FOR_APPROVAL | Masked user/operator fact | Suitable if masked | Suitable if masked | Full-ID-like values rejected at line 648 | Keep masked only |
| `evidenceCaptureRequested` | Required bool | OPTIONAL/REQUIRED_BY_POLICY | Channel/operator fact or policy | Suitable | Suitable | Current contract does not define policy source | Clarify policy-driven semantics |
| `evidenceReferences` | Optional list | REQUIRED_BY_POLICY / OPTIONAL | Metadata-only channel evidence refs | Suitable | Suitable | Storage references appear in request but not readback | Keep references only; no raw payload |
| `actorUserId` | Body field, endpoint overrides with authenticated actor | SERVER_DERIVED | User/service identity | Partial; service ID becomes actor | Partial; service ID becomes actor | Spoof mismatch rejected at endpoint lines 421-429 | Remove from channel responsibility or make optional server-derived |
| `operatorDeviceBindingId` | Optional, prohibited for non-operator | REQUIRED_FOR_OPERATOR_WORKFLOW | Operator Console device context | Not suitable | Not suitable | Operator impersonation | Keep prohibited for WebPay/APT |
| `operatorShiftId` | Optional, prohibited for non-operator | REQUIRED_FOR_OPERATOR_WORKFLOW | Operator Console shift context | Not suitable | Not suitable | Operator impersonation | Keep prohibited for WebPay/APT |
| `requesterAttestation` | Required bool | REQUIRED_FOR_APPROVAL | Claimant/operator attestation | Suitable if semantics defined | Suitable if semantics defined | Attestation role unclear outside operator flow | Define channel-specific meaning |
| `attestationNotes` | Optional | OPTIONAL | Channel/operator note | Suitable with limits | Suitable with limits | Sensitive free text risk | Restrict/log safely |
| `reasonCode` | Optional | OPTIONAL / SAFE_REASON_CODE | Channel/operator reason | Suitable | Suitable | Free-form risk if uncontrolled | Prefer controlled values |
| `decision` | Optional, but approval path needs `APPROVE` for application; prohibited for non-operator by endpoint line 414 | REQUIRED_FOR_OPERATOR_WORKFLOW; SERVER_DECIDED for channels | Central PMS decision, not channel authority | Blocking | Blocking | Channel cannot approve; body decision would be dangerous | Replace channel decision assertion with server-derived decision result |
| `decisionReasonCode` | Optional, prohibited for non-operator | REQUIRED_FOR_OPERATOR_WORKFLOW or SERVER_DERIVED | Central PMS/operator reason | Blocking if required | Blocking if required | Channel reason spoofing | Keep operator-only; server decides channel reason |
| `reviewerUserId` | Optional, prohibited for non-operator | REQUIRED_FOR_OPERATOR_WORKFLOW | Operator reviewer | Not suitable | Not suitable | Reviewer spoofing | Keep prohibited |
| `reviewerAttestation` | Optional bool, prohibited for non-operator when true | REQUIRED_FOR_OPERATOR_WORKFLOW | Operator reviewer | Not suitable | Not suitable | Reviewer spoofing | Keep prohibited |
| `applyPayableBasis` | Optional bool, currently prohibited for non-operator by endpoint line 416 | TRANSPORT_ONLY application intent or SERVER_DERIVED channel policy | Channel intent/server policy | Blocking | Blocking | Current prohibition prevents discounted payable basis | Define non-operator-safe application intent |
| `originalTariffSnapshotId` | Optional | OPTIONAL / SERVER_DERIVED | Current payable-basis context | Suitable if returned from session lookup | Suitable | Stale basis risk | Validate against current effective basis |
| `Idempotency-Key` | Required header | TRANSPORT_ONLY | Client/channel idempotency | Suitable | Suitable | Must not include sensitive data | Keep required |
| `X-Correlation-Id` | Required header | TRACEABILITY_ONLY | Client/channel tracing | Suitable | Suitable | Must not affect semantics | Keep required |

Current fields that prevent WebPay/APT service submission from producing a payable result: `decision`, `decisionReasonCode`, `reviewerUserId`, `reviewerAttestation`, `operatorDeviceBindingId`, `operatorShiftId`, and especially `applyPayableBasis`.

## 6. Status, Result, Error, and Retryability Matrix

| Value | Category | Current source | HTTP mapping | Retryable | Terminal | Client-safe | Recommended disposition |
|---|---|---|---|---|---|---|---|
| `PROCESSING` | command/application state | facade models line 29; application state line 57 | 200 readback or 409 for in-progress errors | Sometimes | No | Yes | Keep but expose stage-specific |
| `ACCEPTED` | one-shot classification | facade models line 40 | 201 when new | No | Contextual | Yes | Keep internal/client-safe |
| `DECISION_ONLY_COMPLETED` | one-shot classification | facade models line 41 | 201/200 | No | Yes | Yes | Keep |
| `DECISION_AND_APPLICATION_COMPLETED` | one-shot classification | facade models line 42 | 201/200 | No | Yes | Yes | Keep |
| `DECISION_COMPLETED_APPLICATION_PROCESSING` | one-shot classification | facade models line 43 | 201/200 with `oneShotComplete=false` | Depends on stage | No | Yes | Keep |
| `IDEMPOTENT_REPLAY` | result classification | endpoint line 119 and model line 44 | 200 | No | Yes | Yes | Keep |
| `IDEMPOTENCY_SEMANTIC_CONFLICT` | error code | endpoint conflict map line 317 | 409 | No | Yes | Yes | Keep as legacy/v1 conflict |
| `STATUTORY_DISCOUNT_DECISION_SEMANTIC_CONFLICT` | error code | endpoint lines 318 and 577 | 409 | No | Yes | Yes | Keep |
| `STATUTORY_DISCOUNT_PAYABLE_BASIS_APPLICATION_SEMANTIC_CONFLICT` | error code | endpoint lines 319 and 578 | 409 | No | Yes | Yes | Keep |
| `STATUTORY_DISCOUNT_DECISION_IN_PROGRESS` | error code | endpoint lines 320 and 579 | 409 | Not encoded as retryable in `BuildError` | No | Yes | Needs explicit retryability |
| `STATUTORY_DISCOUNT_PAYABLE_BASIS_APPLICATION_IN_PROGRESS` | error code | endpoint lines 321 and 580 | 409 | Not encoded as retryable in `BuildError` | No | Yes | Needs explicit retryability |
| `APPROVED` | business-decision status | endpoint line 495; decision states line 24 | 200/201 response | No | Yes | Yes | Keep |
| `REJECTED` | business-decision status | endpoint line 496; decision states line 25 | 200/201 response | No | Yes | Yes | Keep |
| `APPLIED_PAYABLE_BASIS` | response decision status | endpoint line 495; mapping result | 200/201 response | No | Yes for discount application only | Yes | Keep distinct from payment finality |
| `STATUTORY_DISCOUNT_DECISION_NOT_FOUND` | error code | endpoint lines 169 and 581 | 404 | No | Yes | Yes | Keep |
| `UNSAFE_IDENTIFIER_REJECTED` | error code | service line 648; endpoint line 582 | 400 | No | Yes | Yes | Keep |
| `UNSUPPORTED_SOURCE_CHANNEL` | error code | endpoint line 75 | 400 | No | Yes | Yes | Keep |
| `UNSUPPORTED_ENTITLEMENT_TYPE` | error code | service line 642; endpoint line 584 | 400 | No | Yes | Yes | Keep |
| `STATUTORY_DISCOUNT_DECISION_TEMPORARILY_UNAVAILABLE` | error code | endpoint line 583 | 400 in generic catch path | Should be retryable | No | Yes | Correct HTTP/retryability mapping |

The response can represent recoverable in-progress state through `oneShotComplete=false`, stage-specific retryability fields, and `clientResultStatus=IN_PROGRESS`. However, `BuildError()` currently sets `Retryable = false` for all shared error responses at `StatutoryDiscountDecisionEndpoints.cs:565`, so in-progress and temporary-unavailable error responses do not carry reliable retryability. WebPay/APT should not have to infer retryability from free-text or error code names.

## 7. Authenticated Service and Channel Identity Findings

Authenticated identity types:

- User identity from `X-ExitPass-User-Id` or user claims.
- Service identity from `X-ExitPass-Service-Identity-Id` or service/client claims.

Evidence: `ResolveActorId()` in `StatutoryDiscountDecisionEndpoints.cs:449`.

Submit permissions:

- `statutory-discounts.decision.submit.operator-console`
- `statutory-discounts.decision.submit.webpay`
- `statutory-discounts.decision.submit.assisted-payment-terminal`

Evidence: channel permission dictionary in `StatutoryDiscountDecisionEndpoints.cs:469`.

Source channel binding is verified, not blindly trusted. The endpoint resolves the effective source channel from permissions and rejects body mismatch with `CENTRAL_PMS_SOURCE_CHANNEL_MISMATCH` at `StatutoryDiscountDecisionEndpoints.cs:92`.

Actor identity is server-derived in `ToCommand()` and the body `ActorUserId` is rejected when it does not match the authenticated actor at `StatutoryDiscountDecisionEndpoints.cs:421`.

Remaining issue: the same canonical request DTO still exposes operator-only fields to all callers and uses a prohibition matrix rather than a clear per-channel contract. WebPay/APT cannot send operator-only fields, but the current application path still depends on operator-style approval/application inputs for a complete discounted payable result.

## 8. Payable-Basis Readback Findings

The shared response includes safe canonical readback fields:

- `StatutoryDiscountDecisionCommandId`
- `StatutoryDiscountValidationId`
- `OriginalTariffSnapshotId`
- `AppliedTariffSnapshotId`
- `GrossAmountMinorUnits`
- `StatutoryDiscountAmountMinorUnits`
- `NetPayableAmountMinorUnits`
- `Currency`
- `AppliedAt`
- `DecidedAt`
- application command ID/status fields

Evidence: `StatutoryDiscountDecisionResponse` in `StatutoryDiscountDecisionDtos.cs:48`.

Channels can obtain the main amount and linkage facts after an approved/application result. They cannot yet rely on the shared route to produce those facts for WebPay/APT because non-operator channels cannot request `applyPayableBasis` and cannot submit `decision=APPROVE`.

VAT treatment is partially represented: amount fields include gross, VAT-exclusive, VAT amount in staged decision/application records, and fiscal model has `VatExclusiveBasisAmountMinorUnits` and `VatTreatment`; the shared response does not expose `VatAmountMinorUnits` or a canonical `VatTreatment` field directly.

## 9. Payment-Initiation Consumption Findings

Payment initiation consumes effective applied tariff snapshot state and rejects stale submitted basis.

Evidence:

- `CreateOrReusePaymentAttemptHandler` reads `GetEffectiveAppliedTariffSnapshotAsync()` at `src/Services/CentralPms/src/ExitPass.CentralPms.Application/PaymentAttempts/CreateOrReusePaymentAttemptHandler.cs:259`
- rejects invalid effective applied basis at line 264
- rejects stale original tariff snapshot when an applied snapshot exists at line 279
- `TariffSnapshotReadRepository` maps statutory application/decision/validation linkage at `src/Services/CentralPms/src/ExitPass.CentralPms.Infrastructure/PaymentAttempts/TariffSnapshotReadRepository.cs:283`

WebPay/APT should not need to recalculate amounts once the shared route has produced an applied tariff snapshot. The blocker is not payment initiation; it is making the shared channel command capable of producing and exposing the applied basis for non-operator channels.

## 10. POS Fiscal Handoff Findings

Central PMS-held fiscal models already support several finalized statutory-discount facts:

- `StatutoryDiscountDecisionCommandRef`
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

Evidence: `CentralPmsFiscalDiscountReferenceContext` in `PosServerFiscalDocumentClientModels.cs:55`; mapper copies those fields at `PosServerFiscalDocumentRequestMapper.cs:60`; fiscal semantic hash includes those fields at `FiscalSemanticRequestHashCalculator.cs:156`.

Missing or not directly proven:

| Fact | Current posture | Classification |
|---|---|---|
| `statutoryDiscountDecisionCommandId` | Supported as `StatutoryDiscountDecisionCommandRef` | REQUIRED and present in Central PMS model |
| `statutoryDiscountValidationId` | Supported as `DiscountValidationRef` | REQUIRED and present |
| `payableBasisApplicationId` / application command ID | No explicit field in discount reference model | REQUIRED_BEFORE_CHANNEL_INTEGRATION |
| `sourceChannel` | Present | REQUIRED and present |
| decision timestamp | Present | REQUIRED and present |
| beneficiary reference | Present only in `DiscountPrivilegeDetails.BeneficiaryRef` | SAFE_TO_DEFER if legally restricted |
| evidence reference | Present only in `DiscountPrivilegeDetails.EvidenceRef` | SAFE_TO_DEFER if legally restricted |
| approval reference | Present as `ApprovalRef` | REQUIRED if available |
| legal/policy reference | Present as `AppliedPolicyReferenceRef` | REQUIRED and present |
| fiscal wording/code | Discount privilege type code exists; exact wording not proven here | POS_SERVER_INTERNAL_ONLY / SAFE_TO_DEFER |

Adding safe application-command linkage to Central PMS fiscal models and semantic hash appears sufficient inside this repository, but any POS Server wire-contract acceptance cannot be proven without external repository inspection. External POS Server inspection was not needed for this audit because the blocker can be classified from ExitPass-held Central PMS models.

## 11. Legacy Operator Console Route Findings

Legacy routes remain:

- `POST /v1/ops/operator-console/statutory-discounts/draft`
- `POST /v1/ops/operator-console/statutory-discounts/{draftId}/decision`
- `POST /v1/ops/operator-console/statutory-discounts/{draftId}/evidence`
- `POST /v1/ops/operator-console/statutory-discounts/{validationId}/apply-payable-basis`

Evidence: route mappings in `OperatorConsoleStatutoryDiscountDraftEndpoints.cs:78`, `:90`, `:103`, and `:126`.

These routes still call Operator Console services directly. They are not converged onto the shared staged command path. This is a later cross-route consistency risk, not the smallest blocker to WebPay/APT channel contract readiness.

## 12. Database Baseline Findings

Current app-local patches:

- `infra/db/patches/ExitPass_StatutoryDiscountDecisionFacade_v1.3.sql`
- `infra/db/patches/ExitPass_StatutoryDiscountStagedCanonicalCommands_v1.3.sql`

Validation SQL:

- `infra/db/patches/validation/Validate_StatutoryDiscountDecisionFacade_v1.3.sql`
- `infra/db/patches/validation/Validate_StatutoryDiscountStagedCanonicalCommands_v1.3.sql`

The staged patch adds `business_identity`, permits decision hash source versions v1/v2, and creates the application command table. Evidence: staged patch lines 16, 43, and 108. Unique indexes enforce business identity and application-per-decision at lines 222 and 225.

The patch retirement manifest defines canonical coverage rules and shows app-local patches can be classified as active, retired, or partially superseded. Evidence: `infra/db/patches/ExitPass_AppLocal_Db_Patch_Retirement_Manifest_v1.0.md:20`.

Finding: canonical promotion should not be the immediate next task because the immediate channel blocker is DTO/field semantics. Database promotion remains required before environment integration/UAT, especially if clean database rebuilds do not automatically include these app-local patches.

## 13. Privacy and Evidence Findings

No raw evidence/image/full-ID fields are exposed by the shared DTO property names. Evidence: `SharedDecisionContracts_DoNotExposeRawEvidenceOrFullStatutoryIdFields` in `StatutoryDiscountDecisionApiAccessPolicyIntegrationTests.cs:146`.

Unsafe full-ID-like values are rejected without echoing the submitted value. Evidence: endpoint test `Submit_WhenUnsafeIdentifierIsSent_ReturnsBadRequestWithoutEchoingValue` at `StatutoryDiscountDecisionApiAccessPolicyIntegrationTests.cs:223`; service rejection at `StatutoryDiscountDecisionFacadeService.cs:648`.

Readback does not expose evidence payload fields. Evidence: `Read_WhenAuthorized_ReturnsSafeCanonicalResponseWithoutEvidencePayload` at `StatutoryDiscountDecisionApiAccessPolicyIntegrationTests.cs:136`.

Retention posture remains unresolved. This does not block the next contract slice if the contract continues to use evidence references only and avoids retention commitments.

## 14. Legal and Policy Scope Findings

Current runtime scope is fixed to:

- `SENIOR_CITIZEN`
- `PWD`

Evidence: supported entitlement set in `StatutoryDiscountDecisionFacadeService.cs:12`; unsupported entitlement rejection at line 642; staged command validation at `StatutoryDiscountStagedCommandService.cs:445`.

No production behavior was found in the shared facade for local ordinances, residency requirements, driver/passenger requirements, initial free periods, exemptions, overnight/valet/standalone exclusions, stacking, multiple beneficiaries, or group allocation. Existing documentation also states those remain unsupported/deferred, for example `ExitPass_Central_PMS_Statutory_Discount_Staged_Facade_Orchestration_Implementation_Note_v1.0.md:183`.

The future client contract can be stabilized without representing unsupported rules as long as it explicitly limits entitlement type and policy output to the current fixed scope.

## 15. Active Branch and File-Overlap Findings

No active related local/origin branch has unique file diffs against `origin/dev` in this clone. The old staged façade branch is merged. The local Operator Console convergence branch has no unique diff. The origin APT cash receipt branch has no unique diff against this `origin/dev`.

No branch overlap blocks the next backend contract implementation.

## 16. Blocking Findings

### Critical

None.

### High

Finding: WebPay/APT cannot produce an approved discounted payable basis through the shared route.

Impact: Channel integration would either fail to apply the discount or require channel-local approval/calculation, violating authority boundaries.

Evidence: non-operator channels are permitted by RBAC tests, but `ValidateChannelFieldMatrix()` prohibits `decision`, reviewer/device/shift fields, and `applyPayableBasis` for `WEBPAY` and `ASSISTED_PAYMENT_TERMINAL` at `StatutoryDiscountDecisionEndpoints.cs:403`; the facade only creates application-v1 when `ApplyPayableBasis` is true and the decision is approved at `StatutoryDiscountDecisionFacadeService.cs:62`; service validation rejects `applyPayableBasis` unless `decision == APPROVE` at line 662.

Affected files/components: shared DTO, endpoint field matrix, facade command normalization/orchestration.

Blocks WebPay: Yes. Blocks APT: Yes.

Smallest correction: stabilize the shared channel request contract so WebPay/APT submit eligibility/evidence facts and a safe application intent while Central PMS derives the decision and application authority.

Finding: Retryability in error envelopes is not reliable for in-progress/temporary unavailable shared errors.

Impact: WebPay/APT clients would need to infer retry behavior from error codes.

Evidence: `BuildError()` sets `Retryable = false` for all errors at `StatutoryDiscountDecisionEndpoints.cs:565`; in-progress and temporary-unavailable error codes map to client statuses at lines 579-583.

Affected files/components: shared endpoint error mapping.

Blocks WebPay: Yes for robust client integration. Blocks APT: Yes.

Smallest correction: add explicit retryability/recovery mapping per shared error code.

### Medium

Finding: Shared response exposes main payable-basis amounts but omits direct `VatAmountMinorUnits` and explicit canonical `VatTreatment`.

Impact: WebPay/APT may not have all VAT display facts without inference or fiscal-model knowledge.

Evidence: `StatutoryDiscountDecisionResponse` fields at `StatutoryDiscountDecisionDtos.cs:48`; fiscal model has `VatExclusiveBasisAmountMinorUnits` and `VatTreatment` at `PosServerFiscalDocumentClientModels.cs:55`.

Blocks WebPay: Partial. Blocks APT: Partial.

Smallest correction: expose only currently stored safe VAT treatment facts grounded in decision/application records.

Finding: POS fiscal handoff lacks explicit payable-basis application command linkage.

Impact: Fiscal semantic hash can include decision and discount facts but cannot identify application-v1 directly unless carried through reference context.

Evidence: `CentralPmsFiscalDiscountReferenceContext` has decision command ref but no application command ref at `PosServerFiscalDocumentClientModels.cs:55`; semantic hash writes decision command ref at `FiscalSemanticRequestHashCalculator.cs:156`.

Blocks WebPay: No. Blocks APT: No. Blocks fiscal completeness before rollout: Yes.

Smallest correction: add Central PMS-held application command reference into fiscal discount reference context and semantic hash.

Finding: App-local DB patch promotion is not proven canonical.

Impact: clean environment rebuilds may miss shared façade/staged command objects unless patch replay is part of deployment.

Evidence: active patch files under `infra/db/patches`; canonical promotion/retirement policy in `ExitPass_AppLocal_Db_Patch_Retirement_Manifest_v1.0.md:20`.

Blocks WebPay: Not for source implementation. Blocks APT: Not for source implementation. Blocks UAT/deployment: Yes.

Smallest correction: separate canonical database promotion proof after contract stabilization or before environment testing.

### Low

Finding: Operator Console legacy convergence remains undone.

Impact: cross-route uniqueness between legacy Operator Console and shared channels is not yet unified.

Evidence: legacy route mappings at `OperatorConsoleStatutoryDiscountDraftEndpoints.cs:90` and `:126`.

Blocks WebPay/APT contract implementation: No. Blocks production mixed-channel rollout: Yes.

### Informational

Management Platform policy configuration is not required for current fixed Senior Citizen/PWD behavior. Local ordinance and expanded policy support remain out of scope.

## 17. Exact Recommended Next Bounded Task

Task name: **Central PMS statutory-discount shared channel request/status contract stabilization**

Persona: Codex I

Repository: `D:\SourceCodes\ExitPass-Discounts`

Base branch: `dev`

Proposed feature branch: `feature/central-pms-statutory-discount-shared-channel-contract-stabilization`

Exact scope:

- Stabilize the shared request field matrix for `OPERATOR_CONSOLE`, `WEBPAY`, and `ASSISTED_PAYMENT_TERMINAL`.
- Keep source channel server-derived and body mismatch rejected.
- Make WebPay/APT able to submit facts without operator-only fields.
- Define a safe non-operator application intent or server-derived application policy without granting channels approval/calculation authority.
- Preserve current `statutory-discount-decision:{parkingSessionId}:{entitlementType}` identity.
- Preserve current fixed `SENIOR_CITIZEN`/`PWD` behavior.
- Add explicit retryability/recovery mappings for in-progress and temporary-unavailable shared errors.
- Expose only grounded VAT/payable-basis readback facts needed by channels.
- Add focused tests for WebPay/APT happy-path contract shape using shared service fakes and current staged behavior.

Expected file areas:

- `src/Services/CentralPms/src/ExitPass.CentralPms.Contracts/StatutoryDiscounts/`
- `src/Services/CentralPms/src/ExitPass.CentralPms.Api/Endpoints/StatutoryDiscountDecisionEndpoints.cs`
- `src/Services/CentralPms/src/ExitPass.CentralPms.Application/StatutoryDiscounts/`
- focused Central PMS unit/integration tests
- existing Bruno shared statutory-discount scenarios only if request shape changes
- one implementation note under `docs/v1.3/central-pms/implementation-slices/`

Off-limits areas:

- Operator Console UI and legacy route convergence
- WebPay UI/runtime integration
- APT repositories
- POS Server repository
- Management Platform policy implementation
- statutory calculation/VAT behavior changes
- local ordinance rules or seed data
- payment finality, fiscal issuance orchestration, ExitAuthorization, HikCentral calls, payment-provider calls, gate behavior

Required tests:

- shared endpoint/RBAC tests for operator, WebPay service, APT service
- request field matrix tests
- source-channel mismatch/impersonation tests
- WebPay/APT no-operator-field submission tests
- status/retryability error-envelope tests
- focused facade tests for decision/application intent behavior
- existing staged command tests
- existing payable-basis/payment-initiation tests
- existing POS mapper/hash tests if response/fiscal fields change
- `git diff --check`

Completion criteria:

- WebPay/APT clients can build a request without operator-only fields.
- Central PMS remains the only authority for decision and payable-basis application.
- Response status/retryability is explicit and client-safe.
- No channel-local calculation/approval is introduced.
- No unrelated runtime authority changes occur.

## 18. Deferred Tasks

| Task | In next slice? | Reason |
|---|---:|---|
| WebPay integration | No | Client contract is not ready |
| APT integration | No | Client contract is not ready |
| Operator Console convergence | No | Separate cross-route convergence task |
| Canonical database promotion | No | Needed before environment/UAT, but not highest source-contract blocker |
| POS Server contract changes | No | External contract not inspected/changed; Central PMS fiscal model linkage can be handled after request contract |
| Bruno execution | No | Live environment validation, not source prerequisite |
| Management Platform | No | Not required for fixed Senior/PWD scope |
| Local ordinance support | No | Out of legal/policy scope |
| Privacy retention policy | No | Keep metadata-only evidence; retention remains unresolved |

## 19. Final Authorization

**CHANNEL_CONTRACT_IMPLEMENTATION_AUTHORIZED**

Authorization is limited to the bounded Central PMS shared channel request/status contract stabilization task defined in section 17. WebPay integration and APT integration remain unauthorized until that prerequisite is complete and validated.
