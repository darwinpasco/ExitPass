# Statutory Discount Jurisdiction Policy Resolution Design

Status: implementation-readiness design for ExitPass v1.2  
Scope: documentation only; no runtime code, endpoint, database patch, baseline DDL, Bruno collection, UI, payment provider, gate, coupon, reconciliation, or AUB behavior is changed here.

## Purpose

This document defines how ExitPass resolves the applicable statutory discount policy from a parking site's jurisdiction, verified local ordinance policies, site-specific overrides, and mandatory national fallback laws before Operator Console statutory discount draft, decision, and payable-basis application workflows rely on that policy.

The design is deliberately conservative. Parking-specific benefits differ by local government unit. ExitPass must not infer local free parking, free duration, initial-rate exemption, residency limitation, overnight exclusion, valet exclusion, standalone-parking exclusion, or similar parking-specific rules from national law alone.

## Source Baseline

Inspected local sources:

- `docs/operator-console/Philippine_Parking_Statutory_Discount_Local_Ordinances_Detailed_List.docx`
- `docs/operator-console/statutory-discount-payable-basis-application-design.md`
- `docs/operator-console/statutory-discount-applied-tariff-snapshot-lifecycle-design.md`
- `docs/operator-console/statutory-validation-and-access-contract.md`
- `docs/operator-console/operator-console-schema-extension-design.md`
- `docs/ExitPass-v1.2-database-rebuild-baseline.md`
- `infra/db/seed/ExitPass_Reference_Data_v1.2.sql`
- current Operator Console statutory discount draft, decision, and apply-payable-basis code paths
- live PostgreSQL schema for policy, site, evidence, validation, and controlled-code objects

The local ordinance DOCX was inspected as an operational research baseline. It identifies parking-specific local measures across Metro Manila, Rizal, Laguna, Bulacan, Metro Cebu, and Metro Davao, and it explicitly treats public reports, social posts, and ordinance indexes as leads unless official ordinance text or LGU publication has been reviewed.

## Current Baseline

The implemented Operator Console statutory discount chain currently supports:

- access-gated session lookup;
- statutory discount draft creation through `POST /v1/ops/operator-console/statutory-discounts/draft`;
- metadata-only evidence references when evidence capture is requested;
- duplicate-safe statutory discount draft replay;
- access-gated review decision through `POST /v1/ops/operator-console/statutory-discounts/{draftId:guid}/decision`;
- access-gated apply-payable-basis endpoint through `POST /v1/ops/operator-console/statutory-discounts/{validationId:guid}/apply-payable-basis`;
- DB-backed `REQUESTED` rows in `discounts.statutory_discount_payable_basis_applications`;
- design for final `APPLIED` superseding tariff snapshot lifecycle.

Current limitations:

- `OperatorConsoleStatutoryDiscountDraftWriter` inserts `policy_resolution_basis = SYSTEM_DEFAULT`.
- The draft request does not carry a resolved policy identifier.
- There is no backend policy resolver service yet.
- The apply-payable-basis writer uses a fixed national statutory computation formula and does not consume a resolved local policy snapshot.
- Final `APPLIED` tariff snapshot lifecycle is not implemented yet.

## Core Policy Hierarchy

ExitPass should resolve statutory discount policy in this order:

1. Verified site-specific policy override, if explicitly configured and legally approved.
2. Verified city or municipality ordinance for the parking site jurisdiction.
3. Verified province-level rule, if ever applicable and if the site jurisdiction falls under it.
4. National fallback:
   - RA 9994 for Senior Citizens.
   - RA 10754 for PWDs.
5. Fail closed or manual review only if national fallback cannot be mapped or entitlement type is unsupported.

Rules:

- Local verified parking-specific policy overrides national fallback only for parking-specific benefits.
- National fallback applies only when no verified local jurisdiction-specific parking policy is configured.
- National fallback must not automatically create free parking, free-duration, initial-rate exemption, full-fee exemption, resident-only scope, overnight exclusion, valet exclusion, standalone-parking exclusion, or driver/passenger parking-specific rules.
- WebPay must not choose or validate statutory discount policy. Backend policy resolution owns the policy decision.

