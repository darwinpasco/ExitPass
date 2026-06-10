# Operator Console Production Policy Review Checklist v1

This checklist supports review of candidate production statutory discount policy rows before any row becomes DB baseline reference data, import input, or production-active configuration.

Use one checklist per candidate policy row. Senior Citizen and PWD rows must be reviewed separately unless the reviewed legal source truly covers both and the approved data model explicitly supports that representation.

## 1. Source/Legal Review

| Check item | Required evidence | Owner | Pass/Fail/Needs review | Notes |
| --- | --- | --- | --- | --- |
| Official source is available. | Official ordinance, national law, LGU publication, or controlled legal document reference. | Legal/Compliance |  |  |
| Source is enacted and active. | Enactment date, publication record, or controlled legal confirmation. | Legal/Compliance |  |  |
| Legal basis matches the entitlement. | Legal review note mapping source to Senior Citizen or PWD entitlement. | Legal/Compliance |  |  |
| Source reference is controlled and traceable. | Source URL, document reference, and source hash if available. | Legal/Compliance |  |  |
| No raw evidence or personal data is included. | Review of attachments and worksheet fields. | Legal/Compliance |  |  |

## 2. Entitlement Review

| Check item | Required evidence | Owner | Pass/Fail/Needs review | Notes |
| --- | --- | --- | --- | --- |
| Entitlement type is correct. | Candidate row and source interpretation. | Product Owner |  |  |
| Senior Citizen and PWD rows are separated when required. | Candidate row set and entitlement mapping notes. | Product Owner |  |  |
| Senior Citizen row does not imply PWD coverage. | Legal review note. | Legal/Compliance |  |  |
| PWD row does not imply Senior Citizen coverage. | Legal review note. | Legal/Compliance |  |  |
| Required evidence type matches entitlement. | Evidence rule review showing `SENIOR_CITIZEN_ID` or `PWD_ID` as applicable. | Backend/Architecture |  |  |

## 3. Scope Review

| Check item | Required evidence | Owner | Pass/Fail/Needs review | Notes |
| --- | --- | --- | --- | --- |
| Jurisdiction or LGU scope is clear. | `lgu_code`, `jurisdiction_name`, ordinance scope notes. | Operations owner |  |  |
| Site or site-group scope is clear where applicable. | Site list, site-group mapping, or client/site approval. | Operations owner |  |  |
| Residency scope is determined. | Residency clause summary and legal interpretation. | Legal/Compliance |  |  |
| Facility scope is documented. | Facility inclusion/exclusion summary. | Operations owner |  |  |
| Scope conflicts are routed to manual review or rejected. | Review outcome and notes. | Product Owner |  |  |

## 4. Benefit Calculation Review

| Check item | Required evidence | Owner | Pass/Fail/Needs review | Notes |
| --- | --- | --- | --- | --- |
| Benefit type is correct. | Source benefit summary and candidate row. | Product Owner |  |  |
| Discount base scope is correct. | Legal/product interpretation of VAT-exclusive, gross, net, or not-applicable basis. | Backend/Architecture |  |  |
| Free duration is correct when applicable. | Ordinance/source clause and minutes value. | Operations owner |  |  |
| Initial-rate and full-fee exemptions are correct. | Source clause and product interpretation. | Product Owner |  |  |
| Exclusions are correctly represented. | Overnight, valet, and standalone parking exclusion notes. | Operations owner |  |  |

## 5. Evidence/Operator Validation Review

| Check item | Required evidence | Owner | Pass/Fail/Needs review | Notes |
| --- | --- | --- | --- | --- |
| Evidence requirement is documented. | Source clause or operations policy requiring validation. | Operations owner |  |  |
| Required evidence type is correct. | Entitlement-specific evidence mapping. | Backend/Architecture |  |  |
| Operator validation requirement is clear. | Operations workflow note. | Operations owner |  |  |
| Manual review conditions are documented. | Manual-review criteria and escalation notes. | Product Owner |  |  |
| Raw ID values or raw evidence are excluded from worksheet/reporting. | Worksheet and attachment review. | QA reviewer |  |  |

## 6. Effective Date/Lifecycle Review

