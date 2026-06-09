# Operator Console Production Policy Dedicated Registry Test Matrix v1

## 1. Title And Purpose

This document is the production statutory discount policy test matrix and dedicated-registry integration validation plan for Operator Console.

It follows #255 DB baseline implementation in `D:\SourceCodes\ExitPass_DBv1.2` and #256 application resolver/readiness alignment in `D:\SourceCodes\ExitPass`. It is required before production statutory discount auto-application can be considered.

The dedicated registry target is:

- `discounts.statutory_discount_policy_registry`

The transitional compatibility table remains:

- `discounts.discount_policy_references`

Production statutory discount auto-application remains NO-GO until verified production Senior Citizen and PWD policy rows exist in a governed baseline or approved import/admin flow and all dedicated-registry validation passes.

## 2. Scope

In scope:

- dedicated registry availability validation
- policy readiness classification validation
- production fail-closed and manual-review behavior
- compatibility fallback validation
- Senior Citizen policy tests
- PWD policy tests
- evidence rule validation
- site, site group, and jurisdiction scope validation
- effective date validation
- sandbox/dev row rejection
- audit/report visibility validation
- no payment, gate, coupon, or reconciliation mutation checks

Out of scope:

- adding production policy rows
- modifying DB baseline
- backend implementation changes
- frontend implementation changes
- WebPay
- payment provider routing
- AUB
- coupon validation
- reconciliation
- HikCentral or gate implementation
- raw evidence, OCR, or automated ID validation

## 3. Preconditions

- #255 DB repo baseline is merged into the live-aligned DB baseline branch.
- Local test DB is rebuilt or aligned from the `D:\SourceCodes\ExitPass_DBv1.2` baseline, not from ad hoc local SQL.
- `discounts.statutory_discount_policy_registry` exists in the local test DB for dedicated-registry integration validation.
- `discounts.discount_policy_references` still exists for compatibility fallback validation.
- No local DB drift is promoted as baseline.
- Policy test data is prepared through an approved isolated test fixture or disposable test setup.
- Production policy rows remain absent unless explicitly approved through Legal/Product/Compliance/Ops governance.
- Test fixture rows use non-production policy codes and are transactional, disposable, or resettable.

Current local schema inspection on June 9, 2026 found the app DB is still compatibility-only: `discounts.discount_policy_references` exists, while `discounts.statutory_discount_policy_registry` and the #255 dedicated-registry enum types are not present. Dedicated-registry integration tests should therefore be run only after rebuilding or aligning the local test DB from the DB repo baseline.

## 4. Environment Validation Steps

Run these as read-only checks before dedicated-registry integration tests:

1. Confirm `discounts.statutory_discount_policy_registry` exists.
2. Confirm `discounts.discount_policy_references` still exists.
3. Confirm enum types and values exist:
   - `discounts.policy_verification_status_enum`: `LEAD_UNVERIFIED`, `VERIFIED_SECONDARY`, `VERIFIED_OFFICIAL`, `APPROVED_FOR_PILOT`, `ACTIVE_APPROVED`, `PROPOSED_ONLY`, `REJECTED`
   - `discounts.parking_benefit_type_enum`
   - `discounts.discount_base_scope_enum`
   - `discounts.beneficiary_residency_scope_enum`
   - reused entitlement, policy status, policy level, policy type, policy resolution basis, and evidence enums
4. Confirm `sites.site_groups` and `sites.sites` exist.
5. Confirm the dedicated registry has FKs to existing stable site and identity tables where represented by the DB repo baseline.
6. Confirm the readiness script emits `DEDICATED_REGISTRY_PRESENT` when the dedicated registry exists.
7. Confirm `scripts/operator-console/Verify-ProductionPolicyRegistryReadiness.sql` remains read-only and contains no mutation keywords after comment stripping.
8. Confirm no production LGU policy rows were inserted as part of environment setup unless that setup is a formally approved production-like validation run.

