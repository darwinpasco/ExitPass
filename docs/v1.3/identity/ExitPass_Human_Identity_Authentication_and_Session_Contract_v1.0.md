# ExitPass Human Identity, Authentication, and Session Contract v1.0

## 1. Status and scope

| Field | Value |
|---|---|
| Contract | `exitpass-human-identity-authentication:v1` |
| Task | I-018 |
| Status | Architecture frozen; runtime not implemented |
| Authority | Central PMS |
| Consumers | Management Platform, Operator Console, Windows Assisted Payment Terminal (APT) |
| Customer WebPay staff login | Not applicable |

This contract separates three questions on every protected operation:

1. **Authentication:** which human is present?
2. **Authorization:** which effective permissions may that human exercise now?
3. **Scope:** at which Site, Site Group, terminal, shift, custody session, or other governed resource may the permission be exercised?

No frontend role name, permission array, Site selector, browser header, APT cache, external identity-provider group, or service principal permission is human authority. Central PMS resolves all three questions from current server-owned records. Service identity and human identity remain separate and may both be attributed to one call.

## 2. Audit findings

The canonical database already has `identity.users`, `identity.roles`, `identity.permissions`, `identity.user_roles`, `identity.role_permissions`, and `identity.service_identities`. These are a useful identity and RBAC foundation. They do not provide complete authentication.

Current Central PMS has a database-backed permission evaluator, but it does not register ASP.NET authentication or call `UseAuthentication`. `CentralPmsRbacMiddleware` can currently resolve development/test user and permission headers. `OperatorConsoleIdentityContext` accepts identity, Site, Site Group, device, and shift headers or request fallbacks. These mechanisms are not production human authentication and must be disabled outside explicit test environments when the runtime is implemented.

`SessionService` is a health/smoke skeleton only. Management Platform uses an injected development principal. Operator Console uses configured development headers. Windows APT uses configured cashier and shift facts plus a development authentication-session reference. None is production authentication.

## 3. Canonical human identity

- `identity.users.user_id` is the immutable canonical human identifier and the audit actor key.
- `username` is the primary local login identifier. It is displayed in its chosen form but compared using a server-generated `username_normalized` value.
- Username comparison is case-insensitive. Runtime normalization must be deterministic, Unicode-aware, versioned, and applied only server-side. I-019 must add the normalized column and an active uniqueness rule because the current schema has neither.
- Email is a profile and recovery attribute by default. It may become a login alias only when verified, normalized, unique, and explicitly enabled. Email is never implicitly authoritative merely because an OIDC provider returns it.
- `display_name` is presentation data, not an identifier or permission.
- Human identity records never hold service credentials. `identity.service_identities` remains the separate machine identity authority.

Only an `ACTIVE` user inside `effective_from`/`effective_to` may establish or continue a normal session. `INVITED` is limited to governed activation. `LOCKED`, `SUSPENDED`, `INACTIVE`, `RETIRED`, not-yet-effective, and expired users are denied. Retirement is terminal unless a separately approved recovery process exists.

"Disabled" is an API/UI umbrella, not a new canonical status: temporary security/administrative disablement maps to `SUSPENDED`; ordinary deactivation maps to `INACTIVE`; permanent offboarding maps to `RETIRED`. APIs return the exact canonical state and controlled reason.

## 4. Authentication architecture decision

### 4.1 Options evaluated

| Dimension | A: ExitPass local password | B: external OIDC only | C: pluggable local plus OIDC |
|---|---|---|---|
| MP/Operator usability | Direct and deployable | Strong enterprise SSO | Supports both |
| APT cashier usability | Works without corporate accounts | Assumes every cashier has IdP identity | Local baseline; OIDC where practical |
| MFA | ExitPass implements TOTP for privileged Management Platform administrators | IdP can enforce its configured MFA policy | Local TOTP baseline plus validated provider assurance where configured |
| Lockout/reset | ExitPass owns | IdP owns credential controls | Provider-specific authentication, one authorization model |
| Vendor dependency | Low | High | Bounded and configurable |
| Disaster recovery | ExitPass recovery required | IdP availability required | Provider runbooks required |
| Offline behavior | Could be built, but prohibited in v1.3 | Normally unavailable offline | Offline login remains prohibited |
| Enterprise migration | Later redesign risk | Good enterprise posture | Best migration posture |
| Complexity | Moderate | Moderate deployment dependency | Highest implementation complexity, lowest authorization redesign risk |

