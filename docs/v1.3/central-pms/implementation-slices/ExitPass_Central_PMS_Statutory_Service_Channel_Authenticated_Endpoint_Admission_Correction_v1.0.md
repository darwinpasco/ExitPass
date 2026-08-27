# ExitPass Central PMS Statutory Service-Channel Authenticated Endpoint Admission Correction

## Purpose and provenance

This correction closes the production endpoint-admission gap exposed by whole-console acceptance `OPCON-MVP-ACCEPT-20260825T050911Z-FINAL-RERUN` at merged `origin/dev` commit `5264cabed80e16c199ff60718bb308919681b8fa`. The earlier failed acceptance `OPCON-MVP-ACCEPT-20260825T011914Z-merged-rerun` and the later failed acceptance remain unchanged. Neither failure is reclassified as a pass, and whole-console runtime and visual acceptance remains pending.

The preceding service-channel authorization correction already separated human review from post-approval service application. Its production Release topology nevertheless lacked an authentication mechanism that could create a trusted WebPay or Assisted Payment Terminal principal before the shared statutory-decision endpoint performed authorization. Fixture identity/permission headers and RBAC-disabled test arrangements concealed that admission gap.

## Selected production authentication mechanism

Central PMS now extends its existing internal mTLS boundary for the shared statutory-decision endpoint. Kestrel supplies the client certificate through `HttpContext.Connection`; arbitrary forwarded-certificate or identity headers are not read. A presented certificate must:

- have a thumbprint in the deployed internal trust configuration;
- resolve through exactly one deployed certificate-to-credential binding;
- resolve that credential reference to exactly one canonical `identity.service_identities` record;
- use the canonical `MTLS_CERTIFICATE_REFERENCE` credential type;
- be temporally current and not revoked;
- belong to an active supported service identity; and
- have a configured source channel compatible with the canonical owning service.

The deployment-owned binding contains no secret material. It maps the verified certificate thumbprint to the existing canonical credential reference, Central PMS audience, source channel, and operation permissions. The canonical database remains authoritative for identity lifecycle and Site/Site Group assignments. Empty or missing production binding configuration fails closed. Fixture identity and permission headers remain disabled by default and are considered only in explicitly enabled Development, SecureDevelopment, or Test environments.

No second identity registry, shared-secret route, browser token, schema, migration, or locked v1.2 DDL change is introduced.

## Server-owned principal

After credential validation, Central PMS creates an `InternalMtlsServicePrincipal` claims identity from verified transport and server-owned records. The principal contains only these canonical facts:

| Claim | Authority source |
| --- | --- |
| `service_identity_id`, `client_id` | canonical service identity resolved by credential reference |
| `exitpass_audience` | deployment-owned credential binding |
| `source_channel` | deployment binding validated against canonical owning service |
| `source_application` | canonical service identity owning service |
| `credential_type` | verified mTLS authentication result |
| `permission` | deployment-owned credential grant |
| `site_id`, `site_group_id` | active canonical service-principal assignments |

Request headers for service identity, audience, permission, role, Site, Site Group, source channel, reviewer, device, or shift do not create or augment the production principal. The RBAC middleware and statutory endpoint consume the authenticated principal and reject missing authentication, wrong audience, missing permission, source mismatch, lifecycle failure, and incompatible scope with controlled errors.

## Human and service separation

| Caller | Authentication | Device and shift | Permitted statutory role |
| --- | --- | --- | --- |
| Operator Console reviewer | H-006 server session | Required | Review evidence and approve or reject eligibility |
| WebPay service | verified mTLS service principal | Not applicable | Submit its channel request and invoke its permitted deferred application operation |
| Assisted Payment Terminal service | verified mTLS service principal | Not applicable | Submit its channel request and invoke its permitted deferred application operation |
| Management Platform or unrelated service | separate audience | Not applicable | No admission to the WebPay/APT service contract |

The shared route permits a request without a client certificate to continue only so the existing H-006 human authentication and authorization path can evaluate it. An unauthenticated or header-only service request has no service principal and fails closed. Presenting a certificate when mTLS is disabled, unconfigured, untrusted, unknown, expired, revoked, duplicated, or incompatible also fails closed.

Human Operator Console operations still require H-006 authentication, CSRF protection, trusted-device binding, active-shift binding, current credential/session version and authorization epoch, permission, and compatible Site/Site Group scope. Service principals cannot acquire reviewer authority, and human sessions cannot synthesize service authority.

## Statutory lifecycle and attribution

The correction does not change the approved deferred payable-basis lifecycle or calculation rules. Human approval before payable-basis creation records eligibility and reviewer/device/shift attribution but creates no monetary application or applied-tariff snapshot. After a canonical payable basis exists, the authenticated matching WebPay or APT service may request application without an Operator Console device or shift. The existing service authorization, application writer, PHP calculation, idempotency, concurrency, and tariff-snapshot persistence remain authoritative.

The approved human remains decision attribution. The verified service identity is the later application initiator. Rejected and pending requests remain ineligible for application.

## Regression and runtime proof

Permanent production-path coverage exercises real startup/dependency injection, production RBAC, disabled fixture authority, canonical PostgreSQL identity and scope records, and the actual admission middleware. It covers valid WebPay and APT admission; missing, unknown, expired, revoked, disabled, and untrusted credentials; wrong audience; missing permission; wrong Site and Site Group; source-channel isolation; raw identity/permission header spoofing; and Management Platform isolation. Focused unit coverage verifies claim construction and the H-006 no-certificate handoff.

Correction evidence `STAT-SVC-ADMISSION-20260825T065852Z` includes a focused external Release runtime proof with loopback-only HTTPS, disposable client/server certificates, isolated PostgreSQL 16, production RBAC, fixture authority disabled, actual H-006 review, early approval, later PHP application, persistence, attribution separation, and replay convergence. The temporary runtime harness and all credentials, certificates, database resources, and processes are excluded from the review diff and removed during cleanup.

Review posture: `SELF-REVIEWED`
Independent review: `NOT_PERFORMED`

This targeted correction does not mark the Operator Console MVP accepted. The required follow-up is **Operator Console MVP Whole-Console Integrated Runtime and Visual Acceptance Rerun**.