| Check item | Required evidence | Owner | Pass/Fail/Needs review | Notes |
| --- | --- | --- | --- | --- |
| Effective-from date is valid. | Source effective date or approval date basis. | Legal/Compliance |  |  |
| Effective-to date is blank or later than effective-from. | Candidate row date review. | QA reviewer |  |  |
| Superseded or expired policy is not approved as active. | Lifecycle notes and existing policy comparison. | Data/DB owner |  |  |
| Pilot-only lifecycle is clearly marked when applicable. | Approval notes and verification status. | Product Owner |  |  |
| Activation timing is coordinated with site operations. | Site rollout note or client approval. | Operations owner |  |  |

## 7. Import Validation Review

| Check item | Required evidence | Owner | Pass/Fail/Needs review | Notes |
| --- | --- | --- | --- | --- |
| Candidate worksheet columns match the approved template. | CSV header comparison. | QA reviewer |  |  |
| Import template validator passes. | `Test-ProductionPolicyImportTemplate.ps1` result. | QA reviewer |  |  |
| Required fields are populated. | Validator output and row review. | QA reviewer |  |  |
| Controlled values are valid. | Validator output and DB enum/control-code alignment. | Data/DB owner |  |  |
| Review and approval fields match intended verification status. | Candidate row review. | Legal/Compliance |  |  |

## 8. DB Repo Alignment Review

| Check item | Required evidence | Owner | Pass/Fail/Needs review | Notes |
| --- | --- | --- | --- | --- |
| Candidate row source of truth is identified. | DB repo reference-data or approved import/admin decision. | Data/DB owner |  |  |
| No ad hoc local DB row is treated as baseline. | DB alignment note. | Data/DB owner |  |  |
| DB repo path is confirmed as baseline owner. | `D:\SourceCodes\ExitPass_DBv1.2` alignment note. | Data/DB owner |  |  |
| Production seed/reference decision is approved. | Maker/checker approval and DB review notes. | Data/DB owner |  |  |
| Sandbox, test, and pilot rows remain separated from production-active rows. | Reference-data or import governance review. | QA reviewer |  |  |

## 9. Application Behavior Validation

| Check item | Required evidence | Owner | Pass/Fail/Needs review | Notes |
| --- | --- | --- | --- | --- |
| Readiness SQL identifies the candidate correctly. | `Verify-ProductionPolicyRegistryReadiness.sql` result. | Backend/Architecture |  |  |
| Resolver classifies the row correctly. | Policy resolution test or controlled validation result. | Backend/Architecture |  |  |
| Production draft behavior matches readiness classification. | Draft API test or controlled validation result. | QA reviewer |  |  |
| Manual-review rows do not auto-apply in production. | Fail-closed/manual-review validation result. | QA reviewer |  |  |
| Boundary mutation checks stay zero. | Before/after counts for payment, exit, coupon, reconciliation, and gate tables. | QA reviewer |  |  |

## 10. Audit/Reporting Validation

| Check item | Required evidence | Owner | Pass/Fail/Needs review | Notes |
| --- | --- | --- | --- | --- |
| Policy code is visible in reporting. | Audit/reporting screenshot or API response sample without sensitive data. | QA reviewer |  |  |
| Verification status is visible. | Audit/reporting sample. | QA reviewer |  |  |
| Readiness classification is visible. | Audit/reporting sample. | QA reviewer |  |  |
| Manual-review requirement is visible when applicable. | Audit/reporting sample. | Operations owner |  |  |
| Raw evidence, raw ID numbers, and private references are not displayed. | Audit/reporting safety review. | Legal/Compliance |  |  |

## 11. Final Go/No-Go Review

| Check item | Required evidence | Owner | Pass/Fail/Needs review | Notes |
| --- | --- | --- | --- | --- |
| All required reviewers have signed off. | Completed sign-off table. | Product Owner |  |  |
| Candidate is approved for import, pilot only, manual review, rejection, or deferral. | Final review outcome. | Product Owner |  |  |
| DB repo or import/admin path is decided. | Data/DB and Backend/Architecture notes. | Data/DB owner |  |  |
| Production readiness verification passes in the approved environment. | Readiness wrapper result. | QA reviewer |  |  |
| Production auto-application remains NO-GO unless both Senior Citizen and PWD required rows are approved and verified. | Final go/no-go note. | Product Owner |  |  |

## Boundary Confirmations

- No backend behavior changes.
- No frontend behavior changes.
- No database, DDL, migration, or seed mutations.
- No production policy seed data added.
- No WebPay changes.
- No payment/provider routing changes.
- No AUB changes.
- No coupon, reconciliation, HikCentral, or gate changes.
- No sensitive credentials, production IDs, private keys, raw evidence, or personal data added.
