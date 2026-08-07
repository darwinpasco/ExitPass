# ExitPass Human Identity Persistence and API Gap Assessment v1.0

## 1. Audit method

I-018 inspected the current canonical source in `D:\SourceCodes\exitpassdb_v1.2` and current application sources in ExitPass, Management Platform, APT integration, Windows APT, and the bounded POS Server actor-reference surface. No database or runtime files were changed.

The canonical source and generated DDL were also searched for MFA, TOTP/OTP, authenticator, the deferred future WebAuthn/FIDO/passkey capability family, recovery-code, and authentication-assurance objects or columns. No canonical MFA persistence was found.

Classifications:

- `EXISTS_AND_SUFFICIENT`: supports the frozen contract without material schema work.
- `EXISTS_BUT_INCOMPLETE`: useful baseline exists but cannot truthfully support the contract alone.
- `MISSING`: no canonical durable authority was found.
- `NOT_REQUIRED`: intentionally outside ExitPass persistence.

## 2. Canonical identity objects

| Object | Current content | Verdict | Required action |
|---|---|---|---|
| `identity.users` | UUID, username, email/normalized email, display/masked mobile, type/status, lifecycle/effectivity, actor/version | `EXISTS_BUT_INCOMPLETE` | Add normalized username and uniqueness; preserve human profile only |
| `identity.roles` | code/name/type/status, privileged and elevated-approval flags, effectivity | `EXISTS_AND_SUFFICIENT` for role catalog | Add only controlled policy fields proven necessary |
| `identity.permissions` | code/domain/action/status/sensitivity/audit flags | `EXISTS_AND_SUFFICIENT` | Reuse catalog; add authentication/admin permissions through controlled loading |
| `identity.user_roles` | assignment status/reason/effectivity/revocation/review/actors | `EXISTS_BUT_INCOMPLETE` | Reuse assignment; bind general scoped grants and privileged approval |
| `identity.role_permissions` | binding status/effectivity/revocation/actors | `EXISTS_AND_SUFFICIENT` for bindings | Add mutation runtime and elevated approval checks |
| `identity.service_identities` | service credential reference/type/status/rotation/effectivity | `EXISTS_AND_SUFFICIENT` for separation | Never merge with human credential/session tables |
| `operator_console.hr_identity_mappings` | user-to-external-HR hash/masked mapping and lifecycle | `EXISTS_BUT_INCOMPLETE` | HR mapping is not login/OIDC binding; retain for workforce linkage |
| `operator_console.operator_shifts` | user, Site/Site Group, HR mapping, schedule/operational state | `EXISTS_AND_SUFFICIENT` for current operator shift concept | Do not use as general authorization scope |
| `operator_console.shift_takeovers` | original/takeover users, requester/approver, Site, lifecycle | `EXISTS_AND_SUFFICIENT` as an Operator Console takeover precedent | APT custody handover needs its own governed runtime/model audit |
| `audit.audit_events` | controlled event/result, human/service actor, target, channel, hashes, correlation | `EXISTS_AND_SUFFICIENT` as audit envelope | Add controlled identity/auth event types |
| `audit.security_events` | severity/status/result, actor, request hashes, incident/resolution, correlation | `EXISTS_AND_SUFFICIENT` as security envelope | Use for suspicious/lockout/security detections |

Important deficiencies in `identity.users`:

- `username` is not normalized and no username unique index exists;
- `email_normalized` is documented for uniqueness/lookup but no unique index exists;
- lifecycle status/timestamps exist, but no durable cause/event workflow establishes lockout or authentication state.

## 3. Persistence capability matrix

