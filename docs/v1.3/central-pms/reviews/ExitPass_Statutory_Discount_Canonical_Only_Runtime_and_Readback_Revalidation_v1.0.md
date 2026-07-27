# ExitPass Statutory Discount Canonical-Only Runtime and Readback Revalidation v1.0

## 1. Purpose

This report revalidates the merged Central PMS statutory-discount service-channel workflow against the current canonical database generated SQL only. It determines whether the retained shared routes provide durable, channel-safe decision, application, payable-basis, recovery, restart, payment-readiness, and readback facts required before WebPay and Assisted Payment Terminal readiness work can continue.

This is an audit and revalidation report. It does not implement channel-safe readback hardening and does not authorize WebPay or Assisted Payment Terminal integration.

## 2. Repositories and Exact Commits Inspected

| Repository | Path | Branch | Commit |
| --- | --- | --- | --- |
| Application | `D:\SourceCodes\ExitPass-Discounts` | `docs/statutory-discount-canonical-only-runtime-readback-revalidation` from `dev` | `f9de676e639273339f2438b42f552410c53b8a78` |
| Canonical database | `D:\SourceCodes\exitpassdb_v1.2` | `develop` | `636ca9c4b229b1d4e9d517f9251a0d5042950834` |

The canonical database repository was inspected read-only. It contains the statutory-discount canonical promotion in object source and generated SQL.

## 3. Canonical-Only Database Posture

The revalidation used the merged `StatutoryDiscountCanonicalDatabaseFixture` posture:

- Source SQL: `D:\SourceCodes\exitpassdb_v1.2\build\generated\exitpass-full-object.generated.sql`.
- Alignment validator: `D:\SourceCodes\exitpassdb_v1.2\scripts\validation\Validate-V13CentralPmsAlignment.sql`.
- Retired statutory application-local patches were not applied.
- The historical standalone DDL was not used.
- The retired database repository was not used.
- `exitpass_v12_dev` was not modified.
- Disposable database inventory before and after validation showed no leftover `exitpass_statutory_fixture_%` databases.

## 4. Governing Authority Model

The validated authority model remains review-mediated:

1. WebPay or Assisted Payment Terminal submits safe statutory-discount facts.
2. Central PMS creates or resolves one canonical decision-v2 in `AWAITING_REVIEW` / `NOT_DECIDED`.
3. Operator Console discovers and reviews the same canonical decision.
4. Operator Console approves or rejects the decision.
5. A service channel may request payable-basis application only after `COMPLETED` / `APPROVED`.
6. Central PMS owns canonical application-v1 and payable-basis mutation.
7. Payment initiation consumes the effective applied tariff snapshot.

Service channels do not approve entitlement, calculate discounts, determine VAT privilege, finalize payment, issue fiscal documents, issue ExitAuthorization, or control gates.

## 5. Runtime Flow Revalidated

Canonical-only tests prove the normal review-mediated flow for WebPay and APT through intake, Operator Console approval, service-channel application intent, application-v1 `APPLIED`, durable shared GET readback, replay, and payment initiation using the applied snapshot.

The full grouped statutory validation exposed one runtime gap: concurrent service-channel application intent and Operator Console apply convergence can deadlock in PostgreSQL and surface as HTTP 500 instead of a deterministic replay, conflict, or retryable recovery result. This prevents a proceed decision.

## 6. Decision Readback Matrix

