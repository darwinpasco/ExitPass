# ExitPass Human Authentication Runtime Implementation Plan v1.0

## 1. Objective

Deliver one Central PMS-owned human identity, authentication, session, authorization, and scope architecture before Controlled UAT. This plan does not authorize I-018 to implement runtime code or schema.

## 2. Dependency sequence

| Task | Deliverable | Depends on | May run in parallel |
|---|---|---|---|
| I-019 | Canonical human credential, TOTP authenticator, external binding, session, attempt/reset, scoped-grant, privileged-approval foundation | I-018 decisions | Schema design streams may be reviewed together; merge first |
| I-020 | Central PMS authentication/session runtime, local password and TOTP providers, OIDC adapter contract, revocation, current-session, fresh reauthentication, audit | I-019 | After I-019 merge; provider adapters can be parallel substreams behind frozen interface |
| I-021 | Central PMS governed user/role/scope/session administration APIs | I-019; shared I-020 principal/session middleware | Can start after I-019 while I-020 stabilizes, but merge after shared auth boundary |
| H-006 | Management Platform login/current-session/logout consumer | I-020 | Parallel with H-008/J-008 after I-020 contract |
| H-007 | Management Platform user/role/scope/session administration UI | I-021 and H-006 | UI read-only scaffolding may start after I-021 contract; no authoritative mutation before API |
| H-008 | Operator Console login and authenticated reviewer/operator identity | I-020 | Parallel with H-006/J-008; must remove production header fixtures |
| J-008 | APT cashier login plus shift/custody integration | I-020 and approved custody policies | Parallel with H-006/H-008; depends on desktop secure storage and Central PMS APT session API |
| I-022 | Cross-application authentication/RBAC/scope integration proof | I-020, I-021, H-006, H-007, H-008, J-008 | Final integration gate only |

DB-first ordering is mandatory. Application tasks must not add shadow identity tables.

## 3. I-019 acceptance

I-019 must:

- normalize and uniquely constrain local login identifiers;
- add local credential verifiers and external issuer/subject bindings;
- add the minimum privileged-administrator TOTP authenticator persistence: user/authenticator identity, controlled authenticator type, protected-secret ciphertext or approved credential reference, protection-key version, status, enrollment/verification metadata, reset/revocation actor and reason, timestamps, and row version;
- add durable opaque human sessions and revocation/version posture;
- add login/TOTP verification-attempt throttling and one-time activation/reset challenge records;
- add assignment-scoped Site/Site Group/explicit-global grants;
- bind privileged approval to assignments/grants;
- load controlled authentication/session/TOTP enrollment, verification, reset/removal, and administration events and permissions;
- preserve human/service separation;
- prove clean rebuild, upgrade, replay/drift, FKs, uniqueness, effectivity, revocation, concurrency, wrong-family rejection, and no secret/raw token columns.

The TOTP secret does not belong in `identity.users` or generic JSON. It must be encrypted with key protection and access control separate from ordinary database-data access, or represented by an approved protected credential reference. Recovery codes are not required for v1.3; if later approved, only one-way verifiers are stored.

Security review must approve algorithm/provider metadata, encryption/key management, and retention. The schema stores no passwords, plaintext TOTP secrets, OTP codes, QR payloads, raw session/reset secrets, OIDC assertions, arbitrary authorization JSON, or provider access tokens in plaintext.

## 4. I-020 acceptance

I-020 must implement:

- provider-neutral authenticator interface;
- Argon2id local verification and rehash-on-login;
- standards-compatible TOTP enrollment and verification for privileged Management Platform administrators;
- governed TOTP reset/removal that invalidates the old authenticator, protects enrollment material, and never lets an administrator retrieve a user's secret;
- failed TOTP throttling, successful-code replay prevention, bounded clock handling, and privacy-safe events;
- session assurance indicating whether the current account's required TOTP MFA was satisfied, without exposing secret or code material;
- OIDC BFF adapter contract, with provider disabled until configured;
- login anti-enumeration, rate limiting, lockout, and durable events;
- secure web cookies, CSRF, session rotation, current-session readback, idle/absolute expiry;
- APT device-bound human session handled outside React;
- logout, global logout, password change/reset, forced session revoke;
- account/status/effectivity and credential/provider invalidation;
- authorization epoch, selected-operation fresh reauthentication, and sensitive-request authorization/scope re-evaluation;
- production rejection of user/permission/scope fixture headers;
- service-plus-human attribution propagation;
- health/readiness that never leaks provider or credential detail;
- WebAuthn, FIDO2, passkey, security-key, and hardware-token runtime are deferred future enhancements and are not I-020 requirements.