## 5. Test Data Strategy

- Do not add production LGU rows in this slice.
- Use isolated integration-test fixture data only.
- Use clearly non-production policy codes, for example `TEST_OC_POLICY_SENIOR_READY` and `TEST_OC_POLICY_PWD_READY`.
- Do not commit fixture rows as production seed or production reference data.
- Fixture setup must be transactional, disposable, or cleaned by the integration-test harness.
- Cover Senior Citizen and PWD separately.
- Cover local ordinance, site policy, site-group policy, jurisdiction-code policy, and national fallback behavior separately.
- Do not include raw evidence, identity document numbers, production IDs, credentials, private keys, or personal data in fixture rows.
- Any test row that uses sandbox/test/dev/E2E markers must be expected to classify as `SANDBOX_ONLY` and must not be treated as production authority.

## 6. Test Matrix

| Test ID | Category | Scenario | Registry Table State | Policy Row Setup | Environment | Expected Policy Readiness Classification | Expected API / Service Result | Expected Draft Behavior | Expected Reporting Result | Boundary Mutation Expectation | Priority |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| OC-DR-001 | Registry capability | Dedicated registry present | Dedicated present, compatibility present | No row required | Test DB rebuilt from #255 | `DEDICATED_REGISTRY_PRESENT` in readiness SQL | Repository capability chooses dedicated path | No draft attempted | Availability report shows dedicated source | No payment, exit, coupon, gate, or reconciliation rows | P0 |
| OC-DR-002 | Registry capability | Dedicated registry absent, compatibility fallback | Compatibility only | Existing sandbox/compatibility rows only | Current local/dev | `COMPATIBILITY_TABLE_ONLY` | Repository uses compatibility path | Non-production sandbox remains usable | Availability report shows compatibility source | No boundary mutations | P0 |
| OC-DR-003 | Registry capability | Both present, dedicated registry preferred | Dedicated present, compatibility present | Matching row exists in both tables | Test DB | Dedicated row classification wins | API returns dedicated policy fields and verification status | Draft uses dedicated readiness result | Report source is dedicated registry | No boundary mutations | P0 |
| OC-DR-004 | Registry capability | Registry present but no matching rows | Dedicated present | No matching entitlement/scope/fallback row | Production-like | `MISSING_REQUIRED_POLICY` | Policy not resolved; manual review required | Production draft blocked | Report shows missing policy reason | No boundary mutations | P0 |
| OC-DR-005 | Ready verified policies | Senior Citizen active approved official row | Dedicated present | `ACTIVE`, `ACTIVE_APPROVED` or `VERIFIED_OFFICIAL`, `SENIOR_CITIZEN_ID`, valid RA/local reference, valid scope | Production-like fixture | `READY_VERIFIED` | Policy resolved with Senior Citizen policy code | Draft creation allowed only for this ready row | Report shows code, verification status, scope, source | No payment/gate/coupon/reconciliation mutations | P0 |
| OC-DR-006 | Ready verified policies | PWD active approved official row | Dedicated present | `ACTIVE`, `ACTIVE_APPROVED` or `VERIFIED_OFFICIAL`, `PWD_ID`, valid RA/local reference, valid scope | Production-like fixture | `READY_VERIFIED` | Policy resolved with PWD policy code | Draft creation allowed only for this ready row | Report shows code, verification status, scope, source | No boundary mutations | P0 |
| OC-DR-007 | Manual review policies | Approved for pilot | Dedicated present | `ACTIVE`, `APPROVED_FOR_PILOT`, approved metadata present, valid scope | Production-like fixture | `READY_WITH_MANUAL_REVIEW` | Policy may resolve but requires manual review | No automatic approved-ready draft unless explicitly designed | Report shows pilot/manual-review status | No boundary mutations | P0 |
| OC-DR-008 | Manual review policies | Verified secondary source | Dedicated present | `ACTIVE`, `VERIFIED_SECONDARY`, reviewed metadata present | Production-like fixture | `CONFIGURED_BUT_UNVERIFIED` or manual-review result per classifier | Production auto-resolution fail-closed or manual-review only | Draft blocked or manual-review only | Report shows secondary verification | No boundary mutations | P0 |
| OC-DR-009 | Manual review policies | Explicit manual-review readiness | Dedicated present | Valid row with manual-review benefit or unresolved operational caveat | Production-like fixture | `READY_WITH_MANUAL_REVIEW` | API flags `RequiresManualReview=true` | Does not persist automatic approved-ready draft | Report shows manual-review reason | No boundary mutations | P1 |
| OC-DR-010 | Manual review policies | Mixed residency scope | Dedicated present | `beneficiary_residency_scope=MIXED_OR_CONFLICTING` | Production-like fixture | `READY_WITH_MANUAL_REVIEW` or `CONFIGURED_BUT_UNVERIFIED` | Manual review required | Draft blocked or manual-review only | Report shows residency caveat, no raw evidence | No boundary mutations | P1 |
| OC-DR-011 | Not-ready policies | Lead unverified | Dedicated present | `verification_status=LEAD_UNVERIFIED` | Production-like fixture | `CONFIGURED_BUT_UNVERIFIED` | Policy not production-resolved | Draft blocked | Report shows unverified status | No boundary mutations | P0 |
| OC-DR-012 | Not-ready policies | Proposed only | Dedicated present | `verification_status=PROPOSED_ONLY` | Production-like fixture | `CONFIGURED_BUT_UNVERIFIED` or `EXPIRED_OR_INACTIVE` if inactive | Policy not production-ready | Draft blocked | Report shows proposed-only status | No boundary mutations | P0 |
| OC-DR-013 | Not-ready policies | Rejected | Dedicated present | `verification_status=REJECTED` | Production-like fixture | `CONFIGURED_BUT_UNVERIFIED` or `EXPIRED_OR_INACTIVE` if inactive | Policy not production-ready | Draft blocked | Report shows rejected status | No boundary mutations | P0 |
| OC-DR-014 | Not-ready policies | Inactive, suspended, retired, or superseded | Dedicated present | `policy_status` not `ACTIVE` or superseded linkage active | Production-like fixture | `EXPIRED_OR_INACTIVE` | Policy not production-resolved | Draft blocked | Report shows inactive status | No boundary mutations | P0 |
| OC-DR-015 | Not-ready policies | Expired effective date | Dedicated present | `effective_to` before test date | Production-like fixture | `EXPIRED_OR_INACTIVE` | Policy not production-ready | Draft blocked | Report shows expired window | No boundary mutations | P0 |
| OC-DR-016 | Not-ready policies | Future effective date | Dedicated present | `effective_from` after test date | Production-like fixture | `EXPIRED_OR_INACTIVE` | Policy not production-ready | Draft blocked | Report shows future effective window | No boundary mutations | P0 |
| OC-DR-017 | Not-ready policies | Missing source reference | Dedicated present | Blank or missing `source_reference` in fixture attempt | Fixture setup should fail DB constraint; if forced, classifier not ready | `NOT_READY` or DB setup failure | API must not auto-apply | Draft blocked | Report shows source blocker if row exists | No boundary mutations | P0 |
| OC-DR-018 | Not-ready policies | Missing evidence rule | Dedicated present | `requires_evidence=true`, `required_evidence_type` missing, or workflow evidence required but policy evidence false | Production-like fixture | `MISSING_EVIDENCE_RULE` | Policy not production-ready | Draft blocked | Report shows evidence blocker | No boundary mutations | P0 |
| OC-DR-019 | Not-ready policies | Missing site/site-group/jurisdiction scope | Dedicated present | Local/site policy with no `jurisdiction_code`, `site_group_id`, or `site_id` | Production-like fixture | `MISSING_SITE_MAPPING` or DB setup failure if constrained | Policy not resolved to scope | Draft blocked | Report shows scope blocker | No boundary mutations | P0 |
| OC-DR-020 | Not-ready policies | Wrong evidence type for entitlement | Dedicated present | Senior row uses `PWD_ID` or PWD row uses `SENIOR_CITIZEN_ID` | Fixture setup should fail DB constraint; if forced, readiness not ready | `MISSING_EVIDENCE_RULE` or setup failure | Draft blocked | Report shows evidence mismatch | No boundary mutations | P0 |
| OC-DR-021 | Not-ready policies | Sandbox/dev/test/E2E markers | Dedicated present | `TEST_OC_POLICY_*`, `SANDBOX`, `DEV`, or `E2E` markers | Production-like fixture | `SANDBOX_ONLY` | Policy not production authority | Draft blocked in production | Report marks row non-production | No boundary mutations | P0 |
| OC-DR-022 | Fallback behavior | Compatibility table resolves sandbox in non-production | Compatibility only | Existing deterministic sandbox row | Development | `SANDBOX_ONLY` but policy can resolve in non-production | API returns sandbox policy for validation | Draft may be allowed only in non-production fixture flow | Report marks sandbox-only | No boundary mutations | P0 |
| OC-DR-023 | Fallback behavior | Production does not auto-apply compatibility sandbox row | Compatibility only | Sandbox compatibility row only | Production | `SANDBOX_ONLY` | Policy not production-resolved | Draft blocked | Report marks sandbox-only | No boundary mutations | P0 |
| OC-DR-024 | Fallback behavior | Production missing dedicated registry row | Dedicated present | No dedicated production-ready row; compatibility row may exist | Production-like | `MISSING_REQUIRED_POLICY` or manual-review fail-closed | Dedicated path is preferred; compatibility does not mask missing dedicated readiness | Draft blocked | Report shows dedicated source and blocker | No boundary mutations | P0 |
| OC-DR-025 | Draft creation behavior | Production draft succeeds only for ready verified | Dedicated present | `READY_VERIFIED` Senior or PWD row | Production-like fixture | `READY_VERIFIED` | Draft API returns success with policy code | Draft persisted only with ready verified policy | Draft report shows safe policy fields | No payment/gate/coupon/reconciliation mutations | P0 |
| OC-DR-026 | Draft creation behavior | Production draft blocked for sandbox-only | Dedicated or compatibility | Sandbox/test/dev row | Production-like | `SANDBOX_ONLY` | API returns controlled fail-closed/manual-review result | Draft not persisted as approved-ready | Report shows sandbox-only blocker | No boundary mutations | P0 |
| OC-DR-027 | Draft creation behavior | Production draft blocked for not-ready | Dedicated present | Missing evidence, inactive, missing source, or missing scope | Production-like | Matching not-ready classification | API returns blocked result | Draft not persisted | Report shows blocker | No boundary mutations | P0 |
| OC-DR-028 | Draft creation behavior | Manual-review policy does not persist automatic approved-ready draft | Dedicated present | `APPROVED_FOR_PILOT` or `READY_WITH_MANUAL_REVIEW` | Production-like | `READY_WITH_MANUAL_REVIEW` | API flags manual review | No automatic approved-ready draft unless design explicitly permits manual-review draft state | Report shows manual-review status | No boundary mutations | P0 |
| OC-DR-029 | Audit/reporting | Report shows policy code | Dedicated present | Any resolved fixture row | Test | Same as row readiness | API response includes policy code | No draft requirement | Report includes policy code | No boundary mutations | P1 |
| OC-DR-030 | Audit/reporting | Report shows readiness classification | Dedicated present | Ready, manual-review, and not-ready fixture rows | Test | Row-specific classification | API exposes readiness classification | Draft follows classification | Report includes classification | No boundary mutations | P1 |
| OC-DR-031 | Audit/reporting | Report shows verification status | Dedicated present | Rows across verification statuses | Test | Row-specific classification | API exposes or report can display verification status | Draft follows classification | Report includes safe verification status | No boundary mutations | P1 |
| OC-DR-032 | Audit/reporting | Report does not expose raw evidence | Dedicated present | Fixture includes no raw evidence; evidence references only | Test | Not applicable | API does not return raw evidence | Draft stores only allowed references | Report omits raw evidence and personal data | No boundary mutations | P0 |
| OC-DR-033 | Audit/reporting | Report remains read-only | Dedicated present | Any fixture data | Test | Not applicable | Report endpoint uses read-only path | No draft created by report | Report query only | No boundary mutations | P0 |
| OC-DR-034 | Boundary mutation | No payment attempts | Any | Any policy fixture | Test | Not applicable | Policy validation does not call payment flow | No payment draft side effect | Boundary query count unchanged | `core.payment_attempts` unchanged | P0 |
| OC-DR-035 | Boundary mutation | No payment confirmations | Any | Any policy fixture | Test | Not applicable | No payment confirmation call | No confirmation side effect | Boundary query count unchanged | `core.payment_confirmations` unchanged | P0 |
| OC-DR-036 | Boundary mutation | No exit authorizations | Any | Any policy fixture | Test | Not applicable | No exit authorization call | No exit side effect | Boundary query count unchanged | `core.exit_authorizations` unchanged | P0 |
| OC-DR-037 | Boundary mutation | No coupon applications | Any | Any policy fixture | Test | Not applicable | No coupon call | No coupon side effect | Boundary query count unchanged | `coupons.coupon_applications` unchanged | P0 |
| OC-DR-038 | Boundary mutation | No reconciliation rows | Any | Any policy fixture | Test | Not applicable | No reconciliation write | No reconciliation side effect | Boundary query count unchanged | Reconciliation tables unchanged | P0 |
| OC-DR-039 | Boundary mutation | No gate records | Any | Any policy fixture | Test | Not applicable | No gate authorization call | No gate side effect | Boundary query count unchanged | Gate tables unchanged | P0 |