| Field | POST | GET | Classification | Notes |
| --- | --- | --- | --- | --- |
| `statutoryDiscountDecisionCommandId` | Present | Present | PRESENT_AND_DURABLE | Canonical command reference. |
| Decision business identity | Not explicit | Not explicit | DERIVABLE_FROM_DURABLE_STATE | `parkingSessionId` plus `entitlementType`; not directly exposed. |
| `parkingSessionId` | Present | Present | PRESENT_AND_DURABLE | Durable command field. |
| `siteId` | Missing | Missing | MISSING_REQUIRED | Durable source exists; shared DTO omits it. |
| `siteGroupId` | Missing | Missing | MISSING_REQUIRED | Durable source exists; shared DTO omits it. |
| `entitlementType` | Present | Present | PRESENT_AND_DURABLE | Durable command field. |
| `sourceChannel` | Present | Present | PRESENT_AND_DURABLE | Server-derived attribution. |
| `decisionCommandStatus` | Present | Present | PRESENT_AND_DURABLE | Includes `AWAITING_REVIEW` and `COMPLETED`. |
| `decisionResultStatus` | Present | Present | PRESENT_AND_DURABLE | Includes `NOT_DECIDED`, `APPROVED`, `REJECTED`. |
| `resultClassification` | Present | Present | PRESENT_AND_DURABLE | Client-safe classification. |
| `decisionRetryable` | Present | Present | PRESENT_AND_DURABLE | Durable status-derived. |
| `decisionRecoveryClassification` | Present | Present | PRESENT_AND_DURABLE | Includes awaiting-review posture. |
| `decisionRecoveryAction` | Present | Present | DERIVABLE_FROM_DURABLE_STATE | Safe action vocabulary. |
| Decision timestamp | Present when decided | Present when decided | PRESENT_AND_DURABLE | `DecidedAt`; pending rows expose creation time. |
| `statutoryDiscountValidationId` | Present after review linkage | Present after review linkage | PRESENT_AND_DURABLE | Required for application and payment proof. |
| `originalTariffSnapshotId` | Present where supplied/resolved | Present | PRESENT_AND_DURABLE | Durable command/application linkage. |
| `safeErrorCode` | Present for safe failures | Present for safe failures | PRESENT_AND_DURABLE | No unsafe payloads observed. |
| `correlationId` | Present | Present | PRESENT_BUT_TRANSIENT | Transport trace, not semantic or business identity. |

## 7. Application Readback Matrix

| Field | POST | GET | Classification | Notes |
| --- | --- | --- | --- | --- |
| `statutoryDiscountPayableBasisApplicationCommandId` | Present after application | Present after application | PRESENT_AND_DURABLE | Canonical application-v1 reference. |
| `applicationRequested` | Present | Present | PRESENT_AND_DURABLE | False for pending/rejected/decision-only states. |
| `applicationCommandStatus` | Present | Present | PRESENT_AND_DURABLE | Includes `NOT_REQUESTED`, `PROCESSING`, `APPLIED`, failures. |
| `applicationResultClassification` | Present | Present | PRESENT_AND_DURABLE | Client-safe application classification. |
| `applicationRetryable` | Present | Present | PRESENT_AND_DURABLE | Status-derived. |
| `applicationRecoveryClassification` | Present | Present | PRESENT_AND_DURABLE | Status-derived. |
| `applicationRecoveryAction` | Present | Present | DERIVABLE_FROM_DURABLE_STATE | Safe action vocabulary. |
| Application timestamp | Present as `appliedAt` | Present as `appliedAt` | PRESENT_AND_DURABLE | No separate completed timestamp in shared DTO. |
| `statutoryDiscountValidationId` | Present | Present | PRESENT_AND_DURABLE | Durable linkage. |
| `originalTariffSnapshotId` | Present | Present | PRESENT_AND_DURABLE | Durable linkage. |
| `appliedTariffSnapshotId` | Present after application | Present after application | PRESENT_AND_DURABLE | Effective applied snapshot reference. |
| Original amount | Present as `grossAmountMinorUnits` | Present | PRESENT_AND_DURABLE | Durable amount projection. |
| VAT treatment facts | Missing | Missing | MISSING_REQUIRED | VAT-exclusive and VAT amount facts are persisted but not exposed. |
| Discount amount | Present | Present | PRESENT_AND_DURABLE | `statutoryDiscountAmountMinorUnits`. |
| Final payable amount | Present | Present | PRESENT_AND_DURABLE | `netPayableAmountMinorUnits`. |
| `currency` | Present | Present | PRESENT_AND_DURABLE | Durable amount currency. |
| `safeErrorCode` | Present for safe failures | Present for safe failures | PRESENT_AND_DURABLE | Safe error vocabulary. |
| `oneShotComplete` | Present | Present | DERIVABLE_FROM_DURABLE_STATE | True only when durable decision/application posture allows it. |
| `correlationId` | Present | Present | PRESENT_BUT_TRANSIENT | Transport trace only. |

