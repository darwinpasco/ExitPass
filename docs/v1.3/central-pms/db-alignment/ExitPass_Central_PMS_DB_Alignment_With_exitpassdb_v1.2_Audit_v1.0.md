# ExitPass Central PMS DB Alignment With exitpassdb_v1.2 Audit v1.0

## Result

AUDIT COMPLETE.

This audit compared current ExitPass v1.3 Central PMS and Operator Console database expectations against the canonical `exitpassdb_v1.2` repository on `develop`. No database repository files, Central PMS runtime code, tests, POS Server code, migrations, or seed scripts were modified.

## Executive summary

The canonical `exitpassdb_v1.2` repository contains the core v1.2 Central PMS database baseline: identity/RBAC tables, sites, core sessions/payments/tariff snapshots, statutory discount validation/evidence/policy reference tables, policy registry/review tables under `discounts`, audit/reconciliation tables, and `operations.operator_action_logs`.

Current ExitPass v1.3 source has moved beyond that baseline. The main gaps are not broad redesign gaps; they are known app-local extensions that already exist in the ExitPass repo as patches, scripts, or tests but are not yet integrated into the canonical DB repo:

- `core.fiscal_issuance_references` and related fiscal retry/readback/semantic-hash tables.
- `operator_console` schema objects for HR identity mappings, device bindings, shifts, access evaluations, and access-evaluation reasons.
- `discounts.statutory_discount_payable_basis_applications`, its enums, indexes, guardrails, and `discounts.apply_statutory_discount_payable_basis`.
- Operator Console production policy import review queue tables under `operator_console`, while the canonical DB repo has a separate `discounts.statutory_discount_policy_import_review_*` model.
- v1.3 granular permission seeds and seven UAT role bundles.
- UAT fixture seed support for the Management Platform and statutory discount requester/reviewer flows.

Recommended DB repo branch: `feature/v13-central-pms-db-alignment`.

## Repositories inspected

| Repository | Path | Branch/status |
| --- | --- | --- |
| ExitPass primary repo | `D:\SourceCodes\ExitPass` | `feature/central-pms-db-alignment-exitpassdb-v12-audit` |
| Canonical database repo | `D:\SourceCodes\exitpassdb_v1.2` | `develop`, tracking `origin/develop` |

The database repo was cloned locally because it was not present at the expected path. It was then switched to `develop`, pulled from `origin develop`, and left unchanged.

## v1.3 source areas inspected

| Area | Files/directories inspected |
| --- | --- |
| Central PMS infrastructure | `src/Services/CentralPms/src/ExitPass.CentralPms.Infrastructure` |
| Central PMS API/application tests | `src/Services/CentralPms/tests` targeted source searches |
| Fiscal issuance | `FiscalIssuance/*`, `infra/db/patches/ExitPass_CentralPms_FiscalReferenceStatePersistence_v1.3.sql` |
| Operator Console persistence | `OperatorConsole/*Repository.cs`, `OperatorConsole/*Writer.cs`, access/readiness repositories |
| Statutory discounts | Draft, evidence, decision, read, policy resolution, apply payable basis writers/repositories |
| Management Platform identity/RBAC | `ManagementPlatformIdentityRbacInventoryRepository.cs`, management-platform result docs |
| Local/UAT scripts | `scripts/operator-console/*`, `scripts/management-platform/*` |
| v1.3 documents | `docs/v1.3/central-pms`, `docs/v1.3/operator-console`, `docs/v1.3/management-platform`, plus relevant POS/Invoicing and POS Server docs found by recursive search |

## exitpassdb_v1.2 areas inspected

| Area | Files/directories inspected |
| --- | --- |
| Canonical schema | `schema/schema.sql`, `schema/02_enums.generated.sql`, `schema/03_tables.generated.sql`, `schema/04_foreign_keys.generated.sql`, `schema/04a_unique_constraints.generated.sql`, `schema/05_indexes.generated.sql`, `schema/07_functions.generated.sql` |
| Reference data | `reference-data/ExitPass_Reference_Data_v1.2.sql`, `reference-data/templates/statutory_discount_policy_registry_template.csv` |
| Migrations | `migrations/*` |
| DB docs/proposals | `docs/*` |