## 7. Readiness SQL Validation Plan

1. Run `scripts/operator-console/Verify-ProductionPolicyRegistryReadiness.sql` before dedicated-registry tests.
2. Run `scripts/operator-console/Run-ProductionPolicyRegistryReadinessCheck.ps1 -WarnOnly` for local/dev environments where blockers are expected.
3. After local DB rebuild from #255, expect the table availability result to emit `DEDICATED_REGISTRY_PRESENT`.
4. Expect production readiness to remain fail/no-go until approved production Senior Citizen and PWD rows exist.
5. Expect pass only in an isolated test context with approved verified production-like fixture rows, valid source references, valid evidence rules, and valid scope.
6. Confirm sandbox/dev/test/E2E rows are still classified as non-production.
7. Confirm the SQL remains read-only by running the wrapper mutation-keyword guard or equivalent static check.

## 8. Backend Test Plan

- Unit tests for `OperatorConsolePolicyReadinessClassifier`.
- Repository tests proving dedicated registry is preferred when both policy tables exist.
- Repository tests proving compatibility fallback is used when the dedicated registry is absent.
- Service tests for production fail-closed and manual-review behavior.
- API tests for policy resolution response DTOs, including policy code, verification status, readiness classification, and manual-review flags.
- Draft API tests for blocked not-ready policies, sandbox-only policies, missing evidence rules, and ready verified policies.
- Audit/report API tests for safe display fields and absence of raw evidence.
- SQL static tests confirming `Verify-ProductionPolicyRegistryReadiness.sql` has no mutation keywords after comments are stripped.
- Conditional integration tests that skip dedicated-registry DB fixtures when the local DB remains compatibility-only.

