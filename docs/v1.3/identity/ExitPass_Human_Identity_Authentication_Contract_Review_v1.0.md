# ExitPass Human Identity and Authentication Contract Review v1.0

## 1. Review outcome

The contract package is coherent and implementation-ready subject to the explicit Darwin policy approvals. The current platform foundation is **partial**: canonical user/role/permission records and audit envelopes exist, but human credentials, sessions, general Site/Site Group grants, production authentication APIs, and governed user-administration APIs do not.

Recommended architecture: pluggable authentication with required ExitPass local credentials for v1.3 controlled environments/APT users, optional OIDC bindings for enterprise SSO, opaque server-side sessions, and one Central PMS authorization/scope model. Darwin approved a proportionate MFA boundary: TOTP is required only for privileged Management Platform administrators; ordinary Management Platform, Operator Console, and APT accounts use username/password in the v1.3 baseline.

## 2. Repository audit inventory

| Repository | Evidence inspected | Finding |
|---|---|---|
| ExitPass / Central PMS | API composition, RBAC middleware/repository/catalog, Operator identity context, review endpoints, Session Service skeleton, Operator UI fixtures | Permission evaluator exists; production human authentication/session/admin APIs do not |
| Canonical database | identity, operator_console, audit, Site, and evidence-specific scope objects | Human/RBAC baseline exists; credential/session/general scope gaps confirmed |
| Management Platform | auth state, API boundaries, RBAC implementation/audit docs | Development principal only; browser correctly avoids durable authority storage |
| ExitPass-APT integration | architecture/docs | No complete human authentication runtime |
| Windows APT | config/terminal context, encrypted local journal, shift/custody entities/services, security docs | Cashier/shift references are fixtures; shift and custody are durably separate; no login/session authority |
| POS Server | bounded fiscal/admin actor-reference surface | Accepts privacy-safe actor references; remains service-authenticated and must not validate humans |

The protected stash `stash@{0}: On dev: WIP assisted payment terminal Mode 1 assessment` was observed and not applied, popped, dropped, renamed, recreated, or modified.

## 3. Core-question disposition

| # | Question | Frozen answer |
|---:|---|---|
| 1 | Canonical human identity | Immutable `identity.users.user_id` |
| 2 | Login identifier | Normalized username is primary; verified unique email may be optional alias |
| 3 | Username case | Case-insensitive comparison against server-generated normalized value |
| 4 | Email posture | Profile/recovery by default; login only when verified/unique/enabled |
| 5 | Credential storage | Dedicated restricted canonical credential table, separate from users |
| 6 | PostgreSQL or IdP refs | Local one-way verifiers in PostgreSQL; external providers store issuer/subject binding, never IdP password |
| 7 | Mechanism | Pluggable local plus optional OIDC, one Central PMS authorization model |
| 8 | Session model | Durable opaque server-side session, application/audience bound |
| 9 | Logout/revocation | Durable server revoke before client clear; global/admin revocation supported |
| 10 | Expiry | Server-enforced idle and absolute expiry; client clocks are presentation only |
| 11 | User status | Only active/effective users; all other states fail closed |
| 12 | Lockout trigger | Configured rolling local-login failures and security action |
| 13 | Throttling | Account and source adaptive throttling with durable attempt evidence |
| 14 | Password change | Current/recent authentication, rotate version, revoke other sessions |
| 15 | Password reset | Hashed, short-lived, single-use challenge; no admin-set/read password |
| 16 | First login | Activation/first login requires credential change; privileged Management Platform administrators must enroll TOTP before privileged operation |
| 17 | Role assignment | Management Platform request; Central PMS validates/persists/audits |
| 18 | Site scopes | Explicit assignment-scoped Site grants |
| 19 | Site Group scopes | Explicit assignment-scoped group grants; membership server-resolved |
| 20 | Global scope | Explicit privileged grant for eligible central roles only |
| 21 | Effectivity | Evaluated at login and every sensitive operation |
| 22 | Revocation | Retained history; authorization epoch makes it effective immediately |
| 23 | Role-permission audit | Atomic actor/reason/before-after audit plus binding lifecycle |
| 24 | User-role audit | Atomic actor/reason/effectivity/approval/revocation/review audit |
| 25 | Human actor propagation | Session-derived `user_id` plus separate service identity and correlation |
| 26 | Operator reviewer | Session-derived reviewer in decision, evidence access, access evaluation, audit |
| 27 | APT cashier | Session-derived cashier bound to device, Site, shift, custody, transaction audit |
| 28 | MP administrator | Session-derived actor on every administrative mutation |
| 29 | Service separation | `service_identities` authenticates machines; never grants human identity |
| 30 | Session expiry in workflow | Lock/fail next call; reauth then re-resolve; no silent mutation replay |
| 31 | Mid-session revoke | Immediate sensitive-operation re-evaluation; reduce or revoke session |
| 32 | Sensitive authorization | Always reevaluated server-side; selected high-risk operations also require fresh authentication |
| 33 | Client persistence | Safe presentation state only; web session in HttpOnly cookie |
| 34 | Browser prohibition | No credentials/tokens/secrets/permission or scope authority in durable browser storage |
| 35 | APT prohibition | No credentials, tokens, continuation secrets, or permission authority in SQLite/React config |
| 36 | APT after expiry | No new cash/payment authority; preserve recovery evidence only |
| 37 | Offline cashier login | Prohibited in v1.3 |
| 38 | Login and shift | Login authenticates; authorized user separately opens/resumes a shift |
| 39 | Login and custody | Authorized shifted cashier separately opens/resumes custody |
| 40 | Custody inheritance | Prohibited; only governed supervisor handover/takeover |
| 41 | Reauthentication | Password/TOTP administration, privileged role/scope approval, another user's session revocation, supervisor takeover/custody handover, and other explicitly designated high-risk actions |
| 42 | Events | Full list in main contract; use audit/security envelopes and attempt ledger |
| 43 | User administration UI | Management Platform |
| 44 | Role administration UI | Management Platform |
| 45 | Authentication UI | Each staff consumer; shared Central PMS contract |
| 46 | Credential validation | Central PMS authentication runtime/provider adapters |
| 47 | Authorization | Central PMS |
| 48 | Session revocation | Central PMS |
| 49 | APIs | Authentication/session plus Management Platform administration and APT human-session routes |
| 50 | DB changes | I-019 credential/TOTP authenticator/binding/session/attempt/reset/scope/approval foundation |