### 4.2 Recommended v1.3 model

Adopt **Option C, pluggable authentication with one Central PMS authorization model**:

- ExitPass local credentials are the required v1.3 baseline because no enterprise IdP and no Microsoft 365 identity for every cashier may be assumed.
- OIDC is an optional authentication provider through durable external-identity bindings. OIDC groups never silently grant ExitPass roles or scopes.
- Central PMS owns user status, authorization, scope, session revocation, and business-operation revalidation regardless of authentication provider.
- Browser applications use a same-origin backend/session boundary. OIDC access and refresh tokens, when used, remain server-side.
- APT authenticates through its trusted desktop host after device/service authentication. React never receives a service secret or reusable human credential.

OIDC support is architectural in v1.3; activating a provider requires a configured issuer, client, redirect allowlist, binding/provisioning policy, assurance policy, and recovery runbook. It is not required to launch a controlled local-auth deployment.

## 5. Local credential contract

Local credentials belong in PostgreSQL in a dedicated restricted table, not in `identity.users`, because ExitPass must validate them centrally and no external IdP is guaranteed. Store only a one-way verifier and metadata:

- Argon2id verifier, algorithm/version, unique random salt, configured memory/time/parallelism parameters, credential version, created/changed timestamps, change-required flag, and revocation state.
- No plaintext, recoverable encryption, password hint, or raw reset token.
- Argon2id parameters are security-owned configuration, validated against a minimum at startup, benchmarked for deployed hardware, and upgradeable on successful login. OWASP's current minimum is a floor, not a permanent hard-coded target.
- PBKDF2-HMAC-SHA-256 is permitted only when documented FIPS/runtime constraints require it and security approves its configured work factor.
- A server-side pepper is optional defense in depth. If approved, it is versioned in a secrets manager/HSM boundary, never stored beside verifiers, and has a tested compromise/rotation procedure; inability to retrieve it fails authentication closed.
- Passwords are at least 15 characters for single-factor use, allow at least 64 characters, accept Unicode, are checked against compromised/common blocklists, and have no arbitrary composition rule.
- No scheduled password expiry is imposed without compromise or policy cause. Credential compromise, reset, or administrative action can force change.
- Password entry and verification require TLS. Plaintext exists only in bounded request and verifier memory, is never logged, audited, queued, cached, or persisted, and references are released promptly.
- Password history is not required for v1.3 by default; if policy requires it, retain only prior verifiers and prevent recent reuse without recoverable passwords.

### 5.1 Failure, throttling, and lockout

Authentication responses are anti-enumerating. Per-account and per-source adaptive throttles apply before expensive verification and use durable attempt evidence. Recommended default pending approval: five failed attempts within 15 minutes lock local authentication for 15 minutes; increasing repeated abuse produces longer bounded delays and security alerts. Success resets the rolling failure counter but does not erase immutable event evidence.

Lockout never changes roles or scopes. An administrator with explicit unlock authority may unlock another user with reason and audit evidence; self-unlock is prohibited. OIDC lockout remains provider-owned, while ExitPass can independently suspend or revoke sessions.

### 5.2 Change and reset

- Authenticated password change requires fresh authentication, rotates the credential version, and revokes other sessions.
- Self-service reset is available only through a verified recovery channel. Otherwise an authorized administrator issues a single-use, short-lived reset challenge; administrators never choose or see the replacement password.
- Reset challenges are stored only as hashes, are audience/user/purpose bound, expire, and are consumed once.
- Reset, compromise response, and administrative credential revocation invalidate existing sessions.
- Invited/local users must change the bootstrap credential on first successful activation. Prefer a reset-style activation challenge over a temporary password.

## 6. Human-session model

### 6.1 Durable server session

