# ExitPass App-Local DB Patch Retirement Manifest v1.0

## Result

PASSED. Central PMS canonical aligned-DB validation now passes against `exitpassdb_v1.2` generated SQL, so app-local patches covered by that output are retired for canonical validation. Files are retained for historical or legacy-local fallback use unless explicitly classified otherwise.

## Canonical Source

| Item | Value |
| --- | --- |
| Canonical DB repo | `D:\SourceCodes\exitpassdb_v1.2` |
| Canonical generated SQL | `D:\SourceCodes\exitpassdb_v1.2\build\generated\exitpass-full-object.generated.sql` |
| Canonical validation SQL | `D:\SourceCodes\exitpassdb_v1.2\scripts\validation\Validate-V13CentralPmsAlignment.sql` |
| Validation result source | `docs\v1.3\central-pms\db-alignment\ExitPass_Central_PMS_Aligned_exitpassdb_Runtime_Validation_Result_v1.0.md` |

## Classification Terms

| Classification | Meaning |
| --- | --- |
| `RETIRED_CANONICAL_SUPERSEDED` | Covered by canonical `exitpassdb_v1.2` generated SQL; must not be applied for aligned/canonical DB validation. File may remain for history or legacy-local fallback. |
| `RETAINED_HISTORICAL` | Kept only for historical/reference use; not active in canonical validation or known current local/test apply paths. |
| `RETAINED_LEGACY_LOCAL_ONLY` | Still used by a legacy local or non-canonical test path; do not remove without replacing that path. |
| `PARTIALLY_SUPERSEDED_REVIEW_REQUIRED` | Some concepts are canonical, but the patch includes divergent/older objects or naming that needs a separate decision. |
| `STILL_ACTIVE` | Not proven covered by canonical Central PMS validation; remains active or available for its current local/test scope. |

## Retired / Superseded for Canonical Validation

| Patch | Classification | Canonical coverage | Active apply posture | Notes |
| --- | --- | --- | --- | --- |
| `ExitPass_CentralPms_FiscalReferenceStatePersistence_v1.3.sql` | `RETIRED_CANONICAL_SUPERSEDED` | Fiscal issuance reference, retry/readback, semantic-hash support objects are in canonical generated SQL. | Not applied when canonical objects exist; `FiscalReferenceStatePatchHarness` retains it as a legacy fallback for older disposable DBs. | Keep file until fiscal integration tests no longer need legacy fallback support. |
| `ExitPass_OperatorConsoleSchema_v1.2.sql` | `RETIRED_CANONICAL_SUPERSEDED` | `operator_console` readiness/access, HR mapping, device binding, shift, and access evaluation objects are in canonical generated SQL. | No active source/test apply reference found. | Documentation references are historical/design references. |
| `ExitPass_ProductionPolicyImportReviewQueue_v1.2.sql` | `RETIRED_CANONICAL_SUPERSEDED` | `operator_console.production_policy_import_review_*` queue objects are in canonical generated SQL. | `OperatorConsoleProductionPolicyImportApiIntegrationTests` now applies it only if canonical review queue objects are missing. | Legacy fallback only. |
| `ExitPass_StatutoryDiscountPayableBasisApplicationSchema_v1.2.sql` | `RETIRED_CANONICAL_SUPERSEDED` | `discounts.statutory_discount_payable_basis_applications`, related function/constraints/indexes are in canonical generated SQL. | No active source/test apply reference found. | Keep as historical reference. |
| `ExitPass_StatutoryDiscountAppliedTariffSnapshotLifecycle_v1.2.sql` | `RETIRED_CANONICAL_SUPERSEDED` | Applied tariff snapshot lifecycle constraints/indexes are in canonical generated SQL. | No active source/test apply reference found. | Keep as historical reference. |
| `ExitPass_Core_CreateOrReusePaymentAttempt_v1.2.sql` | `RETIRED_CANONICAL_SUPERSEDED` | Typed `core.create_or_reuse_payment_attempt(uuid, uuid, text, text, text, uuid, timestamptz)` is in canonical generated SQL. | No active source/test apply reference found. | Keep as historical reference. |
| `ExitPass_Core_FinalizePaymentAttempt_v1.2.sql` | `RETIRED_CANONICAL_SUPERSEDED` | Typed `core.finalize_payment_attempt(uuid, text, text, uuid, timestamptz)` is in canonical generated SQL. | No active source/test apply reference found. | Keep as historical reference. |
| `ExitPass_Core_RecordPaymentConfirmation_v1.2.sql` | `RETIRED_CANONICAL_SUPERSEDED` | Typed `core.record_payment_confirmation(uuid, text, text, text, uuid, timestamptz)` is in canonical generated SQL. | No active source/test apply reference found. | Keep as historical reference. |
| `ExitPass_Core_IssueExitAuthorization_v1.2.sql` | `RETIRED_CANONICAL_SUPERSEDED` | Typed `core.issue_exit_authorization(uuid, uuid, uuid, uuid, timestamptz)` is in canonical generated SQL. | No active source/test apply reference found. | Keep as historical reference. |
| `ExitPass_Core_ConsumeExitAuthorization_v1.2.sql` | `RETIRED_CANONICAL_SUPERSEDED` | Typed `core.consume_exit_authorization(uuid, uuid, uuid, timestamptz)` is in canonical generated SQL. | No active source/test apply reference found. | Keep as historical reference. |

