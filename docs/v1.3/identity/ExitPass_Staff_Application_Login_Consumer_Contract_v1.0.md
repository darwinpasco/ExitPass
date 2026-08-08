# ExitPass Staff Application Login Consumer Contract v1.0

## 1. Common consumer rules

All staff applications consume `exitpass-human-identity-authentication:v1`. Central PMS authenticates or validates the human session and resolves current authorization/scope. A consumer may render effective permissions and locations but cannot author them.

Common lifecycle:

`unauthenticated -> authenticate -> active session -> resolve current user/permissions/scopes -> operate -> revalidate sensitive action -> logout, expiry, or revocation`

On `401`, the consumer locks the protected workspace and requires login/continuation. On `403`, it removes or disables the denied action without claiming the resource exists. An open workflow is not grandfathered: after reauthentication the server re-resolves the resource, row version, permission, and scope. The client must not silently replay a sensitive mutation.

## 2. Application capability matrix

| Application | Human login | Allowed user types | Baseline capability | Site / Site Group | Global | Session | Sensitive actions | Audit actor | Expiry behavior |
|---|---|---|---|---|---|---|---|---|---|
| Management Platform | Required; privileged administrators add TOTP | `INTERNAL_ADMIN`, approved support/finance/compliance/security users | App access plus operation-specific admin/read permission | Required for scoped administration and views | Explicit eligible central roles only | Secure same-origin web session | User/role/scope changes, policy/config changes, session revocation | Authenticated `user_id` plus service identity | Lock UI; deny mutation; reauthenticate and re-resolve |
| Operator Console | Username/password; no mandatory v1.3 MFA | `OPERATIONS_USER`, `SITE_OPERATOR`, approved reviewers/supervisors | Operator Console access plus workflow-specific permission | Required; shift/device constraints may narrow | Exceptional central review roles only | Secure same-origin web session | Approval/rejection, evidence preview/review, void, takeover | Authenticated reviewer/operator `user_id` | Lock UI; deny decisions; preserve draft only as non-authoritative client state |
| Windows APT | Trusted device/service identity, then cashier username/password; no mandatory v1.3 MFA | `SITE_OPERATOR` is the baseline cashier role; supervisors receive no cashier authority by role name | `apt.access`, own-shift `cashier-shifts.operate`, own-custody `cash-custody.operate`, and `terminal-cash.receive`; payable-basis read is separate | Required and terminal-bound; GLOBAL is prohibited | Not eligible for GLOBAL operation | Device-bound opaque desktop session | Own shift/custody operation and cash receipt; handover remains deferred | Authenticated cashier `user_id` and device service identity | Block new cash; retain custody recovery state; require online reauthentication or a separately approved future recovery workflow |
| WebPay | Not applicable | Customer channel, not staff identity | Existing customer-safe transaction contract | Server-derived transaction scope | Not applicable | Existing customer/channel flow | No staff administration | Existing service/customer-safe attribution | No staff login introduced |

## 3. Management Platform

### 3.1 Login flow

1. Browser loads an unauthenticated shell from the same origin.
2. User submits username/password to the same-origin authentication endpoint, or selects configured OIDC login.
3. Server validates credentials/provider response, ExitPass user status, effectivity, and application eligibility.
4. For an ExitPass-local privileged Management Platform administrator, the server requires a TOTP challenge and creates the authenticated session only after password and TOTP verification succeed. Ordinary Management Platform users do not receive a mandatory TOTP challenge.
5. For configured OIDC, the provider may enforce MFA and Central PMS accepts only an approved provider-assurance mapping; provider MFA/groups do not grant ExitPass authorization or scope.
6. Server creates/rotates an opaque Management Platform session cookie.
7. Browser calls current-session readback and receives safe profile, effective capabilities, bounded scopes, expiry/freshness, MFA-satisfied status where relevant, and support reference.
8. Every API mutation enforces its named policy and canonical target scope server-side.

The local flows are explicitly:

- ordinary user: `username -> password -> authenticated session -> server authorization/scope`;
- privileged administrator: `username -> password -> TOTP challenge -> authenticated session -> server authorization/scope`.

The current injected `createDevelopmentAuthState` may remain only in explicit local/test composition. Production startup must fail closed if real authentication is unavailable. The browser sends no `X-ExitPass-User-Id`, permission, role, service-secret, or Site-authority headers.

### 3.2 Administration flow

Management Platform owns user/role/scope administration screens, but Central PMS owns validation and persistence. UI permission state controls presentation only. The server derives the administrative actor, enforces privilege ceilings and separation of duties, and returns optimistic-concurrency conflicts.

Logout revokes the server session, clears the cookie, and clears sensitive browser state. Browser refresh rediscovers the session from the server. Browser restart ends a non-persistent session by default.

## 4. Operator Console

### 4.1 Login and workspace

