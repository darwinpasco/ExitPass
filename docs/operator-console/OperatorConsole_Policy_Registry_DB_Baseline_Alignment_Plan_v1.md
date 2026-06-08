# Operator Console Policy Registry DB Baseline Alignment Plan v1

## 1. Title And Purpose

This is the DB baseline alignment plan for the Operator Console statutory discount policy registry.

The target DB baseline owner is `D:\SourceCodes\ExitPass_DBv1.2`. App-side readiness verification, sandbox/dev exclusion, and production fail-closed/manual-review behavior now exist in the application repo, but production rollout still needs governed DB baseline alignment before statutory discount auto-application can be approved.

Production statutory discount auto-application remains NO-GO until verified policy rows and a governed policy registry baseline are aligned through the state-based DB repo.

## 2. Scope

In scope:

- target policy registry schema baseline
- compatibility table transition plan
- reference data and production policy row governance
- DB repo change inventory
- state-based validation flow
- local DB rebuild and compare sequence
- rollout dependencies
- readiness verification after DB alignment

Out of scope:

- applying DB changes
- inserting production policy rows
- modifying `D:\SourceCodes\ExitPass_DBv1.2` in this slice
- backend runtime behavior changes
- frontend changes
- WebPay
- payment provider routing
- AUB
- coupon validation
- reconciliation
- HikCentral or gate implementation
- raw evidence, OCR, or automated ID validation

## 3. Current State Summary

Application repo state:

- `docs/operator-console/OperatorConsole_Production_Policy_Registry_Readiness_v1.md` documents production readiness rules and a NO-GO position until verified production policies exist.
- `docs/operator-console/OperatorConsole_Production_Policy_Registry_Admin_Import_Alignment_v1.md` documents policy admin/import governance and DB repo alignment needs.
- `docs/operator-console/OperatorConsole_Production_Policy_Import_Template_v1.csv` is a blank header-only import template.
- `scripts/operator-console/Verify-ProductionPolicyRegistryReadiness.sql` is a read-only compatibility-table readiness check.
- `scripts/operator-console/Run-ProductionPolicyRegistryReadinessCheck.ps1` wraps the readiness SQL in a read-only transaction and reports readiness blockers.
- `infra/db/patches/ExitPass_StatutoryDiscountPolicyRegistrySchema_v1.2.sql` exists as an app-repo patch proposal for a dedicated governed registry, jurisdictions, verification/benefit enums, validation links, and national fallback seed concepts.
- The requested `docs/operator-console/OperatorConsole_Production_Policy_Import_Validation_Rules_v1.md` and `scripts/operator-console/Test-ProductionPolicyImportTemplate.ps1` were not found on the inspected branch.

Known live/local DB state from prior readiness slices:

- The live local DB has `discounts.discount_policy_references`.
- The live local DB does not have `discounts.statutory_discount_policy_registry`.
- Current live policy rows are sandbox/dev-only or fixture fallback rows.
- Production Senior Citizen and PWD policy rows are missing after sandbox/dev rows are excluded.
- The readiness wrapper reports NO-GO or warning-only readiness until production rows exist.

DB repo baseline inspection result:

- `D:\SourceCodes\ExitPass_DBv1.2` exists.
- Git status was readable and reported no changes.
- The DB repo contains generated schema, migration, snapshot, reference-data, drift-report, Atlas config, and baseline alignment script paths.
- The DB repo baseline contains `discounts.discount_policy_references`.
- Direct read-only file search of `schema`, `reference-data`, `migrations`, and `snapshots` found no `discounts.statutory_discount_policy_registry`, `discounts.policy_verification_status_enum`, governed parking benefit enums, or `sites.jurisdictions` baseline objects.
- Reference data contains local-development placeholder policy rows such as `PH_NATIONAL_SENIOR_DEV`, `PH_NATIONAL_PWD_DEV`, `MNT_LOCAL_SENIOR_DEV`, and `MNT_LOCAL_PWD_DEV`.

