# ExitPass Central PMS Statutory Discount Local Ordinance Eligibility Gate Implementation Note v1.0

## Purpose

This note records the first bounded Central PMS local-ordinance eligibility slice for Senior Citizen and PWD statutory parking benefits.

The slice establishes a fail-closed backend authority gate before a service-channel statutory-discount request can enter the canonical decision-v2 workflow. It does not implement WebPay or APT consumers, secure ID-image capture, local-ordinance benefit-effect calculations, POS Server fiscal changes, or controlled UAT.

## Governing Requirement

Senior Citizen and PWD parking benefits may be offered only when an active, applicable city or municipal parking ordinance or controlled local policy covers the resolved parking site, transaction time, parking service, entitlement type, and required beneficiary facts.

When no applicable policy is available, Central PMS rejects the statutory benefit path without creating a decision, review item, validation, application command, payable-basis mutation, or fiscal discount record. Ordinary payment remains available.

## Ordinance Inventory

The current working inventory inspected for model and fixture shape was:

- `D:\Docs\Philippine_Parking_Statutory_Discount_Local_Ordinances_Detailed_List.docx`

The updated path was checked and was not present:

- `D:\Docs\Philippine_Parking_Statutory_Discount_Local_Ordinances_Detailed_List_updated.docx`

The inventory is not embedded in runtime code. Runtime resolution depends on controlled canonical policy records from the canonical PostgreSQL model.

## Parañaque Correction

The Parañaque working authority is represented as verified operational policy authority, not as a proposed or unverified lead:

- Senior Citizen free parking: verified active operational
- PWD free parking: verified active operational
- Coverage: Parañaque residents
- Senior Citizen ordinance text and ordinance number: unavailable online

Unavailable online ordinance text is treated as a source-document availability gap, not an eligibility-existence gap. Unknown ordinance details remain null or unresolved. They are not converted to false, zero, unlimited, or not applicable.

## Canonical Database Authority

The current executable canonical baseline is:

- `D:\SourceCodes\exitpassdb_v1.2\build\generated\exitpass-full-object.generated.sql`

The retired `D:\SourceCodes\ExitPass_DBv1.2` repository and historical standalone DDL are not authority for this implementation.

The runtime uses the promoted canonical objects:

- `sites.jurisdictions`
- `sites.site_jurisdiction_assignments`
- `discounts.statutory_discount_policy_versions`
- `discounts.statutory_discount_policy_version_evidence_requirements`
- `discounts.statutory_discount_decision_policy_authorities`
- `operator_console.statutory_discount_service_channel_reviews`

No application-local SQL patch or canonical database change was added in this slice.

## Site and Jurisdiction Resolution

The resolver uses `core.parking_sessions` as the parking-session authority. The transaction-time basis for policy effectivity is `COALESCE(entry_at, created_at)` from the parking session.

Resolution sequence:

1. Resolve parking session.
2. Read authoritative `site_id` and `site_group_id`.
3. Resolve exactly one effective active `sites.site_jurisdiction_assignments` row for the transaction instant.
4. Join to `sites.jurisdictions` for canonical city or municipality facts.
5. Fail closed when the session is missing, jurisdiction is not configured, or multiple active assignments exist.

Caller-supplied Site, Site Group, jurisdiction, policy, ordinance, amount, benefit, and source-channel authority are not trusted by the resolver.

## Policy Resolution

The resolver reads scoped canonical policy candidates for the jurisdiction and parking site context, then applies deterministic precedence:

1. Site-scoped policy outranks Site Group.
2. Site Group outranks jurisdiction-wide.
3. Lower `precedence_rank` wins within the same scope.
4. Later `transaction_use_effective_from` wins when precedence is equal.
5. Remaining ties are conflicts and fail closed.

The resolver requires the selected policy to be:

- verified for transaction use
- active for transaction use
- effective at the authoritative transaction instant
- not expired
- not suspended
- not withdrawn
- not retired
- not superseded without an active successor
- applicable to parking services
- covering the requested entitlement
- supported by the current payable-basis effect engine
- free of unresolved policy conflict

Verification status remains separate from publication status. `VERIFIED_OFFICIAL` and `VERIFIED_ACTIVE_OPERATIONAL` may become available only when the policy is also published for transaction use.

## Availability Contract

The additive shared route is:

- `POST /v1/statutory-discounts/decisions/availability`

The request accepts:

- `requestReference`
- `parkingSessionId`
- `requestedEntitlementType`
- `beneficiaryResidencySatisfied`

The response returns channel-safe facts including:

- parking session, Site, Site Group, jurisdiction ID, jurisdiction code, and jurisdiction display name
- availability status and covered entitlement types
- immutable policy version ID, policy code, and policy version
- safe ordinance number and title when available
- verification and publication status
- effective window
- residency requirement
- required evidence types
- parking-service applicability
- benefit-effect classification and support status
- source-document availability
- retryability
- safe reason code
- remediation action
- correlation ID

The response does not expose raw ordinance documents, raw statutory IDs, evidence images, reviewer-sensitive data, database internals, connection details, or stack traces.

## Decision Creation Enforcement

`POST /v1/statutory-discounts/decisions` now resolves local-ordinance availability before creating a canonical decision for service-channel intake and Operator Console one-shot submission.

Only `AVAILABLE` requests proceed. Unavailable requests return a safe fail-closed rejection and create no decision-v2, review row, validation, application-v1 command, or payable-basis mutation.

The decision command carries the resolved immutable policy version reference and local-ordinance resolution basis before staging, so the semantic hash includes the governing authority.

## Durable Policy Linkage

Available requests bind the frozen authority in `discounts.statutory_discount_decision_policy_authorities`. The bound authority includes the immutable policy version, jurisdiction, policy code and version, verification and publication posture, parking applicability, benefit type, residency scope, source availability, resolution timestamp, and policy semantic hash.

Service-channel review rows are also updated with the same policy authority reference columns when the intake link exists.

Replay uses the frozen policy authority and does not re-resolve to a newer policy version.

## Operator Console Readback

The service-channel review detail read model now exposes a safe governing-policy block for authorized Operator Console reviewers. It includes jurisdiction, policy code, version, ordinance references when available, verification and publication posture, evidence requirements, parking applicability, benefit type, residency scope, source-document availability, and legal/source references.

Reviewers cannot choose another jurisdiction or ordinance through this backend contract. Approval now requires the existing decision to have frozen policy authority.

## Application-v1 Guard

Service-channel payable-basis application intent now requires the approved decision to have frozen local-ordinance policy authority before application-v1 can be created.

The application path consumes the existing approved decision and does not independently resolve a newer policy. Unsupported local benefit effects remain blocked before decision creation; this slice does not map free parking to the existing 20% statutory-discount calculation.

## Ordinary Payment Fallback

Policy unavailability is not a payment failure. The resolver returns a safe remediation action equivalent to continuing with ordinary payment when the statutory benefit is legally unavailable or unsupported.

No ordinary payable basis is mutated when the policy gate fails.

## Evidence Sequencing

Applicable ordinance eligibility must be resolved before sensitive evidence collection. A no-policy response means no ID fields, image capture, evidence reference, statutory decision, or approvable review item should be created by channel consumers.

This slice enforces backend decision rejection for manipulated submissions but does not implement secure ID-image capture.

## Security and Privacy

The new contract and readback avoid raw ID values, raw evidence, Base64 evidence, images, reviewer-sensitive notes, reviewer identity in channel responses, Operator Console device or shift identity, service credentials, authorization headers, database internals, raw policy documents, unpublished legal notes, and stack traces.

## Tests

Automated proof added or updated:

- availability resolver/facade unit proof
- fail-closed decision creation before staged command creation
- application-v1 guard for missing frozen policy authority
- Operator Console approval policy-authority dependency
- shared availability API contract and RBAC metadata proof
- additive DTO privacy-shape coverage
- stale integration-test constructor updates for residency-aware semantic hashing

## Controlled UAT and Production

Controlled statutory-discount UAT remains blocked until WebPay and APT consume the availability contract, request hiding is implemented, no-evidence-before-eligibility is proven end to end, local benefit effects are supported or explicitly excluded, and manual channel scenarios pass.

Production rollout remains blocked pending controlled UAT, approved policy data assignment, benefit-effect support, fiscal proof, privacy review, and deployment evidence.

## Exact Next Bounded Task

Implement WebPay and APT statutory ordinance availability consumers, starting with WebPay as the reference channel, so covered entitlement visibility and evidence collection are gated by Central PMS availability.

## Sequencing Decision

READY_FOR_WEBPAY_APT_ORDINANCE_AVAILABILITY_CONSUMER_IMPLEMENTATION
