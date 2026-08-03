# ExitPass Management Platform Canonical LGU Coverage Alignment v1.0

## Executive Decision

The Management Platform statutory policy coverage read model now resolves parking coverage through the canonical I-006 Site-to-LGU authority and statutory parking coverage views. Legacy `sites.sites.lgu_code` and compatibility policy-reference rows are no longer authoritative when the canonical coverage view exists.

This slice does not add policy-administration writes. Management Platform statutory coverage remains read-only.

## Canonical Objects Used

- `sites.sites.local_government_unit_id`
- `sites.jurisdictions`
- `sites.metropolitan_area_jurisdictions`
- `sites.metropolitan_areas`
- `sites.site_group_lgu_scopes`
- `discounts.statutory_parking_lgu_policy_coverage`
- `discounts.statutory_parking_site_policy_coverage`
- `discounts.statutory_discount_policy_registry`

## Site Resolution

For `scopeType=SITE`, Central PMS resolves the Site, its Site Group, and the Site's authoritative `local_government_unit_id`. Coverage candidates are read by Site ID from `discounts.statutory_parking_site_policy_coverage`, which derives policy coverage from the Site's canonical LGU.

The legacy `lgu_code` output is retained only as compatibility display data and is derived from the canonical jurisdiction code when available. It is not used to select statutory policies while the canonical view is present.

## Site Group Resolution

For `scopeType=SITE_GROUP`, Central PMS resolves each member Site and each Site's own canonical LGU. It does not collapse multiple LGUs into one arbitrary jurisdiction. Coverage remains per Site. A Site Group that spans multiple LGUs is classified as `MULTI_LGU`, and each Site receives only the policy candidates inherited from its own LGU.

Site Group remains an administrative query scope. It is not a legal ordinance authority.

## Coverage Classification

`ACTIVE_COVERED` requires:

- canonical Site LGU present;
- a policy candidate from the canonical Site coverage view;
- `policy_status = ACTIVE`;
- `coverage_available = true`;
- source verification classified as verified official, verified active operational, verified secondary, or active approved;
- an effective date window covering the evaluation date.

Rows classified as `NO_LOCAL_RULE_FOUND`, `PROPOSED`, `PROPOSED_ONLY`, `LEAD_UNVERIFIED`, unavailable, expired, future-effective, inactive, or malformed do not become active coverage.

## Paranaque Treatment

Paranaque Senior Citizen free-parking coverage remains verified operational coverage with unavailable online source text. The read model preserves:

- verified operational source posture;
- source-document unavailable flag;
- resident-only applicability where represented by the canonical policy record;
- no automatic application authority.

The coverage is not generalized to PWD or to non-Paranaque Sites unless separate canonical policy rows authorize that result.

## Compatibility Posture

Public DTOs retain `JurisdictionOrLocalityReference` for existing clients. New read-only metadata fields expose canonical jurisdiction reference, canonical jurisdiction code, canonical jurisdiction name, jurisdiction type, metropolitan-area references, scope-jurisdiction classification, benefit type, residency scope, source-document availability, and coverage-resolution status.

Compatibility fallback to `discounts.discount_policy_references` remains only for databases that do not yet expose the canonical I-006 coverage view. Current canonical databases use the canonical path first.

## Authorization And Privacy

The endpoint remains protected by `ManagementPlatformStatutoryDiscountPolicyCoverageRead` and permission `statutory-discount-policy.view`. Scope remains server-side: callers cannot make a browser-provided Site, Site Group, or LGU value authoritative.

The response does not expose reviewer identity, customer identifiers, statutory evidence references, evidence hashes, storage locations, payment internals, SQL details, credentials, or stack traces.

## Exclusions

This slice does not implement:

- statutory policy administration writes;
- role or permission administration;
- ordinance document storage;
- evidence upload or preview;
- WebPay or APT payable-basis application changes;
- Operator Console application authority;
- POS Server fiscal behavior;
- canonical database migrations.

## Validation Summary

Validation covers Management Platform coverage unit behavior, API contract mapping, DB-backed canonical repository reads, canonical Site/LGU resolution, Paranaque source-unavailable coverage posture, multi-LGU Site Group per-Site isolation, and legacy `lgu_code` non-authority while the canonical view exists.
