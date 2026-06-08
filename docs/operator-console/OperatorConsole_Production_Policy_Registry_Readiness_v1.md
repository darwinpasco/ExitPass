# Operator Console Production Policy Registry Readiness v1

## Title and Purpose

This is the production statutory discount policy registry readiness package for Operator Console statutory discount validation.

The controlled sandbox validation has passed, including the deterministic Operator Console statutory discount fixture, but production policy readiness must be separately verified. The sandbox policy row `SANDBOX_OC_SD_REQUIRED_EVIDENCE_POLICY_235A` is validation support only and must not be treated as production policy authority.

## Scope

In scope:

- statutory discount policy registry readiness
- local ordinance policy configuration readiness
- national fallback policy readiness
- entitlement-specific policy rows
- evidence requirements
- site, site group, and jurisdiction mapping readiness
- verification status and readiness status
- production go/no-go criteria
- read-only verification SQL

Out of scope:

- policy import automation
- legal opinion drafting
- WebPay
- payment provider routing
- AUB
- coupon validation
- reconciliation
- HikCentral or gate implementation
- raw evidence, OCR, or automated ID validation
- production seed mutations

## Source Artifacts Inspected

Found in repo:

- `docs/operator-console/OperatorConsole_Statutory_Discount_Pilot_Readiness_Signoff_v1.md`
- `docs/operator-console/OperatorConsole_Production_Readiness_Gap_Review_v1.md`
- `docs/operator-console/OperatorConsole_Access_Readiness_API_Backend_Design_v1.md`
- `docs/operator-console/OperatorConsole_Statutory_Discount_Validation_Runbook_v1.md`
- `docs/operator-console/OperatorConsole_Statutory_Discount_Pilot_Triage_Guide.md`
- `docs/operator-console/Philippine_Parking_Statutory_Discount_Local_Ordinances_Detailed_List.docx`
- `docs/operator-console/statutory-discount-jurisdiction-policy-resolution-design.md`
- `infra/db/patches/ExitPass_StatutoryDiscountPolicyRegistrySchema_v1.2.sql`
- `infra/db/patches/validation/Validate_StatutoryDiscountPolicyRegistrySchema_v1.2.sql`
- `src/Services/CentralPms/src/**` statutory discount policy resolution services, repositories, endpoints, and DTOs
- `src/Services/CentralPms/tests/**` statutory discount policy resolution tests

Standalone Operator Console BRD, ExitPass BRD, API Contract Pack, and Engineering Pack files were not found by repo filename search. This package uses repo-available implementation artifacts and docs only.

## Current State Summary

Sandbox validation used:

- Policy ID: `23100000-0000-0000-0000-000000000002`
- Policy Code: `SANDBOX_OC_SD_REQUIRED_EVIDENCE_POLICY_235A`
- Policy Basis: `SITE_POLICY_OPERATIONAL_ONLY`
- Entitlement Type: `SENIOR_CITIZEN`
- Required Evidence Type: `SENIOR_CITIZEN_ID`

That result proves the Operator Console backend/API flow can resolve and apply a configured policy in a controlled sandbox. It does not prove production policy readiness.

Production must use verified policy rows. Rows must be entitlement-specific and jurisdiction/site-aware. Policy resolution must not silently apply generic local parking benefits nationwide, and local benefits must not be inferred from public reports, social posts, or ordinance indexes alone.

## Policy Registry Source Of Truth

Live local schema inspection found the current baseline table:

- `discounts.discount_policy_references`

The dedicated future registry table is present in repo patch form but is not present in the live local baseline:

- `discounts.statutory_discount_policy_registry`
- patch file: `infra/db/patches/ExitPass_StatutoryDiscountPolicyRegistrySchema_v1.2.sql`

### Live Table: `discounts.discount_policy_references`

Actual live columns found:

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
- attribution fields and `row_version`

Actual live enum values include:

- `discount_policy_status_enum`: `DRAFT`, `ACTIVE`, `SUSPENDED`, `SUPERSEDED`, `RETIRED`
- `discount_policy_level_enum`: `NATIONAL_LAW`, `LOCAL_ORDINANCE`, `SITE_POLICY`, `OPERATIONAL_POLICY`
- `discount_policy_type_enum`: `LEGAL_REFERENCE`, `LOCAL_ORDINANCE`, `SITE_POLICY`, `OPERATIONAL_POLICY`, `IMPLEMENTATION_POLICY`
- `policy_resolution_basis_enum`: `LOCAL_ORDINANCE_APPLIED`, `NATIONAL_LAW_FALLBACK`, `SITE_POLICY_OPERATIONAL_ONLY`, `MANUAL_POLICY_SELECTION`, `SYSTEM_DEFAULT`
- `statutory_entitlement_type_enum`: `SENIOR_CITIZEN`, `PWD`, `OTHER_STATUTORY`
- `discount_evidence_type_enum`: `SENIOR_CITIZEN_ID`, `PWD_ID`, `AUTHORIZATION_LETTER`, `SUPPORTING_DOCUMENT`, `VALIDATION_SCREENSHOT`, `HASH_ONLY_REFERENCE`, `OTHER`

