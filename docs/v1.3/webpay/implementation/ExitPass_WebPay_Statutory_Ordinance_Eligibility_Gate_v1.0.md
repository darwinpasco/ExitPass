# ExitPass WebPay Statutory Ordinance Eligibility Gate

## Purpose

This note records the bounded G-004 implementation that makes WebPay consume Central PMS statutory parking local-ordinance availability before showing or submitting a new Senior Citizen or PWD statutory request.

## Authority Boundary

Central PMS remains the authority for statutory parking coverage. WebPay does not interpret ordinance text, infer coverage from location names, calculate benefits, choose tariff snapshots, or apply payable basis changes. Payment Orchestrator is the browser-facing service boundary and calls Central PMS with the server-configured WebPay service identity.

Browser flow:

1. WebPay resolves the parking session through `POST /v1/webpay/parking-session`.
2. WebPay requests browser-safe availability through `POST /v1/webpay/statutory-discounts/availability`.
3. Payment Orchestrator forwards the request to Central PMS `POST /v1/statutory-discounts/decisions/availability`.
4. Central PMS returns authoritative coverage for the resolved parking session.
5. WebPay shows only covered entitlement types for new statutory requests.

The browser never calls Central PMS directly and never sends Central PMS service identity or permission headers.

## Consumed Contract

Payment Orchestrator consumes the merged Central PMS availability contract:

- Route: `POST /v1/statutory-discounts/decisions/availability`
- Request: `StatutoryDiscountParkingAvailabilityRequestDto`
- Response: `StatutoryDiscountParkingAvailabilityResponse`
- Policy metadata: `CentralPmsStatutoryDiscountDecisionRead`

The browser-facing route added for WebPay is:

- Route: `POST /v1/webpay/statutory-discounts/availability`
- Request: `WebPayStatutoryDiscountAvailabilityRequest`
- Response: `WebPayStatutoryDiscountAvailabilityResponse`

The WebPay response exposes only browser-safe availability facts: request reference, parking session scope, availability status, benefit availability flag, covered entitlement types, requested entitlement type, safe reason code, retry posture, remediation action, evidence requirement metadata, and correlation ID.

## Entitlement Behavior

When Central PMS reports active `AVAILABLE` coverage:

- `SENIOR_CITIZEN` and `PWD`: WebPay shows both options.
- `SENIOR_CITIZEN` only: WebPay shows only Senior Citizen.
- `PWD` only: WebPay shows only PWD.

Unavailable entitlement types are not shown as disabled options. The submission path also rechecks the selected entitlement before forwarding the statutory decision command to Central PMS.

## No Coverage

For authoritative no-coverage or non-active policy states returned by Central PMS, WebPay hides the new statutory request entry point, entitlement selector, document controls, and evidence metadata controls. Ordinary payment remains available through the existing payment path and uses the authoritative ordinary payable basis.

No-coverage is not treated as a customer rejection and does not create a statutory decision, review continuation, application, or evidence workflow.

## Unavailable And Malformed Responses

Availability service failures are not interpreted as no coverage. Payment Orchestrator maps timeout, unavailable, authentication, authorization, malformed, unsupported, and unsafe upstream responses to browser-safe classifications. WebPay hides statutory request controls, shows safe retry or service-unavailable guidance, and preserves ordinary payment.

Malformed or unknown availability responses fail closed for statutory request availability.

## Server-Side Submission Gate

`POST /v1/webpay/statutory-discounts/decisions` now performs a pre-submit availability check using the same resolved parking-session scope and requested entitlement. If Central PMS does not report active coverage for that entitlement, Payment Orchestrator returns a browser-safe non-availability response and does not forward the statutory decision submission.

Central PMS decision creation remains the final authority and continues to fail closed if coverage changes between display and submission.

Existing decision readback, continuation recovery, application intent, applied payable-basis payment, and pending regular-payment behavior remain unchanged for already-created statutory workflows.

## Ordinary Payment Preservation

The ordinary no-statutory WebPay path remains available when availability is covered, no coverage, unavailable, malformed, rejected, or pending. G-004 does not alter ordinary amount, currency, tariff snapshot, payment method selection, payment-intent idempotency, provider handoff, Sales Invoice presentation, ExitAuthorization, HikCentral, or gate behavior.

## Browser Security And Storage

The browser uses the existing same-origin WebPay route pattern. WebPay JavaScript does not emit:

- `X-ExitPass-Service-Identity-Id`
- `X-ExitPass-Permissions`
- internal authorization headers
- Central PMS credentials

Availability responses are held only in component memory. They are not written to `localStorage`, `sessionStorage`, IndexedDB, service-worker caches, payment-intent payloads, logs, fiscal records, or Sales Invoice records.

## Validation Coverage

Focused tests cover:

- Payment Orchestrator availability client route, permission, correlation, and safe failure mapping.
- WebPay availability endpoint covered and uncovered responses.
- Server-side decision submission gate rejection without Central PMS decision creation.
- WebPay client same-origin availability request shape and safe error mapping.
- WebPay UI behavior for both covered, Senior Citizen only, PWD only, no coverage, and temporary unavailable.
- Ordinary payment visibility in no-coverage and unavailable states.
- Browser smoke fixture compatibility with the new availability read.

The deterministic browser validation harness is validation-only and lives under WebPay UI test infrastructure:

- Fixture server: `src/Services/WebPayUi/e2e/fixtures/webpay-statutory-ordinance-validation-server.mjs`
- Playwright proof: `src/Services/WebPayUi/e2e/webpay-statutory-ordinance-validation.spec.ts`
- Runner: `src/Services/WebPayUi/scripts/Invoke-WebPayStatutoryOrdinanceEligibilityValidation.ps1`

The harness binds only to `127.0.0.1`, requires `-AcknowledgeValidationOnly`, uses a process-scoped validation nonce for control endpoints, serves the production WebPay build, and switches scenario state only through the runner. Production WebPay source does not call the validation control routes and the production build must not contain validation scenario identifiers.

Supported synthetic validation scenarios are both covered, Senior Citizen only, PWD only, authoritative no coverage, future-effective, expired, inactive, incomplete-policy, unavailable, timeout, authorization failure, malformed response, unknown classification, unsupported contract version, coverage removed before submission, selected entitlement removed before submission, pending-review continuation, rejected request, crafted entitlement, mismatched Site, mismatched Site Group, stale parking session, and ordinary payment preservation.

## Manual Validation

Significant browser validation is performed through the deterministic validation harness in a local headed browser. The proof must verify entitlement visibility, hidden unavailable controls, server-side crafted and stale submission rejection, pending-review continuation recovery, rejected readback, ordinary payment preservation, same-origin browser routes, absence of privileged browser headers, absence of direct Central PMS URLs, no persisted ordinance availability response in browser storage, desktop and narrow layouts, keyboard navigation, visible focus, and safe teardown.

Current browser recovery proof covers durable recovery metadata creation and restart GET readback without a repeated decision POST. A separate pending-review regular-payment action and full pending-review panel re-display after a manual post-restart ticket re-lookup remain outside this G-004 gate and require the existing continuation/pay-regular slice to provide the customer-visible payment mode.

## Non-Goals

G-004 does not implement secure evidence upload, evidence storage, OCR, malware scanning, continuation URLs, document-type policy UI, pay-regular command changes, approval/payment race arbitration, late approval handling, statutory calculation, VAT calculation, fiscal issuance, APT behavior, POS Server behavior, HikCentral, or gate control.

## Controlled UAT And Production

WebPay statutory controlled UAT is not authorized. Production rollout is not authorized.