## Current alignment summary

| Object family | Current alignment | Classification |
| --- | --- | --- |
| Core identity tables | `identity.users`, `identity.roles`, `identity.permissions`, `identity.user_roles`, `identity.role_permissions`, and `identity.service_identities` exist in the DB repo and match the current inventory API baseline. | ALIGNED |
| Baseline RBAC reference data | v1.2 roles/permissions exist, but v1.3 seven role bundles and granular permission codes are only in ExitPass scripts/source. | APP_EXTENSION_NOT_IN_DB_REPO |
| Sites/site groups | `sites.site_groups`, `sites.sites`, `sites.device_assignments`, and lanes exist. | ALIGNED |
| Operator Console device/shift access model | Current source/scripts expect `operator_console.hr_identity_mappings`, `operator_device_bindings`, `operator_device_assignment_history`, `operator_shifts`, `operator_access_evaluations`, and `operator_access_evaluation_reasons`; these are not in DB repo schema. | APP_EXTENSION_NOT_IN_DB_REPO |
| Operator action logs | DB repo has `operations.operator_action_logs`; v1.3 source records Operator Console actions through this table using generic action type plus action reason/action notes metadata. | ALIGNED_WITH_CONVENTION |
| Fiscal issuance reference/status | Current source requires `core.fiscal_issuance_references` and fiscal retry/readback/semantic-hash support tables; DB repo does not contain them. | APP_EXTENSION_NOT_IN_DB_REPO |
| Statutory validation/evidence/policy references | DB repo has `discounts.statutory_discount_validations`, `discounts.discount_evidence_references`, and `discounts.discount_policy_references`; source uses them. | PARTIALLY_ALIGNED |
| Statutory policy registry | DB repo has `discounts.statutory_discount_policy_registry`; ExitPass also carries app-local policy registry patch history. | ALIGNED_WITH_PATCH_HISTORY |
| Statutory payable-basis application | Current source requires durable `discounts.statutory_discount_payable_basis_applications`, enums, indexes, triggers, and function; DB repo schema does not contain them. | APP_EXTENSION_NOT_IN_DB_REPO |
| Policy import review | DB repo has `discounts.statutory_discount_policy_import_review_*`; current app uses `operator_console.production_policy_import_review_*`. | SOURCE_USES_DIFFERENT_NAME |
| Payments/payable basis | `core.tariff_snapshots`, `core.payment_attempts`, and `core.payment_confirmations` exist. Applied discount lifecycle indexes/constraints are app-local patch objects. | PARTIALLY_ALIGNED |
| Audit/reconciliation | DB repo has baseline `audit.*`, `reconciliation.*`, and `operations.operator_action_logs`. v1.3 action/reporting semantics mostly ride on `operator_action_logs` JSON/reason metadata. | PARTIALLY_ALIGNED |
| POS Server fiscal document tables | POS Server-owned objects are outside Central PMS DB repo scope. | POS_SERVER_OWNED |

## Object family comparison