## 4. State-Based DB Repo Ownership

`D:\SourceCodes\ExitPass_DBv1.2` is the source of truth for the DB baseline.

Local databases must be derived from the DB repo baseline, not promoted back into Git as undocumented drift. App repo scripts and docs may verify readiness, document expected behavior, and provide inspection helpers, but they must not become the DB baseline.

Ad hoc local rows, local fixture fallback rows, integration-test seed rows, and manual database edits are not production baseline. Any schema object, constraint, enum, index, FK, validation linkage, or approved production policy reference data required for rollout must be represented in the DB repo and reproducible from Git.

## 5. Target Registry Recommendation

Recommendation: C. Hybrid transition.

The DB baseline should add a governed dedicated `discounts.statutory_discount_policy_registry` while retaining `discounts.discount_policy_references` temporarily for compatibility.

Recommended target posture:

- Add dedicated `discounts.statutory_discount_policy_registry` to the DB repo baseline.
- Retain `discounts.discount_policy_references` during transition.
- Gradually update app code and readiness scripts to prefer the dedicated registry when present.
- Keep compatibility-table production policy use explicit and temporary.
- Deprecate compatibility-only production policy use after the governed registry is proven in local rebuild, staging/prod-like validation, and controlled pilot checks.

Rationale:

- The compatibility table lacks structured verification status, benefit details, residency scope, exclusion flags, review/approval metadata, and policy snapshot support.
- The app repo already contains a dedicated registry patch concept, but it is not the baseline owner.
- Production policy rows require governance and traceability beyond the current compatibility reference table.

## 6. Proposed DB Artifacts

DB repo artifacts that may need to be added or updated in a future DB-repo slice:

- `schema/02_enums.generated.sql` for governed verification, benefit, residency, and related controlled values.
- `schema/03_tables.generated.sql` for `discounts.statutory_discount_policy_registry` and any jurisdiction scope table such as `sites.jurisdictions`.
- `schema/04_foreign_keys.generated.sql` for links to `sites.sites`, `sites.site_groups`, jurisdiction tables, `identity.users`, `identity.service_identities`, and validation tables where applicable.
- `schema/04a_unique_constraints.generated.sql` for policy code and baseline uniqueness constraints where modeled as constraints.
- `schema/05_indexes.generated.sql` for policy lookup and readiness indexes.
- `schema/schema.sql` as the composed state-based schema source.
- `reference-data/ExitPass_Reference_Data_v1.2.sql` only if approved production baseline rows are represented as reference data.
- `migrations/*` only if the repo keeps migration artifacts alongside the state-based schema.
- `snapshots/ExitPass_Full_Database_Creation_DDL_v1.2.sql` if snapshots are regenerated as part of baseline promotion.
- `scripts/Align-ExitPassV12DbBaseline.ps1` or README/build notes if the validation flow changes.
- Validation/query scripts for registry availability, row readiness, reference-data row counts, and drift checks.
- `atlas.hcl` only if Atlas environment or source paths must change.

## 7. Proposed Table/Entity Model

The target registry should conceptually support these fields. This section is not DDL.

- `statutory_discount_policy_registry_id` or equivalent primary key
- `policy_code`
- `policy_name`
- `policy_description`
- `entitlement_type`
- `policy_status`
- `verification_status`
- `policy_level`
- `policy_type`
- `policy_resolution_basis`
- `benefit_type`
- `discount_base_scope`
- jurisdiction, site, and site-group scope fields
- LGU or jurisdiction bridge fields where needed during transition
- ordinance reference
- legal basis reference
- national law reference
- residency scope fields
- facility scope and exclusion fields
- overnight, valet, standalone parking, driver/passenger, and other exclusion flags
- evidence requirement fields, including required evidence type or evidence retention policy where applicable
- `effective_from`
- `effective_to`
- `source_reference`
- reviewed by and reviewed at metadata
- approved by and approved at metadata
- activation/suspension/retirement metadata where supported
- audit columns for created/updated actor and timestamp
- correlation/reference fields
- policy snapshot JSON or equivalent immutable snapshot support for downstream validations

