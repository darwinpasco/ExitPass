# Operator Console Production Policy Fixture Review Package v1

## 1. Title And Purpose

This is the review package for candidate production statutory discount policy rows for Operator Console.

This package gives Product, Legal/Compliance, Operations, QA, Backend/Architecture, Data/DB, and site/client stakeholders a controlled way to review candidate Senior Citizen and PWD policy rows before any row becomes DB baseline reference data, import input, or production-active configuration.

This package does not approve, import, seed, insert, or activate any production policy by itself.

## 2. Scope

In scope:

- review of candidate Senior Citizen and PWD policy rows
- ordinance and source evidence review
- entitlement-specific policy review
- residency and facility scope review
- evidence requirement review
- effective date review
- policy readiness classification review
- DB repo alignment requirement
- sign-off workflow

Out of scope:

- actual import
- DB mutation
- production seed creation
- admin UI
- backend behavior changes
- frontend behavior changes
- WebPay
- payment provider routing
- AUB
- coupons
- reconciliation
- HikCentral or gate implementation
- raw evidence, OCR, or automated ID validation

## 3. Current Readiness Position

The dedicated statutory discount policy registry exists in the DB baseline owned by `D:\SourceCodes\ExitPass_DBv1.2`:

- `discounts.statutory_discount_policy_registry`

The application resolver/readiness logic prefers the dedicated registry when present and retains compatibility fallback to:

- `discounts.discount_policy_references`

Dedicated-registry integration behavior has been validated using non-production test fixtures, and Operator Console UI/reporting now exposes policy readiness indicators.

Production policy rows are still missing. Current readiness remains:

- `DEDICATED_REGISTRY_PRESENT`: 1
- `MISSING_REQUIRED_POLICY`: 2

Production statutory discount auto-application remains NO-GO until approved Senior Citizen and PWD production policy rows exist, are verified, and pass readiness validation.

## 4. Candidate Policy Row Review Workflow

1. Prepare a candidate worksheet using the approved policy import/review fields.
2. Attach official ordinance, national-law, or controlled source evidence.
3. Complete Legal/Compliance review for source authority, legal coverage, and enactment status.
4. Complete Product review for intended customer experience and entitlement scope.
5. Complete Operations review for site, facility, cashier/operator, and manual-review implications.
6. Complete engineering validation against import rules, policy resolver behavior, readiness classification, and reporting visibility.
7. Complete maker/checker approval before any row can be submitted to DB repo baseline work or a future governed import/admin flow.
8. Decide whether the approved row belongs in DB repo reference data or a governed admin/import input.
9. Run readiness verification after the row is inserted into an approved environment.
10. Activate for controlled pilot only after site/client signoff and operational controls are confirmed.
11. Consider production activation only after Senior Citizen and PWD required rows are both approved, encoded, tested, and verified.

## 5. Required Reviewers

| Role | Responsibility |
| --- | --- |
| Product Owner | Confirms intended policy behavior, rollout scope, and user/operator impact. |
| Legal/Compliance reviewer | Confirms source authority, legal basis, ordinance interpretation, and approval status. |
| Operations owner | Confirms site operating model, manual-review process, facility exclusions, and staff readiness. |
| Backend/Architecture reviewer | Confirms resolver/readiness behavior, API exposure, and fail-closed behavior. |
| QA reviewer | Confirms test coverage, fixture isolation, readiness validation, and regression scope. |
| Data/DB owner | Confirms DB repo alignment, reference-data governance, and no ad hoc baseline drift. |
| Site/Client representative | Confirms local site/client policy applicability when the row is site or client specific. |

## 6. Required Evidence Package

Each candidate row must have a controlled evidence package with:

- official ordinance copy, national-law reference, or official LGU publication
- source URL or controlled document reference
- source document hash when available
- summary of beneficiary scope
- summary of parking benefit
- residency scope determination
- facility scope and exclusions
- effective date basis
- evidence requirement basis
- review notes

Do not attach raw ID images, ID numbers, private evidence files, credentials, secrets, or personal data to this package.

## 7. Candidate Policy Fields To Review

Identity fields:

- `policy_code`
- `policy_name`
- `entitlement_type`
- `verification_status`

Scope fields:

- `lgu_code`
- `jurisdiction_name`
- `site_group_code`
- `site_code`
- `policy_level`
- `policy_type`
- `policy_resolution_basis`
- `beneficiary_residency_scope`