| Family | DB repo baseline | Current v1.3 source expectation | Delta |
| --- | --- | --- | --- |
| Identity/RBAC | Core identity tables and v1.2 baseline roles/permissions exist. | Management Platform inventory reads identity tables; UAT seed adds seven role bundles and granular permissions. | Add v1.3 reference-data seeds; no schema redesign required for basic roles/permissions. |
| Management Platform | No Management Platform UI tables; identity tables support read-only inventory. | Inventory API surfaces users, role assignments, device bindings, shifts, and gaps. | Seed and visibility objects are app-local; no mutation/admin audit table exists yet. |
| Operator Console | `operations.operator_action_logs` exists. No `operator_console` schema in canonical schema. | Source and UAT scripts use `operator_console.*` access/readiness tables when present. | Promote `ExitPass_OperatorConsoleSchema_v1.2.sql` style objects into DB repo. |
| Statutory discounts | Core validation, evidence, policy reference, and policy registry objects exist. | Source additionally uses payable-basis application table/function and UAT seed rows. | Promote payable-basis application patch and align policy registry/app schema. |
| Fiscal issuance | No Central PMS fiscal reference table in canonical DB repo. | Fiscal issuance reference/status/readback/retry/semantic hash logic requires `core.fiscal_issuance_references` and support tables. | Promote v1.3 fiscal reference persistence patch to DB repo. |
| Payments/payable | Payment and tariff tables exist. | Discounted applied tariff flow uses active/superseded snapshot lifecycle and payment attempts from applied snapshot. | Add app-local statutory applied tariff snapshot lifecycle indexes/constraints if absent. |
| Policy import review | Canonical DB repo has discount-owned policy import review tables. | Current app uses operator-console-owned production policy import review queue tables. | Ownership/name decision needed before DB repo implementation. |
| Sites/devices/shifts | Sites/site groups/device assignments exist. | Operator Console device binding and shifts use dedicated `operator_console` objects. | Keep `sites.device_assignments` as general device assignment; add operator-console trusted browser/device and shift objects. |
| Audit/reporting | Audit tables and operator action logs exist. | Fiscal status/void/statutory audit reports read safe action-log metadata and domain rows. | Add indexes/action-code conventions if reporting performance requires them. |

## Identity/RBAC deltas

| Item | Finding | Classification |
| --- | --- | --- |
| `identity.users`, `identity.roles`, `identity.permissions` | Canonical tables exist and current source reads them safely. | ALIGNED |
| `identity.user_roles`, `identity.role_permissions` | Canonical assignment tables exist and Management Platform inventory can read them. | ALIGNED |
| v1.3 seven UAT role bundles | ExitPass scripts seed `SYSTEM_RBAC_ADMINISTRATOR`, `PLATFORM_ADMINISTRATOR`, `OPERATIONS_SUPERVISOR`, `OPERATOR_SUPPORT_STAFF`, `FINANCE_RECONCILIATION_ANALYST`, `COMPLIANCE_POLICY_ADMINISTRATOR`, and `EXECUTIVE_MANAGEMENT`; canonical reference data still has v1.2 role codes such as `SYSTEM_ADMIN`, `OPERATIONS_MANAGER`, `SITE_OPERATOR`, and `SUPPORT_AGENT`. | APP_EXTENSION_NOT_IN_DB_REPO |
| Granular v1.3 permissions | App/scripts use permissions such as `management-platform.identity-rbac.inventory.read`, `statutory-discounts.*`, `fiscal-issuance.status.read`, `fiscal-issuance.void.command`, and audit/report permissions. Canonical reference data has v1.2 permissions such as `discounts.validate_statutory`, `audit.read`, and `identity.manage`. | APP_EXTENSION_NOT_IN_DB_REPO |
| User/site/device/shift assignments | Role assignments are supported; operator site/device/shift scope is app-local under `operator_console`. | APP_EXTENSION_NOT_IN_DB_REPO |
| Admin audit for user/role changes | Target scope exists in v1.3 docs, but no confirmed canonical admin mutation/audit implementation. | DEFERRED_TARGET |

## Management Platform deltas