## National Fallback Behavior

National fallback is mandatory when no verified local jurisdiction-specific parking statutory discount policy is configured.

Senior Citizen fallback:

- entitlement type: `SENIOR_CITIZEN`
- national law reference: `RA 9994`
- `policy_resolution_basis = NATIONAL_LAW_FALLBACK`
- benefit type: statutory discount and VAT treatment on the chargeable parking fee basis configured for the backend computation
- parking-specific benefits: false or not applicable unless a verified local policy separately grants them

PWD fallback:

- entitlement type: `PWD`
- national law reference: `RA 10754`
- `policy_resolution_basis = NATIONAL_LAW_FALLBACK`
- benefit type: statutory discount and VAT treatment on the chargeable parking fee basis configured for the backend computation
- parking-specific benefits: false or not applicable unless a verified local policy separately grants them

National fallback does not mean:

- automatic free parking;
- automatic free first hours;
- automatic exemption from initial rate;
- automatic full fee exemption;
- automatic overnight, valet, or standalone parking coverage;
- automatic local residency preference.

## Local Ordinance Behavior

The inspected DOCX research baseline enumerates local parking statutory discount ordinances with differing rules. ExitPass should model those differences explicitly instead of flattening them into a generic discount flag.

## Local Ordinance Research Baseline

Operational conclusions from `Philippine_Parking_Statutory_Discount_Local_Ordinances_Detailed_List.docx`:

- ExitPass must not implement a generic nationwide Senior Citizen/PWD free-parking rule.
- Local parking-specific benefits must be resolved by site jurisdiction and active verified local policy.
- Each ordinance should be stored as a separate policy row per entitlement type because LGUs differ by Senior/PWD coverage, residency scope, benefit type, free duration, exclusions, verification status, and effective dates.
- The policy registry must support jurisdiction/LGU, ordinance reference, entitlement type, beneficiary residency scope, parking benefit type, free duration, initial-rate exemption, full fee exemption, overnight exclusion, valet exclusion, standalone-parking exclusion, driver/passenger condition, evidence requirement, verification status, effective date, source reference, `reviewed_by`, and `reviewed_at`.
- Verification statuses should include `VERIFIED_OFFICIAL`, `VERIFIED_SECONDARY`, `LEAD_UNVERIFIED`, `PROPOSED`, and `NO_LOCAL_RULE_FOUND`.
- `VERIFIED_OFFICIAL` can be used for production auto-resolution.
- `VERIFIED_SECONDARY` may require compliance or site approval before auto-resolution.
- `LEAD_UNVERIFIED` and `PROPOSED` must not auto-apply.
- `NO_LOCAL_RULE_FOUND` resolves to the national fallback policy: RA 9994 for Senior Citizens and RA 10754 for PWDs.
- National fallback is not automatic free parking.
- Local free parking, free duration, initial-rate exemption, residency restriction, overnight exclusion, valet exclusion, standalone-parking exclusion, and driver/passenger condition must come only from verified configured local ordinance policy rows.

The DOCX remains an operational research document, not a legal opinion. Official ordinance text or LGU publication should be retained as the source reference before a policy row is marked production auto-resolvable.

Required local-policy dimensions:

- covered entitlement types: Senior Citizen, PWD, or both;
- beneficiary residency scope: resident-only, Philippine resident, all eligible beneficiaries, or manual review;
- parking benefit type: statutory discount, free initial duration, initial-rate exemption, full fee exemption, operational courtesy, or mixed;
- free duration in minutes;
- initial-rate exemption flag;
- full fee exemption flag;
- overnight exclusion flag;
- valet exclusion flag;
- standalone parking exclusion flag;
- mall/customer/tenant/resident scope where applicable;
- driver/passenger condition where applicable;
- required ID/evidence;
- source verification status;
- effective date and optional expiry;
- ordinance or resolution reference;
- source URL or document reference;
- reviewer and review timestamp.

Local policy auto-resolution is allowed only for verified rows. Unverified research leads must remain visible for compliance review but must not change payable basis automatically.

## Free-Period And Succeeding-Hours Rule