## 8. Retry and Recovery Matrix

| State | POST result | GET result | Retryable | Client action | Payment or cash may proceed |
| --- | --- | --- | --- | --- | --- |
| Decision `RECEIVED` / `PROCESSING` | In progress | In progress | True only for technical recovery | Wait and retry/poll original posture | No |
| Decision `AWAITING_REVIEW` / `NOT_DECIDED` | Awaiting review | Awaiting review | False | Poll readback or wait for review | No |
| Decision `COMPLETED` / `APPROVED` | Approved or application path | Approved | False | Application may be requested | No, until application is `APPLIED` |
| Decision `COMPLETED` / `REJECTED` | Non-approved | Rejected | False | Do not retry same facts | No |
| Decision `FAILED_RETRYABLE` | Retryable failure | Retryable failure | True | Retry original key | No |
| Decision `FAILED_NON_RETRYABLE` | Terminal failure | Terminal failure | False | Do not retry unchanged request | No |
| Application `NOT_REQUESTED` | Decision-only or non-applicable | Not requested | False | Request application only after approval | No |
| Application `RECEIVED` / `PROCESSING` | Processing | Processing | True only where original-key recovery applies | Wait and retry/poll original posture | No |
| Application `APPLIED` | Applied | Applied | False | Proceed to payment readiness checks | Payment may proceed; APT cash still needs readiness audit |
| Application `FAILED_RETRYABLE` | Retryable failure | Retryable failure | True | Retry original key | No |
| Application `FAILED_NON_RETRYABLE` | Terminal failure | Terminal failure | False | Do not retry unchanged request | No |

The concurrent service-channel and Operator Console apply path violates the desired recovery posture because PostgreSQL SQLSTATE `40P01` can escape as HTTP 500.

## 9. WebPay Readiness Findings

WebPay can safely submit pending-review intake, poll canonical decision state, distinguish awaiting review from approval and rejection, request application after approval, recover applied state through shared GET, and avoid local discount calculation for final payable amount and discount amount.

WebPay is not ready for controlled UAT because:

- Concurrent service-channel and Operator Console apply convergence can deadlock and return HTTP 500.
- Shared readback does not expose `siteId` and `siteGroupId`.
- Shared readback does not expose explicit VAT-exclusive and VAT amount facts required for a complete channel-safe statutory breakdown.
- A separate readiness authorization audit is still required after runtime and readback gaps are closed.

## 10. APT Readiness Findings

APT can use durable canonical identifiers to submit intake, poll review outcome, request application after approval, recover applied application state, and avoid local authoritative discount calculation.

APT is not ready for controlled UAT or cash-readiness authorization because:

- The same concurrent application convergence deadlock can surface as HTTP 500.
- Restart-safe shared readback omits `siteId`, `siteGroupId`, and explicit VAT treatment facts.
- APT cash acceptance must remain blocked until a later readiness audit proves statutory application is complete, durable, non-conflicted, non-processing, and payable.

## 11. Restart and Polling Proof

The canonical-only integration flow proves restart-style recovery through durable shared GET:

- POST intake creates durable decision-v2.
- GET readback returns `AWAITING_REVIEW` / `NOT_DECIDED`.
- Operator Console approval completes the same decision.
- GET readback returns `COMPLETED` / `APPROVED`.
- POST application intent creates or resolves application-v1.
- GET readback returns application command ID, applied snapshot, final payable amount, currency, and `APPLIED` posture.
- Replay returns the durable result rather than reapplying.