I-019 must add durable human sessions. A session contains an opaque public reference, a hash of the client secret, `user_id`, authentication provider/binding, application/audience, assurance level including whether the account's current MFA requirement was satisfied, device/service binding where applicable, created/authenticated/last-seen/idle-expiry/absolute-expiry timestamps, credential and authorization epochs, status, revocation reason/actor, and correlation-safe metadata. Raw session secrets and provider tokens are never stored in logs or audit events.

Every request resolves the session server-side. Central PMS is the trusted session issuer. Each session has one explicit application audience (`management-platform`, `operator-console`, or `apt`); cross-audience use is rejected. Effective permissions and scopes are evaluated from current status/effectivity and current grants for sensitive operations. Embedded claims are hints only and cannot outlive server revocation.

### 6.2 Web session

Management Platform and Operator Console use opaque server-side sessions delivered in a host-only cookie:

- `Secure`, `HttpOnly`, `Path=/`, no `Domain`, and `SameSite=Strict` by default (`Lax` only for a documented OIDC callback need).
- Non-persistent browser cookie unless an approved continuation policy requires otherwise.
- State-changing requests require an origin check and a session-bound anti-CSRF token. `SameSite` alone is not the complete CSRF control.
- Rotate the session identifier after login, provider callback, password change, fresh reauthentication, and privilege elevation.
- Authentication/session responses use `Cache-Control: no-store`.
- No access token, refresh token, session ID, permission authority, or credential is stored in `localStorage`, `sessionStorage`, IndexedDB, Cache Storage, or frontend-managed cookies.

If OIDC is active, the web backend is a confidential BFF. It stores provider tokens server-side and exposes only the ExitPass session to the browser.

### 6.3 APT session

The desktop host first establishes device/service identity, then submits human credentials to Central PMS over TLS. APT receives a device-bound opaque human session or short-lived access token plus rotating continuation secret. The React WebView receives only safe session status and commands. Reusable session/continuation material is held in desktop-process memory; restart continuation, if enabled, uses Windows OS-protected credential storage, never APT SQLite or frontend configuration.

APT has no offline cashier login in v1.3. Cached identity, permissions, or an old session reference cannot authorize new work.

### 6.4 Expiry and continuation

Recommended defaults pending Darwin approval:

| Application | Idle timeout | Absolute timeout | Continuation |
|---|---:|---:|---|
| Management Platform | 15 minutes | 8 hours | Rotating server session after current validation |
| Operator Console | 15 minutes | 8 hours | Same |
| APT | 15 minutes | 12 hours, never beyond governed shift policy | Device-bound rotating continuation while online |

Idle and absolute expiry are server-enforced. A client timer is presentation only. Selected high-risk actions require fresh authentication no older than five minutes by default, while all sensitive actions require current authorization and Site/Site Group scope re-evaluation. For a non-MFA user, fresh authentication normally means re-entering the user's password. For a privileged Management Platform administrator, fresh privileged authentication also requires the account's current TOTP MFA policy to be satisfied. Fresh authentication is not universal MFA. Open pages may remain visible only under privacy-safe lock treatment; the next API call is denied until reauthentication. Unsaved sensitive input must not silently submit after reauthentication without user confirmation.

### 6.5 Revocation

- Logout revokes the current session server-side before clearing client material.
- Global logout revokes all user sessions.
- Administrators may enumerate safe session metadata and force revocation with explicit permission and reason.
- Suspension, inactivation, retirement, credential reset/compromise, or provider-binding revocation invalidates all sessions.
- Role or scope revocation updates an authorization epoch and is enforced on the next sensitive request. A session may continue with reduced access only if safe; otherwise it is revoked.
- A late request from a revoked/rotated session cannot overwrite an operation completed by a current session.

Recommended concurrency pending approval: maximum three concurrent web sessions per user across Management Platform and Operator Console; one active APT human session per user-terminal pair and one active cashier identity per terminal. Privileged administrators may be restricted further. New sessions over the limit revoke the oldest after explicit notice; they never silently transfer custody.

## 7. Server-side operation rules