Proof requires PostgreSQL 16, Production-hosted Central PMS, local login, optional disposable OIDC provider when implemented, restart/revocation/expiry/concurrency, browser cookie/CSRF posture, APT device binding, no offline login, privacy scans, and cleanup.

## 5. I-021 acceptance

I-021 must implement SELECT and mutation APIs for:

- user list/detail/create/invite/profile/lifecycle;
- lock/unlock and reset challenge;
- roles/permissions and controlled role-permission bindings;
- user-role assignment/revocation;
- assignment-scoped Site/Site Group/global grants/revocation;
- privileged request/independent approval;
- access reviews;
- safe session enumeration/revocation;
- privacy-safe identity audit history.
- safe TOTP status/readiness and governed privileged-administrator enrollment/reset/removal operations with no secret readback.

Tests must prove no self-escalation/self-unlock, assignment ceiling, last-active-admin protection, separation of duties, optimistic concurrency, idempotency/conflict, effectivity, immediate revocation, anti-enumeration, atomic audit, no secret DTOs, and Site/Site Group scope.

## 6. Consumer acceptance

### H-006

- same-origin login/current session/logout;
- username/password for ordinary users and username/password plus TOTP for privileged administrators;
- no bearer/refresh token in browser storage;
- safe expiry/revocation handling;
- server-owned permissions/scopes;
- no production development principal.

### H-007

- complete bounded user/role/scope/session administration UI;
- safe privileged-administrator TOTP status, enrollment, and reset/removal ceremonies with no post-enrollment secret display;
- privileged approval and access-review workflows;
- version conflict and safe-error handling;
- no client-authored actor, role, permission, or scope authority.

### H-008

- Operator Console authenticated workspace;
- username/password login with no mandatory v1.3 MFA;
- current reviewer/operator `user_id` from session;
- Site/Site Group/device/shift revalidation;
- fresh password reauthentication only for a separately designated high-risk operation; ordinary statutory review does not require TOTP merely because it is controlled;
- production removal of identity/permission header authority;
- approve/reject and evidence-review regression.

### J-008

- device identity then human login;
- cashier and supervisor username/password login with no mandatory v1.3 MFA;
- opaque human session outside React/SQLite;
- own-shift/custody open and resume;
- no cross-cashier custody inheritance;
- normal logout blocked with open custody;
- expiry/revocation blocks new cash while preserving recovery evidence;
- governed supervisor handover with the supervisor's fresh password authentication, current permission/scope, and audit;
- no offline login;
- cash/fiscal/statutory readiness regressions.

## 7. I-022 integration proof

I-022 is the pre-UAT authentication gate. It must prove:

1. local login across all three staff applications;
2. configured OIDC path if included in the target deployment;
3. privileged Management Platform username/password plus TOTP MFA and no mandatory MFA for ordinary Management Platform, Operator Console, or APT accounts;
4. one canonical user and consistent actor attribution;
5. role/permission/scope effectivity and immediate revocation;
6. cross-Site/Site Group anti-enumeration;
7. server-side sensitive-action revalidation;
8. logout, global revoke, password reset, disablement, idle/absolute expiry, restart, and concurrency;
9. Operator Console reviewer attribution;
10. APT device/human/shift/custody separation and expiry behavior;
11. POS/fiscal actor reference propagation without credential delegation;
12. privacy, security, audit, rollback, and disaster-recovery proof;
13. complete cleanup and no protected-database use.

## 8. Decision gates before runtime completion

### 8.1 Approved decision

`DR-04 - MFA policy` is **APPROVED** by Darwin:

- privileged Management Platform administrators require username/password plus TOTP;
- ordinary Management Platform, Operator Console, and APT users do not require MFA in the v1.3 baseline;
- selected sensitive supervisor operations require fresh authentication, current authorization/scope, and audit, not universal MFA;
- WebAuthn, FIDO2, passkeys, security keys, and hardware-token distribution are deferred future enhancements;
- a configured OIDC provider may enforce its own MFA, but Central PMS retains authorization and scope authority.

### 8.2 Unresolved decisions

Darwin approval is still required for the following. Recommended defaults are fail-safe so schema/interface work can proceed.

| ID | Decision | Options | Recommendation | Default without approval | Blocks |
|---|---|---|---|---|---|
| DR-01 | Primary model | Local only; OIDC only; pluggable | Pluggable, local baseline | Local provider plus disabled OIDC adapter | Final I-020 architecture approval |
| DR-02 | Local credentials | Required; optional; prohibited | Required for v1.3 APT/control environments | Required | I-019 verifier and I-020 local login |
| DR-03 | OIDC | Required now; optional; deferred | Optional provider-neutral support | Disabled until customer configuration | Provider rollout, not local deployment |
| DR-05 | Reset | Admin temp password; verified self-service; challenge | Hashed one-time challenge, self-service when verified | Admin-issued challenge only | I-019/I-020 reset flow |
| DR-06 | Timeouts | application-specific choices | MP/OC 15m idle/8h absolute; APT 15m/12h cap | Recommended values | Configuration sign-off |
| DR-07 | Concurrency | One; bounded; unlimited | Three web; one user-terminal APT | One APT, three web | Session policy tests |
| DR-08 | APT logout with custody | Auto-close; allow; deny/handover | Deny normal logout; never auto-close | Deny | J-008 flow |
| DR-09 | APT expiry with custody | Continue; auto-close; lock/recover | Lock new cash; preserve custody; reauth/handover | Lock | J-008 flow |
| DR-10 | Privileged assignment | Single admin; two-person; external | Two-person durable approval | Deny activation | I-021 privileged grants |
| DR-11 | Global scope | Broad; role allowlist; none | Explicit central role allowlist | None | Global operations only |

Implications:

- Option C adds implementation effort but prevents an authorization redesign when enterprise SSO is introduced.
- Privileged TOTP MFA and two-person grants add bounded administrative friction and materially reduce account-takeover/self-escalation risk without imposing MFA on routine operator or cashier work.
- APT lock-on-expiry can interrupt operations but is required to avoid unauthenticated cash acceptance; governed recovery handles open custody.
- Bounded concurrency improves usability while preserving revocation and anomaly detection.

## 9. Controlled UAT gate

Authentication and user administration are pre-UAT P0 work. Controlled UAT is not authorized until I-019, I-020, I-021, required consumer tasks, and I-022 are merged and their environment-specific decisions are approved. At minimum the target UAT deployment must have:

- real staff login with no fixture headers/principals;
- privileged Management Platform username/password plus TOTP before privileged administration;
- server sessions and revocation;
- governed users/roles/scopes;
- current Site/Site Group enforcement;
- authoritative Operator reviewer and APT cashier attribution;
- username/password login without mandatory MFA for ordinary Management Platform, Operator Console, APT cashier, or APT supervisor accounts;
- fresh password reauthentication for designated APT supervisor handover/takeover operations;
- APT expiry/custody protections;
- audit/security events and operational recovery;
- privacy/security validation and support runbooks.

Because WebAuthn, FIDO2, passkeys, and security keys are deferred future enhancements, Controlled UAT does not require them. It also does not require cashier MFA, supervisor MFA, or Operator Console MFA. Controlled UAT remains unauthorized until the listed runtime and consumer foundations merge.

## 10. Safe parallelization

After I-019 merges, I-020 authentication and I-021 administration service development may overlap behind one shared principal/session contract, but I-021 cannot expose mutations without I-020 enforcement. After I-020's contract is stable, H-006, H-008, and J-008 can run in parallel. H-007 can build against I-021 contracts in parallel with later I-021 validation. I-022 begins only after all runtime and consumer branches are integrated on latest development baselines.