| Item | Finding | Classification |
| --- | --- | --- |
| Read-only inventory API storage | Inventory can read canonical identity tables. It reports gaps when `operator_console.operator_device_bindings` or `operator_console.operator_shifts` are missing. | PARTIALLY_ALIGNED |
| UAT identity/RBAC seed | `scripts/management-platform/Seed-ManagementPlatformUatIdentityRbac.sql` is in ExitPass only. | APP_EXTENSION_NOT_IN_DB_REPO |
| Role bundle catalog | Static target role bundles exist in app source/docs; persisted roles can be seeded through existing identity tables. | APP_EXTENSION_NOT_IN_DB_REPO |
| Management Platform UI/admin tables | Not required in this slice; no canonical DB objects identified. | DEFERRED_TARGET |
| Assignment inventory | Current app can read role assignments and optional operator shift/device bindings. Broader admin/global site-scope assignment persistence remains incomplete. | DESIGN_DECISION_NEEDED |

## Operator Console deltas

| Item | Finding | Classification |
| --- | --- | --- |
| `operations.operator_action_logs` | Present in canonical DB repo. Source uses `action_type = CONTROLLED_RECHECK`, stores actual action in `action_reason_code`, and stores safe JSON metadata in `action_notes`. | ALIGNED_WITH_CONVENTION |
| Operator Console action codes | v1.3 action codes such as `VIEW_FISCAL_ISSUANCE_STATUS` and `VOID_FISCAL_DOCUMENT` are not enum values in `operations.operator_action_type_enum`; they are recorded as reason codes. | DESIGN_DECISION_NEEDED |
| `operator_console.hr_identity_mappings` | Required by readiness/preflight scripts and app-local patch; not in canonical DB repo schema. | APP_EXTENSION_NOT_IN_DB_REPO |
| `operator_console.operator_device_bindings` | Required by readiness/preflight scripts and Management Platform inventory gap reporting; not in canonical DB repo schema. | APP_EXTENSION_NOT_IN_DB_REPO |
| `operator_console.operator_device_assignment_history` | Required by access readiness/preflight; not in canonical DB repo schema. | APP_EXTENSION_NOT_IN_DB_REPO |
| `operator_console.operator_shifts` | Required by readiness/preflight and identity/RBAC inventory site-scope readback; not in canonical DB repo schema. | APP_EXTENSION_NOT_IN_DB_REPO |
| `operator_console.operator_access_evaluations` and reasons | Integration tests and app-local patch support these, while current writer also uses `operations.operator_action_logs`; not in canonical DB repo schema. | DESIGN_DECISION_NEEDED |
| Production policy import review queue | Current app persists `operator_console.production_policy_import_review_*` objects; canonical DB repo has `discounts.statutory_discount_policy_import_review_*`. | SOURCE_USES_DIFFERENT_NAME |

## Statutory discount deltas

| Item | Finding | Classification |
| --- | --- | --- |
| `discounts.discount_policy_references` | Canonical DB repo table exists with policy code/version, entitlement, scope, evidence flags, and status fields. | ALIGNED |
| `discounts.statutory_discount_validations` | Canonical DB repo table exists with requester, validated-by, policy reference, evidence flags, amount fields, and active-session uniqueness. | PARTIALLY_ALIGNED |
| Validation reviewer naming | DB repo uses `validated_by_user_id`; v1.3 source/docs often use reviewer/approver wording. Same concept, different operator-facing term. | SOURCE_USES_DIFFERENT_NAME |
| `discounts.discount_evidence_references` | Canonical table exists and supports metadata/reference storage, status, redaction, retention, and captured-by fields. | ALIGNED |
| `discounts.statutory_discount_policy_registry` | Canonical DB repo includes the registry and related enums; ExitPass app-local patch also contains registry evolution and site jurisdiction additions. | PARTIALLY_ALIGNED |
| `discounts.statutory_discount_payable_basis_applications` | App source inserts into this table and expects stable non-null `PayableBasisApplicationId`; canonical DB repo schema does not include the table. | APP_EXTENSION_NOT_IN_DB_REPO |
| `discounts.statutory_discount_payable_application_status_enum` and channel enum | App-local patch defines these enums; canonical DB repo does not. | APP_EXTENSION_NOT_IN_DB_REPO |
| `discounts.apply_statutory_discount_payable_basis` | Current apply writer calls this function; canonical DB repo does not include it. | APP_EXTENSION_NOT_IN_DB_REPO |
| Minor-unit amount fields | Payable-basis application stores gross/VAT/VAT-exclusive/discount/final in minor units; canonical validation/tariff tables store numeric amount fields. | DESIGN_DECISION_NEEDED |
| Requester-vs-approver SoD | Source enforces requester-vs-approver in backend decision flow; canonical table can store requester and validator identities, but DB constraints do not enforce SoD. | PARTIALLY_ALIGNED |

