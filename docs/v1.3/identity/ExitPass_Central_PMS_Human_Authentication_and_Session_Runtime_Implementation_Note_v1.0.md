# ExitPass Central PMS Human Authentication and Session Runtime Implementation Note v1.0

## 1. Scope

I-020 implements the Central PMS runtime defined by I-018 on the canonical I-019 identity schema. It does not add an application-owned identity store, staff login UI, user/role/scope administration API, shift workflow, or cash-custody workflow.

Central PMS remains authoritative for human credential validation, human-session lifecycle, current authentication assurance, effective permissions, and Site/Site Group scope. Human, service, device, Windows, shift, and custody identities remain separate.

## 2. Runtime architecture

The runtime has four boundaries:

- `HumanAuthenticationService` owns authentication/session policy and safe public outcomes.
- `PostgresHumanAuthenticationRepository` uses the canonical `identity` and `audit` objects created by I-019.
- `HumanSessionAuthenticationHandler` resolves opaque server-side sessions into an authenticated human principal.
- channel-specific HTTP endpoints deliver secure web cookies or APT device-bound opaque session credentials.

Effective authorization is queried from current role, permission, user-role, and user-role-scope records. A client-supplied role, permission array, actor ID, Site, or Site Group is never human authority in Production.

## 3. Local username and password

Local login resolves the canonical normalized username and validates an effective active user and current local credential. Password verifiers use Argon2id through the maintained `Konscious.Security.Cryptography.Argon2` package. The runtime validates algorithm version and bounded work parameters, uses constant-time verifier comparison, and can replace a weaker valid verifier after successful authentication.

Passwords are bounded before hashing, are never persisted or logged, and are not returned. Unknown users, wrong passwords, unusable accounts, and invalid credentials share anti-enumerating public outcomes.

## 4. TOTP

TOTP uses RFC 6238-compatible SHA-1 semantics through `Otp.NET`, with configured digits, step, and bounded clock skew. It is required only for privileged Management Platform accounts. Ordinary Management Platform users, Operator Console users, APT cashiers, and APT supervisors do not require MFA under the v1.3 policy.

Enrollment returns the shared secret and provisioning URI once. Confirmation activates the canonical authenticator and requires a subsequent privileged login. Successful TOTP time steps are recorded atomically so the same step cannot be replayed. Password and TOTP attempts share durable, bounded throttling evidence.

The canonical database stores only an AES-GCM protected envelope plus non-secret key reference/version metadata. The protection key is supplied externally through configuration or a secret provider. It is not stored in source or ordinary database data. TOTP operations fail closed when key protection is unavailable. Enrollment, confirmation, verification, throttling, and reset events contain no code, seed, envelope, or provisioning URI.

TOTP reset is an I-021 administration-service primitive. It moves the authenticator to `RESET_REQUIRED` and revokes active MFA-satisfied sessions in one database transaction.

WebAuthn, FIDO2, and passkeys remain deferred. OIDC remains a disabled provider-neutral adapter boundary and supplies no ExitPass role or scope authority.

## 5. Human sessions

The runtime issues a cryptographically random opaque value containing a public session reference and secret. PostgreSQL stores only the SHA-256 secret hash. Sessions are bound to user, audience, credential version, authorization epoch snapshot, assurance, idle expiry, absolute expiry, and optional device service identity.

Supported audiences are:

- `MANAGEMENT_PLATFORM`
- `OPERATOR_CONSOLE`
- `APT`

Session continuation rotates the opaque credential and revokes the prior session atomically. Password change rotates the credential, increments credential authority, revokes other active sessions, and issues the replacement in one transaction. Activation/password-reset challenges are hash-only, purpose-bound, expiring, consumed once, and applied atomically with credential rotation and session invalidation.

Account suspension, inactivation, retirement, credential-version change, idle expiry, absolute expiry, explicit logout, global logout, and administrative revocation fail closed. Current authorization is re-read on every current-session response and protected RBAC operation; role or scope changes therefore take effect without trusting login-time client state.

Final timeout, concurrency, and reset-delivery policies remain configuration or follow-up decisions under DR-05, DR-06, and DR-07. The schema, service, and endpoints do not hard-code them as immutable policy.

## 6. Web session and CSRF posture

Management Platform and Operator Console receive a host-only `Secure`, `HttpOnly`, `SameSite=Strict` session cookie with no `Domain` attribute. Authentication and session responses use `Cache-Control: no-store, private`, `Pragma: no-cache`, and `X-Content-Type-Options: nosniff`.

State-changing web requests require ASP.NET Core antiforgery validation. Login and current-session responses expose the paired request token in `X-CSRF-Token`; the token is not bearer authentication authority and must remain in application memory rather than browser persistent storage. Allowed web origins are configured explicitly. No refresh or bearer token is placed in browser storage.

## 7. APT posture

APT login is accepted only after the existing trusted service/mTLS boundary supplies an active device service identity assigned to the Site. The resulting human session is audience-bound to `APT` and device-bound. Wrong-device and cross-audience reuse fail closed.

Device trust is not human MFA. The APT cannot perform offline human login, and the human session remains independent of cashier shift and cash-custody state. J-008 owns desktop consumption and shift/custody integration.

## 8. API surface

Web routes:

- `POST /v1/human-authentication/login`
- `GET /v1/human-authentication/session`
- `POST /v1/human-authentication/session/continue`
- `POST /v1/human-authentication/logout`
- `POST /v1/human-authentication/logout-all`
- `POST /v1/human-authentication/reauthenticate`
- `POST /v1/human-authentication/password/change`
- `POST /v1/human-authentication/password-reset-requests`
- `POST /v1/human-authentication/password-resets`
- `POST /v1/human-authentication/activations`
- `POST /v1/human-authentication/totp/enrollment`
- `POST /v1/human-authentication/totp/enrollment/confirm`

APT service routes:

- `POST /v1/apt/human-sessions`
- `GET /v1/apt/human-sessions/{sessionReference}`
- `POST /v1/apt/human-sessions/{sessionReference}/continue`
- `POST /v1/apt/human-sessions/{sessionReference}/reauthenticate`
- `POST /v1/apt/human-sessions/{sessionReference}/logout`

Current-session readback exposes safe user/session references, username, display name, audience, assurance, MFA posture, current permissions, current Site/Site Group/global scope, expiry, device reference where applicable, and correlation reference. It exposes no password, verifier, TOTP material, opaque web session credential, provider token, or database detail.

## 9. Production fixture-header posture

Production rejects development human-authority headers including `X-ExitPass-User-Id`, `X-Operator-User-Id`, and permission headers. The RBAC middleware accepts fixture human identity and permissions only when both an explicit Development/SecureDevelopment/Test environment and fixture option are enabled. Authenticated Production actor identity comes from the resolved human session. Existing service identity remains a separate mTLS-governed authority.

## 10. Audit and health

Authentication attempts are recorded in `identity.authentication_attempts`. Privacy-safe authentication/session events create linked `audit.audit_events` and `audit.security_events` records using canonical I-019 controlled codes. Event payloads contain bounded classifications, opaque references, actor/service IDs, privacy-safe source hashes, and correlation IDs only.

Readiness checks the canonical authentication tables and TOTP protection configuration without identifying users, credentials, authenticators, or key material.

## 11. Configuration dependencies

Production configuration must provide:

- canonical PostgreSQL connectivity;
- a non-source TOTP protection key, key reference, and version through an approved secret mechanism;
- explicit trusted web origins;
- approved session, lockout, and Argon2id parameters;
- existing mTLS/service identity configuration for APT;
- a governed challenge-delivery adapter before activation/password-reset delivery is enabled.

The built-in challenge delivery and OIDC adapters remain disabled. No email/SMS reset channel or OIDC provider is silently activated.

## 12. Validation

Focused validation uses a unique loopback-only PostgreSQL 16 container. Each run creates a new disposable database, applies the current canonical full generated DDL, runs the canonical Central PMS alignment validator, executes the tests, and drops the database.

Covered behavior includes Argon2id success/failure/rehash, anti-enumeration, account lifecycle, durable lockout/release, privileged TOTP requirement/replay/throttling/enrollment/confirmation/reset, ordinary channel MFA policy, APT device binding, session issue/hash-only persistence/restart/rotation/expiry/revocation, live authorization readback, credential invalidation, one-time password reset, linked audit/security events, secure cookie attributes, antiforgery rejection/success, and Production fixture-header rejection.

Final focused results:

- Release Central PMS API build: passed with 0 warnings and 0 errors.
- Human-authentication and Production fixture-header unit/security tests: 25 passed, 0 failed, 0 skipped.
- Canonical PostgreSQL repository and API integration tests: 9 passed, 0 failed, 0 skipped.
- Application `git diff --check`: passed.

The complete Central PMS unit command produced 1,624 passed and 48 failed tests on both the I-020 feature and a pristine `origin/dev` worktree. Failed test names and normalized causes were identical: one pre-existing PayMongo case, one fiscal retry null-reference case, and the existing Controlled-UAT fiscal harness/invocation/void families. I-020 introduced no unit regression.

The complete integration command produced 718 passed, 110 failed, and 2 skipped tests on the feature, compared with 713 passed, 106 failed, and 2 skipped on pristine `origin/dev`. The feature adds nine I-020 tests. Four unrelated payment/fiscal tests failed only in the shared aggregate fixture and all four passed when rerun by the exact test name against a fresh canonical disposable database. This classifies the delta as shared-fixture ordering/interference, not an I-020 product regression.

An actual HTTPS Production host proof activated a synthetic disposable user through the public activation route, logged in through `POST /v1/human-authentication/login`, and read the current session with status 200. The API process was then force-terminated. A newly started Production process resolved the same durable opaque session with status 200 and the `MANAGEMENT_PLATFORM` audience. A fixture human-authority header was rejected with status 400 in Production.

The hosted canonical database contained five proof session rows and all five stored only valid SHA-256 session-secret hashes. The identity schema contained zero prohibited raw password, session-secret, bearer/refresh-token, TOTP-code/seed, provisioning, or provider-token columns. Production proof logs contained no runtime password, activation challenge, session/TOTP secret, authorization material, or connection string. No production credential or TOTP protection key was written to source.

## 13. Follow-up boundary

I-021 owns governed user/role/scope administration, privileged access decisions, administrative session enumeration/revocation endpoints, MFA status/reset/removal endpoints, and access review. I-020 supplies the authenticated principal, current-session authority, MFA administration primitive, and revocation primitives needed by I-021.

H-006, H-008, and J-008 own application login consumers. I-022 remains the cross-application integration and Controlled-UAT authentication gate. Controlled UAT and production rollout remain unauthorized.
