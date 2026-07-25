# ExitPass Statutory Discount Canonical Database Baseline Gap and Rework Audit

## 1. Purpose

This docs-only audit reviews completed and merged Codex I statutory-discount work against the current canonical database source. The immediate goal is to decide whether statutory-discount implementation may continue, or whether work must pause for canonical database baseline rework.

The audit compares:

- Application repository: `D:\SourceCodes\ExitPass-Discounts`
- Current canonical database repository: `D:\SourceCodes\exitpassdb_v1.2`
- Historical database repository, read-only evidence only: `D:\SourceCodes\ExitPass_DBv1.2`
- Historical standalone artifact, evidence only: `ExitPass_Full_Database_Creation_DDL_v1.2.sql`

No runtime, API, SQL, test, Bruno, WebPay, APT, Operator Console, POS Server, Management Platform, or canonical database source files were changed.

## 2. Repositories and Exact Commits Inspected

| Repository | Branch | Commit | Status |
| --- | --- | --- | --- |
| `D:\SourceCodes\ExitPass-Discounts` | `docs/statutory-discount-canonical-db-baseline-gap-rework-audit` | `154291dcf4a33438937093b2557defc47d512bd7` | Clean before report creation; aligned with `origin/dev` |
| `D:\SourceCodes\exitpassdb_v1.2` | `develop` | `32cc5174d507f4a75414a6fb88e983fb1d63aca1` | Clean; aligned with `origin/develop` |
| `D:\SourceCodes\ExitPass_DBv1.2` | `develop` | `456bfa0b423b3e34993a2a2c31c065b7b1347a06` | Clean; historical evidence only |

Application `origin/dev` incorporated: `154291dcf4a33438937093b2557defc47d512bd7`.

## 3. Current Canonical Database Authority

The current canonical database source is `D:\SourceCodes\exitpassdb_v1.2`.

Authoritative executable output:

- `build/generated/exitpass-full-object.generated.sql`

Supporting evidence:

- `objects/exitpass-full-object-apply-order.txt`
- `scripts/validation/Validate-ExitPassFullObjectSourceLayout.ps1`
- `scripts/validation/Validate-V13CentralPmsAlignment.sql`
- `build/generated/v13-central-pms-alignment.generated.sql`

Object-source layout validation passed with:

- schema: 17
- types: 157
- tables: 102
- constraints: 44
- indexes: 456
- functions: 16
- triggers: 2
- comments: 1729
- reference-data: 3
- uat: 4
- extensions: 1
- total: 2531

The retired `D:\SourceCodes\ExitPass_DBv1.2` repository and `ExitPass_Full_Database_Creation_DDL_v1.2.sql` are not current executable database authority.

## 4. Historical Database-Source Context

The historical DB repository remains useful for trace evidence. Its recent SQL history includes:

- `456bfa0 Align generated vendor acknowledgment schema artifacts`
- `f02c5f5 Add vendor payment acknowledgment schema`
- `8604488 #255 Add statutory discount policy registry baseline`

The current canonical database repository also contains `integration.vendor_payment_acknowledgments` in current object source and generated SQL. That object is not the statutory-discount gap found here.

The statutory-discount gap is narrower: newer staged statutory-discount command and service-channel review-linkage objects exist in application-local patches and application runtime expectations, but not yet in canonical `exitpassdb_v1.2` object source or generated full SQL.

## 5. Executive Verdict

The merged statutory-discount application behavior is not invalidated, but its clean database reproducibility is not canonical-only.

Current result:

- Current runtime is correct only when current canonical generated SQL is combined with the active application-local statutory patch chain.
- Current canonical generated SQL alone does not contain the staged decision-v2, application-v1 command, AWAITING_REVIEW, validation-linkage, or service-channel review-linkage objects required by merged runtime.
- Prior validations against `exitpass_v12_dev` or app-local patches remain useful behavior evidence, but they are not proof of canonical database release reproducibility.

Highest-priority blocker:

- Promote the active statutory-discount staged command, review-linkage, pending-review, validation-linkage, and related constraints/indexes/comments into the canonical database repository before further statutory-discount implementation proceeds.

## 6. Sequencing Verdict

PAUSE_FOR_CANONICAL_DATABASE_REWORK

Evidence:

- Canonical-only disposable DB built from `exitpassdb_v1.2\build\generated\exitpass-full-object.generated.sql` passed Central PMS alignment validation, but object checks found no `discounts.statutory_discount_decision_commands`, no `discounts.statutory_discount_payable_basis_application_commands`, no `operator_console.statutory_discount_service_channel_reviews`, and no decision-convergence metadata columns on `discounts.statutory_discount_validations`.
- Canonical-plus-active-app-local-patches disposable DB passed the statutory patch validators and focused real review-mediated post-approval application tests.
- This means the application behavior can work, but not from canonical database source alone.

Channel-safe readback hardening and any further WebPay/APT readiness work must wait until the canonical database workstream closes this reproducibility gap or explicitly provides a controlled handoff branch with the missing objects.

## 7. Complete Codex I Task Inventory

| # | Task | Evidence | DB assumption observed |
| --- | --- | --- | --- |
| 1 | Statutory-discount system-wide baseline audit | `docs/v1.3/operator-console/reviews/ExitPass_Statutory_Discount_System_Wide_Baseline_Audit_v1.0.md` | Historical v1.2 DDL explicitly treated as stale/superseded for runtime migration posture |
| 2 | Shared Central PMS statutory-discount facade | `docs/v1.3/central-pms/implementation-slices/ExitPass_Central_PMS_Shared_Statutory_Discount_Decision_Facade_Implementation_Note_v1.0.md`; commit `e59ce71` | App-local decision facade patch |
| 3 | Integration-readiness and thread handoff audit | `docs/v1.3/central-pms/reviews/ExitPass_Statutory_Discount_Integration_Readiness_and_Thread_Handoff_v1.0.md`; commit `ac9388a` | App-local/cumulative runtime evidence |
| 4 | Channel-contract readiness | `docs/v1.3/central-pms/implementation-slices/ExitPass_Central_PMS_Statutory_Discount_Channel_Contract_Readiness_Implementation_Note_v1.0.md`; commit `4275810` | Runtime/DTO work; DB proof depends on later patches |
| 5 | Staged canonical command design | `docs/v1.3/central-pms/reviews/ExitPass_Statutory_Discount_Staged_Canonical_Command_Design_Decision_v1.0.md`; commit `7a54ac3` | Design approved staged command objects not yet canonical |
| 6 | Staged canonical decision-v2 and application-v1 commands | `docs/v1.3/central-pms/implementation-slices/ExitPass_Central_PMS_Statutory_Discount_Staged_Canonical_Commands_Implementation_Note_v1.0.md`; commit `133091e` | App-local staged command patch |
| 7 | Shared one-shot staged facade orchestration | `docs/v1.3/central-pms/implementation-slices/ExitPass_Central_PMS_Statutory_Discount_Staged_Facade_Orchestration_Implementation_Note_v1.0.md`; commit `22492ea` | Requires staged command app-local objects |
| 8 | Operator Console decision-route convergence | `docs/v1.3/central-pms/implementation-slices/ExitPass_Central_PMS_Operator_Console_Statutory_Discount_Decision_Convergence_Implementation_Note_v1.0.md`; commit `1255e1a` | Requires staged commands and validation metadata patch |
| 9 | Operator Console apply-payable-basis convergence | `docs/v1.3/central-pms/implementation-slices/ExitPass_Central_PMS_Operator_Console_Statutory_Discount_Apply_Convergence_Implementation_Note_v1.0.md`; commit `41af10e` | Requires staged application command objects; legacy payable-basis objects are canonical |
| 10 | WebPay/APT integration-readiness authorization audit | `docs/v1.3/central-pms/reviews/ExitPass_Statutory_Discount_WebPay_APT_Integration_Readiness_Authorization_v1.0.md`; commit `da2c0eb` | Needs revalidation against current canonical source plus patch status |
| 11 | Service-channel decision-authority design | `docs/v1.3/central-pms/reviews/ExitPass_Statutory_Discount_Service_Channel_Decision_Authority_Design_Decision_v1.0.md`; commit `b916af2` | Correctly identifies review-mediated authority; persistence still app-local |
| 12 | Service-channel pending-review intake | `docs/v1.3/central-pms/implementation-slices/ExitPass_Central_PMS_Statutory_Discount_Service_Channel_Pending_Review_Intake_Implementation_Note_v1.0.md`; commit `3950a33` | App-local AWAITING_REVIEW constraint change |
| 13 | Operator Console service-channel review linkage | `docs/v1.3/central-pms/implementation-slices/ExitPass_Central_PMS_Statutory_Discount_Service_Channel_Operator_Console_Review_Linkage_Implementation_Note_v1.0.md`; commit `c42b887` | App-local review table and linkage |
| 14 | Service-channel post-approval application intent | `docs/v1.3/central-pms/implementation-slices/ExitPass_Central_PMS_Statutory_Discount_Service_Channel_Post_Approval_Application_Intent_Implementation_Note_v1.0.md`; commit `7e28baa` | App-local review-to-validation linkage |
| 15 | Related SQL patches, tests, fixtures, and Bruno scenarios | `infra/db/patches`, `src/Services/CentralPms/tests`, `bruno/operator-console-statutory-discount-draft` | Mixed canonical, app-local, and shared integration DB assumptions |