## Fiscal issuance / Sales Invoice deltas

| Item | Finding | Classification |
| --- | --- | --- |
| `core.fiscal_issuance_references` | Required by `PostgresFiscalIssuanceReferenceRepository`, status APIs, Operator Console facade, fiscal void, and runtime proofs. Not present in DB repo. | APP_EXTENSION_NOT_IN_DB_REPO |
| Fiscal issuance semantic hash columns | Source/test harness requires semantic request hash status/value/algorithm/source version/source fact count/safe summary/recorded-at columns. | APP_EXTENSION_NOT_IN_DB_REPO |
| Fiscal retry/readback/audit tables | App-local v1.3 patch includes attempt history, exception reviews, readback reconciliations, retry command/schedule/execution preparations, semantic hash recalculation previews, and backfill workflow/mutation tables. | APP_EXTENSION_NOT_IN_DB_REPO |
| POS Server fiscal document ID/number fields | Source records `pos_server_fiscal_document_id`, `fiscal_document_number`, sequence, state, evidence status, result classification, and error posture in Central PMS. | APP_EXTENSION_NOT_IN_DB_REPO |
| Fiscal void action audit | Uses `operations.operator_action_logs` plus `core.fiscal_issuance_references`; action reports are app/source-level. | PARTIALLY_ALIGNED |
| POS fiscal document tables | Direct POS Server `pos.fiscal_documents` objects are POS Server-owned and should not be added to Central PMS DB repo. | POS_SERVER_OWNED |

## Payments / payable basis deltas

| Item | Finding | Classification |
| --- | --- | --- |
| `core.tariff_snapshots` | Canonical table exists with gross/statutory/coupon/net amounts, validation linkage, active uniqueness, and superseded linkage. | ALIGNED |
| Applied statutory discount snapshot lifecycle | ExitPass app-local patch adds unique applied discount lifecycle constraints/indexes around statutory discount validation and applied snapshots. | APP_EXTENSION_NOT_IN_DB_REPO |
| `core.payment_attempts` and `core.payment_confirmations` | Canonical tables exist and are used by payment/fiscal flows. | ALIGNED |
| Payment-to-Sales-Invoice discount traceability | v1.3 proof uses applied tariff snapshot and fiscal reference context; canonical DB repo lacks fiscal issuance reference table and payable-basis application table. | PARTIALLY_ALIGNED |
| No unsafe side-effect tables | Payment provider, HikCentral, gate, refund/reversal, and rendering side effects remain outside statutory apply flow. | ALIGNED |

## Sites/devices/shifts deltas

| Item | Finding | Classification |
| --- | --- | --- |
| `sites.site_groups`, `sites.sites` | Canonical tables exist and support UAT site/site group context. | ALIGNED |
| `sites.device_assignments` | Canonical table exists for general device assignment. | ALIGNED |
| Operator Console trusted device binding | Current v1.3 Operator Console readiness uses `operator_console.operator_device_bindings`, not `sites.device_assignments`. | APP_EXTENSION_NOT_IN_DB_REPO |
| Operator shifts | Current v1.3 Operator Console readiness uses `operator_console.operator_shifts`; canonical DB repo has no matching shift table. | APP_EXTENSION_NOT_IN_DB_REPO |
| UAT site/device/shift seed | ExitPass scripts seed deterministic UAT rows for site group `77000000-0000-0000-0000-000000000001`, site `77000000-0000-0000-0000-000000000002`, requester/reviewer shifts, and device binding when operator-console tables exist. | TEST_OR_FIXTURE_ONLY until promoted |