Actual live constraints and indexes include:

- primary key on `discount_policy_reference_id`
- unique `policy_code, policy_version`
- foreign keys to `sites.site_groups`, `sites.sites`, `identity.users`, and `identity.service_identities`
- self-references for parent and fallback policy references
- unique active local policy index on entitlement, `lgu_code`, site group, site, policy level, and version where `policy_status = ACTIVE` and `lgu_code IS NOT NULL`

The live table does not have:

- `jurisdiction_id`
- separate `verification_status`
- structured benefit type
- free duration fields
- residency scope
- explicit exclusion flags
- reviewer fields
- policy snapshot JSON

### Live Policy Row State

Read-only local inspection found:

| Metric | Count |
| --- | ---: |
| Total policy rows | 5 |
| Active rows | 5 |
| Sandbox rows | 1 |
| Senior Citizen rows | 3 |
| PWD rows | 2 |
| Rows requiring evidence capture | 5 |
| Local ordinance rows | 3 |
| National reference rows | 2 |
| Rows without site, site group, or LGU scope | 2 |

The current live rows are development/sandbox-oriented:

- `PH_NATIONAL_SENIOR_DEV`
- `PH_NATIONAL_PWD_DEV`
- `MNT_LOCAL_SENIOR_DEV`
- `MNT_LOCAL_PWD_DEV`
- `SANDBOX_OC_SD_REQUIRED_EVIDENCE_POLICY_235A`

The current national rows use `DEV_PLACEHOLDER_*` legal references, not production-ready RA references. The local rows use `DEV_PLACEHOLDER_*` ordinance references. This is not production-ready policy data.

### Repo Patch: `discounts.statutory_discount_policy_registry`

The repo patch defines the intended governed registry with:

- `sites.jurisdictions`
- `sites.sites.jurisdiction_id`
- `discounts.statutory_discount_policy_registry`
- verification enum `discounts.policy_verification_status_enum`
- benefit and scope enums
- national fallback constraints for RA 9994 and RA 10754
- local ordinance constraints requiring jurisdiction and ordinance reference
- unverified rows prevented from active status
- future links from `discounts.statutory_discount_validations` to resolved policy and jurisdiction

That patch is not applied to the inspected live local baseline.

## Required Production Policy Fields

Production policy rows must have these fields or equivalent governed data:

- policy ID
- policy code
- policy name
- entitlement type
- policy status
- verification status
- policy level
- policy type
- policy resolution basis
- site ID, site group ID, or jurisdiction ID, as applicable
- ordinance reference, legal basis reference, or national law reference
- beneficiary residency scope, if supported
- benefit type
- free duration minutes, if applicable
- initial-rate exempt flag, if applicable
- full-fee exempt flag, if applicable
- VAT-exempt or VAT-exclusive discount basis
- overnight excluded flag
- valet excluded flag
- standalone parking excluded flag
- driver/passenger required flag
- requires evidence
- required evidence type
- requires operator validation
- effective from
- effective to
- source reference
- reviewed by and reviewed at, if supported
- correlation ID or audit metadata, if supported

In the current live table, several of these are not structurally supported. Until the governed registry is applied, production readiness requires compensating manual review and documented operational acceptance.

## Entitlement-Specific Policy Rule

Senior Citizen and PWD must be configured as separate policy rows where benefit scope differs.

A city that covers Senior Citizens does not automatically cover PWDs. A city that covers PWDs does not automatically cover Senior Citizens. Residency scope must be explicit when required by ordinance. Facility scope must be explicit when an ordinance applies only to specific facilities.

## Local Ordinance Readiness Rule

Do not auto-apply local parking statutory benefits unless the official ordinance copy has been reviewed and encoded.

Public reports, social posts, and ordinance indexes are leads, not production-ready sources. Unverified policies must route to manual review or site-approved operational fallback, not automatic production application.

## National Fallback Policy Rule

RA 9994 and RA 10754 are national fallback references for Senior Citizens and PWDs.

The fallback applies only when no verified local parking-specific policy is configured. The fallback must not override a verified local ordinance configuration. Local policy resolution must remain traceable through policy code, policy basis, legal basis, entitlement type, and site/jurisdiction scope.

## Production Readiness Classifications

