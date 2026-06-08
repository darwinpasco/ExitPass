# Operator Console Production Policy Registry Admin Import Alignment v1

## 1. Title And Purpose

This document is the production policy registry/admin/import alignment plan for Operator Console statutory discount rollout.

Production statutory discount auto-application remains blocked until a governed policy registry, verified production policy data, admin/import controls, and state-based database repository alignment are ready.

This is a design and DB-alignment decision slice only. It does not implement production policy import, admin UI, production seed data, database migrations, or local database changes.

## 2. Scope

In scope:

- production policy registry target
- DB baseline alignment decision
- policy admin/import operating model
- verified ordinance intake
- policy row governance
- policy verification statuses
- policy activation workflow
- audit/change-control expectations
- required DB repository alignment
- next implementation slices

Out of scope:

- actual DB mutation
- production seed creation
- policy import implementation
- admin UI implementation
- WebPay
- payment provider routing
- AUB
- coupon validation
- reconciliation
- HikCentral/gate implementation
- raw evidence, OCR, or automated ID validation

## 3. Source Artifacts Inspected

Application repo artifacts inspected:

- `docs/operator-console/OperatorConsole_Production_Policy_Registry_Readiness_v1.md`
- `docs/operator-console/OperatorConsole_Production_Readiness_Gap_Review_v1.md`
- `docs/operator-console/OperatorConsole_Statutory_Discount_Pilot_Readiness_Signoff_v1.md`
- `docs/operator-console/Philippine_Parking_Statutory_Discount_Local_Ordinances_Detailed_List.docx`
- `scripts/operator-console/Verify-ProductionPolicyRegistryReadiness.sql`
- `scripts/operator-console/Run-ProductionPolicyRegistryReadinessCheck.ps1`
- `infra/db/patches/ExitPass_StatutoryDiscountPolicyRegistrySchema_v1.2.sql`
- `infra/db/patches/ExitPass_OperatorConsoleSchema_v1.2.sql`
- Central PMS Operator Console statutory discount policy resolution, draft, read, apply, endpoint, DTO, and test files

External DB repo inspected read-only:

- `D:\SourceCodes\ExitPass_DBv1.2`
- `schema/03_tables.generated.sql`
- `reference-data/ExitPass_Reference_Data_v1.2.sql`
- `migrations/20260512012142_baseline_v1_2.sql`
- `snapshots/ExitPass_Full_Database_Creation_DDL_v1.2.sql`
- `atlas.hcl`

DB repo Git status could not be read because Git reported the repo as a dubious-ownership directory for the sandbox user. No global Git safe-directory setting was changed.

## 4. Current State Summary

Live local DB schema inspection confirms the current compatibility table exists:

- `discounts.discount_policy_references`

Live local DB schema inspection confirms the dedicated production registry table is absent:

- `discounts.statutory_discount_policy_registry`

The live local policy rows are not a production baseline. They are development placeholders plus local fixture fallback rows:

- `PH_NATIONAL_SENIOR_DEV`
- `PH_NATIONAL_PWD_DEV`
- `MNT_LOCAL_SENIOR_DEV`
- `MNT_LOCAL_PWD_DEV`
- local test fixture fallback rows for `PH_RA9994_SENIOR_CITIZEN_NATIONAL_FALLBACK` and `PH_RA10754_PWD_NATIONAL_FALLBACK`

The state-based DB repo baseline currently contains the compatibility table and development placeholder policy reference rows. Direct file search found no dedicated `discounts.statutory_discount_policy_registry`, `discounts.policy_verification_status_enum`, governed `parking_benefit_type_enum`, or `sites.jurisdictions` baseline objects in `D:\SourceCodes\ExitPass_DBv1.2`.

Production required Senior Citizen and PWD policy rows are still missing from the governed baseline. The local fixture fallback rows do not make the environment production-ready.

#250 now blocks production draft creation and production policy auto-application for sandbox-only or not-ready policies. Production fail-closed/manual-review behavior is present in the backend, but verified production policy data is still required before rollout.

The readiness wrapper was run with `-WarnOnly` after read-only schema inspection. Current local result:

- `COMPATIBILITY_TABLE_ONLY`: 1
- `SANDBOX_ONLY`: 4
- `READY_WITH_MANUAL_REVIEW`: 4

This current local result is a warning state, not production readiness. The documented expected clean local readiness result remains NO-GO when production Senior Citizen and PWD policies are missing after sandbox/dev rows are excluded.

## 5. Target Production Policy Registry Position

Recommendation:

