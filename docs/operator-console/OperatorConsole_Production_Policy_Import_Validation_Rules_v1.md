# Operator Console Production Policy Import Validation Rules v1

## 1. Title And Purpose

This document defines validation rules for future production statutory discount policy import into the Operator Console policy registry workflow.

The validation rules do not approve any policy row for production by themselves. A row that passes template validation is only eligible for governed review. Actual database changes, imports, and production activation must follow state-based database governance, compliance approval, and the approved import or admin workflow.

Production statutory discount auto-application remains blocked while required Senior Citizen and PWD production policy rows are missing, unverified, sandbox-only, inactive, expired, unscoped, or not aligned with the state-based database baseline.

## 2. Scope

In scope:

- CSV and template field definitions
- field-level validation rules
- cross-field validation rules
- entitlement-specific validation
- jurisdiction and site scope validation
- evidence rule validation
- legal and ordinance source validation
- review and approval fields
- import rejection rules
- manual review routing rules
- database alignment reminders

Out of scope:

- actual database import
- production policy seed creation
- admin UI
- backend runtime behavior changes
- WebPay
- payment provider routing
- AUB
- coupon validation
- reconciliation
- HikCentral or gate implementation
- raw evidence, OCR, or automated ID validation

## 3. Source Alignment

Primary aligned artifacts:

- `docs/operator-console/OperatorConsole_Production_Policy_Registry_Admin_Import_Alignment_v1.md`
- `docs/operator-console/OperatorConsole_Production_Policy_Import_Template_v1.csv`
- `docs/operator-console/OperatorConsole_Production_Policy_Registry_Readiness_v1.md`
- `scripts/operator-console/Verify-ProductionPolicyRegistryReadiness.sql`
- `infra/db/patches/ExitPass_StatutoryDiscountPolicyRegistrySchema_v1.2.sql`
- Central PMS policy readiness classification model from #250

The current live compatibility table is `discounts.discount_policy_references`. The dedicated governed registry table, `discounts.statutory_discount_policy_registry`, is present in application patch form but is not the accepted state-based DB baseline yet.

The import-facing values below are the proposed controlled import contract. Where they differ from current patch or compatibility-table enum names, the mismatch is documented as an alignment item and must not be fixed through ad hoc local DB changes.

## 4. Template Fields

The production policy import template is:

- `docs/operator-console/OperatorConsole_Production_Policy_Import_Template_v1.csv`

The template must remain header-only until an approved import process exists. Candidate import files may use the same headers, but they must not be treated as source of truth until governance approval and DB repo alignment are complete.