| Classification | Meaning |
| --- | --- |
| `READY_VERIFIED` | Active, scoped, entitlement-specific, source-reviewed, evidence-configured policy row is ready for production auto-resolution. |
| `READY_WITH_MANUAL_REVIEW` | Row may support controlled pilot or manual review, but lacks full production verification metadata or governed registry support. |
| `CONFIGURED_BUT_UNVERIFIED` | Row exists but source verification is not production-grade. |
| `MISSING_REQUIRED_POLICY` | Expected entitlement or fallback policy is absent. |
| `MISSING_SITE_MAPPING` | Policy cannot be resolved to a site, site group, LGU, or jurisdiction. |
| `MISSING_EVIDENCE_RULE` | Evidence requirement is absent or incomplete. |
| `EXPIRED_OR_INACTIVE` | Row is inactive, retired, suspended, superseded, or outside its effective window. |
| `SANDBOX_ONLY` | Row is explicitly test/sandbox/dev and must not be production policy authority. |
| `NOT_READY` | Any unresolved blocker remains. |

## Readiness Assessment Matrix

| Readiness Area | Required condition | How to verify | Failure mode | Production blocker? |
| --- | --- | --- | --- | --- |
| Policy table exists | A governed policy table is present. | Inspect `information_schema.tables`. | No policy source exists. | Yes |
| Active policy rows exist | Active rows exist for expected entitlements. | Count active rows by entitlement. | Missing Senior Citizen or PWD policy. | Yes |
| Sandbox rows excluded | Test rows are not used in production. | Search policy code/name/description for sandbox/dev/test markers. | Sandbox policy resolves in production. | Yes |
| Senior Citizen rows | Senior-specific rows exist where expected. | Filter `entitlement_type = SENIOR_CITIZEN`. | Senior benefit incorrectly inferred. | Yes |
| PWD rows | PWD-specific rows exist where expected. | Filter `entitlement_type = PWD`. | PWD benefit incorrectly inferred. | Yes |
| Site/site group/jurisdiction mapping | Policy resolves to explicit scope. | Check `site_id`, `site_group_id`, `lgu_code`, or future `jurisdiction_id`. | Generic nationwide local benefit. | Yes |
| Evidence rule | Evidence capture or required evidence type is encoded. | Check `requires_evidence_capture` or future `requires_evidence`. | Approval can proceed without required evidence. | Yes |
| Operator validation requirement | Operator validation is required for Operator Console flow. | Check `requires_operator_validation`. | Policy bypasses controlled operator flow. | Yes |
| Effective dates | Row is currently effective. | Check effective window. | Expired or premature row resolves. | Yes |
| Legal references | Official ordinance or national law reference is present. | Check local/national/legal references and source references. | Untraceable legal basis. | Yes |
| Verification status | Official or approved verification status exists. | Future registry: check `verification_status`; current table: manual evidence required. | `ACTIVE` is mistaken for legal review. | Yes |
| Local vs national fallback traceability | Policy basis identifies local or national fallback path. | Inspect resolved policy response and audit report. | Fallback silently overrides local rule. | Yes |
| Policy resolution tests | Senior/PWD fallback, local verified, unverified local, missing jurisdiction, and boundary checks exist. | Inspect policy-resolution tests. | Resolver behavior not controlled. | Conditional |
| Audit/report visibility | Safe policy references visible in audit/reporting. | Use Operator Console audit/reporting. | Supervisors cannot review basis. | Conditional |

## LGU And Local Ordinance Readiness Summary

The detailed local ordinance DOCX exists in the repo. It is an operational research document, not a legal opinion. It explicitly says ordinance text must be obtained and reviewed before production configuration, and public reports/social posts/indexes are leads unless official ordinance text or LGU publication has been reviewed.

Operational position from the document:

- Strict production candidates after ordinance copy review: Quezon City, Manila, Mandaluyong, Las Pinas, Muntinlupa, Paranaque, Santa Rosa, and Cebu City.
- Verification-needed before auto-application: Marikina, Antipolo, Taytay, Marilao, Malolos, Mandaue, and Tagum.
- Senior-only based on available sources: Mandaluyong and Marikina.
- PWD-only based on available sources: Paranaque.
- PWD proposed only: Mandaue City.
- Resident-only or likely resident-only caveats exist for Quezon City, Las Pinas, Paranaque, and Marikina.
- Mixed or conflicting ordinance basis exists for Mandaluyong and Cebu City.
- Unverified coverage exists for Antipolo, Taytay, and Malolos.

No local ordinance from that DOCX should become production auto-resolution data until the official ordinance copy is reviewed and the entitlement/scope fields are encoded.

## Read-Only SQL Verification

The companion script is:

- `scripts/operator-console/Verify-ProductionPolicyRegistryReadiness.sql`

It is read-only and uses the live baseline table `discounts.discount_policy_references`. It reports:

- policy registry table availability
- row-level readiness classification
- entitlement coverage
- sandbox/dev policy detection
- missing evidence requirement detection
- inactive/expired policy detection
- missing legal/ordinance reference detection
- missing site/site group/LGU scope detection

Because the live local baseline does not contain `discounts.statutory_discount_policy_registry`, the script does not query that missing table directly. It reports its absence through `to_regclass`.

## Backend Behavior Expectations

Policy resolution must return traceable policy basis. Unverified local policy must not be auto-applied as production-ready. Missing required policy must fail closed or route to manual review based on configured operating mode.

Sandbox policy rows must be rejected or ignored in production. Policy resolution must not mutate payment, provider, gate, coupon, or reconciliation records.

## UI And Reporting Implications

Operator Console should eventually show:

- policy code
- policy basis
- verification status
- evidence requirement
- local ordinance or national fallback reference
- manual review reason when policy readiness is not satisfied

Audit/reporting should expose safe policy references but not raw evidence. Operators should see when manual review is required because policy is unverified, missing, expired, unscoped, or sandbox-only.

## Gap List

1. `OC-POLICY-GAP-001` - Dedicated governed policy registry absent from live local baseline.
   - Risk: current production readiness relies on a compatibility table without structured verification/benefit fields.
   - Owner: Backend/Architecture.
   - Next slice: `#249 Operator Console production policy readiness SQL verification integration`.
   - Blocker: Yes.

2. `OC-POLICY-GAP-002` - Current live rows are dev/sandbox placeholders.
   - Risk: placeholder rows could be mistaken for production policy authority.
   - Owner: Backend/QA/Operations.
   - Next slice: `#250 Operator Console policy resolution fail-closed/manual-review behavior`.
   - Blocker: Yes.

3. `OC-POLICY-GAP-003` - No live verification status column.
   - Risk: `ACTIVE` can be confused with official legal/source verification.
   - Owner: Backend/Compliance.
   - Next slice: `#251 Operator Console production policy registry admin/import design`.
   - Blocker: Yes.

4. `OC-POLICY-GAP-004` - National fallback rows in live baseline use dev placeholder references.
   - Risk: RA 9994/RA 10754 fallback cannot be production-signed from current rows.
   - Owner: Compliance/Backend.
   - Next slice: `#253 Operator Console production statutory discount policy test matrix`.
   - Blocker: Yes.

5. `OC-POLICY-GAP-005` - Local ordinance rows lack production-reviewed ordinance source metadata.
   - Risk: local free parking or facility-specific benefits may be applied incorrectly.
   - Owner: Compliance/Operations.
   - Next slice: `#251 Operator Console production policy registry admin/import design`.
   - Blocker: Yes.

6. `OC-POLICY-GAP-006` - Structured benefit fields are absent from live compatibility table.
   - Risk: free duration, residency, exclusion, and succeeding-hour behavior cannot be encoded safely.
   - Owner: Backend/Architecture.
   - Next slice: `#251 Operator Console production policy registry admin/import design`.
   - Blocker: Yes.

7. `OC-POLICY-GAP-007` - Site/jurisdiction mapping is still LGU-code based in current resolver.
   - Risk: policy resolution depends on denormalized `sites.sites.lgu_code`.
   - Owner: Backend/Operations.
   - Next slice: `#249 Operator Console production policy readiness SQL verification integration`.
   - Blocker: Conditional.

8. `OC-POLICY-GAP-008` - UI/reporting does not yet surface policy readiness classification.
   - Risk: operators and supervisors may not see why policy is manual-review only.
   - Owner: Frontend/QA.
   - Next slice: `#252 Operator Console policy readiness UX/reporting indicators`.
   - Blocker: Conditional.

## Recommended Next Slices

Recommended bounded slices:

- `#249 Operator Console production policy readiness SQL verification integration`
- `#250 Operator Console policy resolution fail-closed/manual-review behavior`
- `#251 Operator Console production policy registry admin/import design`
- `#252 Operator Console policy readiness UX/reporting indicators`
- `#253 Operator Console production statutory discount policy test matrix`

Recommended immediate next slice: `#249 Operator Console production policy readiness SQL verification integration`.

Reason: the live baseline currently contains only development/sandbox policy rows and lacks the governed registry table. Before behavior changes or admin/import design, the team needs repeatable read-only readiness verification that can run in local, staging, and production-like environments without mutating policy data.

## Go/No-Go Position

- GO for sandbox/pilot validation using deterministic policy fixtures.
- CONDITIONAL GO for controlled operational pilot only if site-approved policies are manually verified and documented.
- NO-GO for full production statutory discount auto-application until production policy registry rows are verified, encoded, scoped, and tested.

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