## 4. Material decision records

### DR-01 Primary model

- **Options:** local only; external OIDC only; pluggable local plus OIDC.
- **Recommendation:** pluggable, with local required baseline and OIDC optional.
- **Implication:** more provider abstraction work, no future authorization redesign, supports cashiers without enterprise accounts.
- **Default:** local enabled; OIDC adapter disabled until configured.
- **Approval/runtime:** Darwin approval required; I-019 interfaces can proceed, I-020 final architecture sign-off depends on it.

### DR-02 Local credentials

- **Options:** required; optional; prohibited.
- **Recommendation:** required for v1.3 controlled/APT environments.
- **Implication:** ExitPass owns verifier security, reset, throttling, lockout, and recovery.
- **Default:** required, fail closed if unavailable.
- **Approval/runtime:** I-019/I-020 local path blocked if rejected.

### DR-03 External OIDC

- **Options:** required now; optional provider; deferred completely.
- **Recommendation:** provider-neutral support and bindings, optional deployment activation.
- **Implication:** enterprise SSO/MFA path without IdP-group authority.
- **Default:** disabled.
- **Approval/runtime:** local deployment not blocked; enterprise provider rollout blocked until approved/configured.

### DR-04 MFA

- **Status:** **APPROVED by Darwin; prior WebAuthn/FIDO2-first posture superseded and deferred as a future enhancement.**
- **Decision:** privileged Management Platform administrators require username/password plus standards-compatible TOTP MFA.
- **Ordinary users:** ordinary Management Platform, Operator Console, APT cashier, and APT supervisor accounts do not require MFA in the v1.3 baseline.
- **Sensitive supervisor actions:** require the supervisor's fresh username/password authentication, current permission, current Site/Site Group scope, accountable identity, and audit where policy designates reauthentication; this is not universal MFA.
- **OIDC:** a configured external provider may enforce its own MFA and ExitPass may consume approved assurance mappings, but provider groups/MFA do not become ExitPass authorization or scope.
- **Deferred:** WebAuthn, FIDO2, passkeys, Windows Hello-based passkeys, security/hardware keys, and passkey recovery are future security enhancements, not v1.3 MVP, Controlled UAT, or initial-production requirements.
- **Implication:** protects privileged administration while avoiding cashier hardware distribution, routine operator MFA prompts, and deferred future passkey enrollment/recovery infrastructure.
- **Runtime:** privileged Management Platform Controlled UAT remains blocked until TOTP persistence, enrollment, verification, session assurance, reset/removal, throttling, and audit are implemented.

### DR-05 Reset

- **Options:** administrator-set temporary password; verified self-service; single-use challenge.
- **Recommendation:** single-use challenge, self-service through verified channel when available, otherwise admin-issued challenge.
- **Implication:** administrators never know user passwords; needs notification/recovery operations.
- **Default:** admin-issued challenge only.
- **Approval/runtime:** reset runtime depends on chosen recovery channels.

### DR-06 Session timeouts

