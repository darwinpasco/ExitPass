# Multi-Site HikCentral Site Adapter Routing

Status: Proposed v1.3 architecture correction

## Decision

ExitPass has one logical cloud-hosted Central PMS. Each parking Site with HikCentral Professional has one separately deployable on-premises Site Integration Adapter instance and one local HikCentral instance. Normal traffic is:

```text
Central PMS -> Site-specific Vendor PMS Adapter -> that Site's HikCentral
```

The adapter host is `ExitPass.VendorPmsAdapter.Api`. It exposes authenticated provider-neutral operations under `/v1/vendor/*` for session resolution, tariff calculation, paid-state confirmation, passageway synchronization, identity, liveness, and readiness. HikCentral signing, credentials, URLs, DTOs, envelopes, and application codes stay inside that host.

This decision supersedes v1.2 assumptions that imply a global Central PMS HikCentral client, a functional standalone Session Service, or a Central PMS physical-gate integration.

## Immutable Site Adapter Binding

One adapter process binds to exactly one `site_id`, `site_group_id`, `vendor_system_id`, adapter service identity, adapter endpoint identity, HikCentral base URL, mounted credential set, user ID, API version, parking-lot index, request timezone, environment, timeout policy, retry bound, and activation state. Missing, disabled, ambiguous, or mismatched configuration fails readiness or the request. Production HikCentral and adapter traffic requires TLS over an approved private tunnel or overlay; public exposure is prohibited. HTTP is accepted only by explicit task-owned `IntegrationTest` configuration.

The adapter authenticates Central PMS independently from HikCentral AK/SK authentication. Secrets are read only from the configured mounted-secret root. Central PMS stores a controlled `file:` reference for its adapter credential, never an AppSecret value.

Each adapter deployment also declares an immutable `AllowedOperations` set. Authentication is evaluated first, then the adapter derives the required operation from the matched provider-neutral route and authorizes it against that server-side set. Missing or unknown operation configuration fails readiness. Caller-supplied identity, Site, adapter, or permission headers cannot grant an operation. Insufficient server-granted permission returns controlled `403 SITE_ADAPTER_PERMISSION_REQUIRED`; payment confirmation requires both its operation grant and the separate confirmation activation control.

## Registry And Routing

Central PMS resolves `site_id + site_group_id + optional vendor_system_id` through:

- `integration.vendor_systems`: active environment-matched Vendor System and adapter base URI in `base_url_ref`;
- `integration.adapter_mappings`: active Site binding whose `vendor_object_type` is `SITE_ADAPTER` and whose `vendor_object_ref` is the adapter service identity UUID;
- `integration.vendor_endpoints`: active `SITE_ADAPTER_API` operation and credential-reference link;
- `integration.integration_credential_references`: active, effective Central PMS credential owner with an external secret reference;
- `identity.service_identities`: active adapter identity.

Exactly one effective row must resolve. Zero or multiple rows fail closed. There is no production global default and no cross-Site fallback. Routing profiles are not cached by this implementation, so database effective dates and revocation are evaluated on every call.

## Projection

The existing centralized scheduler remains a hosted part of the one logical Central PMS and processes all enabled Site-scoped targets. Target-scoped PostgreSQL locks prevent overlap. Each target is routed independently to its registered adapter. Successful pages are normalized inside the adapter and committed atomically in Central PMS. Failed cycles preserve the previous successful projection.

Projection records retain Site Group, Site, Vendor System, vendor record/session reference, card, usable plate, parking-lot reference, source adapter identity, source timestamp, projection timestamp, correlation ID, and status. Blank, null, `Unknown`, `N/A`, and unusable plates are not stored as lookup identifiers. Projection is not tariff, payment, fiscal, exit, or gate authority.

Site Group lookup requires exact identifier equality inside the submitted Site Group. Only active projections with a trusted source adapter and an enabled target are eligible. Ambiguous matches fail closed. Live tariff calculation always follows the resolved Site route; stale projection data never creates a payable basis.

## Tariff And Payment Acknowledgment

Live resolution persists immutable Site, Site Group, Vendor System, source adapter identity, vendor session reference, payable-basis identity, and tariff evidence. Paid-state acknowledgment reloads that context and requires the same adapter identity. Retry preserves the idempotency key and cannot switch Sites, vendors, or adapters.

The adapter may call `/artemis/api/vehicle/v1/parkingfee/confirm` only when its independent `ConfirmPaymentEnabled` control is explicitly enabled. That call informs HikCentral of ExitPass-established payment finality. It is not a gate command.

## Gate Boundary

Central PMS and the Site Adapter do not open physical gates, call HikCentral gate-control APIs, or call a Gate Integration Service. HikCentral exclusively manages cameras, lanes, controllers, and gates. Gate devices communicate with HikCentral, and HikCentral does not call ExitPass. Internal `ExitAuthorization` evidence does not prove barrier movement. Dormant direct-gate code remains retired and non-reachable; broad deletion is outside this correction.

## Session Service

`ExitPass.SessionService.Api` is a health/smoke scaffold. It is not a functional session-resolution dependency and is excluded from Central PMS startup, readiness, and the corrected IST contract matrix. Parking-session resolution remains in Central PMS, supported by projections and provider-neutral Site Adapter calls. The scaffold is not deleted here because solution-wide removal was not proven safe.

## Failure And Security Controls

- authenticated Central PMS service identity and separate mounted credential;
- one-Site adapter authorization before provider I/O;
- TLS/private endpoint in production;
- explicit bounded request size, bounded timeout, and zero automatic retries until a separately reviewed retry policy is configured;
- correlation propagation and stable sanitized errors;
- no secret or upstream raw-body logging;
- no mock fallback in production;
- no cross-Site fallback;
- payment confirmation disabled independently by default;
- new projection targets disabled by default.

## Deployment Dependency

Production deployment still requires an approved private Site-to-cloud connectivity mechanism, endpoint certificates, secret mounts, and per-Site operational approval. Tunnel-provider selection is intentionally outside this source correction.