- Central PMS derives `actor_user_id` from the authenticated session. A body/header user ID is rejected if present outside explicit test fixtures and can never override the principal.
- Service-to-service calls carry both authenticated service identity and the propagated human actor/session reference. The receiving authority revalidates allowed propagation; it does not convert service permissions into human permissions.
- Approval, rejection, evidence review, user/role/scope administration, fiscal void/approval, supervisor takeover, and custody handover require current server authorization and recent authentication.
- Role/scope effectivity is evaluated at operation time, not only login time.
- Idempotency replay returns the original actor-bound outcome. A different actor or changed semantics conflicts.

## 8. APT authentication, shift, and custody

The following identities are distinct:

`human authentication != Windows account != APT device/service identity != cashier shift != cash-custody session`

Required flow:

`terminal start -> device/service trust -> cashier login -> Central PMS authorization/scope -> shift open/resume -> custody open/resume -> transactions -> custody close -> shift close -> logout`

Rules:

- A cashier may resume only their own open shift and custody after online reauthentication and current Site/terminal authorization.
- Another cashier cannot inherit or resume another cashier's custody. A governed supervisor handover/takeover is a distinct online operation with both identities, reason, count/reconciliation evidence, the supervisor's fresh username/password authentication, current permission and scope, and audit. TOTP is not required for an APT supervisor under the v1.3 baseline.
- A second cashier may authenticate for a supervisor action, but cannot become the transaction actor while another custody remains active without completed handover.
- Normal logout is denied while custody is open. Logout never automatically closes custody or shift.
- Forced revocation or session expiry leaves physical custody records open for reconciliation but locks new tender and new `CASH_RECEIVED` operations. The same cashier must reauthenticate or a supervisor must complete governed handover.
- An irreversible local cash event already durably recorded before expiry is reconciled; it is not erased. Expiry blocks later authority and submission steps unless the recovery contract explicitly permits them.
- APT SQLite may retain opaque user, session-reference, shift, custody, terminal, Site, Site Group, and audit linkage needed for recovery. It must not retain passwords, credential verifiers, reset/refresh secrets, bearer tokens, permission authority, or offline login material.

## 9. Approved MFA posture

Darwin approved the following risk-based v1.3 policy:

- Privileged Management Platform administrators authenticate with username/password plus standards-compatible TOTP MFA. Enrollment is required before privileged administration in Controlled UAT or production.
- Ordinary Management Platform users authenticate with username/password. MFA is not mandatory in the v1.3 baseline.
- Operator Console users, including ordinary operators and supervisors, authenticate with username/password. MFA is not mandatory in the v1.3 baseline.
- APT cashiers and supervisors authenticate with username/password after the terminal establishes separate trusted device/service identity. Device trust is not a human authentication factor.
- Selected sensitive supervisor and security operations require fresh reauthentication, current permission, current Site/Site Group scope, a second accountable human where the workflow requires one, and explicit audit attribution. Fresh reauthentication does not automatically mean MFA.
- WebAuthn, FIDO2 security keys, passkeys, Windows Hello-based passkeys, hardware-token distribution, and passkey recovery are deferred future security enhancements outside v1.3 MVP, Controlled UAT, and initial production rollout.
- A configured external OIDC provider may enforce its own MFA policy. ExitPass accepts provider assurance only through an approved issuer/authentication-context/freshness mapping. IdP groups and provider MFA never become ExitPass permissions or Site/Site Group scope.

### 9.1 TOTP contract

ExitPass uses the standard time-based one-time password semantics defined by RFC 6238. ExitPass does not create a custom OTP algorithm or authenticator application. Standards-compatible authenticator applications can be used.