## 8. Per-Task Database-Assumption Matrix

| Task | Stale standalone DDL | Retired DB repo | Live accumulated DB | Current canonical SQL | Canonical plus app-local patches |
| --- | --- | --- | --- | --- | --- |
| System-wide baseline audit | Evidence only | Evidence only | Evidence only | Not validated | Not applicable |
| Shared facade | No direct current proof | No direct current proof | Likely used for local validation | Fails without app-local table | Required |
| Channel-contract readiness | No | No | Used by surrounding tests | Partial | Required for persistence |
| Staged command design | No | No | Not applicable | Not yet represented | Required implementation target |
| Staged command implementation | No | No | Often used | Fails | Required |
| Shared staged facade orchestration | No | No | Often used | Fails | Required |
| Operator Console decision convergence | No | No | Often used | Fails due missing staged/metadata columns | Required |
| Operator Console apply convergence | No | No | Often used | Fails due missing staged app command table | Required |
| Service-channel pending-review intake | No | No | Often used | Fails due missing AWAITING_REVIEW support | Required |
| Service-channel review linkage | No | No | Often used | Fails due missing review table | Required |
| Post-approval application intent | Corrected note cites canonical plus patches | No | Previously observed | Fails without patches | Passed focused tests |
| Payment-initiation effective snapshot work | Older DDL not current | Historical only | Supported | Canonical objects present | Also passes |
| POS fiscal semantic hash tests | No statutory staged schema dependency | No | Supported | Canonical objects present | Also passes |
| Bruno statutory scenarios | No live proof in this audit | No | Manual/live environment likely | Not runnable canonical-only | Require canonical plus patches |
| Test infrastructure | Some non-statutory unit tests still read standalone DDL | No | Default shared DB is `exitpass_v12_dev` | Incomplete fixture support | Mixed, one fixture drift found |

## 9. Per-Task Classification and Severity

| Task | Classification | Severity | Rollout impact | Owner |
| --- | --- | --- | --- | --- |
| System-wide baseline audit | DOCUMENTATION_CORRECTION_REQUIRED | LOW | DOES_NOT_BLOCK | Codex I |
| Shared facade | APP_LOCAL_PATCH_REQUIRES_PROMOTION | HIGH | BLOCKS_NEXT_IMPLEMENTATION | Canonical database workstream |
| Integration-readiness handoff | DOCUMENTATION_CORRECTION_REQUIRED | MEDIUM | BLOCKS_CONTROLLED_UAT | Codex I |
| Channel-contract readiness | CANONICAL_ALIGNED_REVALIDATION_REQUIRED | MEDIUM | BLOCKS_CONTROLLED_UAT | Codex I |
| Staged command design | CANONICAL_ALIGNED_REVALIDATION_REQUIRED | MEDIUM | BLOCKS_CONTROLLED_UAT | Shared ownership |
| Staged commands implementation | APP_LOCAL_PATCH_REQUIRES_PROMOTION | HIGH | BLOCKS_NEXT_IMPLEMENTATION | Canonical database workstream |
| Shared staged facade orchestration | APP_LOCAL_PATCH_REQUIRES_PROMOTION | HIGH | BLOCKS_NEXT_IMPLEMENTATION | Canonical database workstream |
| Operator Console decision convergence | APP_LOCAL_PATCH_REQUIRES_PROMOTION | HIGH | BLOCKS_NEXT_IMPLEMENTATION | Canonical database workstream |
| Operator Console apply convergence | APP_LOCAL_PATCH_REQUIRES_PROMOTION | HIGH | BLOCKS_NEXT_IMPLEMENTATION | Canonical database workstream |
| WebPay/APT readiness audit | DOCUMENTATION_CORRECTION_REQUIRED | MEDIUM | BLOCKS_CONTROLLED_UAT | Codex I |
| Service-channel decision-authority design | CANONICAL_ALIGNED_REVALIDATION_REQUIRED | MEDIUM | BLOCKS_CONTROLLED_UAT | Shared ownership |
| Service-channel pending-review intake | APP_LOCAL_PATCH_REQUIRES_PROMOTION | HIGH | BLOCKS_NEXT_IMPLEMENTATION | Canonical database workstream |
| Operator Console review linkage | APP_LOCAL_PATCH_REQUIRES_PROMOTION | HIGH | BLOCKS_NEXT_IMPLEMENTATION | Canonical database workstream |
| Post-approval application intent | APP_LOCAL_PATCH_REQUIRES_PROMOTION | HIGH | BLOCKS_NEXT_IMPLEMENTATION | Canonical database workstream |
| Test fixtures and Bruno evidence | TEST_BASELINE_DRIFT | HIGH | BLOCKS_NEXT_IMPLEMENTATION | Codex I |

Counts:

- Audited tasks: 15
- CANONICAL_ALIGNED: 0
- Revalidation required: 4
- Canonical promotion required: 7
- Runtime rework required: 0 from this audit
- Documentation correction required: 3 primary tasks, plus targeted notes listed below
- Test baseline drift: 1 grouped test-infrastructure item

## 10. Application-Local Patch Inventory