If a verified local policy grants a free initial parking period, that free period is applied before statutory discount computation.

Rules:

- The payable amount for the free period is zero.
- Senior Citizen/PWD discount must not also apply to the zero-price free period.
- Succeeding hours after the free period use the configured `succeeding_hours_discount_rule`.
- Production-safe default: `REGULAR_RATE` for succeeding hours unless the verified ordinance or site-approved legal policy explicitly allows statutory discount treatment on the remaining chargeable portion.

Required future fields:

- `free_duration_minutes`
- `free_period_application`
- `succeeding_hours_discount_rule`
- `discount_base_scope`
- `stacking_policy`
- `legal_basis_priority`

Recommended controlled values:

`free_period_application`:

- `BEFORE_DISCOUNT_COMPUTATION`

`succeeding_hours_discount_rule`:

- `REGULAR_RATE`
- `APPLY_NATIONAL_STATUTORY_DISCOUNT`
- `APPLY_LOCAL_STATUTORY_DISCOUNT`
- `MANUAL_REVIEW`

`discount_base_scope`:

- `FULL_PARKING_FEE`
- `CHARGEABLE_PORTION_ONLY`
- `NOT_APPLICABLE`

`stacking_policy`:

- `NO_STACKING_ON_FREE_PERIOD`
- `ALLOW_DISCOUNT_ON_SUCCEEDING_HOURS`
- `MANUAL_REVIEW`

`legal_basis_priority`:

- `LOCAL_ORDINANCE_FIRST`
- `NATIONAL_FALLBACK_ONLY_IF_NO_LOCAL_POLICY`
- `SITE_POLICY_REQUIRES_REVIEW`

## Policy Verification Status

Recommended verification statuses:

- `VERIFIED_OFFICIAL`
- `VERIFIED_SECONDARY`
- `LEAD_UNVERIFIED`
- `PROPOSED`
- `NO_LOCAL_RULE_FOUND`

Rules:

- `VERIFIED_OFFICIAL` may be used for production auto-resolution.
- `VERIFIED_SECONDARY` requires site/compliance approval before production auto-resolution.
- `LEAD_UNVERIFIED` must not auto-apply.
- `PROPOSED` must not auto-apply.
- `NO_LOCAL_RULE_FOUND` resolves to national fallback.

The current schema has `discount_policy_status_enum` values `DRAFT`, `ACTIVE`, `SUSPENDED`, `SUPERSEDED`, and `RETIRED`, but it does not have a separate verification status. Do not use `ACTIVE` as a substitute for legal/source verification.

## Schema Inspection Findings

### discounts.discount_policy_references

Existing columns include:

- `discount_policy_reference_id`
- `policy_code`
- `policy_name`
- `policy_description`
- `policy_type`
- `policy_level`
- `entitlement_type`
- `national_law_reference`
- `local_ordinance_reference`
- `lgu_code`
- `jurisdiction_name`
- `site_group_id`
- `site_id`
- `parent_policy_reference_id`
- `fallback_policy_reference_id`
- `precedence_rank`
- `policy_version`
- `requires_operator_validation`
- `requires_evidence_capture`
- `evidence_retention_policy_code`
- `policy_status`
- `effective_from`
- `effective_to`
- attribution and `row_version`

Relevant indexes:

- `uq_discount_policy_references__policy_code_version`
- `ux_discount_policy_references__active_local_policy` on `(entitlement_type, lgu_code, site_group_id, site_id, policy_level, policy_version)` where `policy_status = ACTIVE` and `lgu_code IS NOT NULL`

Gaps:

- no verification status;
- no structured benefit type;
- no free-duration/succeeding-hours fields;
- no residency scope;
- no explicit national fallback rows per RA 9994 and RA 10754 in the inspected seed; current seed has one broad senior fallback row and one development local senior row;
- no policy snapshot JSON column;
- no ordinance source metadata beyond reference strings.

### discounts.statutory_discount_validations

Existing policy-related columns:

- `evaluated_policy_reference_id`
- `applied_policy_reference_id`
- `fallback_policy_reference_id`
- `policy_resolution_basis`
- `local_ordinance_applied`
- `national_law_fallback_applied`