| Column | Description | Required | Allowed values | Format | Non-production example | Validation failure behavior |
| --- | --- | --- | --- | --- | --- | --- |
| `policy_code` | Stable controlled policy code. | Required | Uppercase controlled code; no sandbox/test/dev markers. | `^[A-Z0-9][A-Z0-9_]{2,127}$` | `DUMMY_QA_SENIOR_POLICY` | Hard reject. |
| `policy_name` | Human-readable policy name. | Required | Non-empty text. | 1-256 characters. | `Dummy QA Senior Policy` | Hard reject. |
| `entitlement_type` | Covered statutory entitlement. | Required | `SENIOR_CITIZEN`, `PWD`, `OTHER_STATUTORY`. | Controlled code. | `SENIOR_CITIZEN` | Hard reject. |
| `lgu_code` | Local government code or approved operational code. | Conditional | Controlled code from approved scope registry. | Uppercase code or blank. | `DUMMY_LGU` | Manual review if unknown; hard reject when local scope is otherwise absent. |
| `jurisdiction_name` | Human-readable jurisdiction name. | Conditional | Approved jurisdiction name. | Text or blank. | `Dummy City` | Manual review if ambiguous; hard reject when local scope is otherwise absent. |
| `site_group_code` | Site group scope for policy. | Conditional | Approved site group code. | Uppercase code or blank. | `DUMMY_GROUP` | Manual review if unknown; hard reject when required scope is absent. |
| `site_code` | Site scope for policy. | Conditional | Approved site code. | Uppercase code or blank. | `DUMMY_SITE` | Manual review if unknown; hard reject when required scope is absent. |
| `policy_level` | Level at which the policy applies. | Required | `NATIONAL_LAW`, `LOCAL_ORDINANCE`, `SITE_POLICY`, `OPERATIONAL_POLICY`. | Controlled code. | `LOCAL_ORDINANCE` | Hard reject. |
| `policy_type` | Policy source/type category. | Required | `LEGAL_REFERENCE`, `LOCAL_ORDINANCE`, `SITE_POLICY`, `OPERATIONAL_POLICY`, `IMPLEMENTATION_POLICY`. | Controlled code. | `LOCAL_ORDINANCE` | Hard reject. |
| `policy_resolution_basis` | Resolver basis to persist for traceability. | Required | `LOCAL_ORDINANCE_APPLIED`, `NATIONAL_LAW_FALLBACK`, `SITE_POLICY_OPERATIONAL_ONLY`, `MANUAL_POLICY_SELECTION`, `SYSTEM_DEFAULT`. | Controlled code. | `LOCAL_ORDINANCE_APPLIED` | Hard reject. |
| `benefit_type` | Benefit behavior represented by the policy. | Required | `STATUTORY_DISCOUNT_VAT_EXEMPT`, `FREE_DURATION`, `INITIAL_RATE_EXEMPTION`, `FULL_FEE_EXEMPTION`, `LOCAL_RULE`, `MANUAL_REVIEW`. | Controlled code. | `STATUTORY_DISCOUNT_VAT_EXEMPT` | Hard reject. |
| `discount_base_scope` | Fee basis for discount computation. | Required | Import contract: `VAT_EXCLUSIVE`, `GROSS`, `NET`, `NOT_APPLICABLE`; patch currently uses `FULL_PARKING_FEE`, `CHARGEABLE_PORTION_ONLY`, `NOT_APPLICABLE`. | Controlled code. | `VAT_EXCLUSIVE` | Hard reject until DB alignment maps the value. |
| `free_duration_minutes` | Free parking duration, if applicable. | Conditional | Integer minutes or blank. | Non-negative integer. | `60` | Hard reject if negative or inconsistent with benefit flags. |
| `initial_rate_exempt` | Whether the initial parking rate is exempted. | Required | `true`, `false`. | Boolean. | `false` | Hard reject. |
| `full_fee_exempt` | Whether the full parking fee is exempted. | Required | `true`, `false`. | Boolean. | `false` | Hard reject. |
| `overnight_excluded` | Whether overnight parking is excluded. | Required | `true`, `false`. | Boolean. | `true` | Hard reject. |
| `valet_excluded` | Whether valet parking is excluded. | Required | `true`, `false`. | Boolean. | `true` | Hard reject. |
| `standalone_parking_excluded` | Whether standalone parking is excluded. | Required | `true`, `false`. | Boolean. | `false` | Hard reject. |
| `driver_or_passenger_required` | Whether the beneficiary must be driver or passenger. | Required | `true`, `false`. | Boolean. | `true` | Hard reject. |
| `beneficiary_residency_scope` | Residency requirement. | Required | `RESIDENT_ONLY`, `NON_RESIDENT_ALLOWED`, `MIXED_OR_CONFLICTING`, `UNVERIFIED`, `NOT_APPLICABLE`; patch currently uses `MIXED`. | Controlled code. | `RESIDENT_ONLY` | Hard reject for invalid value; manual review for `MIXED_OR_CONFLICTING` or `UNVERIFIED`. |
| `requires_evidence` | Whether evidence is required by policy/workflow. | Required | `true`, `false`. | Boolean. | `true` | Hard reject. |
| `required_evidence_type` | Evidence type required when evidence is required. | Conditional | `SENIOR_CITIZEN_ID`, `PWD_ID`, `AUTHORIZATION_LETTER`, `SUPPORTING_DOCUMENT`, `VALIDATION_SCREENSHOT`, `HASH_ONLY_REFERENCE`, `OTHER`. | Controlled code or blank when evidence is not required. | `SENIOR_CITIZEN_ID` | Hard reject if missing or mismatched when evidence is required. |
| `requires_operator_validation` | Whether Operator Console validation is required. | Required | `true`, `false`. | Boolean. | `true` | Hard reject for production auto-application when false unless formally exempted. |
| `legal_basis_reference` | General legal reference when not ordinance/national-law specific. | Conditional | Reviewed legal source reference. | Text or blank. | `DUMMY LEGAL BASIS` | Hard reject when no legal, ordinance, or national reference exists. |
| `ordinance_reference` | Local ordinance reference. | Conditional | Official ordinance identifier or blank. | Text or blank. | `DUMMY ORDINANCE 0000` | Hard reject for local ordinance rows when absent. |
| `national_law_reference` | National fallback law reference. | Conditional | `RA 9994`, `RA 10754`, or approved national-law reference. | Text or blank. | `RA 9994` | Hard reject for national fallback rows when absent or entitlement-mismatched. |
| `source_reference` | Reviewed source document or controlled reference. | Required | Official source, approved secondary source, or controlled document reference. | Text. | `DUMMY_SOURCE_REF` | Hard reject when absent. |
| `verification_status` | Review/verification state of the source. | Required | `LEAD_UNVERIFIED`, `VERIFIED_SECONDARY`, `VERIFIED_OFFICIAL`, `APPROVED_FOR_PILOT`, `ACTIVE_APPROVED`, `PROPOSED_ONLY`, `REJECTED`. | Controlled code. | `VERIFIED_SECONDARY` | Hard reject if invalid; non-approved states cannot become active. |
| `effective_from` | First effective policy date. | Required | Valid date. | `yyyy-MM-dd`. | `2099-01-01` | Hard reject. |
| `effective_to` | End date, if known. | Optional | Valid date later than `effective_from`. | `yyyy-MM-dd` or blank. | `2099-12-31` | Hard reject if earlier than or equal to `effective_from`. |
| `reviewed_by` | Reviewer identifier or controlled reviewer reference. | Conditional | Approved user or reviewer reference. | Text or blank. | `dummy-reviewer` | Hard reject for `VERIFIED_SECONDARY` or higher when absent. |
| `reviewed_at` | Review timestamp. | Conditional | Valid timestamp. | ISO 8601 timestamp or `yyyy-MM-dd`. | `2099-01-02T00:00:00Z` | Hard reject for `VERIFIED_SECONDARY` or higher when absent or invalid. |
| `approved_by` | Approver identifier or controlled approver reference. | Conditional | Approved user or approver reference. | Text or blank. | `dummy-approver` | Hard reject for `APPROVED_FOR_PILOT` or `ACTIVE_APPROVED` when absent. |
| `approved_at` | Approval timestamp. | Conditional | Valid timestamp. | ISO 8601 timestamp or `yyyy-MM-dd`. | `2099-01-03T00:00:00Z` | Hard reject for `APPROVED_FOR_PILOT` or `ACTIVE_APPROVED` when absent or invalid. |
| `notes` | Non-authoritative notes for review. | Optional | Non-sensitive text only. | Text or blank. | `Dummy review note` | Warn if sensitive/raw evidence appears to be included. |