Benefit fields:

- `benefit_type`
- `discount_base_scope`
- `free_duration_minutes`
- `initial_rate_exempt`
- `full_fee_exempt`
- `overnight_excluded`
- `valet_excluded`
- `standalone_parking_excluded`
- `driver_or_passenger_required`

Evidence fields:

- `requires_evidence`
- `required_evidence_type`
- `requires_operator_validation`

Source and legal fields:

- `legal_basis_reference`
- `ordinance_reference`
- `national_law_reference`
- `source_reference`

Review and approval fields:

- `reviewed_by`
- `reviewed_at`
- `approved_by`
- `approved_at`
- `notes`

Lifecycle and effective date fields:

- `effective_from`
- `effective_to`

## 8. Senior Citizen And PWD Separation Rule

Senior Citizen and PWD policy rows must be separate rows unless the data model explicitly represents both and the legal basis truly matches both entitlements.

A Senior Citizen ordinance does not imply PWD coverage. A PWD ordinance does not imply Senior Citizen coverage.

Different residency, facility, site, site-group, jurisdiction, benefit, evidence, or effective-date scopes require separate rows.

## 9. Classification Rules For Review

| Review outcome | Meaning |
| --- | --- |
| `APPROVE_FOR_IMPORT` | Candidate is approved for governed DB repo reference-data work or approved import/admin input. |
| `APPROVE_FOR_PILOT_ONLY` | Candidate may be used for controlled pilot with manual controls, but is not full production auto-application authority. |
| `ROUTE_TO_MANUAL_REVIEW` | Candidate may support manual review but must not auto-apply in production. |
| `REJECT_NEEDS_SOURCE` | Candidate lacks official or controlled source evidence. |
| `REJECT_SCOPE_UNCLEAR` | Beneficiary, residency, site, facility, or jurisdiction scope is ambiguous. |
| `REJECT_NOT_ENACTED` | Source does not prove enacted or active authority. |
| `REJECT_DUPLICATE` | Candidate duplicates or conflicts with an existing reviewed row. |
| `DEFER_PENDING_LEGAL_REVIEW` | Legal/compliance interpretation is unresolved. |

## 10. Production Readiness Decision

A candidate row is not production-ready until it has completed review and approval.

An approved candidate row is not production-active until DB repo or approved import/admin governance is completed and the target environment has been verified.

Readiness verification must pass after the row is inserted into an approved environment.

Production statutory discount auto-application remains NO-GO until both Senior Citizen and PWD required rows are present, verified, production-active, and tested.

## 11. Engineering Validation Expectations

Before a reviewed row can be considered for baseline or approved import:

- import template validator passes
- readiness SQL identifies the row correctly
- policy resolver classifies the row correctly
- draft behavior matches readiness classification
- audit/reporting displays safe policy details
- boundary mutation checks stay zero for payment, exit, coupon, reconciliation, and gate tables

## 12. DB Repo Alignment

If candidate rows become baseline or reference data, they belong in:

- `D:\SourceCodes\ExitPass_DBv1.2`

Application repo templates, worksheets, docs, and local fixture data do not become DB source of truth.

No ad hoc local DB row should be treated as baseline. Local DB state must be derived from the DB repo baseline or an approved import/admin workflow.

## 13. Sign-Off Table

| Role | Name | Decision | Date | Notes | Signature/Initials |
| --- | --- | --- | --- | --- | --- |
| Product Owner |  |  |  |  |  |
| Legal/Compliance reviewer |  |  |  |  |  |
| Operations owner |  |  |  |  |  |
| Backend/Architecture reviewer |  |  |  |  |  |
| QA reviewer |  |  |  |  |  |
| Data/DB owner |  |  |  |  |  |
| Site/Client representative |  |  |  |  |  |

## 14. Go/No-Go Position

- GO for candidate policy review.
- GO for controlled pilot only after site-approved policy evidence, manual controls, and operational signoff are complete.
- NO-GO for production statutory discount auto-application until approved policy rows are encoded, verified, tested, and readiness checks pass.

## 15. Recommended Next Slice

Recommended next slice:

- #261 Production policy candidate worksheet validation and sample dry run

Reason: the review package defines governance. The next bounded step is to validate the candidate worksheet process with non-production/sample dry-run data without approving, importing, or seeding production policy rows.

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