- Use a governed dedicated policy registry model aligned in `D:\SourceCodes\ExitPass_DBv1.2`.
- Retain `discounts.discount_policy_references` only as transitional compatibility support while the resolver and reports move to the governed registry.
- Do not make production policy rows ad hoc application seed rows.
- Do not treat local DB drift or integration-test fixture rows as source of truth.

Rationale:

- The compatibility table does not have enough structured fields for production legal/source verification, benefit scope, residency scope, exclusions, reviewer/approver metadata, or immutable policy snapshots.
- The application repo already has a dedicated registry patch concept in `infra/db/patches/ExitPass_StatutoryDiscountPolicyRegistrySchema_v1.2.sql`.
- The DB repo is the state-based baseline owner and currently does not contain the governed registry objects.
- Production statutory discount policy rows need compliance review and change control; they should be represented through the state-based DB repo or approved admin/import workflow, not application-local seed drift.

Decision position:

- Target state should be a dedicated governed production registry.
- Compatibility mapping should remain temporary and explicit.
- Production rollout should not proceed until DB repo baseline, import controls, and verified policy data are aligned.

## 6. Required Production Policy Data Model

Production-ready policy data must include, at minimum:

- `policy_code`
- `policy_name`
- `entitlement_type`
- `benefit_type`
- `discount_base_scope`
- `policy_level`
- `policy_type`
- `policy_resolution_basis`
- `verification_status`
- site, site group, and jurisdiction scope
- ordinance, legal, or national reference
- residency scope
- facility scope and exclusions
- `free_duration_minutes`
- `initial_rate_exempt`
- `full_fee_exempt`
- `overnight_excluded`
- `valet_excluded`
- `standalone_parking_excluded`
- `driver_or_passenger_required`
- `requires_evidence`
- required evidence type
- `requires_operator_validation`
- `effective_from`
- `effective_to`
- source document reference
- reviewed by and reviewed at
- approved by and approved at
- audit and correlation metadata

The compatibility table supports only a subset of these fields. Production policy readiness therefore requires either the governed registry baseline or formally accepted manual-review compensating controls for a controlled pilot.

## 7. Policy Governance Lifecycle

Recommended lifecycle states:

- `DRAFT`
- `UNDER_REVIEW`
- `VERIFIED_OFFICIAL`
- `VERIFIED_SECONDARY`
- `APPROVED_FOR_PILOT`
- `ACTIVE`
- `SUSPENDED`
- `SUPERSEDED`
- `RETIRED`

Recommended responsibilities:

| Action | Actor | Control |
| --- | --- | --- |
| Create draft policy | Product Ops or authorized Policy Admin | Must cite source document reference and scope assumptions. |
| Review source | Legal/Compliance | Must verify official ordinance, national law, or approved secondary source. |
| Review product impact | Product and Operations | Must confirm benefit behavior, exclusions, and site applicability. |
| Approve for pilot | Product Owner plus Compliance | Must produce approval record and pilot scope. |
| Activate | Authorized Policy Admin with maker-checker approval | Must be audited and effective-dated. |
| Suspend or retire | Compliance, Product Owner, or Operations lead | Must include reason code, timestamp, and actor. |
| Supersede | Authorized Policy Admin | Must link old and new policy row and preserve historical audit. |

Audit requirements:

- Every create, update, approve, activate, suspend, retire, and supersede action must capture actor, timestamp, reason, correlation ID, and before/after policy snapshot.
- Production activation must not be possible for `DRAFT`, `UNDER_REVIEW`, `LEAD_UNVERIFIED`, or `PROPOSED` source states.
- Separate Senior Citizen and PWD rows are required when rules, evidence, residency, or benefits differ.

## 8. Admin/Import Operating Model

Supported future options:

- manual admin entry for one-off controlled policy rows
- controlled CSV import for reviewed policy batches
- migration/state-based seed for approved baseline policies
- external legal document reference attachment or link
- maker-checker review for approval and activation

Recommended phased approach:

Phase 1: Controlled CSV/import file reviewed by Legal, Product, Compliance, and Operations.

- Use a blank import template with strict validation rules.
- Import should create `DRAFT` or `UNDER_REVIEW` rows only by default.
- Activation should require a separate approval step.
- No public report or social post should be imported as production-active policy authority.

Phase 2: Admin UI with maker-checker.

- Add create/edit/review/approve/activate/suspend/retire workflow.
- Show policy readiness classification and resolver impact.
- Require reason codes and immutable audit snapshots.