The app-repo patch currently summarizes a possible model, including jurisdictions, verification status, benefit/scope enums, national fallback constraints, local ordinance constraints, and future validation links. The DB repo baseline proposal should review that patch, split schema from reference-data decisions, and restate the final model inside `D:\SourceCodes\ExitPass_DBv1.2`.

## 8. Constraint And Index Expectations

Recommended constraints:

- Unique `policy_code`, or unique `policy_code` plus explicit version if versioned policy rows are retained.
- Active policy uniqueness for entitlement plus jurisdiction/site/site-group scope plus effective period, where feasible.
- Controlled status values for policy lifecycle and verification status.
- Effective date validity: `effective_to` must be null or greater than `effective_from`.
- Evidence rule consistency, including evidence required when the Operator Console workflow requires evidence.
- Sandbox/test/dev policy marker exclusion from `ACTIVE` plus approved production states, if enforceable.
- Legal/source reference required for verified or active rows.
- Official ordinance reference required for verified local ordinance rows.
- RA 9994 required for Senior Citizen national fallback rows.
- RA 10754 required for PWD national fallback rows.
- Local ordinance policies require jurisdiction, site, or site-group scope.
- Review/approval fields required for approved or active rows, if enforceable without breaking system-service seeded rows.
- Row version and audit actor consistency checks where consistent with existing DB repo patterns.

Recommended indexes:

- `policy_code`
- `entitlement_type`
- `policy_status`
- `verification_status`
- `policy_status, verification_status`
- jurisdiction scope
- site scope
- site-group scope
- entitlement plus scope lookup
- effective dates
- national law reference where present
- local ordinance reference where present

## 9. Reference Data Strategy

Options:

- No production policy reference data in the baseline; production rows enter only through approved admin/import workflows.
- Approved baseline seed/reference data in the DB repo.
- Pilot-only reference data separated from production baseline.

Recommendation:

- Production policy rows must be reviewed and approved by Legal/Product/Compliance/Ops before becoming baseline.
- If production rows are inserted as reference data, they must live in `D:\SourceCodes\ExitPass_DBv1.2`, be traceable to reviewed source references, and be reproducible from Git.
- Pilot/sample/sandbox rows must remain explicitly separated and must not be production-active.
- Current dev placeholder rows must not be promoted as production policy authority.
- National fallback rows for RA 9994 and RA 10754 may be candidates for governed baseline reference data only after approval of legal/source wording, benefit modeling, and activation metadata.

## 10. Import/Admin Relationship

The #251/#252 import/admin artifacts fit as pre-import governance, not as DB baseline by themselves.

- The blank CSV template defines the proposed policy import contract.
- CSV validation is pre-import governance.
- Approved import output can become DB repo reference data or input to a future admin/import API.
- Import must not bypass maker-checker, legal, compliance, product, or operations approval.
- Import should default to draft/review states unless an approved activation workflow exists.
- Readiness SQL and the wrapper must be run after import or baseline alignment.

Because the validation-rules markdown and offline validator script were not present on the inspected branch, the future DB baseline proposal should confirm the finalized import validation contract before encoding production reference data.

## 11. Alignment Workflow

Recommended sequence:

1. Confirm the target registry design, including compatibility bridge duration and ownership.
2. Update DB repo baseline artifacts in `D:\SourceCodes\ExitPass_DBv1.2`.
3. Add or update reference data only after Legal/Product/Compliance/Ops approval.
4. Run DB repo state-based validation and Atlas compare.
5. Rebuild or update the local dev DB from the DB repo baseline.
6. Rerun `scripts/operator-console/Verify-ProductionPolicyRegistryReadiness.sql` and `scripts/operator-console/Run-ProductionPolicyRegistryReadinessCheck.ps1`.
7. Run Central PMS policy resolution tests.
8. Run Operator Console statutory discount controlled validation.
9. Only then consider production rollout.