Gaps:

- no policy snapshot JSON;
- current draft writer does not populate evaluated/applied/fallback policy references;
- no explicit link to jurisdiction;
- no structured fields for resolved benefit behavior.

### sites.sites and sites.site_groups

`sites.sites` already has:

- `city`
- `province`
- `country_code`
- `lgu_code`
- `site_group_id`
- `site_id`

This is sufficient for an MVP resolver if `lgu_code` is complete and governed. It is not sufficient as a long-term jurisdiction registry because it lacks PSGC normalization, jurisdiction type, official names, and verification status.

### config.controlled_code_sets

`config.controlled_code_sets` can hold controlled values such as verification statuses, benefit types, free-period rules, and succeeding-hours rules if the project chooses controlled-code rows over PostgreSQL enums.

## Recommended Data Model

Do not implement these objects in this slice.

### Jurisdiction Registry

Recommended table: `config.jurisdictions` or `sites.jurisdictions`.

Fields:

- `jurisdiction_id`
- `country_code`
- `province_name`
- `city_municipality_name`
- `barangay_name`
- `psgc_code`
- `lgu_code`
- `jurisdiction_type`
- `jurisdiction_status`
- effective window and attribution fields

### Site To Jurisdiction Mapping

Recommended table: `sites.site_jurisdiction_mappings`.

Fields:

- `site_jurisdiction_mapping_id`
- `site_id`
- `site_group_id`
- `jurisdiction_id`
- `mapping_status`
- `effective_from`
- `effective_to`
- attribution fields

`sites.sites.lgu_code`, `city`, and `province` may be retained as denormalized display fields, but policy resolution should use the governed mapping.

### Statutory Discount Policy Registry

Recommended table: `discounts.statutory_discount_policy_registry`, or extend `discounts.discount_policy_references` only if the project accepts a wider policy table.

Fields:

- `statutory_discount_policy_id`
- `jurisdiction_id` nullable for national fallback
- `policy_code`
- `policy_name`
- `entitlement_type`
- `policy_resolution_basis`
- `policy_level`
- `policy_type`
- `ordinance_reference`
- `legal_basis_reference`
- `national_law_reference`
- `verification_status`
- `beneficiary_residency_scope`
- `benefit_type`
- `free_duration_minutes`
- `initial_rate_exempt_flag`
- `full_fee_exempt_flag`
- `overnight_excluded_flag`
- `valet_excluded_flag`
- `standalone_parking_excluded_flag`
- `driver_or_passenger_required_flag`
- `free_period_application`
- `succeeding_hours_discount_rule`
- `discount_base_scope`
- `stacking_policy`
- `legal_basis_priority`
- `requires_operator_validation`
- `requires_evidence`
- `effective_from`
- `effective_to`
- `status`
- `source_reference`
- `reviewed_by_user_id`
- `reviewed_at`
- `policy_snapshot_json`

National fallback seed rows must be separate by entitlement:

- `PH_RA9994_SENIOR_CITIZEN_NATIONAL_FALLBACK`
- `PH_RA10754_PWD_NATIONAL_FALLBACK`

## Policy Resolution Algorithm

Inputs:

- `siteId`
- `siteGroupId`
- `entitlementType`
- `parkingSessionId`
- request timestamp

Algorithm:

1. Load the parking session and verify it belongs to the requested site/site group.
2. Load the site and governed jurisdiction mapping.
3. If no jurisdiction mapping or trusted `lgu_code` exists, return `SITE_JURISDICTION_NOT_CONFIGURED`.
4. Check active verified site-specific policy override for site, entitlement, and timestamp.
5. Check active verified local city/municipality policy by jurisdiction and entitlement.
6. Check active verified province-level policy by jurisdiction hierarchy and entitlement, if supported.
7. If a verified local policy exists, resolve:
   - `policy_resolution_basis = LOCAL_ORDINANCE_APPLIED`
   - set evaluated/applied policy reference
   - set fallback policy reference to the applicable national fallback where useful for audit