| Capability | Classification | Evidence and exact gap |
|---|---|---|
| Users | `EXISTS_BUT_INCOMPLETE` | Canonical profile/lifecycle exists; login normalization and administration runtime absent |
| Roles | `EXISTS_AND_SUFFICIENT` | Canonical role catalog supports privilege indicators and effectivity |
| Permissions | `EXISTS_AND_SUFFICIENT` | Canonical permission catalog supports sensitivity/audit posture |
| User-role assignments | `EXISTS_BUT_INCOMPLETE` | Assignment and review exist; no scoped grant or privileged approval binding |
| Role-permission bindings | `EXISTS_AND_SUFFICIENT` | Durable effectivity/revocation exists; APIs absent |
| Local human credentials | `MISSING` | No verifier, algorithm/version, credential state, change requirement, history, or compromise metadata |
| External identity bindings | `MISSING` | HR mapping is not issuer/subject authentication binding; no OIDC provider/subject binding |
| Human sessions | `MISSING` | No session secret hash, audience, assurance, expiry, activity, device binding, or status |
| Session revocations | `MISSING` | No durable current/global revocation or user authorization epoch |
| Login attempts | `MISSING` | No throttle/attempt ledger |
| Lockouts | `EXISTS_BUT_INCOMPLETE` | `users.user_status=LOCKED` and `locked_at` exist; no cause, counter/window, unlock evidence, or policy state |
| Password reset/change | `MISSING` | No reset/activation challenge, consumption, change-required, or credential version |
| MFA authenticator registration | `MISSING` | No user authenticator identity, type, enrollment, status, or uniqueness authority |
| Protected TOTP secret/credential material | `MISSING` | No encrypted secret, protected credential reference, protection-key version, or restricted-verifier boundary |
| MFA lifecycle | `MISSING` | No enrollment, activation, reset, removal, revocation, or last-safe-use metadata |
| MFA verification attempts | `MISSING` | No TOTP attempt, replay-prevention, throttle, or lockout evidence |
| MFA reset/revocation | `MISSING` | No governed reset/removal actor, reason, invalidation, or session-impact record |
| Session assurance state | `MISSING` | No durable indication that the current account's required MFA was satisfied |
| MFA audit/security events | `EXISTS_BUT_INCOMPLETE` | Generic audit/security envelopes exist, but controlled TOTP enrollment/verification/throttle/reset/removal events do not |
| MFA recovery codes | `NOT_REQUIRED` | Deferred for v1.3; if later approved, only one-way verifiers are permitted |
| Site grants | `MISSING` | Evidence-specific scope and shifts exist, but no general human role-assignment Site grant |
| Site Group grants | `MISSING` | Same; canonical group membership alone does not assign a user |
| Global grants | `MISSING` | No explicit bounded global grant/eligibility |
| Privileged assignment approvals | `EXISTS_BUT_INCOMPLETE` | Role flag says elevated approval required; no request/decision ledger binds approval to assignment/scope |
| Access reviews | `EXISTS_BUT_INCOMPLETE` | Last-review fields exist only on user roles; no complete review outcome/campaign or scope review |
| Authentication/security events | `EXISTS_BUT_INCOMPLETE` | Generic audit/security envelopes exist; controlled authentication events and attempt linkage are absent |
| External IdP credentials/tokens | `NOT_REQUIRED` | ExitPass must not store IdP passwords; provider tokens only encrypted/server-side when needed for session continuation |

## 4. Minimum I-019 schema scope

Names are provisional and must follow canonical database conventions:

1. Extend `identity.users` with `username_normalized`, authentication/authorization epoch fields where justified, and active uniqueness for normalized username and verified login email.
2. Add `identity.local_credentials` with one-way verifier metadata and lifecycle.
3. Add `identity.external_identity_bindings` keyed by provider/issuer and immutable subject, never email alone.
4. Add minimum TOTP authenticator persistence, provisionally a restricted `identity.user_mfa_authenticators` authority: user ID, controlled authenticator type, encrypted secret material or approved protected credential reference, key/protection version, status, enrollment/activation/last-used timestamps where safe, reset/revocation actors and reasons, creation/update actors, and row version. It must not store secret material in `identity.users` or generic JSON.
5. Add `identity.human_sessions` with hashed secret, audience, assurance including current MFA satisfaction where required, device/provider binding, idle/absolute expiry, status/revocation, credential/authorization/authenticator versions.
6. Add authentication verification attempts or equivalent durable bounded evidence for password and TOTP throttling, successful-code replay prevention, and lockout without storing passwords or OTP codes.
7. Add hashed, expiring, one-time activation/reset challenges or a general purpose-bound credential challenge table.
8. Add `identity.user_role_scope_grants` for explicit `SITE`, `SITE_GROUP`, and approved `GLOBAL` assignment scopes.
9. Add a privileged-access request/approval ledger or equivalent binding for elevated assignments.
10. Add controlled identity/authentication/TOTP enrollment, verification, throttling, reset/removal, assignment, and session-assurance event codes plus required constraints/indexes/FKs.

TOTP secret material must be encrypted at rest or represented by an approved protected credential mechanism whose key/access boundary is separate from ordinary database-data access. Provisioning QR payloads, submitted TOTP codes, plaintext secrets, and raw authenticator responses are not persisted. Recovery codes are not part of the v1.3 minimum.

Do not add generic JSON authority, raw tokens, passwords, recoverable password encryption, external assertions, or browser/device secrets. Audit/security tables should be reused rather than shadowed.

## 5. Current runtime/API verdict

### 5.1 Authentication

`MISSING`.

Central PMS has no `AddAuthentication`, no `UseAuthentication`, and no human login/logout/current-session/password/session-revoke routes. `SessionService` exposes only smoke/health behavior. Existing RBAC middleware is authorization scaffolding and currently permits explicit test/operator headers; it is not a credential validator.

Central PMS also has no TOTP enrollment, confirmation, verification, status, reset/removal, verification-throttling, or MFA session-assurance runtime. These capabilities are `MISSING` and are required only for privileged Management Platform administrators under the approved v1.3 policy.

### 5.2 User administration

`MISSING` for mutation, `PARTIAL` for read-only inventory.

`GET /v1/ops/management-platform/identity-rbac/inventory` returns a safe RBAC inventory. No list/detail/create/invite/update/status/lock/reset/role/scope/access-review/session-administration API was found.

### 5.3 Authorization

`PARTIAL`.