## Partial Supersession

| Patch | Classification | Canonical coverage | Active apply posture | Notes |
| --- | --- | --- | --- | --- |
| `ExitPass_StatutoryDiscountPolicyRegistrySchema_v1.2.sql` | `PARTIALLY_SUPERSEDED_REVIEW_REQUIRED` | Canonical output supports current policy-resolution path through `sites.sites.lgu_code` and current discount policy objects. | No active source/test apply reference found. | Patch also contains older/dedicated jurisdiction registry concepts such as `sites.jurisdictions`; do not retire/delete until a policy-registry ownership decision is made. |

## Retained Active / Out-of-Scope Patches

These patches were not proven superseded by the Central PMS aligned-exitpassdb validation result and remain available for their current local/test scopes.

| Patch | Classification | Reason |
| --- | --- | --- |
| `ExitPass_GateAuthorizationConsumedProcessingInbox_v1.2.sql` | `STILL_ACTIVE` | Gate-processing app-local scope was not part of the Central PMS canonical retirement proof. |
| `ExitPass_GateCommandLifecycle_v1.2.sql` | `STILL_ACTIVE` | Gate-command lifecycle app-local scope was not part of the Central PMS canonical retirement proof. |
| `ExitPass_GateCommandRetryFailurePolicy_v1.2.sql` | `STILL_ACTIVE` | Gate-command retry policy app-local scope was not part of the Central PMS canonical retirement proof. |
| `ExitPass_HikCentralGateActionAudit_v1.2.sql` | `STILL_ACTIVE` | HikCentral/gate audit app-local scope was not part of the Central PMS canonical retirement proof. |
| `ExitPass_PaymentProviderRoutingPolicy_v1.2.sql` | `STILL_ACTIVE` | Still referenced by Payment Orchestrator routing integration tests. |
| `ExitPass_PayMongoPaymentRailReferenceData_v1.2.sql` | `STILL_ACTIVE` | Still referenced by Payment Orchestrator persistence tests. |
| `ExitPass_QrphPayMongoRoutingOverride_v1.2.sql` | `STILL_ACTIVE` | Still referenced by Payment Orchestrator routing integration tests. |

## Active References Found

| Reference | Posture |
| --- | --- |
| `src\Services\CentralPms\tests\ExitPass.CentralPms.IntegrationTests\Shared\FiscalReferenceStatePatchHarness.cs` | Legacy fallback only. It applies `ExitPass_CentralPms_FiscalReferenceStatePersistence_v1.3.sql` only when canonical fiscal reference objects are missing. |
| `src\Services\CentralPms\tests\ExitPass.CentralPms.IntegrationTests\Api\OperatorConsoleProductionPolicyImportApiIntegrationTests.cs` | Updated in this slice to apply `ExitPass_ProductionPolicyImportReviewQueue_v1.2.sql` only when canonical review queue objects are missing. |
| `src\Services\PaymentOrchestrator\tests\...` | Payment Orchestrator patches remain out of scope and active for their existing tests. |

## Rules for Future Work

- For aligned/canonical Central PMS validation, start from `exitpassdb_v1.2\build\generated\exitpass-full-object.generated.sql`.
- Do not apply `RETIRED_CANONICAL_SUPERSEDED` patches on top of canonical generated SQL.
- New database object changes should be made in `exitpassdb_v1.2` object source first.
- Keep app-local patches only for historical review, old local rebuild paths, or explicit legacy fallback tests.
- Do not delete or move patches classified `PARTIALLY_SUPERSEDED_REVIEW_REQUIRED` or `STILL_ACTIVE` without a separate proof slice.