| Patch | Current classification | Evidence |
| --- | --- | --- |
| `infra/db/patches/ExitPass_StatutoryDiscountDecisionFacade_v1.3.sql` | ACTIVE_APP_LOCAL / REQUIRED_FOR_CURRENT_RUNTIME | Creates `discounts.statutory_discount_decision_commands`; not found in canonical generated SQL |
| `infra/db/patches/ExitPass_StatutoryDiscountStagedCanonicalCommands_v1.3.sql` | ACTIVE_APP_LOCAL / REQUIRED_FOR_CURRENT_RUNTIME | Alters decision command table and creates `discounts.statutory_discount_payable_basis_application_commands` |
| `infra/db/patches/ExitPass_OperatorConsoleStatutoryDiscountDecisionConvergence_v1.3.sql` | ACTIVE_APP_LOCAL / REQUIRED_FOR_CURRENT_RUNTIME | Adds safe decision metadata columns to `discounts.statutory_discount_validations` |
| `infra/db/patches/ExitPass_StatutoryDiscountServiceChannelPendingReviewIntake_v1.3.sql` | ACTIVE_APP_LOCAL / REQUIRED_FOR_CURRENT_RUNTIME | Adds `AWAITING_REVIEW` result/recovery/status support |
| `infra/db/patches/ExitPass_StatutoryDiscountServiceChannelOperatorConsoleReviewLinkage_v1.3.sql` | ACTIVE_APP_LOCAL / REQUIRED_FOR_CURRENT_RUNTIME | Creates `operator_console.statutory_discount_service_channel_reviews` |
| `infra/db/patches/ExitPass_StatutoryDiscountServiceChannelPostApprovalApplicationIntent_v1.3.sql` | ACTIVE_APP_LOCAL / REQUIRED_FOR_CURRENT_RUNTIME | Adds `statutory_discount_validation_id` linkage to service-channel review table |
| `infra/db/patches/ExitPass_StatutoryDiscountPayableBasisApplicationSchema_v1.2.sql` | RETIRED_CANONICAL_SUPERSEDED | Canonical generated SQL includes `discounts.statutory_discount_payable_basis_applications` and apply routine; manifest says not to apply for aligned/canonical validation |
| `infra/db/patches/ExitPass_StatutoryDiscountAppliedTariffSnapshotLifecycle_v1.2.sql` | RETIRED_CANONICAL_SUPERSEDED | Canonical generated SQL includes applied tariff snapshot constraints/indexes |
| `infra/db/patches/ExitPass_StatutoryDiscountPolicyRegistrySchema_v1.2.sql` | PARTIALLY_CANONICAL / STATUS_UNRESOLVED | Manifest classifies as `PARTIALLY_SUPERSEDED_REVIEW_REQUIRED` |

Required active validation scripts:

- `infra/db/patches/validation/Validate_OperatorConsoleStatutoryDiscountDecisionConvergence_v1.3.sql`
- `infra/db/patches/validation/Validate_StatutoryDiscountDecisionFacade_v1.3.sql`
- `infra/db/patches/validation/Validate_StatutoryDiscountStagedCanonicalCommands_v1.3.sql`
- `infra/db/patches/validation/Validate_StatutoryDiscountServiceChannelPendingReviewIntake_v1.3.sql`
- `infra/db/patches/validation/Validate_StatutoryDiscountServiceChannelOperatorConsoleReviewLinkage_v1.3.sql`
- `infra/db/patches/validation/Validate_StatutoryDiscountServiceChannelPostApprovalApplicationIntent_v1.3.sql`

## 11. Canonical Object-Source Inventory

Canonical objects found:

| Object | Canonical source |
| --- | --- |
| `discounts.statutory_discount_validations` | `objects/schemas/discounts/tables/discounts.statutory_discount_validations.sql`; apply-order line 719 |
| `discounts.discount_evidence_references` | `objects/schemas/discounts/tables/discounts.discount_evidence_references.sql`; apply-order line 596 |
| `discounts.discount_policy_references` | `objects/schemas/discounts/tables/discounts.discount_policy_references.sql`; apply-order line 629 |
| `discounts.statutory_discount_payable_basis_applications` | `objects/schemas/discounts/tables/discounts.statutory_discount_payable_basis_applications.sql`; apply-order line 2493 |
| `discounts.apply_statutory_discount_payable_basis(uuid,uuid,uuid)` | `objects/schemas/discounts/functions/discounts.apply_statutory_discount_payable_basis.sql`; apply-order line 2515 |
| Applied tariff snapshot lifecycle | `objects/schemas/core/constraints/core.ck_tariff_snapshots__statutory_discount_link_has_discount.sql`; `objects/schemas/core/indexes/core.ux_tariff_snapshots__statutory_discount_validation_applied.sql` |
| `integration.vendor_payment_acknowledgments` | `objects/schemas/integration/tables/integration.vendor_payment_acknowledgments.sql`; apply-order line 1414 |

Canonical objects not found:

| Object or value | Search result |
| --- | --- |
| `discounts.statutory_discount_decision_commands` | Not found in `objects`, `migrations`, `build/generated`, or `scripts` |
| `discounts.statutory_discount_payable_basis_application_commands` | Not found in `objects`, `migrations`, `build/generated`, or `scripts` |
| `operator_console.statutory_discount_service_channel_reviews` | Not found in `objects`, `migrations`, `build/generated`, or `scripts` |
| `AWAITING_REVIEW` staged decision status/result classification | Not found in canonical object source or generated SQL |
| `discounts.statutory_discount_validations.id_document_type` | Not found in canonical generated SQL |
| `discounts.statutory_discount_validations.masked_id_reference` | Not found in canonical generated SQL |

## 12. Object-Definition Comparison

| Object | Current application expectation | Current canonical state | Gap |
| --- | --- | --- | --- |
| `discounts.statutory_discount_decision_commands` | Authoritative shared/canonical decision command table, business identity, idempotency, semantic hash, command status, result classification, linkage IDs | Absent | Critical canonical promotion gap |
| `discounts.statutory_discount_payable_basis_application_commands` | Canonical application-v1 command table, one application per decision, semantic hash, idempotency, payable-basis linkage | Absent | Critical canonical promotion gap |
| `operator_console.statutory_discount_service_channel_reviews` | Durable review discovery/detail/completion linkage for service-channel pending review | Absent | Critical canonical promotion gap |
| `AWAITING_REVIEW` | Durable staged decision command status and result classification for service-channel intake | Absent because staged command table absent | Critical canonical promotion gap |
| `NOT_DECIDED` | Required decision result for pending review and decision-v2 lifecycle | Absent because staged command table absent | Critical canonical promotion gap |
| `discounts.statutory_discount_validations` decision metadata | Requires `id_document_type`, `issuing_authority`, `id_expiry_date`, `masked_id_reference`, `requester_attestation`, `attestation_notes` for reviewed service-channel linkage | Base table exists; additive columns absent | High canonical promotion gap |
| `discounts.statutory_discount_payable_basis_applications` | Existing authoritative payable-basis mutation row | Present | Canonical-aligned |
| `discounts.apply_statutory_discount_payable_basis(uuid,uuid,uuid)` | Existing durable payable-basis writer | Present | Canonical-aligned |
| Applied tariff snapshot support | Effective applied tariff snapshot linkage | Present | Canonical-aligned |
| `integration.vendor_payment_acknowledgments` | Payment/finality-adjacent object, not statutory staged command authority | Present | Canonical-aligned |

## 13. Historical-to-Canonical Trace