No positive readback proof depends on in-memory endpoint state, browser state, APT-local calculation, or direct seeding of a completed application.

## 12. Concurrency and Replay Proof

Canonical-only grouped validation proves decision replay, cross-channel decision convergence, service-channel application replay, cross-channel application convergence, and duplicate application prevention for the covered paths.

The required concurrent service-channel and Operator Console apply convergence proof failed reproducibly. The failing test is:

`StatutoryDiscountServiceChannelPostApprovalApplicationIntentApiIntegrationTests.ConcurrentServiceChannelAndOperatorConsoleApplicationIntent_CreatesOneApplicationAndOneAppliedSnapshot`

Failure was PostgreSQL SQLSTATE `40P01` deadlock during `PostgresStatutoryDiscountStagedCommandRepository.InsertApplicationAsync`, surfaced through the API as HTTP 500. This is a runtime recovery gap.

## 13. Payment-Initiation Readiness

Payment initiation remains aligned for the successful application flow:

- It reads the effective applied tariff snapshot.
- It uses the Central PMS-approved final payable amount.
- It rejects stale original tariff-snapshot references in focused tests.
- It does not recalculate the statutory discount.
- It does not recalculate VAT privilege.
- It does not apply the discount twice on replay.

Payment initiation must not proceed for `AWAITING_REVIEW`, rejected, processing, conflicted, or terminal failure states.

## 14. Durable Source Trace

| Response fact | Durable source |
| --- | --- |
| Decision command ID/status/result/recovery | `discounts.statutory_discount_decision_commands` |
| Application command ID/status/result/recovery | `discounts.statutory_discount_payable_basis_application_commands` |
| Validation ID and reviewed facts | `discounts.statutory_discount_validations` plus review linkage |
| Service-channel review attribution | `operator_console.statutory_discount_service_channel_reviews` |
| Original tariff snapshot | decision/application command linkage |
| Applied tariff snapshot | application command and applied tariff-snapshot lifecycle |
| Discount and final payable amount | statutory validation/application projection |
| Payment effective snapshot | `TariffSnapshotReadRepository.GetEffectiveAppliedTariffSnapshotAsync` projection |

Completed statutory application readback does not require a HikCentral call to reconstruct the applied discount state.

## 15. Security and Privacy Findings

Shared service-channel responses did not expose raw ID images, Base64 evidence, raw evidence bytes, full statutory IDs, unmasked identity values, Operator Console device or shift identity, permission internals, persistence table identifiers, secret-bearing data, payment-provider payloads, or restricted evidence.

Evidence remains reference-only. Reviewer-sensitive details are not required for WebPay or APT shared readback.

## 16. Reviewer-Attribution Posture

Service-channel readback does not need reviewer identity. The required facts are the canonical decision result, decision timestamp, validation linkage, source-channel attribution, and application state.

Reviewer attribution belongs in Operator Console review records and audit readback. It should remain separate from service-channel source attribution.

## 17. Vendor Dependency Findings

Shared statutory POST/GET readback for completed application state is durable and database-backed. It does not need live vendor parking resolution to return canonical decision/application IDs, statuses, snapshots, discount amount, final payable amount, or currency.

General vendor parking-session freshness, current tariff recalculation, channel-adjacent parking resolution, fiscal readiness, and APT cash-readiness remain outside this revalidation and should be handled in later bounded readiness work.

## 18. Contract and DTO Inventory

Retained public routes:

- `POST /v1/statutory-discounts/decisions`
- `GET /v1/statutory-discounts/decisions/{statutoryDiscountDecisionCommandId}`

Primary DTOs:

- `StatutoryDiscountDecisionRequest`
- `StatutoryDiscountDecisionResponse`
- `StatutoryDiscountDecisionReadbackResponse`
- `StatutoryDiscountDecisionErrorResponse`