No step should treat ad hoc local database edits as baseline.

## 12. Drift And Rollback Rules

- Do not promote local DB drift.
- All schema and reference-data changes must be reproducible from Git.
- Rollback must be represented in the DB repo state, not as untracked manual delete statements.
- Retired or superseded policy rows should normally be represented as state transitions, not silent deletion.
- Silent deletion of policy rows should require explicit baseline cleanup approval.
- Compatibility data and dedicated registry data must be compared during transition to prevent split-brain policy behavior.
- Drift reports should be attached to DB repo validation evidence before production rollout decisions.

## 13. DB Repo Inspection Result

Read-only inspection was attempted and completed without modifying the DB repo.

Repository:

- Path: `D:\SourceCodes\ExitPass_DBv1.2`
- Exists: yes
- Git status readable: yes
- Git status output: clean
- Files modified by this slice: none

Relevant paths found:

- `atlas.hcl`
- `scripts/Align-ExitPassV12DbBaseline.ps1`
- `schema/schema.sql`
- `schema/02_enums.generated.sql`
- `schema/03_tables.generated.sql`
- `schema/04_foreign_keys.generated.sql`
- `schema/04a_unique_constraints.generated.sql`
- `schema/05_indexes.generated.sql`
- `reference-data/ExitPass_Reference_Data_v1.2.sql`
- `migrations/20260512012142_baseline_v1_2.sql`
- `migrations/20260512_restore_v12_constraints_and_payment_routine.sql`
- `snapshots/ExitPass_Full_Database_Creation_DDL_v1.2.sql`
- `snapshots/ExitPass_Full_Database_Creation_DDL_v1.2.live_schema.sql`
- `drift-reports/*`

Findings:

- Dedicated registry objects found in DB repo baseline: no.
- `discounts.statutory_discount_policy_registry` found in schema/reference-data/migrations/snapshots search: no.
- `discounts.policy_verification_status_enum` and governed benefit/residency enums found in baseline search: no.
- `sites.jurisdictions` baseline object found: no.
- Compatibility table found: yes, `discounts.discount_policy_references`.
- Compatibility-table reference data found: yes, local-development placeholder rows.
- Atlas/state-based config found: yes, `atlas.hcl` and baseline alignment script.

Limitations:

- This slice used filesystem and Git inspection only for the DB repo.
- No DB repo files were modified.
- No local database state was changed.
- No fresh SQL readiness execution was performed in this slice.

## 14. Application Repo Impact

Likely app repo updates after DB alignment:

- Readiness SQL may need to prefer `discounts.statutory_discount_policy_registry` when present and fall back to `discounts.discount_policy_references` only during transition.
- Policy resolution repositories may need a dedicated registry query path.
- Tests need dedicated registry fixtures for verified national fallback, verified local ordinance, unverified local ordinance, missing jurisdiction, expired policy, missing evidence rule, and sandbox/dev exclusion.
- Audit/reporting may need to display registry verification status, policy source, policy basis, effective window, and manual-review reason.
- Operator Console UI may need policy readiness indicators that explain verified, manual-review, unverified, missing, expired, sandbox-only, and scope-missing states.
- Compatibility-table query paths should be marked transitional once the dedicated registry is in the DB baseline.

## 15. Go/No-Go Position

- GO for sandbox/pilot validation using deterministic fixture policies.
- CONDITIONAL GO for controlled operational pilot only with manually verified site-approved policy evidence.
- NO-GO for full production statutory discount auto-application until DB baseline alignment is complete and verified.

## 16. Gap List