`CentralPmsRbacRepository.UserHasAnyPermissionAsync` checks active/effective users, user-role assignments, roles, role-permission bindings, and permissions. Named policy metadata exists. The current general middleware does not provide complete Site/Site Group grant enforcement and can accept permission headers. Sensitive domain services sometimes implement separate scope models, such as statutory evidence scope grants and Operator Console access evaluations. These cannot substitute for one general human authorization contract.

## 6. Current client findings

| Consumer | Current implementation | Gap |
|---|---|---|
| Management Platform | Injected development auth principal and Vite permission/site values; relative APIs; avoids browser durable storage | No production login, privileged TOTP challenge/enrollment, current-session readback, expiry, logout, or user administration |
| Operator Console | Header/fallback user, permission, device, shift, Site/Site Group context; durable access evaluations | No authenticated principal; request/header actor can represent fixture identity; reviewer attribution must be session-derived |
| Windows APT | Configured cashier/shift context; encrypted local shift/custody records with authentication-session reference | No credential/session runtime; development auth reference; supplied values are not Central PMS authority |
| APT integration repo | Design/integration material, no complete human-auth runtime | Needs Central PMS contract consumption |
| POS Server | Service/API-key boundary and privacy-safe actor references for fiscal/admin audit | Correctly not a human credential validator; Central PMS must propagate validated actor reference |

## 7. Required authentication APIs

| Route concept | Request | Response/posture |
|---|---|---|
| `POST /v1/human-authentication/login` | username, password, application, anti-automation context | Cookie session plus safe current-session DTO; generic failure |
| `POST /v1/human-authentication/oidc/{provider}/start` | application/return intent | Server-owned challenge and safe redirect |
| OIDC callback | provider state/code | Server validates and binds; browser receives only ExitPass session |
| `GET /v1/human-authentication/session` | cookie | Safe current user, assurance, expiry, permissions/scopes |
| `POST /v1/human-authentication/session/continue` | cookie plus CSRF | Rotated session or safe reauth requirement |
| `POST /v1/human-authentication/logout` | cookie plus CSRF | Durable revoke and cookie clear |
| `POST /v1/human-authentication/logout-all` | recent auth | Revoke all user sessions |
| password change/reset routes | purpose-specific facts | No verifier/token exposure; session revocation |
| `POST /v1/human-authentication/totp/enrollments` | fresh privileged session plus CSRF | One-time governed provisioning response; secret/QR never returned again or logged |
| `POST /v1/human-authentication/totp/enrollments/{enrollmentReference}/confirm` | short-lived TOTP code plus CSRF | Activates authenticator and rotates session assurance; no code echo |
| privileged login TOTP verification | pending login reference plus short-lived code | Creates session only after password and required TOTP succeed |
| governed TOTP reset/removal | fresh authorized administrator action, target reference, reason, expected version | Invalidates old authenticator, applies session policy, emits safe events; no secret readback |
| APT human-session routes | device-authenticated request plus credentials/session | Device-bound opaque session handled by desktop host |

## 8. Required user administration APIs

Central PMS requires read/list and governed mutation routes for users, lifecycle, local reset challenge, external identity bindings, safe privileged-account MFA status/reset/removal, roles, permissions, role assignments, assignment-scoped grants, privileged decisions, access reviews, and safe session list/revoke. The exact proposed route set is in `ExitPass_User_Role_and_Scope_Administration_Contract_v1.0.md`.

Every mutation requires authenticated administrator, dedicated permission, assignment ceiling, canonical scope, expected row version, idempotency key, controlled reason, effectivity, correlation, and atomic audit. Unknown and unauthorized references follow anti-enumeration policy.

## 9. Evidence references

Canonical source facts were verified from:

- `objects/schemas/identity/tables/identity.users.sql`
- `objects/schemas/identity/tables/identity.roles.sql`
- `objects/schemas/identity/tables/identity.permissions.sql`
- `objects/schemas/identity/tables/identity.user_roles.sql`
- `objects/schemas/identity/tables/identity.role_permissions.sql`
- `objects/schemas/identity/tables/identity.service_identities.sql`
- `objects/schemas/operator_console/tables/operator_console.hr_identity_mappings.sql`
- `objects/schemas/operator_console/tables/operator_console.operator_shifts.sql`
- `objects/schemas/operator_console/tables/operator_console.shift_takeovers.sql`
- `objects/schemas/audit/tables/audit.audit_events.sql`
- `objects/schemas/audit/tables/audit.security_events.sql`

Application facts were verified from Central PMS `Program.cs`, `CentralPmsRbacMiddleware`, `CentralPmsRbacRepository`, `OperatorConsoleIdentityContext`, Session Service smoke source, Management Platform `auth.ts` and RBAC audit, Windows APT configuration/local journal source and non-auth closeout, and bounded POS Server actor-reference source.

## 10. Database decision

Canonical changes are required before runtime authentication. I-018 intentionally changes no database repository. I-019 must implement and validate the minimum canonical objects before I-020/I-021 runtime merges.