1. Unauthenticated Operator Console shows login only.
2. User supplies username/password. Central PMS authenticates the human and creates an Operator Console-audience session; no mandatory v1.3 TOTP challenge applies to ordinary operators or supervisors.
3. The workspace resolves current permissions, Site/Site Group grants, device binding, and active operator shift where the workflow requires them.
4. Queue/detail reads and each sensitive operation independently re-evaluate scope and lifecycle.
5. Audit records use the authenticated `identity.users.user_id`.

Current `X-Operator-User-Id`, `X-ExitPass-User-Id`, permission arrays, Site headers, and request-body user IDs are test fixtures, not production authority. Production middleware must reject or ignore them as authority. `OperatorConsoleIdentityContext` must resolve actor and scope from the authenticated principal and server records.

### 4.2 Reviewer attribution

For statutory approval, rejection, and evidence review:

- `reviewer_user_id`, decision actor, evidence-access actor, and audit actor come from the current session;
- the statutory request/decision/evidence set/item, Site, Site Group, shift/device requirements, and reviewer permission are revalidated together;
- a body `ReviewerUserId`, if retained for contract compatibility, must equal the authenticated actor and is not authority; the preferred mutation contract omits it;
- the access-evaluation ID records the exact server decision used;
- idempotent replay cannot change reviewer identity;
- approval/rejection does not imply payable-basis application authority.

Operator Console does not administer users. It may expose a bounded self-session/logout surface and governed supervisor workflows only.

Ordinary statutory approval, rejection, or evidence review does not require TOTP merely because it is controlled. Central PMS permission, Site/Site Group scope, lifecycle, and actor attribution remain mandatory. A separately designated high-risk operation may require fresh password authentication.

## 5. Assisted Payment Terminal

### 5.1 Startup and login

1. Desktop host establishes terminal device/service trust separately.
2. React shell displays safe terminal readiness and login state but receives no service secret.
3. Desktop host sends cashier username/password credentials to Central PMS over TLS.
4. Central PMS authenticates the human and resolves the operation-specific APT permissions, Site/Site Group scope, terminal binding, and current status.
5. Desktop host holds the opaque device-bound human session outside React and SQLite.
6. Cashier opens or resumes only their own authorized shift, then opens/resumes their own custody.

The configured `APT_CASHIER_ID`, display name, shift ID, and development session references remain fixture-only. Windows login is not ExitPass login. A device service identity may call an APT endpoint but may not act as a cashier without the separately authenticated human. Device trust is not MFA and does not replace username/password.

### 5.2 Operational permission contract

Central PMS and every APT consumer must keep these authorities separate:

| Capability | Permission | Exact boundary |
|---|---|---|
| Enter and use the APT application | `apt.access` | Requires a current APT-audience human session, device binding, active account, and Site/Site Group scope. It authorizes no shift, custody, cash, or `CASH_RECEIVED` operation. |
| Open, resume, or close the authenticated cashier's own shift | `cashier-shifts.operate` | Own-shift authority only. It authorizes no other cashier's shift, custody, cash receipt, or supervisor handover. |
| Open, resume, or close the authenticated cashier's own custody | `cash-custody.operate` | Own-custody authority only. It authorizes no other cashier's custody, cash receipt, or supervisor handover. |
| Participate in cash acceptance immediately before `CASH_RECEIVED` | `terminal-cash.receive` | Must be re-evaluated with the current human session, account, device, Site/Site Group scope, own shift, own custody, payable basis, POS/fiscal readiness, and every existing cash-readiness dimension. The permission alone never authorizes `CASH_RECEIVED`. |
| Resolve or revalidate payable-basis readiness | `terminal-cash.payable-basis.read` | Read-only facade authority. It authorizes none of the four operational capabilities above and causes no payment, shift, custody, fiscal, or `CASH_RECEIVED` side effect. |

`SITE_OPERATOR` is the intended v1.3 baseline cashier role for the four operational permissions. The canonical catalog and bindings are an I-021B database dependency. `OPERATIONS_SUPERVISOR` receives none of them automatically; a supervisor who also performs cashier duties must separately hold an effective, scoped `SITE_OPERATOR` assignment. No role-name check is authority.

All four operational permissions require online Central PMS re-evaluation, current device/service binding, and an effective Site or Site Group grant. Null Site fields never mean global access, and no GLOBAL APT operation is permitted. The desktop may display permissions but cannot author or cache them as authority.

### 5.3 Shift and custody invariants

- Human session, shift, and custody have separate IDs and lifecycles.
- A shift binds cashier, authentication session reference, terminal, Site, Site Group, POS Server, and open/close evidence.
- Custody additionally binds the shift and opening/closing cash evidence.
- A valid session is required to open/resume and to start new payment work. A stale reference in encrypted SQLite is recovery linkage only.
- One cashier cannot inherit another's shift/custody. Full governed supervisor handover remains deferred pending DR-08/DR-09; no current role name or v1.3 MVP APT permission authorizes it.
- Logout does not close shift or custody. Normal logout with open custody is denied. Forced expiry/revocation locks transaction initiation while retaining physical-custody records.
- Same-cashier resume requires online reauthentication and current authorization.
- No offline login or offline permission grant exists in v1.3.