Phase 3: Periodic policy review and expiry automation.

- Notify owners before `effective_to`.
- Flag source documents requiring revalidation.
- Auto-route expired or superseded rows to manual review.

## 9. Ordinance Source Verification Process

Production-ready local policy rows require an official ordinance copy or official LGU publication.

Rules:

- Public reports, social posts, and ordinance indexes are leads only.
- Legal/Compliance review is required before production activation.
- Residency scope must be encoded explicitly.
- Facility scope and exclusions must be encoded explicitly.
- Senior Citizen and PWD policies must be separate rows when rules differ.
- Proposed-only ordinances must not become `ACTIVE`.
- Unclear or conflicting source coverage must stay `UNDER_REVIEW`, `VERIFIED_SECONDARY`, or `APPROVED_FOR_PILOT` with manual review, depending on approved operating controls.

The ordinance DOCX is operational research, not a legal opinion. It identifies candidate and lead jurisdictions, but no row from it should become production-active without official source review.

## 10. DB Repository Alignment Plan

State-based DB repo steps:

1. Inspect `D:\SourceCodes\ExitPass_DBv1.2` current baseline.
2. Compare DB repo baseline with application repo patches:
   - `infra/db/patches/ExitPass_StatutoryDiscountPolicyRegistrySchema_v1.2.sql`
   - `infra/db/patches/ExitPass_OperatorConsoleSchema_v1.2.sql`
3. Decide whether the dedicated statutory discount policy registry belongs in the DB baseline.
4. Decide whether national fallback rows belong in reference data, an approved import file, or an admin-created production baseline.
5. Update DB repo schema/reference-data artifacts only after approval.
6. Validate with Atlas/state-based comparison if used by the project.
7. Rebuild local DB from the DB repo baseline.
8. Rerun `scripts/operator-console/Run-ProductionPolicyRegistryReadinessCheck.ps1 -WarnOnly`.
9. Update application repo references/docs only after the DB repo baseline decision is stable.

Do not let local DB drift become baseline. Do not insert ad hoc production rows directly into the local database and treat them as source of truth.

The current application patch includes both governed schema and national fallback row inserts. Before DB repo alignment, the team should decide whether schema and policy data should be split so production baseline data follows an approved policy import/governance process.

## 11. Policy Import File Proposal

A future import template should use these columns:

- `policy_code`
- `policy_name`
- `entitlement_type`
- `lgu_code`
- `jurisdiction_name`
- `site_group_code`
- `site_code`
- `policy_level`
- `policy_type`
- `policy_resolution_basis`
- `benefit_type`
- `discount_base_scope`
- `free_duration_minutes`
- `initial_rate_exempt`
- `full_fee_exempt`
- `overnight_excluded`
- `valet_excluded`
- `standalone_parking_excluded`
- `driver_or_passenger_required`
- `beneficiary_residency_scope`
- `requires_evidence`
- `required_evidence_type`
- `requires_operator_validation`
- `legal_basis_reference`
- `ordinance_reference`
- `national_law_reference`
- `source_reference`
- `verification_status`
- `effective_from`
- `effective_to`
- `reviewed_by`
- `reviewed_at`
- `approved_by`
- `approved_at`
- `notes`

The companion blank template is:

- `docs/operator-console/OperatorConsole_Production_Policy_Import_Template_v1.csv`

The template contains headers only. It does not contain production policy rows.

## 12. Readiness And Go/No-Go Criteria

Ready for sandbox:

- deterministic sandbox fixture policy rows exist
- sandbox/dev markers are present and classified as non-production
- #250 fail-closed behavior keeps sandbox rows out of production auto-application
- no production seed dependency is implied

Ready for controlled pilot:

- pilot site and entitlement scope are documented
- policy source is manually verified and approved for pilot
- operator manual review is required where governed registry metadata is incomplete
- production readiness check is run and its limitations are documented
- Operations, Product, and Compliance approve pilot controls

Ready for production auto-application:

- governed policy registry exists in the DB repo baseline
- Senior Citizen and PWD national fallback rows are verified and active
- local ordinance rows are official-source verified, scoped, effective, and active
- policy rows encode residency, facility scope, exclusions, benefit behavior, evidence requirements, and operator validation requirements
- maker-checker audit exists for activation
- policy readiness wrapper reports no blockers
- application resolver reads from the governed source or a formally approved compatibility bridge

No-go conditions:

- missing production Senior Citizen or PWD policy rows
- sandbox/dev/test rows are the only rows available
- local ordinance source is public-report-only
- proposed-only ordinance is marked active
- verification status is missing or unapproved
- scope, evidence, effective date, or legal reference is missing
- DB repo baseline does not match local/prod-like database state
- ad hoc local DB rows are treated as baseline

Production auto-application remains NO-GO while policies are missing, unverified, sandbox-only, expired, inactive, unscoped, or not aligned to the governed DB baseline.

## 13. Gap List

| Gap | Description | Risk | Owner | Next slice | Production blocker |
| --- | --- | --- | --- | --- | --- |
| `OC-POLICY-IMPORT-GAP-001` | DB repo baseline lacks the dedicated governed statutory discount policy registry. | Compatibility table cannot encode production governance fields. | Backend/DB Architecture | #253 | Yes |
| `OC-POLICY-IMPORT-GAP-002` | DB repo reference data contains dev placeholder policy rows. | Dev rows could be mistaken for production authority without #250 controls. | Backend/Compliance | #252 | Yes |
| `OC-POLICY-IMPORT-GAP-003` | Production policy import validation rules are not implemented. | Invalid or incomplete rows could enter review workflow. | Backend/Product Ops | #252 | Yes |
| `OC-POLICY-IMPORT-GAP-004` | No maker-checker admin/import workflow exists. | Single-actor activation could bypass legal/compliance review. | Product/Backend/UI | #254 | Yes |
| `OC-POLICY-IMPORT-GAP-005` | Official ordinance source intake process is not operationalized. | Public leads could become active policy incorrectly. | Compliance/Ops | #252 | Yes |
| `OC-POLICY-IMPORT-GAP-006` | Compatibility resolver still reads `discounts.discount_policy_references`. | Governed registry data would not be used automatically. | Backend | #253 | Conditional |
| `OC-POLICY-IMPORT-GAP-007` | Policy readiness UX/reporting indicators are not complete. | Operators may not see why manual review is required. | Frontend/Backend | #255 | Conditional |
| `OC-POLICY-IMPORT-GAP-008` | Production statutory discount policy test matrix is not complete. | Policy edge cases may be untested before rollout. | QA/Backend | #256 | Yes |

## 14. Recommended Next Slices

Recommended bounded next slices:

- #252 Operator Console statutory discount policy import template and validation rules
- #253 Operator Console policy registry DB baseline alignment plan for ExitPass_DBv1.2
- #254 Operator Console policy admin/import API design
- #255 Operator Console policy readiness UX/reporting indicators
- #256 Operator Console production statutory discount policy test matrix

Recommended immediate next slice: #252 Operator Console statutory discount policy import template and validation rules.

Reason: validation rules should be agreed before DB baseline changes or admin APIs. The import contract can define required fields, allowed enum values, source-document requirements, activation constraints, and maker-checker expectations without mutating the database.

## 15. DB Change Decision

Database changes required now: Deferred.

Reason:

- This slice is an alignment/design slice.
- The dedicated registry is absent from the live local DB and the DB repo baseline.
- DB changes are required for the target production model, but they must be approved and made in `D:\SourceCodes\ExitPass_DBv1.2`, not applied ad hoc locally.

Exact future DB repo artifacts likely requiring change:

- `schema/02_enums.generated.sql`
- `schema/03_tables.generated.sql`
- `schema/04_foreign_keys.generated.sql`
- `schema/04a_unique_constraints.generated.sql`
- `schema/05_indexes.generated.sql`
- `schema/schema.sql`
- `reference-data/ExitPass_Reference_Data_v1.2.sql`, if approved baseline policies are represented as reference data
- `migrations/*`, if the project keeps migration artifacts alongside state-based schema
- `snapshots/ExitPass_Full_Database_Creation_DDL_v1.2.sql`, if snapshots are regenerated as part of baseline promotion

Baseline owner:

- `D:\SourceCodes\ExitPass_DBv1.2`

Before any DB change is applied:

- approve the registry model and lifecycle
- approve import validation rules
- decide schema-only versus schema-plus-baseline-policy data
- update DB repo artifacts
- validate with Atlas/state-based comparison
- rebuild local DB from repo baseline
- rerun production policy readiness verification

## 16. Boundary Confirmations

- No backend behavior changes.
- No frontend behavior changes.
- No database, DDL, migration, or seed mutations.
- No production policy seed data added.
- No WebPay changes.
- No payment/provider routing changes.
- No AUB changes.
- No coupon, reconciliation, HikCentral, or gate changes.
- No sensitive credentials, production IDs, private keys, raw evidence, or personal data added.