## 9. Integration Validation Sequence

1. Sync DB repo baseline.
2. Rebuild or align local test DB from DB repo baseline.
3. Confirm dedicated registry exists.
4. Run readiness SQL.
5. Run policy resolution unit tests.
6. Run dedicated registry repository tests.
7. Run policy resolution API tests.
8. Run draft fail-closed tests.
9. Run audit/report tests.
10. Run full Operator Console statutory discount controlled validation only if fixture data is ready.
11. Run boundary mutation SQL.

## 10. Boundary SQL Checks

Use read-only count snapshots before and after validation flows. The exact query shape should follow the repo's integration-test conventions and table availability checks, but the boundary tables to cover are:

- `core.payment_attempts`
- `core.payment_confirmations`
- `core.exit_authorizations`
- `coupons.coupon_applications`
- `gates.gate_authorization_consumptions`, if present
- reconciliation tables such as `reconciliation.reconciliation_items`, if present

Expected result: Operator Console statutory discount policy resolution, readiness inspection, draft fail-closed validation, and reporting checks do not create payment attempts, payment confirmations, exit authorizations, coupon applications, reconciliation rows, or gate records.

## 11. Go/No-Go Criteria

- GO for compatibility-only sandbox validation using deterministic non-production fixture policies.
- GO for dedicated-registry integration testing after the local test DB is rebuilt or aligned from the #255 DB repo baseline.
- CONDITIONAL GO for controlled operational pilot only with manually verified, site-approved policy evidence and documented manual-review controls.
- NO-GO for full production statutory discount auto-application until verified production policy rows exist, dedicated-registry tests pass, readiness SQL reports no blockers, and no boundary mutation regressions are detected.