### 5.4 Cash operation boundary

Before shift open, custody open, tender start, and `CASH_RECEIVED`, Central PMS revalidates the human session and the corresponding operation-specific permission and scope. Immediately before `CASH_RECEIVED`, `terminal-cash.receive` is required together with the current device binding, own shift, own custody, payable-basis revalidation, POS/fiscal readiness, and all other existing terminal-cash readiness conditions. Session expiry during custody blocks new payments. If `CASH_RECEIVED` was already durably recorded, recovery preserves and reconciles the physical event; another cashier cannot assume it through login.

The POS Server receives a privacy-safe human actor reference where fiscal/audit contracts require it and a separate Central PMS/APT service identity. POS Server does not validate human passwords or determine ExitPass roles.

## 6. Session UI behavior

| Condition | Consumer behavior | Server behavior |
|---|---|---|
| Idle warning | Warn without extending solely by client activity | Extend only on approved server-observed activity |
| Idle/absolute expiry | Lock protected UI | Deny and mark expiry event |
| Permission revoked | Refresh/disable affected commands | Re-evaluate immediately; deny sensitive operation |
| Site scope revoked | Remove location after readback | Anti-enumerate resource and deny |
| Password reset | Return to login | Revoke existing sessions |
| Account suspension | Return safe suspended/unavailable message | Revoke all sessions |
| Open unsaved workflow | Preserve minimum in-memory draft only | Re-resolve and require explicit resubmission |
| APT open custody | Lock cash actions; show governed recovery | Keep custody open; require reauth or handover |

## 7. Client persistence prohibitions

### 7.1 Browsers

Do not persist credentials, session/access/refresh tokens, role or permission authority, Site/Site Group authority, OIDC assertions, TOTP secrets/codes/provisioning QR payloads, password/reset challenges, or sensitive admin payloads in Web Storage, IndexedDB, Cache Storage, service workers, URLs, or frontend-managed cookies.

### 7.2 APT

Do not persist passwords, verifiers, reset challenges, TOTP secrets/codes, optional future recovery-code verifiers, bearer/refresh/session secrets, service credentials, or permission caches in React configuration, SQLite, logs, support bundles, or browser storage. OS-protected desktop credential storage may hold only an approved rotating continuation secret.

## 8. Required authentication APIs

### 8.1 Same-origin web

- `POST /v1/human-authentication/login`
- `POST /v1/human-authentication/oidc/{providerCode}/start`
- `GET /v1/human-authentication/oidc/{providerCode}/callback`
- `GET /v1/human-authentication/session`
- `POST /v1/human-authentication/session/continue`
- `POST /v1/human-authentication/logout`
- `POST /v1/human-authentication/logout-all`
- `POST /v1/human-authentication/password/change`
- `POST /v1/human-authentication/password-reset-requests`
- `POST /v1/human-authentication/password-resets`
- privileged-administrator TOTP enrollment/confirmation and governed reset/removal routes defined by I-020/I-021.

Login returns safe session/profile status and sets the cookie; it does not return the cookie value in JSON. Current-session readback includes user reference, username/display name, application, assurance/freshness, whether the account's current MFA requirement is satisfied, effective permissions, effective Site/Site Group/global scope, idle/absolute expiry, and support reference. It excludes TOTP secret/code/QR material, session secrets, and raw role internals not needed by the consumer.

### 8.2 APT desktop

- `POST /v1/apt/human-sessions`
- `GET /v1/apt/human-sessions/{sessionReference}`
- `POST /v1/apt/human-sessions/{sessionReference}/continue`
- `POST /v1/apt/human-sessions/{sessionReference}/reauthenticate`
- `POST /v1/apt/human-sessions/{sessionReference}/logout`
- `POST /v1/apt/cashier-shifts`
- `POST /v1/apt/cashier-shifts/{shiftReference}/resume`
- `POST /v1/apt/cashier-shifts/{shiftReference}/close`
- governed custody routes in J-008 scope; supervisor handover routes remain deferred pending DR-08/DR-09.

APT requests require both authenticated device/service identity and the device-bound human session. Public references remain opaque.

## 9. Manual consumer acceptance for runtime tasks

I-018 requires no walkthrough. H-006/H-008/J-008 must later prove login, restart, logout, idle/absolute expiry, revocation during an open workflow, cross-Site denial, safe current-user presentation, and absence of client-side secrets. J-008 additionally proves own-shift resume, blocked cross-cashier custody, expiry during custody, supervisor handover, and no offline login.