The shared response DTO already exposes canonical command IDs, decision/application statuses, retryability, recovery posture, snapshots, gross amount, statutory discount amount, final payable amount, currency, timestamps, safe errors, and one-shot completion.

DTO gaps for complete channel-safe readback:

- Missing `siteId`.
- Missing `siteGroupId`.
- Missing explicit VAT-exclusive amount.
- Missing explicit VAT amount.
- Missing explicit application completed timestamp separate from `appliedAt`, if the channel contract requires that distinction.

## 19. Gap Inventory

| Gap | Classification | Severity | Rollout impact |
| --- | --- | --- | --- |
| Concurrent service-channel and Operator Console apply can deadlock and return HTTP 500 | RETRY_RECOVERY_GAP | CRITICAL | BLOCKS_NEXT_IMPLEMENTATION, BLOCKS_WEBPAY_CONTROLLED_UAT, BLOCKS_APT_CONTROLLED_UAT, BLOCKS_PRODUCTION |
| Shared readback omits `siteId` and `siteGroupId` | PUBLIC_CONTRACT_FIELD_GAP | MEDIUM | BLOCKS_WEBPAY_CONTROLLED_UAT, BLOCKS_APT_CONTROLLED_UAT |
| Shared readback omits explicit VAT-exclusive and VAT amount facts | PUBLIC_CONTRACT_FIELD_GAP | HIGH | BLOCKS_WEBPAY_CONTROLLED_UAT, BLOCKS_APT_CONTROLLED_UAT |
| Contract suite has unrelated environment/provider failures | TEST_COVERAGE_GAP | LOW | DOES_NOT_BLOCK this statutory finding |
| Broad channel-adjacent vendor parking readback not hardened | CHANNEL_SPECIFIC_GAP | MEDIUM | BLOCKS_WEBPAY_CONTROLLED_UAT, BLOCKS_APT_CONTROLLED_UAT |

## 20. Severity and Rollout Impact

The critical blocker is runtime recovery behavior for concurrent service-channel and Operator Console apply convergence. Channel-safe readback hardening should wait until that concurrency path is corrected and revalidated.

No production rollout, controlled WebPay UAT, or controlled APT UAT should proceed from this state.

## 21. Tests and Validation Evidence

Validation performed:

- Central PMS Application build passed.
- Central PMS API build passed in Release.
- Central PMS unit-test build passed.
- Central PMS integration-test build passed.
- Central PMS contract-test build passed.
- Fixture safety tests passed: 5/5.
- Focused statutory/unit/POS/WebPay/payment tests passed: 206/206.
- APT payable-basis contract subset passed: 3/3.
- Retirement guard passed.
- Disposable database inventory before and after validation showed no leftover fixture databases.
- Grouped statutory run with the full target set: 161 passed, 1 failed.
- Focused rerun of the failed concurrency test failed again with the same SQLSTATE `40P01` deadlock.
- Grouped statutory run excluding only the confirmed blocker passed twice: 161/161 and 161/161.
- Controlled parallel-capable grouped run excluding only the confirmed blocker passed: 161/161.

## 22. Known Unrelated Baseline Failures

The full contract-test execution failed 17 of 61 tests in non-statutory areas:

- ExitAuthorization contract tests attempted to call `localhost:8080` and received connection refused.
- Payment outcome contract tests expected `200` but received `409`.

These are recorded as unrelated environment/provider baseline failures for this audit. The contract-test project build passed, and the APT payable-basis contract subset passed.

## 23. Sequencing Decision

`PAUSE_FOR_RUNTIME_REWORK`

The evidence does not support proceeding directly to readback hardening because a proof-grade concurrent application convergence path returns HTTP 500 under canonical-only validation.

## 24. Exact Next Bounded Task