## 5. Allowed Values

### Import Contract Values

`entitlement_type`:

- `SENIOR_CITIZEN`
- `PWD`
- `OTHER_STATUTORY`

`policy_level`:

- `NATIONAL_LAW`
- `LOCAL_ORDINANCE`
- `SITE_POLICY`
- `OPERATIONAL_POLICY`

`policy_type`:

- `LEGAL_REFERENCE`
- `LOCAL_ORDINANCE`
- `SITE_POLICY`
- `OPERATIONAL_POLICY`
- `IMPLEMENTATION_POLICY`

`policy_resolution_basis`:

- `LOCAL_ORDINANCE_APPLIED`
- `NATIONAL_LAW_FALLBACK`
- `SITE_POLICY_OPERATIONAL_ONLY`
- `MANUAL_POLICY_SELECTION`
- `SYSTEM_DEFAULT`

`benefit_type`:

- `STATUTORY_DISCOUNT_VAT_EXEMPT`
- `FREE_DURATION`
- `INITIAL_RATE_EXEMPTION`
- `FULL_FEE_EXEMPTION`
- `LOCAL_RULE`
- `MANUAL_REVIEW`

`discount_base_scope`:

- `VAT_EXCLUSIVE`
- `GROSS`
- `NET`
- `NOT_APPLICABLE`