| Object family | Historical source | App-local patch | Current canonical source | Current status |
| --- | --- | --- | --- | --- |
| Legacy statutory validations/evidence/policy refs | Present in historical v1.2 lineage | Earlier patches and runtime | Present in `exitpassdb_v1.2` object source | Promoted |
| Legacy payable-basis applications | Present in historical lineage | `ExitPass_StatutoryDiscountPayableBasisApplicationSchema_v1.2.sql` | Present in canonical object source and generated SQL | Promoted; app-local patch retired |
| Applied tariff snapshot lifecycle | Present through prior app-local work | `ExitPass_StatutoryDiscountAppliedTariffSnapshotLifecycle_v1.2.sql` | Present in canonical object source and generated SQL | Promoted; app-local patch retired |
| Staged decision-v2 command table | App-local only | `ExitPass_StatutoryDiscountDecisionFacade_v1.3.sql` plus staged patch | Absent | Not promoted |
| Staged application-v1 command table | App-local only | `ExitPass_StatutoryDiscountStagedCanonicalCommands_v1.3.sql` | Absent | Not promoted |
| Pending-review status constraints | App-local only | `ExitPass_StatutoryDiscountServiceChannelPendingReviewIntake_v1.3.sql` | Absent | Not promoted |
| Operator Console service-channel review table | App-local only | `ExitPass_StatutoryDiscountServiceChannelOperatorConsoleReviewLinkage_v1.3.sql` | Absent | Not promoted |
| Review-to-validation linkage | App-local only | `ExitPass_StatutoryDiscountServiceChannelPostApprovalApplicationIntent_v1.3.sql` | Absent | Not promoted |
| Vendor payment acknowledgments | Historical source at `ExitPass_DBv1.2` commit `456bfa0` | Not statutory app-local | Present in current canonical object source and generated SQL | Promoted |

## 14. Canonical-Only Disposable Validation

Disposable database:

- `exitpass_statutory_canonical_only_audit_codexi`

Inputs:

- `D:\SourceCodes\exitpassdb_v1.2\build\generated\exitpass-full-object.generated.sql`
- `D:\SourceCodes\exitpassdb_v1.2\scripts\validation\Validate-V13CentralPmsAlignment.sql`

Result:

- Canonical full generated SQL applied successfully.
- Central PMS alignment validation passed.
- PostgreSQL version: `PostgreSQL 16.14 on x86_64-pc-linux-musl`.
- Disposable database was dropped afterward.

Focused statutory object check:

| Check | Result |
| --- | --- |
| `discounts.statutory_discount_validations` | Present |
| `discounts.discount_evidence_references` | Present |
| `discounts.discount_policy_references` | Present |
| `discounts.statutory_discount_payable_basis_applications` | Present |
| `discounts.apply_statutory_discount_payable_basis(uuid,uuid,uuid)` | Present |
| `discounts.statutory_discount_decision_commands` | Absent |
| `discounts.statutory_discount_payable_basis_application_commands` | Absent |
| `operator_console.statutory_discount_service_channel_reviews` | Absent |
| `discounts.statutory_discount_validations.id_document_type` | Absent |
| `discounts.statutory_discount_validations.masked_id_reference` | Absent |

Conclusion:

- Canonical-only is not a sufficient runtime baseline for merged statutory staged/service-channel behavior.

## 15. Canonical-Plus-Patches Disposable Validation

Disposable database:

- `exitpass_statutory_canonical_plus_patches_audit_codexi`

Inputs:

- Current canonical generated SQL
- Active statutory app-local patch chain, in deterministic order

Patch order used:

1. `infra/db/patches/ExitPass_OperatorConsoleStatutoryDiscountDecisionConvergence_v1.3.sql`
2. `infra/db/patches/ExitPass_StatutoryDiscountDecisionFacade_v1.3.sql`
3. `infra/db/patches/ExitPass_StatutoryDiscountStagedCanonicalCommands_v1.3.sql`
4. `infra/db/patches/ExitPass_StatutoryDiscountServiceChannelPendingReviewIntake_v1.3.sql`
5. `infra/db/patches/ExitPass_StatutoryDiscountServiceChannelOperatorConsoleReviewLinkage_v1.3.sql`
6. `infra/db/patches/ExitPass_StatutoryDiscountServiceChannelPostApprovalApplicationIntent_v1.3.sql`

Validation SQL results:

| Validation | Result |
| --- | --- |
| `Validate_OperatorConsoleStatutoryDiscountDecisionConvergence_v1.3.sql` | Passed |
| `Validate_StatutoryDiscountDecisionFacade_v1.3.sql` | Passed |
| `Validate_StatutoryDiscountStagedCanonicalCommands_v1.3.sql` | Passed |
| `Validate_StatutoryDiscountServiceChannelPendingReviewIntake_v1.3.sql` | Passed |
| `Validate_StatutoryDiscountServiceChannelOperatorConsoleReviewLinkage_v1.3.sql` | Passed |
| `Validate_StatutoryDiscountServiceChannelPostApprovalApplicationIntent_v1.3.sql` | Passed |

Idempotency check:

- Reapplying `ExitPass_StatutoryDiscountServiceChannelPostApprovalApplicationIntent_v1.3.sql` passed.
- The audit did not classify every predecessor patch as fully idempotent for repeated application on a fully patched DB. One fixture-level reapplication failure was observed and is captured below.

Focused object check after patches:

| Check | Result |
| --- | --- |
| `discounts.statutory_discount_decision_commands` | Present |
| `discounts.statutory_discount_payable_basis_application_commands` | Present |
| `operator_console.statutory_discount_service_channel_reviews` | Present |
| `discounts.statutory_discount_validations.id_document_type` | Present |
| `operator_console.statutory_discount_service_channel_reviews.statutory_discount_validation_id` | Present |
| `AWAITING_REVIEW` support in decision command status constraint | Present |

Focused runtime tests against this disposable DB:

- `StatutoryDiscountServiceChannelPostApprovalApplicationIntentApiIntegrationTests`: passed 8/8.
- `OperatorConsoleServiceChannelStatutoryDiscountReviewApiIntegrationTests`: passed 8/8.
- `StatutoryDiscountDecisionFacadeRepositoryTests`: passed 5/5.
- Payment initiation / create-or-reuse payment attempt / TerminalCash filter: passed 63/63.
- Focused Central PMS unit statutory/fiscal/POS filter: passed 209/209.
- Focused unit staged/review/facade filter: passed 63/63.

Observed fixture drift:

- Combined integration filter including `StatutoryDiscountStagedCommandRepositoryTests` failed 1/31 after the canonical-plus-patches DB was already fully patched.
- Failure: `P0001: decision semantic source-version constraint does not preserve v1 and allow v2`.
- Source: `src/Services/CentralPms/tests/ExitPass.CentralPms.IntegrationTests/Persistence/StatutoryDiscountStagedCommandRepositoryTests.cs:466` applies a patch sequence again through `EnsurePatchAppliedAndValidatedAsync`.
- This is test-infrastructure baseline drift, not proof that the runtime flow is broken.

## 16. Patch-Order Analysis

Recommended deterministic app-local order until canonical promotion:

1. `ExitPass_OperatorConsoleStatutoryDiscountDecisionConvergence_v1.3.sql`
2. `ExitPass_StatutoryDiscountDecisionFacade_v1.3.sql`
3. `ExitPass_StatutoryDiscountStagedCanonicalCommands_v1.3.sql`
4. `ExitPass_StatutoryDiscountServiceChannelPendingReviewIntake_v1.3.sql`
5. `ExitPass_StatutoryDiscountServiceChannelOperatorConsoleReviewLinkage_v1.3.sql`
6. `ExitPass_StatutoryDiscountServiceChannelPostApprovalApplicationIntent_v1.3.sql`