Implement a bounded Central PMS runtime fix for statutory-discount service-channel and Operator Console application-intent concurrency recovery.

Proposed scope:

- Repository: `D:\SourceCodes\ExitPass-Discounts`.
- Base branch: `dev`.
- Proposed branch: `fix/central-pms-statutory-discount-application-intent-concurrency-recovery`.
- Fix deterministic SQLSTATE `40P01` exposure in concurrent service-channel and Operator Console apply convergence.
- Preserve one canonical application-v1, one payable-basis mutation, one applied tariff snapshot, and no duplicate VAT or discount effect.
- Return deterministic replay, conflict, or retryable recovery instead of HTTP 500.
- Do not change public DTOs, statutory formulas, VAT treatment, payment finality, fiscal issuance, ExitAuthorization, gates, WebPay, or APT.
- Re-run the full canonical-only grouped statutory validation, payment-initiation proof, and cleanup proof.

## 25. Tasks That Must Wait

- Channel-safe application readback hardening.
- WebPay/APT readiness re-authorization audit.
- WebPay integration implementation.
- APT desktop integration implementation.
- APT cash-readiness authorization.
- Production rollout readiness.

## 26. Controlled-UAT Impact

WebPay and APT controlled UAT remain blocked by the runtime concurrency/recovery gap and by missing shared readback fields for site context and explicit VAT treatment facts.

## 27. Production-Rollout Impact

Production rollout remains blocked. The current state is not acceptable for production because concurrent application convergence can surface an internal database deadlock as HTTP 500 and because channel-safe durable readback is not yet complete.

## 28. Known Limitations

- This report does not execute live Bruno authenticated scenarios.
- This report does not inspect or modify WebPay, APT, POS Server, or canonical database source.
- Vendor parking-resolution and channel-adjacent readback were inspected only as needed to separate durable statutory readback from live vendor dependencies.
- Contract-test failures outside statutory-discount were not repaired.

## 29. Evidence Appendix

Key files inspected:

- `src/Services/CentralPms/src/ExitPass.CentralPms.Contracts/StatutoryDiscounts/StatutoryDiscountDecisionDtos.cs`
- `src/Services/CentralPms/src/ExitPass.CentralPms.Api/Endpoints/StatutoryDiscountDecisionEndpoints.cs`
- `src/Services/CentralPms/src/ExitPass.CentralPms.Application/StatutoryDiscounts/StatutoryDiscountDecisionFacadeModels.cs`
- `src/Services/CentralPms/src/ExitPass.CentralPms.Infrastructure/StatutoryDiscounts/PostgresStatutoryDiscountStagedCommandRepository.cs`
- `src/Services/CentralPms/src/ExitPass.CentralPms.Infrastructure/StatutoryDiscounts/PostgresStatutoryDiscountServiceChannelReviewRepository.cs`
- `src/Services/CentralPms/src/ExitPass.CentralPms.Infrastructure/PaymentAttempts/TariffSnapshotReadRepository.cs`
- `src/Services/CentralPms/tests/ExitPass.CentralPms.IntegrationTests/Api/StatutoryDiscountServiceChannelPostApprovalApplicationIntentApiIntegrationTests.cs`
- `src/Services/CentralPms/tests/ExitPass.CentralPms.IntegrationTests/Api/OperatorConsoleServiceChannelStatutoryDiscountReviewApiIntegrationTests.cs`
- `src/Services/CentralPms/tests/ExitPass.CentralPms.IntegrationTests/Repositories/StatutoryDiscountStagedCommandRepositoryTests.cs`
- `D:\SourceCodes\exitpassdb_v1.2\build\generated\exitpass-full-object.generated.sql`
- `D:\SourceCodes\exitpassdb_v1.2\scripts\validation\Validate-V13CentralPmsAlignment.sql`

Validation commands are recorded in the completion summary for this branch.

## 30. Final Authorization Lines

WebPay integration: not authorized yet
APT integration: not authorized yet