- TOTP is used only for accounts and operations whose policy requires MFA; ordinary staff are not automatically enrolled.
- Each authenticator has unique secret material. The secret is a sensitive credential and is never stored in `identity.users`, logged, audited, returned after enrollment, placed in browser storage, or placed in APT SQLite.
- Provisioning secret/QR material is displayed only during a governed enrollment ceremony over TLS and is not recoverable from ordinary administration APIs.
- Verifier material is encrypted at rest or held by an approved protected credential mechanism. Encryption keys and access policy are separate from ordinary database-data access. Only the authentication verifier process can use decrypted material, and plaintext lifetime in memory is bounded.
- Codes are short-lived, accepted only inside the configured bounded clock window, rate limited, and never treated as reusable credentials. A successfully accepted code cannot be replayed in the same time step.
- Algorithm, digits, time step, accepted clock skew, issuer label, key protection, and throttling limits are security-owned interoperable configuration validated at startup. Defaults must conform to RFC 6238 and supported authenticator applications.
- Enrollment, successful/failed verification, throttling, reset/removal, and recovery actions produce privacy-safe audit/security events without the code, secret, QR payload, or raw authenticator response.
- Disabling or removing MFA from a privileged administrator is itself a privileged, fresh-authenticated, independently authorized action.

### 9.2 TOTP recovery

- An administrator can see safe MFA status/readiness but can never retrieve another user's TOTP secret or generate codes for that user.
- TOTP reset invalidates the old authenticator and starts a new governed enrollment. It requires explicit privileged identity-administration authority, adequate identity verification, a controlled reason, and audit evidence.
- Self-reset cannot silently bypass MFA. A user who cannot satisfy existing MFA follows the approved identity-recovery channel and independent administration checks.
- Reset/removal revokes or re-evaluates existing privileged sessions according to session policy; the default is to revoke sessions whose assurance depended on the removed authenticator.
- Recovery codes are deferred for v1.3 because no demonstrated operational need requires them. If later approved, only one-way verifiers may be stored and each code is single use.

MFA and TOTP are not implemented by I-018.

## 10. Security and audit events

Use existing `audit.audit_events` for business/security attribution and `audit.security_events` for security detection. Add controlled event codes; do not create an unrelated audit mechanism. Durable login-attempt data required for throttling is separate from immutable audit evidence.

Required events include login success/failure, account lock/unlock, logout, idle/absolute expiry, session continuation/rotation/revocation, global logout, credential change/reset, provider binding, user lifecycle changes, role and permission binding changes, Site/Site Group/global grants and revocations, access reviews, privileged approvals, denied authorization, suspicious repeated authentication, fresh-reauthentication success/failure, TOTP enrollment/verification/throttling/reset/removal, APT shift/custody open/close/handover, and session expiry during custody.

Events contain controlled result/reason, actor user and service IDs where applicable, target opaque IDs, application/channel, Site/Site Group, timestamps, correlation, and hashed network/user-agent evidence when policy permits. They never contain passwords, verifiers, reset/session secrets, bearer/refresh tokens, raw OIDC assertions, full contact details, request bodies, connection strings, or stack traces.

## 11. Privacy contract

Staff-facing applications may display username, display name, masked contact details, effective roles, effective scopes, account/session status, effectivity, last successful login, and safe audit/support references when the viewer is authorized.

They must never display password, password verifier/hash/salt/pepper, reset/activation token, TOTP secret/code/QR payload, optional future recovery-code verifier, session or refresh secret, raw security token/assertion, credential reference, private key, connection string, or unrestricted internal diagnostic. Audit/support responses use opaque correlation and event references.

## 12. Security references

- NIST SP 800-63B-4, Authentication and Authenticator Management: <https://pages.nist.gov/800-63-4/sp800-63b.html>
- OWASP Password Storage Cheat Sheet: <https://cheatsheetseries.owasp.org/cheatsheets/Password_Storage_Cheat_Sheet.html>
- OWASP Session Management Cheat Sheet: <https://cheatsheetseries.owasp.org/cheatsheets/Session_Management_Cheat_Sheet.html>
- RFC 10017, OAuth 2.0 for Browser-Based Applications: <https://www.rfc-editor.org/rfc/rfc10017.html>
- RFC 6238, TOTP: Time-Based One-Time Password Algorithm: <https://www.rfc-editor.org/rfc/rfc6238.html>

These sources guide implementation parameters. ExitPass configuration remains security-owned and must be reviewed at implementation and production-readiness time.

## 13. Explicit exclusions

I-018 does not implement credentials, sessions, login UI, MFA, OIDC, administration APIs, database changes, APT handover, or runtime authorization migration. WebPay remains customer-facing and has no staff login requirement in v1.3.