## Audit/reconciliation/reporting deltas

| Item | Finding | Classification |
| --- | --- | --- |
| `audit.audit_events`, `audit.audit_trail_entries`, `audit.security_events`, `audit.evidence_links` | Canonical DB repo includes baseline audit objects. | ALIGNED |
| `operations.operator_action_logs` | Canonical table exists; v1.3 operator workflow reporting depends on it. | ALIGNED_WITH_CONVENTION |
| Fiscal status view audit | Reads safe `VIEW_FISCAL_ISSUANCE_STATUS` metadata from `operator_action_logs`; no raw payload table required. | ALIGNED_WITH_CONVENTION |
| Fiscal void audit | Reads safe `VOID_FISCAL_DOCUMENT` metadata and joins fiscal references; needs fiscal reference table. | PARTIALLY_ALIGNED |
| Statutory discount audit | Reads validations, evidence references, payable-basis application rows, tariff snapshots, and action logs; needs payable-basis application table. | PARTIALLY_ALIGNED |
| Reconciliation tables | Canonical repo includes reconciliation tables; no new current-state delta found for this audit beyond reporting/audit permissions. | ALIGNED |
| Management Dashboard/reporting future objects | v1.3 target scope requires dashboards/reporting/export audit, but source does not yet require new DB objects beyond existing audit/reconciliation and future read models. | DEFERRED_TARGET |

## Classification table

| Classification | Meaning | Objects/examples |
| --- | --- | --- |
| ALIGNED | Exists in DB repo and source uses it consistently. | `identity.users`, `identity.roles`, `identity.permissions`, `identity.user_roles`, `identity.role_permissions`, `sites.sites`, `core.tariff_snapshots`, `core.payment_attempts`, `core.payment_confirmations` |
| ALIGNED_WITH_CONVENTION | Canonical object exists, but source uses an established convention rather than exact enum/table names. | `operations.operator_action_logs` with action codes in `action_reason_code` |
| PARTIALLY_ALIGNED | Base object exists but v1.3 source needs additional related objects, constraints, or semantics. | statutory validations plus payable-basis application; fiscal void audit plus fiscal reference table |
| APP_EXTENSION_NOT_IN_DB_REPO | Required by v1.3 source/app-local patches/scripts but absent from canonical DB repo. | `core.fiscal_issuance_references`, `operator_console.*`, `discounts.statutory_discount_payable_basis_applications`, v1.3 RBAC seed permissions |
| SOURCE_USES_DIFFERENT_NAME | Source and DB repo model similar concepts with different ownership/naming. | `operator_console.production_policy_import_review_*` vs `discounts.statutory_discount_policy_import_review_*`; validated/reviewer wording |
| TEST_OR_FIXTURE_ONLY | Used for local/UAT smoke support and not necessarily canonical production reference data. | deterministic UAT session `E2E-231-SESSION-001`, UAT users/site/device/shift rows |
| POS_SERVER_OWNED | Belongs to POS Server database/repo, not Central PMS canonical DB. | `pos.fiscal_documents`, POS fiscal sequences/document status history |
| DESIGN_DECISION_NEEDED | Ownership or semantics are unclear and should be decided before migration. | policy import review queue ownership; operator action-code enum strategy; minor-unit vs numeric amount storage harmonization |
| DEFERRED_TARGET | v1.3 target capability, not required by current source yet. | Management Platform UI/admin mutation audit objects, dashboard/reporting read model tables |

## Recommended db repo implementation plan

Do not implement in this audit branch. Recommended ordered work for `exitpassdb_v1.2`:

1. Create DB repo branch `feature/v13-central-pms-db-alignment` from `develop`.
2. Add/promote the Central PMS fiscal reference persistence objects from `infra/db/patches/ExitPass_CentralPms_FiscalReferenceStatePersistence_v1.3.sql`, including validation script coverage.
3. Add/promote the Operator Console schema objects from `infra/db/patches/ExitPass_OperatorConsoleSchema_v1.2.sql`, with explicit ownership notes for HR mapping, device binding, shift, access evaluation, and entitlement fingerprint objects.
4. Add/promote statutory payable-basis application objects from `infra/db/patches/ExitPass_StatutoryDiscountPayableBasisApplicationSchema_v1.2.sql`, including enums, table, function, constraints, and indexes.
5. Add/promote statutory applied tariff snapshot lifecycle constraints from `infra/db/patches/ExitPass_StatutoryDiscountAppliedTariffSnapshotLifecycle_v1.2.sql`.
6. Reconcile policy import review ownership before adding canonical objects:
   - either map current app to the canonical `discounts.statutory_discount_policy_import_review_*` model,
   - or formally add `operator_console.production_policy_import_review_*` as the UAT/operator review queue and document why both exist.
7. Add v1.3 RBAC reference data:
   - seven role bundles,
   - granular permissions,
   - role-permission mappings,
   - deterministic local/UAT users only if DB repo accepts local/UAT reference data.
8. Add or update data dictionary/object inventory for every new schema/table/enum/function/index.
9. Add validation scripts proving required v1.3 objects exist and unsafe POS Server-owned objects are not added to Central PMS.
10. Rebuild/apply from a clean disposable database and run Central PMS focused integration tests that depend on these objects.

## Proposed exitpassdb_v1.2 branch

`feature/v13-central-pms-db-alignment`

## Risks and sequencing

| Risk | Recommendation |
| --- | --- |
| Fiscal issuance code depends on a table absent from canonical DB repo. | Promote fiscal reference persistence first because it gates status, void, replay/conflict, and exit authorization fiscal gating. |
| Operator Console readiness tables are optional in some scripts but meaningful for UAT identity/device/shift posture. | Promote `operator_console` schema second, before broad UAT. |
| Policy import review has two parallel models. | Resolve ownership/naming before adding both models to canonical DB. |
| Payable-basis application table/function is required by current source. | Promote as a focused additive migration with validation, then run statutory discount apply/payment proof tests. |
| v1.3 permissions are app-local seeds. | Add them as reference-data after schema alignment so UAT roles can be recreated consistently. |
| Canonical DB repo is v1.2 while app scope is v1.3. | Keep changes additive and clearly label v1.3 Central PMS alignment; do not remove v1.2 baseline objects unless separately approved. |
| POS Server ownership boundary could be blurred by fiscal proof requirements. | Keep POS fiscal document/sequence tables out of Central PMS DB repo; store only Central PMS fiscal references and POS Server IDs/numbers returned by integration. |

## Explicit non-goals

This audit did not and must not in this branch:

- modify `D:\SourceCodes\exitpassdb_v1.2`
- create migration SQL
- change Central PMS runtime code
- change tests
- change POS Server
- change Operator Console UI
- change statutory discount logic
- change fiscal issuance logic
- change identity/RBAC behavior
- create user management UI
- create mutation APIs
- call payment provider, HikCentral, gate, POS Server, or rendering services

## Files changed

- Added `docs/v1.3/central-pms/db-alignment/ExitPass_Central_PMS_DB_Alignment_With_exitpassdb_v1.2_Audit_v1.0.md`.

No source code, tests, database repository files, migration SQL, or runtime configuration files were changed.

## Validation

Validation required for this doc-only audit:

- `git diff --check` in `D:\SourceCodes\ExitPass`
- `git status --short --untracked-files=all` in `D:\SourceCodes\ExitPass`
- `git status --short --branch --untracked-files=all` in `D:\SourceCodes\exitpassdb_v1.2`

No full test suites were run because this was a database alignment audit only and no runtime source/test code was changed.