| Gap | Description | Risk | Owner | Target repo | Recommended next slice | Production blocker |
| --- | --- | --- | --- | --- | --- | --- |
| `OC-DBPOLICY-GAP-001` | Dedicated governed statutory discount policy registry is absent from the DB repo baseline. | Compatibility table cannot encode required production governance fields. | DB Architecture/Backend | `D:\SourceCodes\ExitPass_DBv1.2` | #254 | Yes |
| `OC-DBPOLICY-GAP-002` | DB repo contains dev placeholder policy reference rows. | Placeholder rows could be mistaken for production authority if controls regress. | Compliance/Backend/Ops | `D:\SourceCodes\ExitPass_DBv1.2` | #254 | Yes |
| `OC-DBPOLICY-GAP-003` | Production Senior Citizen and PWD policy rows are missing after sandbox/dev rows are excluded. | Production statutory discount resolution has no approved authority. | Compliance/Product/Ops | `D:\SourceCodes\ExitPass_DBv1.2` or approved admin/import path | #254 | Yes |
| `OC-DBPOLICY-GAP-004` | Verification status is not present in the compatibility baseline. | `ACTIVE` can be confused with official legal/source approval. | DB Architecture/Compliance | `D:\SourceCodes\ExitPass_DBv1.2` | #254 | Yes |
| `OC-DBPOLICY-GAP-005` | Structured benefit, residency, exclusion, and evidence fields are not fully modeled in the compatibility table. | Local ordinance behavior can be encoded ambiguously or incompletely. | DB Architecture/Product | `D:\SourceCodes\ExitPass_DBv1.2` | #254 | Yes |
| `OC-DBPOLICY-GAP-006` | Jurisdiction scope baseline is missing from the DB repo. | Site-to-policy resolution can remain denormalized and hard to govern. | DB Architecture/Ops | `D:\SourceCodes\ExitPass_DBv1.2` | #254 | Conditional |
| `OC-DBPOLICY-GAP-007` | Import validation rules and offline validator were not found on the inspected branch. | Policy intake controls may be incomplete or not merged. | Backend/Product Ops | app repo | #258 | Conditional |
| `OC-DBPOLICY-GAP-008` | Readiness SQL currently targets the compatibility table only. | Dedicated registry readiness may not be verified after DB alignment. | Backend/QA | app repo | #255 | Conditional |
| `OC-DBPOLICY-GAP-009` | App resolver currently depends on compatibility-oriented policy resolution paths. | Governed registry rows may not affect runtime behavior until query paths are updated. | Backend | app repo | #255 | Conditional |
| `OC-DBPOLICY-GAP-010` | Operator-facing readiness indicators are not complete. | Operators and supervisors may not see why manual review is required. | Frontend/Backend/QA | app repo | #257 | Conditional |

## 17. Recommended Next Slices

Recommended bounded next slices:

- #254 ExitPass_DBv1.2 statutory discount policy registry baseline proposal
- #255 Operator Console policy readiness dedicated-registry query design
- #256 Operator Console production statutory discount policy test matrix
- #257 Operator Console policy readiness UX/reporting indicators
- #258 Operator Console policy admin/import API design

Recommended immediate next slice: #254 ExitPass_DBv1.2 statutory discount policy registry baseline proposal.

Reason: production rollout needs the DB repo source of truth to define the governed registry baseline before app query paths, test matrices, UX, or import APIs can be finalized.

## 18. DB Change Decision

- Database changes required now: Yes, but not in this app-repo slice.
- Target repo: `D:\SourceCodes\ExitPass_DBv1.2`.
- Required before production auto-application: Yes.
- Local DB mutation performed in this slice: No.
- DB repo mutation performed in this slice: No.

## 19. Boundary Confirmations

- No backend behavior changes.
- No frontend behavior changes.
- No database, DDL, migration, or seed mutations.
- No production policy seed data added.
- No WebPay changes.
- No payment/provider routing changes.
- No AUB changes.
- No coupon, reconciliation, HikCentral, or gate changes.
- No sensitive credentials, production IDs, private keys, raw evidence, or personal data added.