- **Options:** common values; application-specific; shift-length sessions.
- **Recommendation:** MP/OC 15-minute idle and 8-hour absolute; APT 15-minute idle and 12-hour absolute cap, never an authority to continue cash after expiry.
- **Implication:** security/usability tradeoff; APT recovery path mandatory.
- **Default:** recommended values.
- **Approval/runtime:** configuration can be implemented; production values need sign-off.

### DR-07 Concurrent sessions

- **Options:** single; bounded; unlimited.
- **Recommendation:** up to three web sessions; one active APT user-terminal session and one cashier per terminal.
- **Implication:** reasonable web usability; prevents terminal/custody ambiguity.
- **Default:** recommendation.
- **Approval/runtime:** concurrency tests require approved limits.

### DR-08 APT logout with custody

- **Options:** auto-close; allow logout leaving custody; deny normal logout/handover.
- **Recommendation:** deny normal logout; never auto-close; governed supervisor handover.
- **Implication:** operational friction preserves physical accountability.
- **Default:** deny.
- **Approval/runtime:** J-008 behavior depends on approval.

### DR-09 APT expiry during custody

- **Options:** allow continued cash; auto-close; lock and recover.
- **Recommendation:** lock new payment/cash authority, keep custody open, require same-user reauth or supervisor handover.
- **Implication:** safe interruption with durable recovery.
- **Default:** lock.
- **Approval/runtime:** J-008 behavior depends on approval.

### DR-10 Privileged assignment approval

- **Options:** one administrator; two-person; external ticket approval.
- **Recommendation:** durable two-person approval inside ExitPass, optionally linked to external ticket.
- **Implication:** prevents self-escalation; adds administrative lead time.
- **Default:** privileged assignment remains pending/denied.
- **Approval/runtime:** I-021 privileged activation blocked until approved.

### DR-11 Global-scope eligibility

- **Options:** any administrator; central role allowlist; no global scope.
- **Recommendation:** explicit allowlist for central security/operations roles with two-person approval.
- **Implication:** preserves Site least privilege.
- **Default:** no global grants.
- **Approval/runtime:** Site/Site Group runtime proceeds; global operations blocked.

## 5. Required v1.3 safeguards and deferrals

Required: no self-escalation/self-unlock, last-admin protection, explicit role/scope effectivity, two-person privileged assignment, independent approval, server-side scope resolution, immediate sensitive revocation, privileged Management Platform TOTP MFA, secure sessions/CSRF, anti-enumerating login/reset, no offline APT login, no cross-cashier custody inheritance, full audit attribution, and no client authority.

Deferred: WebAuthn, FIDO2, passkeys, security/hardware keys, recovery codes, mandatory MFA for ordinary staff/cashiers/supervisors, SCIM/HR auto-provisioning, IdP-group role automation, just-in-time elevation, generalized break-glass, offline login, passwordless-only deployment, and automated access-review campaigns beyond the minimum governed review records. This is an approved risk-based product decision, not an accidental omission of MFA.

## 6. Controlled UAT decision

**Not ready and not authorized.** The current system does not have production human authentication, sessions, general scoped grants, administration APIs, or privileged-administrator TOTP. Controlled UAT requires I-019 through I-022 and the required H/J consumer tasks. Before Controlled UAT exercises privileged Management Platform administration, real username/password plus TOTP login, durable human sessions/revocation, and server-authoritative permission and Site/Site Group scope must be active. Because WebAuthn, FIDO2, and passkeys are deferred future enhancements, Controlled UAT does not require them. Controlled UAT also does not require Operator Console MFA, APT cashier MFA, or APT supervisor MFA.

## 7. Runtime authorization decision

Runtime implementation is **partially authorized** by the frozen technical contract: DB/interface design and fail-safe defaults can proceed. DR-04 is approved. Other unresolved material production/UAT policy values in DR-01 through DR-03 and DR-05 through DR-11 retain their existing approval requirements.

## 8. Review checklist

- [x] One canonical human identity selected.
- [x] Authentication, authorization, and scope separated.
- [x] Local and OIDC paths compared without assuming an IdP.
- [x] Central PMS authority preserved.
- [x] Human/service identity separated.
- [x] Session, revocation, expiry, and client storage frozen.
- [x] Management Platform ownership frozen.
- [x] Operator reviewer attribution frozen.
- [x] APT human/device/shift/custody separation frozen.
- [x] Schema and API gaps classified from source.
- [x] Privacy and audit event posture frozen.
- [x] Follow-up sequence and safe parallel work identified.
- [x] Darwin approved DR-04's proportional TOTP MFA policy.
- [ ] Darwin approves the remaining material decision records.
- [ ] Runtime/database tasks implemented and validated.
- [ ] Controlled UAT authorized in a later task.