`beneficiary_residency_scope`:

- `RESIDENT_ONLY`
- `NON_RESIDENT_ALLOWED`
- `MIXED_OR_CONFLICTING`
- `UNVERIFIED`
- `NOT_APPLICABLE`

`required_evidence_type`:

- `SENIOR_CITIZEN_ID`
- `PWD_ID`
- `AUTHORIZATION_LETTER`
- `SUPPORTING_DOCUMENT`
- `VALIDATION_SCREENSHOT`
- `HASH_ONLY_REFERENCE`
- `OTHER`

`verification_status`:

- `LEAD_UNVERIFIED`
- `VERIFIED_SECONDARY`
- `VERIFIED_OFFICIAL`
- `APPROVED_FOR_PILOT`
- `ACTIVE_APPROVED`
- `PROPOSED_ONLY`
- `REJECTED`

### Known Alignment Mismatches

The current compatibility table does not contain structured `verification_status`, `benefit_type`, `discount_base_scope`, `beneficiary_residency_scope`, reviewer/approver, free-duration, or exclusion fields.

The application patch for the future registry currently defines these values:

- `discounts.policy_verification_status_enum`: `VERIFIED_OFFICIAL`, `VERIFIED_SECONDARY`, `LEAD_UNVERIFIED`, `PROPOSED`, `NO_LOCAL_RULE_FOUND`
- `discounts.beneficiary_residency_scope_enum`: `RESIDENT_ONLY`, `NON_RESIDENT_ALLOWED`, `MIXED`, `UNVERIFIED`, `NOT_APPLICABLE`
- `discounts.parking_benefit_type_enum`: `STATUTORY_DISCOUNT_VAT_EXEMPT`, `FREE_DURATION`, `INITIAL_RATE_EXEMPTION`, `FULL_FEE_EXEMPTION`, `LOCAL_RULE`, `MANUAL_REVIEW`
- `discounts.discount_base_scope_enum`: `FULL_PARKING_FEE`, `CHARGEABLE_PORTION_ONLY`, `NOT_APPLICABLE`

Before any import implementation, #253 must decide whether the import contract maps values to the patch enums, changes the DB baseline enums, or narrows the import values to the accepted DB baseline. This document does not change database or runtime enum behavior.

## 6. Field-Level Validation Rules

- `policy_code` is required, must be uppercase controlled code, must not contain spaces, and must not contain `SANDBOX`, `TEST`, `DEV`, or `E2E`.
- `policy_name` is required and must not describe the row as sandbox, test, dev, fixture, or example.
- `entitlement_type` is required and must be one of the allowed values.
- `policy_level`, `policy_type`, `policy_resolution_basis`, `benefit_type`, `discount_base_scope`, `beneficiary_residency_scope`, and `verification_status` are required controlled values.
- Boolean fields must be `true` or `false`.
- `effective_from` is required.
- `effective_to` is optional, but when supplied it must be later than `effective_from`.
- `free_duration_minutes` must be blank or a non-negative integer.
- `source_reference` is required for every candidate row.
- At least one legal authority reference is required: `legal_basis_reference`, `ordinance_reference`, or `national_law_reference`.
- `ordinance_reference` is required for `LOCAL_ORDINANCE` policy rows or `LOCAL_ORDINANCE_APPLIED` resolution.
- `national_law_reference` is required for `NATIONAL_LAW` policy rows or `NATIONAL_LAW_FALLBACK` resolution.
- `SENIOR_CITIZEN` standard policies require `SENIOR_CITIZEN_ID` when `requires_evidence=true`.
- `PWD` standard policies require `PWD_ID` when `requires_evidence=true`.
- `requires_evidence=true` requires `required_evidence_type`.
- `requires_operator_validation` should be `true` for controlled Operator Console validation unless an approved exemption exists.
- `reviewed_by` and `reviewed_at` are required for `VERIFIED_SECONDARY`, `VERIFIED_OFFICIAL`, `APPROVED_FOR_PILOT`, or `ACTIVE_APPROVED`.
- `approved_by` and `approved_at` are required for `APPROVED_FOR_PILOT` or `ACTIVE_APPROVED`.
- `PROPOSED_ONLY`, `LEAD_UNVERIFIED`, and `REJECTED` rows must not be marked or interpreted as production-active.
- `notes` must not contain raw evidence, identity document numbers, private keys, credentials, or personal data.