8. If no verified local policy exists, resolve national fallback:
   - `SENIOR_CITIZEN -> RA 9994`
   - `PWD -> RA 10754`
   - `policy_resolution_basis = NATIONAL_LAW_FALLBACK`
9. If entitlement type is unsupported, return `STATUTORY_DISCOUNT_ENTITLEMENT_NOT_SUPPORTED`.
10. Return resolved policy and immutable policy snapshot.
11. Persist resolved policy reference and basis into `discounts.statutory_discount_validations` during draft creation.

Unverified local rows:

- `LEAD_UNVERIFIED` and `PROPOSED` rows do not block national fallback unless compliance explicitly configures manual review behavior.
- If a local row is known but not production-approved, the resolver should return `LOCAL_POLICY_REQUIRES_MANUAL_REVIEW` only when compliance config says unresolved local ambiguity must be reviewed. Otherwise national fallback applies.

## Contract Recommendation

Recommended future read-only endpoint:

`POST /v1/ops/operator-console/statutory-discounts/resolve-policy`

Request:

- `userId`
- `operatorDeviceBindingId`
- `siteId`
- `siteGroupId`
- `operatorShiftId`
- `parkingSessionId`
- `entitlementType`
- `correlationId`

Response:

- `accessEvaluationId`
- `accessAllowed`
- `policyResolved`
- `policyResolutionBasis`
- `statutoryDiscountPolicyId`
- `jurisdictionId`
- `entitlementType`
- `policyLevel`
- `policyType`
- `legalBasisReference`
- `ordinanceReference`
- `nationalLawReference`
- `verificationStatus`
- `benefitType`
- `freeDurationMinutes`
- `succeedingHoursDiscountRule`
- `discountBaseScope`
- `stackingPolicy`
- `requiresOperatorValidation`
- `requiresEvidence`
- `policySnapshot`
- `ineligibilityReason`
- `errorCode`
- `correlationId`

Draft integration:

- draft creation must call backend policy resolution or require a server-issued resolved policy token/reference;
- draft must persist `policy_resolution_basis`;
- draft must persist evaluated/applied/fallback policy references where schema supports it;
- draft must persist a policy snapshot after a future schema patch adds one;
- draft must not accept a frontend-chosen policy as authoritative.

## Access Gating

The resolver should use the existing Operator Console access evaluator:

- `workflowCode = STATUTORY_DISCOUNT_VALIDATION`
- `controlledActionCode = START_WORKFLOW`

`START_WORKFLOW` is currently supported and best matches policy resolution before draft creation. Do not silently invent `RESOLVE_POLICY`. If product wants a distinct `RESOLVE_POLICY` action, add it in a prerequisite evaluator/action-code slice.

If access is denied:

- persist access evaluation;
- do not perform policy lookup beyond minimal request validation;
- return `ACCESS_DENIED`.

## Interaction With Payable-Basis Computation

Payable-basis application must use the resolved policy snapshot from the validation, not recompute legal policy resolution later.

Rules:

- local free period applies before discount computation;
- discount does not stack on a zero-price free period;
- succeeding-hours rule controls the remaining chargeable portion;
- national fallback applies only when no verified local policy exists;
- national fallback must not create local free parking;
- national fallback can apply to the chargeable portion only when the resolved policy snapshot says the remaining fee is subject to national statutory treatment;
- `discounts.statutory_discount_payable_basis_applications.computation_basis_json` should include policy ID, policy version, resolution basis, free-period rule, succeeding-hours rule, discount base scope, and rounding mode.

## Failure Behavior

Recommended deterministic errors:

- `ACCESS_DENIED`
- `SITE_JURISDICTION_NOT_CONFIGURED`
- `STATUTORY_DISCOUNT_POLICY_NOT_RESOLVED`
- `STATUTORY_DISCOUNT_POLICY_UNVERIFIED`
- `STATUTORY_DISCOUNT_ENTITLEMENT_NOT_SUPPORTED`
- `LOCAL_POLICY_REQUIRES_MANUAL_REVIEW`
- `NATIONAL_FALLBACK_POLICY_NOT_CONFIGURED`

Suggested response handling:

- access denied may remain a `200` access envelope if consistent with existing Operator Console endpoints;
- unsupported entitlement and missing request fields should be `400`;
- site/jurisdiction missing may be `422` or deterministic `200` ineligibility response depending on project convention;
- unexpected failures remain `500`.

## Required Future DB Changes

Future DB patch items:

- jurisdiction registry;
- site-to-jurisdiction mapping;
- statutory discount policy registry or substantial extension of `discounts.discount_policy_references`;
- national fallback seed rows for RA 9994 and RA 10754;
- verified local ordinance policy rows from reviewed LGU documents;
- policy verification status controlled code or enum;
- benefit type, residency scope, free-period, succeeding-hours, discount base, stacking, and legal-priority controlled codes;
- effective-date uniqueness constraints;
- one active policy per jurisdiction, entitlement, policy scope, and version/effective window;
- policy snapshot storage or reference from `discounts.statutory_discount_validations`;
- compatibility migration from current broad development fallback/local seed rows to entitlement-specific production rows.

## Required Future Tests

Unit tests:

- verified local policy resolves before national fallback;
- unverified local lead does not auto-apply;
- no local policy resolves RA 9994 for Senior Citizen;
- no local policy resolves RA 10754 for PWD;
- national fallback does not grant free parking;
- free-period policy returns `BEFORE_DISCOUNT_COMPUTATION` and the configured succeeding-hours rule;
- site without jurisdiction fails closed;
- unsupported entitlement fails closed;
- draft persists resolved policy basis and references.

Integration tests:

- policy resolver reads site jurisdiction mapping;
- draft creation persists evaluated/applied/fallback policy reference IDs;
- apply-payable-basis uses policy snapshot fields;
- national fallback rows are required and deterministic;
- no payment, provider, gate, coupon, reconciliation, settlement, UI, or AUB writes occur.

## Manual Coverage Plan

Future Bruno/manual tests:

- resolve Quezon City local policy after it is verified and seeded;
- resolve Manila local policy after it is verified and seeded;
- resolve Muntinlupa local policy after it is verified and seeded;
- no local policy Senior Citizen resolves RA 9994 fallback;
- no local policy PWD resolves RA 10754 fallback;
- unverified local lead returns manual review or national fallback according to compliance configuration;
- free-period with regular succeeding hours;
- free-period with configured statutory succeeding-hours discount.

Until official ordinance text or LGU publication is reviewed for a specific policy row, manual local-policy cases should be marked non-production or pending verification.

## Recommended Implementation Sequence

1. `#192` Add DB support for jurisdiction and statutory discount policy registry.
2. `#193` Seed national fallback policies RA 9994 and RA 10754.
3. `#194` Seed verified local ordinance policy rows from reviewed LGU documents.
4. `#195` Implement policy resolution read-only endpoint.
5. `#196` Integrate resolved policy into statutory discount draft.
6. `#197` Update payable-basis application to use resolved policy snapshot.
7. `#198` Add Bruno/manual coverage for jurisdiction policy resolution.

## Open Decisions

- Final jurisdiction source of truth: PSGC-backed registry, internal registry, or governed `sites.sites.lgu_code`.
- Which local ordinances are `VERIFIED_OFFICIAL` for production.
- Whether `VERIFIED_SECONDARY` can auto-resolve after site/compliance approval or must always require manual review.
- Residency validation model and evidence requirements.
- Whether site-specific policy overrides may narrow local ordinance benefits or only configure operational implementation.
- How to handle mixed or ambiguous ordinances such as rules with mall, tenant, resident, passenger, or driver conditions.
- Whether succeeding hours should ever default to national fallback when the local ordinance grants only a free initial period.
- Whether current `discounts.discount_policy_references` should be extended or superseded by a dedicated `statutory_discount_policy_registry`.
- Who reviews and approves local policy rows and how review evidence is retained.

## Boundary

This design does not implement runtime behavior, endpoints, tests, database patches, baseline DDL, Bruno files, Operator Console UI, WebPay UI, Payment Orchestrator, Gate Integration Service, vendor PMS adapter behavior, coupon logic, reconciliation logic, Docker files, CI/CD files, or AUB routing/configuration/invocation.