Key findings:

- `ExitPass_StatutoryDiscountDecisionFacade_v1.3.sql` creates the base decision command table that later staged patches alter.
- `ExitPass_StatutoryDiscountStagedCanonicalCommands_v1.3.sql` adds v2 command semantics and creates application-v1 command persistence.
- `ExitPass_StatutoryDiscountServiceChannelPendingReviewIntake_v1.3.sql` assumes the staged command table already exists.
- `ExitPass_StatutoryDiscountServiceChannelOperatorConsoleReviewLinkage_v1.3.sql` assumes staged decision commands already exist.
- `ExitPass_StatutoryDiscountServiceChannelPostApprovalApplicationIntent_v1.3.sql` assumes the service-channel review table already exists.
- `ExitPass_OperatorConsoleStatutoryDiscountDecisionConvergence_v1.3.sql` must precede real review-mediated application proof because it adds validation metadata needed by the review-to-validation linkage path.
- Retired canonical patches such as `ExitPass_StatutoryDiscountPayableBasisApplicationSchema_v1.2.sql` must not be applied as part of aligned canonical validation.

## 17. Test-Fixture and Integration-Harness Analysis

| Fixture or helper | Finding | Impact |
| --- | --- | --- |
| `src/Services/CentralPms/tests/ExitPass.CentralPms.IntegrationTests/Shared/CentralPmsIntegrationTestConfiguration.cs` | Default connection string is `Host=localhost;Port=5433;Database=exitpass_v12_dev;...` | Tests default to a shared accumulated integration DB unless env vars are overridden |
| `src/Services/CentralPms/tests/ExitPass.CentralPms.IntegrationTests/Shared/StatutoryDiscountReviewIntegrationTestSupport.cs:20` | `EnsureSchemaAsync` applies app-local patches directly through `ExecuteSqlFileAsync` | Mutates target DB; not a disposable canonical rebuild harness |
| `StatutoryDiscountReviewIntegrationTestSupport.cs:32` | Applies retired `ExitPass_StatutoryDiscountPayableBasisApplicationSchema_v1.2.sql` | Conflicts with manifest rule for aligned canonical validation |
| `StatutoryDiscountReviewIntegrationTestSupport.cs:33-37` | Applies statutory app-local patches, but not `ExitPass_OperatorConsoleStatutoryDiscountDecisionConvergence_v1.3.sql` | Works only when the target DB already has validation metadata columns |
| `StatutoryDiscountStagedCommandRepositoryTests.cs:466` | Test fixture reapplies patch chain and validation SQL | Failed when run on already patched canonical-plus-patches DB, proving fixture baseline drift |
| `StatutoryDiscountDecisionFacadeRepositoryTests.cs:200` | Applies facade patch directly through test helper | Useful local fallback, but not canonical-only proof |
| `OperatorConsoleStatutoryDiscountDecisionApiIntegrationTests.cs:469-473` | Applies payable-basis, facade, staged, and decision-convergence patches | Mixed retired plus active patch posture |
| `OperatorConsoleStatutoryDiscountApplyPayableBasisApiIntegrationTests.cs:643-647` | Same mixed patch posture | Requires normalization after canonical promotion |
| `src/Services/CentralPms/tests/ExitPass.CentralPms.UnitTests/VendorSessions/VendorSessionProjectionTests.cs:303` | Reads `ExitPass_Full_Database_Creation_DDL_v1.2.sql` | Non-statutory unit test still depends on stale standalone artifact |
| `src/Services/CentralPms/tests/ExitPass.CentralPms.UnitTests/VendorSessions/VendorSessionProjectionSchedulerTests.cs:127` | Reads `ExitPass_Full_Database_Creation_DDL_v1.2.sql` | Non-statutory unit test still depends on stale standalone artifact |
| `src/Services/PaymentOrchestrator/tests/ExitPass.PaymentOrchestrator.IntegrationTests/Persistence/ProviderSessionRepositorySqlContractTests.cs:36` | Reads `ExitPass_Full_Database_Creation_DDL_v1.2.sql` | Separate test-infrastructure drift outside statutory runtime |

Test-infrastructure conclusion:

- Current fixtures are sufficient for behavior regression in an accumulated local DB or explicitly patched disposable DB.
- They are not sufficient as canonical-only rebuild proof.
- Some fixtures apply retired canonical patches and omit required active predecessor patches.
- A separate test-infrastructure task is required after canonical promotion.

## 18. Current Runtime Correctness

| Baseline | Result | Evidence |
| --- | --- | --- |
| Canonical-only DB | Not runnable for merged staged/service-channel statutory runtime | Missing staged command tables, review table, AWAITING_REVIEW support, validation metadata columns |
| Canonical DB plus active app-local patch chain | Correct for focused reviewed post-approval application flow | Patch validators passed; post-approval API tests passed 8/8; review-linkage API tests passed 8/8; payment-initiation/TerminalCash filter passed 63/63 |
| Current shared integration DB `exitpass_v12_dev` | Not audited as authority in this report | Live DB may contain accumulated patches; source-of-truth remains canonical repo |

Implementation correctness is not disproven. Deployment reproducibility from canonical-only source is disproven.

## 19. Prior-Validation Trustworthiness Matrix

| Validation evidence type | Classification | Reason |
| --- | --- | --- |
| App-local patch validators against a disposable DB seeded from canonical generated SQL plus active patches | TRUSTWORTHY_WITH_LIMITATION | Proves behavior if the patch chain is applied; does not prove canonical promotion |
| Real WebPay/APT review-mediated post-approval application tests against canonical plus active patches | TRUSTWORTHY_WITH_LIMITATION | Proves runtime flow with required patches present |
| Tests against `exitpass_v12_dev` | REVALIDATION_REQUIRED | Shared DB can accumulate objects and rows not traceable to current canonical source |
| Prior validation using `ExitPass_Full_Database_Creation_DDL_v1.2.sql` as sole baseline | INVALIDATED_BY_BASELINE_DRIFT | Standalone DDL is older and no longer authoritative |
| Fixture-level patch application without disposable isolation | INSUFFICIENT_EVIDENCE | May mutate shared DB and may hide missing canonical source |
| Canonical-only Central PMS alignment validation | TRUSTWORTHY but incomplete for statutory staged runtime | Validator does not currently require staged statutory command/review objects |

## 20. Documentation Drift Inventory

Documents that need targeted correction or qualification:

| Path | Drift |
| --- | --- |
| `docs/v1.3/central-pms/reviews/ExitPass_Statutory_Discount_Integration_Readiness_and_Thread_Handoff_v1.0.md` | Handoff should be qualified against current canonical-only gap before channel readiness resumes |
| `docs/v1.3/central-pms/reviews/ExitPass_Central_PMS_Statutory_Discount_Channel_Contract_Readiness_Audit_v1.0.md` | Earlier priority assessment said canonical promotion was not immediate; current evidence reverses sequencing |
| `docs/v1.3/central-pms/reviews/ExitPass_Statutory_Discount_WebPay_APT_Integration_Readiness_Authorization_v1.0.md` | Readiness posture must reference canonical DB gap before any channel authorization |
| `docs/v1.3/central-pms/implementation-slices/ExitPass_Central_PMS_Statutory_Discount_Staged_Canonical_Commands_Implementation_Note_v1.0.md` | Should clarify app-local implementation until canonical DB promotion |
| `docs/v1.3/central-pms/implementation-slices/ExitPass_Central_PMS_Statutory_Discount_Service_Channel_Pending_Review_Intake_Implementation_Note_v1.0.md` | Should qualify AWAITING_REVIEW SQL as app-local until promotion |
| `docs/v1.3/central-pms/implementation-slices/ExitPass_Central_PMS_Statutory_Discount_Service_Channel_Operator_Console_Review_Linkage_Implementation_Note_v1.0.md` | Should qualify review-linkage table as app-local until promotion |
| `docs/v1.3/central-pms/implementation-slices/ExitPass_Central_PMS_Statutory_Discount_Service_Channel_Post_Approval_Application_Intent_Implementation_Note_v1.0.md` | Correctly cites canonical plus active patches, but should be cross-linked to this audit after merge |
| `docs/ExitPass-v1.2-database-rebuild-baseline.md` | Still states old standalone DDL as authoritative v1.2 baseline |
| `docs/operator-console/OperatorConsole_Production_Policy_Registry_Readiness_v1.md` | References historical `D:\SourceCodes\ExitPass_DBv1.2` as database source |
| `docs/v1.3/operator-console/runbooks/*Statutory_Discount*` | Several runbooks cite `exitpass_v12_dev` as local runtime DB; acceptable as runbook context but not source authority |

This audit intentionally does not edit those documents.

## 21. Canonical Database Promotion Gaps

Required promotion candidates:

- `discounts.statutory_discount_decision_commands`
- `discounts.statutory_discount_payable_basis_application_commands`
- Decision-v2 command status/result/recovery constraints including `AWAITING_REVIEW`, `PROCESSING`, `COMPLETED`, retryable failure states, `NOT_DECIDED`, `APPROVED`, and `REJECTED`
- Decision-v2 semantic source/version constraints
- Application-v1 semantic source/version constraints
- Business identity and idempotency unique indexes
- Safe linkage columns from `ExitPass_OperatorConsoleStatutoryDiscountDecisionConvergence_v1.3.sql`
- `operator_console.statutory_discount_service_channel_reviews`
- `operator_console.statutory_discount_service_channel_reviews.statutory_discount_validation_id`
- Review discovery indexes
- Review-to-validation uniqueness/indexes
- Comments for all promoted objects and columns
- Validation SQL equivalent to current app-local validators
- RBAC/reference-data seeds if any runtime policy depends on seed presence not already canonical

## 22. Application Patch-Retirement Gaps

After canonical promotion:

- Move or reclassify superseded active statutory app-local patches.
- Update `infra/db/patches/ExitPass_AppLocal_Db_Patch_Retirement_Manifest_v1.0.md`.
- Stop applying retired canonical patches in statutory test helpers.
- Preserve controlled upgrade compatibility for environments that already applied app-local patches.
- Keep historical patches available only as legacy fallback until explicitly removed.

## 23. Test-Infrastructure Rework

Required separately from product runtime:

- Add a canonical-generated-SQL disposable DB fixture for statutory persistence/API tests.
- Use deterministic app-local patch chain only until canonical promotion is complete.
- Remove or gate test-time application of `ExitPass_StatutoryDiscountPayableBasisApplicationSchema_v1.2.sql` when canonical objects exist.
- Add `ExitPass_OperatorConsoleStatutoryDiscountDecisionConvergence_v1.3.sql` to any remaining pre-promotion fixture that requires service-channel review-to-validation linkage.
- Avoid direct permanent mutation of `exitpass_v12_dev` for proof-grade integration tests.
- Serialize or isolate patch application to avoid shared-DB concurrency and reapplication drift.
- Replace stale standalone DDL reads in non-statutory unit/integration tests with canonical generated SQL or extracted source checks.

## 24. Runtime Rework, Only Where Required

No runtime rework is required by this audit.

The runtime defect hypothesis was tested as follows:

- Canonical plus active app-local patches passed the real service-channel post-approval application-intent API test class.
- Payment-initiation and TerminalCash filtered tests passed against the same corrected database.
- The one observed failure is a test fixture reapplication drift, not a runtime application defect.

Runtime rework should be reopened only if canonical promotion reveals object-definition incompatibility, or if post-promotion tests fail against a clean canonical-only database.

## 25. Documentation Corrections

Recommended docs-only correction task after canonical promotion:

- Add a short correction note to prior readiness/implementation reports that previously treated app-local validation as sufficient.
- Update old references that imply `ExitPass_DBv1.2` or `ExitPass_Full_Database_Creation_DDL_v1.2.sql` are current database authority.
- Keep historical evidence references, but label them as historical.
- Update UAT runbooks to distinguish local environment target `exitpass_v12_dev` from canonical DB source.

Do not perform that correction in the canonical promotion task unless the database workstream explicitly owns it.

## 26. Revalidation Requirements

After canonical database promotion, revalidate:

- Canonical object-source layout validation
- Generated full SQL rebuild
- Central PMS alignment validation
- Canonical-only statutory object checks
- Staged command repository tests
- Shared facade repository tests
- Pending-review intake API tests
- Operator Console review-linkage API and repository tests
- Post-approval application-intent API tests
- Operator Console decision/apply regression tests
- Payable-basis and payment-initiation tests
- POS mapper and fiscal semantic-hash tests
- WebPay-adjacent tests
- TerminalCash regressions
- Bruno structural validation
- `git diff --check`
- Markdown trailing-whitespace check

Acceptance criterion:

- Current statutory runtime must pass against a disposable DB built from canonical generated SQL alone, without applying the active statutory app-local patches.

## 27. Prioritized Bounded Rework Plan

### A. Canonical Database Promotion

Task title:

- Promote Central PMS statutory-discount staged command and service-channel review database objects to canonical source.

Purpose:

- Make the merged statutory-discount staged/service-channel runtime reproducible from `exitpassdb_v1.2` generated SQL.

Repository:

- `D:\SourceCodes\exitpassdb_v1.2`

Owner/persona:

- Canonical database workstream, Codex I if assigned.

Base branch:

- `develop`

Proposed feature branch:

- `feature/statutory-discount-staged-service-channel-canonical-db-promotion`

Scope:

- Add object-source files, apply-order entries, comments, validation SQL, generated SQL refresh, and idempotent upgrade proof for the active statutory staged/review-linkage patch chain.

Validation:

- Rebuild disposable DB from generated full SQL.
- Run object-source layout validation.
- Run Central PMS alignment validation.
- Run statutory object checks.
- Run current Central PMS statutory focused tests against canonical-only output.

Rollout blocker closed:

- BLOCKS_NEXT_IMPLEMENTATION.

Must precede channel-safe readback hardening:

- Yes.

### B. Application Patch Retirement

Task title:

- Retire statutory staged/service-channel app-local patches after canonical promotion.

Repository:

- `D:\SourceCodes\ExitPass-Discounts`

Proposed branch:

- `chore/statutory-discount-retire-promoted-app-local-patches`

Scope:

- Update manifest and test harness patch posture; do not remove historical files unless separately approved.

Dependency:

- Canonical database promotion merged.

Must precede channel-safe readback hardening:

- Prefer yes, unless readback hardening is explicitly constrained to canonical promotion branch.

### C. Test-Infrastructure Rework

Task title:

- Align statutory-discount tests to canonical generated SQL and disposable database fixtures.

Repository:

- `D:\SourceCodes\ExitPass-Discounts`

