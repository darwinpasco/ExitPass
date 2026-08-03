# ExitPass Central PMS APT Statutory Ordinance Availability Read API Implementation Note v1.0

## Purpose

J-005 adds an APT-facing, read-only Central PMS API for statutory parking ordinance availability and immediate pre-cash revalidation. The Assisted Payment Terminal uses this API to decide whether to offer or continue a Senior Citizen or PWD statutory path for the authoritative Site.

## Routes

- `POST /v1/apt/statutory-discounts/ordinance-availability/resolve`
- `POST /v1/apt/statutory-discounts/ordinance-availability/revalidate`

Both routes use the `AptStatutoryOrdinanceAvailabilityRead` policy and the `statutory-discounts.ordinance-availability.read.apt` permission. Both require an APT service identity and the existing `X-Site-Id` Site scope header.

## Request Contract

The request carries `siteGroupId`, `siteId`, `terminalId`, optional `vendorSystemId`, `entitlementType`, and `correlationId`.

Exactly one lookup mode is accepted:

- `parkingSessionId`
- `ticketReference`
- `plateNumber`

Malformed scope, unsupported entitlement, missing lookup mode, or multiple lookup modes fail closed.

## Response Contract

The response returns APT-safe facts only:

- classification
- entitlement type
- ordinance coverage available
- statutory request allowed
- pre-cash revalidation passed
- ready for statutory cash flow
- ordinary payment preserved
- parking session, Site, and Site Group identifiers
- resolved scope type
- coverage and policy status classifications
- safe effective dates where available
- safe authority and jurisdiction references where already modeled
- support reference
- correlation ID and evaluation timestamp
- retryability and safe message

The response does not include ordinance documents, evidence references, reviewer notes, statutory identity data, SQL, infrastructure details, service credentials, or internal policy-engine state.

## Authority Reuse

The endpoint reuses Central PMS canonical authority:

- `IParkingSessionReadRepository.GetByIdAsync`
- `IParkingSessionReadRepository.FindByTicketReferenceAsync`
- `IParkingSessionReadRepository.FindByPlateNumberAsync`
- `IManagementPlatformStatutoryDiscountPolicyCoverageRepository`
- shared `StatutoryDiscountPolicyCoverageEvaluator`

The implementation does not introduce a second policy engine, jurisdiction resolver, HikCentral client, or browser-managed Site Group model.

## Availability Semantics

`AVAILABLE` means Central PMS found active applicable Site-level coverage for the requested entitlement. It allows the APT to show the statutory request option but does not approve entitlement evidence, apply a benefit, or change payable basis.

`NOT_AVAILABLE`, `NO_CONFIGURED_POLICY`, `NOT_YET_EFFECTIVE`, `EXPIRED`, and `INACTIVE` suppress the statutory path for that entitlement and preserve ordinary payment when ordinary payment readiness independently passes.

`SOURCE_UNAVAILABLE` and `MALFORMED_AUTHORITATIVE_STATE` fail closed for the statutory path. They are not reported as no coverage.

Senior Citizen and PWD are evaluated separately.

## Pre-Cash Revalidation

The revalidation route checks the same canonical session, Site scope, entitlement, and policy coverage authority immediately before the APT may continue a statutory path toward physical cash custody.

`PASSED_UNCHANGED` is returned only when coverage remains available. Any failed classification returns `FAILED`, `preCashRevalidationPassed=false`, and `readyForStatutoryCashFlow=false`.

The endpoint does not accept cash, create a payment intent, create or apply a statutory decision, or transition to `CASH_RECEIVED`.

## Ordinary Payment Boundary

`ordinaryPaymentPreserved=true` means the statutory ordinance check does not block ordinary payment. Ordinary payment still depends on the separate payable-basis, payment, terminal-cash, and fiscal readiness rules.

## Read-Only Boundary

The implementation is SELECT-only at the repository layer for this API. It does not create or update:

- statutory decisions
- statutory applications
- Operator Console reviews
- payable basis
- payment intents or attempts
- parking-session or tariff state
- policy state
- cash-custody state
- fiscal documents
- POS Server, Payment Orchestrator, HikCentral, or Vendor PMS commands

The API integration tests include a row-count check over payment, cash, fiscal, exit, and gate side-effect tables before and after the read endpoint call.

## Security and Privacy

The endpoint requires a service identity and the narrow APT ordinance availability permission. Human-user-only calls fail closed.

The safe error envelope uses correlation IDs and omits stack traces, SQL, table or column names, database names, internal service URLs, credentials, protected evidence, and statutory identity data.

## Validation

Automated coverage includes:

- service classification tests for available, no configured policy, future, expired, inactive, source unavailable, ambiguous session, and scope conflict
- endpoint authorization and Site-scope tests
- machine-readable contract tests
- RBAC policy mapping tests
- database-backed row-count no-write proof

No significant manual testing is required for J-005 because this is backend-only and has no user-facing runtime surface.

## J-004 Dependency

J-004 may consume:

- `POST /v1/apt/statutory-discounts/ordinance-availability/resolve`
- `POST /v1/apt/statutory-discounts/ordinance-availability/revalidate`
- `AptStatutoryOrdinanceAvailabilityRequest`
- `AptStatutoryOrdinanceAvailabilityResponse`

The APT must still treat the response as Central PMS advisory authority for UI gating only. Statutory application, payable-basis authority, cash acceptance, and fiscal issuance remain governed by their existing Central PMS contracts.

Controlled UAT and production rollout remain unauthorized.