## 7. Cross-Field Validation Rules

- Senior Citizen policy rows must not imply PWD coverage unless a separate PWD row exists for the same governed scope and effective period.
- PWD policy rows must not imply Senior Citizen coverage unless a separate Senior Citizen row exists for the same governed scope and effective period.
- Local ordinance policies require `ordinance_reference` and at least one explicit scope field: `lgu_code`, `jurisdiction_name`, `site_group_code`, or `site_code`.
- National fallback policies require `national_law_reference` and should not specify a local ordinance as the primary basis.
- `SITE_POLICY_OPERATIONAL_ONLY` must not be treated as legal ordinance authority unless explicitly reviewed and routed as manual-review or pilot-only.
- `free_duration_minutes` must align with `benefit_type`, `initial_rate_exempt`, and `full_fee_exempt`.
- `FREE_DURATION` requires either `free_duration_minutes` or an approved manual-review reason.
- `INITIAL_RATE_EXEMPTION` requires `initial_rate_exempt=true`.
- `FULL_FEE_EXEMPTION` requires `full_fee_exempt=true`.
- `full_fee_exempt=true` and `initial_rate_exempt=true` conflict handling must be explicit in notes and approval records.
- `standalone_parking_excluded`, `valet_excluded`, and `overnight_excluded` must be explicitly encoded when the ordinance requires exclusions.
- Resident-only policies require `beneficiary_residency_scope=RESIDENT_ONLY`.
- `beneficiary_residency_scope=MIXED_OR_CONFLICTING` or `UNVERIFIED` blocks production auto-application until resolved or formally approved for pilot manual review.
- `requires_evidence=false` on Senior Citizen or PWD production policies requires formal compliance exemption.
- `requires_operator_validation=false` blocks controlled Operator Console production auto-application unless the policy is outside this workflow and explicitly approved.
- Duplicate active policy scope for the same entitlement, site/jurisdiction, and overlapping effective period is not allowed unless supersession is explicit.

## 8. Ordinance Verification Rules

- Official ordinance copy or official LGU publication is required for production automatic local policy application.
- Public reports, social posts, community posts, news summaries, and ordinance indexes are leads only.
- Legal or Compliance review is required before production activation.
- `source_reference` must point to the reviewed source or controlled internal source record.
- Residency scope must be encoded explicitly.
- Facility scope and exclusions must be encoded explicitly.
- Senior Citizen and PWD policy rows must be separate when rules, evidence, residency, exclusions, or benefits differ.
- Proposed ordinances must remain `PROPOSED_ONLY` or `LEAD_UNVERIFIED`.
- Unresolved residency scope, facility scope, ordinance version, or benefit details block `ACTIVE_APPROVED`.

The ordinance DOCX in this repo is operational research, not a legal opinion. It identifies leads and candidate jurisdictions, but no row from it should become production-active without official source review.

## 9. Import Rejection Rules

Hard reject the candidate file or row when any of these conditions are present:

- missing required field
- invalid enum or controlled value
- duplicate CSV header
- missing required CSV column
- unexpected header order for a controlled template import
- duplicate `policy_code` in the same file
- sandbox, test, dev, fixture, or E2E marker in a production row
- `policy_code` beginning with `EXAMPLE`
- proposed-only policy marked or implied as active approved
- no legal, ordinance, national law, or source reference
- mismatch between entitlement and required evidence type
- active-approved policy with missing reviewer or approver fields
- local ordinance row without jurisdiction, LGU, site group, or site scope
- national fallback row without required national law reference
- expired row submitted as active without supersession or retirement context
- duplicate active policy for the same entitlement, site/jurisdiction, and effective period unless supersession is explicit
- raw evidence, identity document number, production credential, private key, or personal data appears in a row

## 10. Manual Review Routing Rules

Route to manual review instead of immediate hard reject when the candidate row has a plausible official or controlled source but unresolved operational details:

- secondary source only
- mixed residency scope
- conflicting ordinance versions
- facility scope unclear
- benefit type unclear
- free-duration or initial-rate details unclear
- effective date unclear but source appears official
- site or site group mapping exists but needs master-data confirmation
- compatibility-table mapping exists but governed registry fields are incomplete
- `VERIFIED_SECONDARY` source needs Compliance or site approval before pilot

Manual review does not permit production auto-application. It only preserves a candidate row for governed review and later approval.

## 11. Future Import Process

Recommended staged process:

1. Validate CSV offline.
2. Legal and Compliance review source references.
3. Product and Operations approve policy behavior, site scope, exclusions, and rollout scope.
4. Align the state-based DB repo or use an approved admin/import workflow.
5. Run production policy readiness verification.
6. Run policy resolution and statutory discount draft tests.
7. Activate for controlled pilot with manual-review controls.
8. Activate production auto-application only after governed registry readiness is proven.

Production imports should create `DRAFT` or `UNDER_REVIEW` records by default until maker-checker approval is implemented. `ACTIVE_APPROVED` must require a separate approval and activation record.

## 12. DB Alignment Rule

Approved production policy rows must not be inserted ad hoc into the local database and treated as source of truth.

If policies become baseline or reference data, update and validate the state-based DB repository:

- `D:\SourceCodes\ExitPass_DBv1.2`

Required DB alignment steps:

1. Decide the governed registry schema and import enum values.
2. Update DB repo schema/reference-data artifacts only after approval.
3. Validate with the project's state-based or Atlas comparison workflow.
4. Rebuild the local database from the DB repo baseline or compare drift.
5. Rerun `scripts/operator-console/Run-ProductionPolicyRegistryReadinessCheck.ps1 -WarnOnly`.
6. Update application repo docs and import references only after DB baseline ownership is clear.

No local DB drift should become baseline. No direct local production policy row insert should be treated as authoritative.

## 13. Offline Validator Script

The optional offline validator is:

- `scripts/operator-console/Test-ProductionPolicyImportTemplate.ps1`

Expected behavior:

- accepts `-Path` to a CSV file
- checks duplicate headers
- checks required columns are present
- checks exact header order for the controlled template contract
- accepts the candidate worksheet review columns after the import template columns
- reports `PASS`, `WARN`, and `FAIL`
- emits row numbers and a summary line
- treats the official template as safe only when it is header-only
- validates candidate rows for obvious field, enum, date, evidence, source, sandbox/dev marker, and duplicate-code errors
- rejects `DRY_RUN_ONLY` and `EXAMPLE_DO_NOT_IMPORT` rows as not importable
- does not connect to the database
- does not import anything
- does not mutate files
- does not print secrets

## 14. Dry-Run Validation After #261

#261 adds an offline sample dry run:

- `docs/operator-console/samples/OperatorConsole_Production_Policy_Candidate_Dry_Run_Sample_v1.csv`
- `docs/operator-console/OperatorConsole_Production_Policy_Candidate_Dry_Run_Report_v1.md`

The sample is deliberately non-production and includes bad rows. It is expected to fail validation so reviewers can confirm that the validator catches dry-run/example markers, missing source references, entitlement/evidence mismatches, proposed-only approval mistakes, sandbox/test markers, and duplicate policy codes.

The header-only candidate worksheet remains safe and should validate with no hard failures:

- `docs/operator-console/OperatorConsole_Production_Policy_Candidate_Worksheet_v1.csv`

This validation remains offline only. It does not connect to the database, execute SQL, import rows, mutate files, or approve production policy data.

## 15. Recommended Next Slices

Recommended bounded next slices:

- #253 Operator Console policy registry DB baseline alignment plan for ExitPass_DBv1.2
- #254 Operator Console policy admin/import API design
- #255 Operator Console policy readiness UX/reporting indicators
- #256 Operator Console production statutory discount policy test matrix

Recommended immediate next slice: #253 Operator Console policy registry DB baseline alignment plan for ExitPass_DBv1.2.

Reason: the import template now defines candidate policy shape and validation rules. The next blocker is deciding how the governed registry, enum values, and baseline/reference policy data are represented in the state-based DB repository.

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