## 12. Risks And Mitigations

| Risk | Impact | Mitigation |
| --- | --- | --- |
| Local DB not rebuilt from DB repo baseline | Dedicated-registry tests cannot exercise the actual table. | Gate dedicated integration tests on read-only table availability checks. |
| Fixture rows accidentally treated as production policy | Test policy authority could leak into rollout decisions. | Use `TEST_OC_POLICY_*`, transactional setup, and explicit `SANDBOX_ONLY` expectations for marked rows. |
| Compatibility fallback masks missing dedicated registry | Runtime appears usable while production registry is absent. | Test both table-available and compatibility-only modes; require `DEDICATED_REGISTRY_PRESENT` before dedicated validation signoff. |
| Manual-review rows accidentally auto-applied | Pilot or secondary-source rows become production authority. | Assert `RequiresManualReview=true` and draft blocking or manual-review-only draft behavior. |
| Policy scope ambiguity | Benefits apply to wrong site, group, or jurisdiction. | Test site, site group, jurisdiction code, and missing-scope cases separately. |
| Evidence requirement mismatch | Senior Citizen or PWD approval proceeds with wrong evidence type. | Test `SENIOR_CITIZEN_ID` and `PWD_ID` separately and include mismatch cases. |
| Reporting exposes unsafe data | Raw evidence or personal data appears in reports. | Assert reports display safe policy metadata only. |
| Boundary side effects | Policy validation creates payment, gate, coupon, or reconciliation records. | Run before/after boundary count checks for all listed tables. |

## 13. Recommended Next Slices

Recommended bounded next slices:

- #258 Dedicated registry integration test fixture and repository/API tests
- #259 Operator Console policy readiness UX/reporting indicators
- #260 Production policy fixture review package
- #261 Production policy import/admin API design

Recommended immediate next slice: #258 Dedicated registry integration test fixture and repository/API tests.

Reason: the DB baseline and resolver/readiness alignment are now defined, but the dedicated-registry path still needs controlled integration fixtures and repository/API validation after the local test DB is rebuilt from the DB repo baseline.

## 14. Boundary Confirmations

- No backend behavior changes.
- No frontend behavior changes.
- No database, DDL, migration, or seed mutations.
- No production policy seed data added.
- No WebPay changes.
- No payment/provider routing changes.
- No AUB changes.
- No coupon, reconciliation, HikCentral, or gate changes.
- No sensitive credentials, production IDs, private keys, raw evidence, or personal data added.