Proposed branch:

- `test/statutory-discount-canonical-db-fixture-alignment`

Scope:

- Replace shared `exitpass_v12_dev` mutation assumptions; normalize patch chain; remove retired-patch application from canonical paths.

Dependency:

- Canonical promotion merged or available as a tracked DB branch.

Must precede channel-safe readback hardening:

- Yes for proof-grade validation.

### D. Runtime Rework

Task title:

- None currently required.

Trigger:

- Only if post-promotion validation finds schema/contract drift affecting runtime behavior.

### E. Documentation Corrections

Task title:

- Correct statutory-discount database-source posture in prior reports and notes.

Repository:

- `D:\SourceCodes\ExitPass-Discounts`

Proposed branch:

- `docs/statutory-discount-database-source-posture-corrections`

Dependency:

- This audit and canonical promotion plan accepted.

### F. Revalidation

Task title:

- Revalidate statutory-discount runtime against canonical-only database baseline.

Repository:

- `D:\SourceCodes\ExitPass-Discounts`

Proposed branch:

- `test/statutory-discount-canonical-only-runtime-revalidation`

Dependency:

- Canonical promotion and app fixture alignment.

## 28. Exact Next Task

Exact next bounded task:

- Promote the active statutory-discount staged command, pending-review, service-channel review-linkage, and post-approval validation-linkage database objects into the canonical database repository.

Repository:

- `D:\SourceCodes\exitpassdb_v1.2`

Persona:

- Codex I

Base branch:

- `develop`

Proposed feature branch:

- `feature/statutory-discount-staged-service-channel-canonical-db-promotion`

Off-limits:

- Do not change application runtime behavior, WebPay, APT, Operator Console UI, POS Server runtime, statutory calculations, VAT behavior, payment finality, ExitAuthorization, fiscal issuance authority, gates, or privacy-retention policy.

Completion criteria:

- Canonical generated SQL alone contains all required objects.
- Canonical-only disposable DB passes statutory object checks.
- Current Central PMS statutory focused tests pass without app-local statutory patch application.
- App-local patches are classified for retirement in a follow-up application task.

## 29. Tasks That Must Wait

Must wait:

- Channel-safe application readback hardening.
- WebPay statutory-discount source integration.
- APT statutory-discount desktop/source integration.
- Controlled UAT authorization.
- Production rollout authorization.
- App-local statutory patch retirement, until canonical promotion merges.
- Documentation correction sweep, unless explicitly requested as an independent docs-only task.

May proceed only as read-only analysis:

- Further audit of channel-safe readback requirements.
- Review of canonical DB promotion PR once created.

## 30. Controlled-UAT Impact

Controlled UAT is blocked until:

- Canonical database promotion lands.
- Tests can rebuild from canonical generated SQL alone.
- Active app-local statutory patch chain is no longer required for clean environment setup.
- Bruno or live authenticated environment proof is rerun on the promoted canonical baseline.

## 31. Production-Rollout Impact

Production rollout is blocked until:

- Canonical DB promotion is complete.
- Application patch retirement and fixture alignment are complete.
- Runtime revalidation passes on canonical-only output.
- Channel-safe readback hardening and a separate WebPay/APT readiness authorization review are complete.
- Privacy-retention posture remains explicitly unresolved and must be handled before production if policy requires a retention term.

## 32. Known Limitations

- This audit did not modify runtime or database source.
- This audit did not inspect WebPay or APT external repositories.
- This audit did not treat `exitpass_v12_dev` as source authority.
- Broad full-suite execution was not attempted; focused statutory/payment validation was used to support the database gap finding.
- One initial parallel build produced MSBuild cache write conflicts; an escalated sequential build passed afterward.
- One focused integration filter failed due fixture reapplication drift after the target DB was already patched.

## 33. Evidence Appendix

### Code Evidence

- Shared routes: `src/Services/CentralPms/src/ExitPass.CentralPms.Api/Endpoints/StatutoryDiscountDecisionEndpoints.cs:24`, `:26`, `:37`
- Operator Console review routes: `src/Services/CentralPms/src/ExitPass.CentralPms.Api/Endpoints/OperatorConsoleStatutoryDiscountDraftEndpoints.cs:90`, `:101`
- Staged repository table usage: `src/Services/CentralPms/src/ExitPass.CentralPms.Infrastructure/StatutoryDiscounts/PostgresStatutoryDiscountStagedCommandRepository.cs`
- Service-channel review repository table usage: `src/Services/CentralPms/src/ExitPass.CentralPms.Infrastructure/StatutoryDiscounts/PostgresStatutoryDiscountServiceChannelReviewRepository.cs`
- Shared response/readback status mapping: `src/Services/CentralPms/src/ExitPass.CentralPms.Api/Endpoints/StatutoryDiscountDecisionEndpoints.cs:509`
- Application constants: `src/Services/CentralPms/src/ExitPass.CentralPms.Application/StatutoryDiscounts/StagedStatutoryDiscountCommandModels.cs:14`, `:27`
- Payment effective snapshot readback: `src/Services/CentralPms/src/ExitPass.CentralPms.Infrastructure/PaymentAttempts/TariffSnapshotReadRepository.cs:222`
- Vendor parking effective readback: `src/Services/CentralPms/src/ExitPass.CentralPms.Infrastructure/VendorParking/VendorParkingResolutionPersistence.cs:924`

### Database Evidence

- Canonical apply order: `D:\SourceCodes\exitpassdb_v1.2\objects\exitpass-full-object-apply-order.txt`
- Canonical generated SQL: `D:\SourceCodes\exitpassdb_v1.2\build\generated\exitpass-full-object.generated.sql`
- Canonical Central PMS validation: `D:\SourceCodes\exitpassdb_v1.2\scripts\validation\Validate-V13CentralPmsAlignment.sql`
- App-local retirement manifest: `infra/db/patches/ExitPass_AppLocal_Db_Patch_Retirement_Manifest_v1.0.md`
- Active app-local patches and validators listed in sections 10 and 16.

### Validation Commands and Results

Commands run:

```powershell
git branch --show-current
git status --short --branch --untracked-files=all
git fetch origin --prune
git log --oneline HEAD..origin/dev
git rev-parse HEAD
git rev-parse origin/dev

git -C D:\SourceCodes\exitpassdb_v1.2 branch --show-current
git -C D:\SourceCodes\exitpassdb_v1.2 status --short --branch --untracked-files=all
git -C D:\SourceCodes\exitpassdb_v1.2 fetch origin --prune
git -C D:\SourceCodes\exitpassdb_v1.2 log --oneline HEAD..origin/develop
git -C D:\SourceCodes\exitpassdb_v1.2 rev-parse HEAD
git -C D:\SourceCodes\exitpassdb_v1.2 rev-parse origin/develop

git -C D:\SourceCodes\ExitPass_DBv1.2 branch --show-current
git -C D:\SourceCodes\ExitPass_DBv1.2 status --short --branch --untracked-files=all
git -C D:\SourceCodes\ExitPass_DBv1.2 rev-parse HEAD

rg -n --hidden "statutory_discount|AWAITING_REVIEW|service_channel_reviews|payable_basis_application|applied_tariff_snapshot|decision_command|vendor_payment_acknowledg" objects migrations build/generated scripts .github
rg -n "statutory_discount_decision_commands|statutory_discount_payable_basis_application_commands|statutory_discount_service_channel_reviews|AWAITING_REVIEW|NOT_DECIDED" build/generated/exitpass-full-object.generated.sql objects migrations scripts
rg --files infra/db/patches infra/db/patches/validation infra/db/patches/retired
rg -n "EnsurePatchAppliedAndValidatedAsync|ExecuteSqlFileAsync|EnsureSchemaAsync|ExitPass_Full_Database_Creation_DDL_v1.2.sql|exitpass_v12_dev|ConnectionStrings__MainDatabase" src/Services/CentralPms/tests src/Services/PaymentOrchestrator/tests
git log --no-textconv -S"ExitPass_DBv1.2" --all --oneline
git log --no-textconv -S"ExitPass_Full_Database_Creation_DDL_v1.2.sql" --all --oneline
git log --no-textconv -S"exitpassdb_v1.2" --all --oneline
git log --no-textconv -S"statutory_discount_decision_commands" --all --oneline
git log --no-textconv -S"statutory_discount_payable_basis_application_commands" --all --oneline
git log --no-textconv -S"statutory_discount_service_channel_reviews" --all --oneline
git log --no-textconv -S"AWAITING_REVIEW" --all --oneline
git log --no-textconv -S"statutory_discount_validation_id" --all --oneline

powershell -NoProfile -ExecutionPolicy Bypass -File scripts\validation\Validate-ExitPassFullObjectSourceLayout.ps1 -SkipBuild

docker exec exitpass-postgres psql -v ON_ERROR_STOP=1 -U exitpass -d postgres -c "CREATE DATABASE exitpass_statutory_canonical_only_audit_codexi;"
Get-Content -Raw D:\SourceCodes\exitpassdb_v1.2\build\generated\exitpass-full-object.generated.sql | docker exec -i exitpass-postgres psql -q -v ON_ERROR_STOP=1 -U exitpass -d exitpass_statutory_canonical_only_audit_codexi
Get-Content -Raw D:\SourceCodes\exitpassdb_v1.2\scripts\validation\Validate-V13CentralPmsAlignment.sql | docker exec -i exitpass-postgres psql -v ON_ERROR_STOP=1 -U exitpass -d exitpass_statutory_canonical_only_audit_codexi

docker exec exitpass-postgres psql -v ON_ERROR_STOP=1 -U exitpass -d postgres -c "CREATE DATABASE exitpass_statutory_canonical_plus_patches_audit_codexi;"
Get-Content -Raw D:\SourceCodes\exitpassdb_v1.2\build\generated\exitpass-full-object.generated.sql | docker exec -i exitpass-postgres psql -q -v ON_ERROR_STOP=1 -U exitpass -d exitpass_statutory_canonical_plus_patches_audit_codexi
# Active statutory app-local patches applied in section 16 order.
# Validation SQL applied in section 15.

dotnet build src\Services\CentralPms\src\ExitPass.CentralPms.Application\ExitPass.CentralPms.Application.csproj --no-restore
dotnet build src\Services\CentralPms\src\ExitPass.CentralPms.Api\ExitPass.CentralPms.Api.csproj --no-restore -v:minimal -p:BuildInParallel=false -p:UseSharedCompilation=false
dotnet build src\Services\CentralPms\tests\ExitPass.CentralPms.IntegrationTests\ExitPass.CentralPms.IntegrationTests.csproj --no-restore -v:minimal -p:BuildInParallel=false -p:UseSharedCompilation=false
dotnet test src\Services\CentralPms\tests\ExitPass.CentralPms.UnitTests\ExitPass.CentralPms.UnitTests.csproj --no-build --filter "FullyQualifiedName~StatutoryDiscount|FullyQualifiedName~FiscalSemantic|FullyQualifiedName~PosServerFiscal" -v:minimal
dotnet test src\Services\CentralPms\tests\ExitPass.CentralPms.UnitTests\ExitPass.CentralPms.UnitTests.csproj --no-build --filter "FullyQualifiedName~StatutoryDiscountDecisionFacadeServiceTests|FullyQualifiedName~StatutoryDiscountStagedCommandServiceTests|FullyQualifiedName~OperatorConsoleServiceChannelStatutoryDiscountReviewServiceTests" -v:minimal
dotnet test src\Services\CentralPms\tests\ExitPass.CentralPms.IntegrationTests\ExitPass.CentralPms.IntegrationTests.csproj --no-build --filter "FullyQualifiedName~StatutoryDiscountServiceChannelPostApprovalApplicationIntentApiIntegrationTests" -v:minimal
dotnet test src\Services\CentralPms\tests\ExitPass.CentralPms.IntegrationTests\ExitPass.CentralPms.IntegrationTests.csproj --no-build --filter "FullyQualifiedName~OperatorConsoleServiceChannelStatutoryDiscountReviewApiIntegrationTests" -v:minimal
dotnet test src\Services\CentralPms\tests\ExitPass.CentralPms.IntegrationTests\ExitPass.CentralPms.IntegrationTests.csproj --no-build --filter "FullyQualifiedName~StatutoryDiscountDecisionFacadeRepositoryTests" -v:minimal
dotnet test src\Services\CentralPms\tests\ExitPass.CentralPms.IntegrationTests\ExitPass.CentralPms.IntegrationTests.csproj --no-build --filter "FullyQualifiedName~PaymentInitiation|FullyQualifiedName~CreateOrReusePaymentAttempt|FullyQualifiedName~TerminalCash" -v:minimal

docker exec exitpass-postgres psql -v ON_ERROR_STOP=1 -U exitpass -d postgres -c "SELECT version();"
docker exec exitpass-postgres psql -v ON_ERROR_STOP=1 -U exitpass -d postgres -c "DROP DATABASE IF EXISTS exitpass_statutory_canonical_only_audit_codexi WITH (FORCE);"
docker exec exitpass-postgres psql -v ON_ERROR_STOP=1 -U exitpass -d postgres -c "DROP DATABASE IF EXISTS exitpass_statutory_canonical_plus_patches_audit_codexi WITH (FORCE);"
```

Validation results:

- Application repo branch/status/head checks passed.
- Canonical DB repo branch/status/head checks passed.
- Historical DB repo status/head checks passed.
- Canonical object-source layout validation passed.
- Canonical generated full SQL apply passed.
- Central PMS alignment validation passed on canonical-only DB.
- Canonical-only statutory object checks failed for required staged/service-channel objects, as expected by the audit finding.
- Canonical-plus-patches apply and validation passed.
- Current branch post-approval application-intent patch reapplied successfully.
- Focused Central PMS Application build passed.
- Central PMS API build passed on escalated sequential retry with existing XML/nullability warnings only.
- Central PMS integration-test build passed on escalated sequential retry with existing warnings only.
- Focused unit statutory/fiscal/POS tests passed 209/209.
- Focused unit staged/review/facade tests passed 63/63.
- Post-approval application-intent API tests passed 8/8 against canonical plus patches.
- Operator Console service-channel review API tests passed 8/8 against canonical plus patches.
- Decision facade repository tests passed 5/5 against canonical plus patches.
- Payment-initiation / create-or-reuse / TerminalCash filter passed 63/63 against canonical plus patches.
- Combined integration filter including `StatutoryDiscountStagedCommandRepositoryTests` failed 1/31 due fixture reapplication drift on an already-patched DB.
- Disposable databases were dropped; final check returned zero rows for their names.

## 34. Final Authorization Lines

WebPay integration: not authorized yet
APT integration: not authorized yet
