# ExitPass Central PMS Human Identity Administration APIs Implementation Note v1.0

## 1. Purpose and boundary

I-021 implements the Central PMS administrative API and persistence layer for governed human users, role assignments, assignment-scoped authorization grants, privileged-access decisions, access reviews, privacy-safe session inventory, MFA status, and identity audit history. It uses the canonical I-019 `identity` and `audit` objects directly. It adds no application-owned identity tables and no database schema.

Management Platform owns the future H-007 user interface. I-020 owns login, authenticated-principal middleware, authentication freshness, credential challenges, session revocation, and MFA reset/removal runtime. The I-021 endpoints accept only an authenticated human principal and session reference supplied by the I-020 principal boundary; legacy fixture headers and request-body actor identifiers are not authority.

## 2. API surface

All routes are under `/v1/management-platform/identity`.

| Capability | Method and route |
|---|---|
| User search | `GET /users` |
| Invite user | `POST /users` |
| User detail | `GET /users/{userReference}` |
| Profile/effectivity update | `PATCH /users/{userReference}` |
| Lifecycle | `POST /users/{userReference}/{activate|suspend|inactivate|retire|lock|unlock}` |
| Credential reset request | `POST /users/{userReference}/credential-reset-challenges` |
| Role catalog | `GET /roles` |
| Permission catalog | `GET /permissions` |
| Role assignment/revocation | `POST /users/{userReference}/role-assignments` and `POST .../{assignmentReference}/revoke` |
| Scope grant/revocation | `POST .../{assignmentReference}/scope-grants` and `POST .../{grantReference}/revoke` |
| Privileged request | `POST /privileged-access-requests` |
| Privileged request read/decision | `GET /privileged-access-requests/{requestReference}` and `POST .../decision` |
| Access review | `POST /users/{userReference}/access-reviews` |
| Session inventory/revocation | `GET /users/{userReference}/sessions`, `POST .../{sessionReference}/revoke`, and `POST .../revoke-all` |
| MFA status/reset/remove | `GET /users/{userReference}/mfa-status`, `POST .../reset`, and `POST .../remove` |
| Identity audit history | `GET /users/{userReference}/audit-events` |

Pagination is optional and server bounded. Responses use opaque UUID references, masked email/mobile data, safe lifecycle classifications, and correlation references. Request contracts contain no actor, permission, role-authority, password, TOTP, session-secret, token, or ciphertext fields.

## 3. Authority and scope

Every repository operation validates the durable I-020 human session, Management Platform audience, active/effective user, session expiry, authorization-epoch snapshot, current active/effective role assignment, current role-permission binding, and current permission. Target operations additionally apply the actor's canonical `identity.user_role_scope_grants`.

`SITE` and `SITE_GROUP` scopes are assignment-scoped and effective-dated. A Site Group grant includes Sites canonically belonging to that group. `GLOBAL` is explicit; null Site fields never imply global access. Because DR-11 is unresolved, I-021 rejects direct GLOBAL grants with `GLOBAL_SCOPE_POLICY_NOT_APPROVED` and creates no default global authority.

Role and scope assignment to self, role revocation from self, lifecycle administration of self, self-unlock, self credential/MFA administration, and self approval of privileged requests are prohibited. Delegation is limited to roles and scopes the actor currently holds or a canonically recognized system identity administrator ceiling. Browser selectors and actor fields do not create authority.

## 4. User lifecycle and concurrency

Invites create canonical `identity.users` rows in `INVITED` state. Normalized username uniqueness remains database-owned. Profile and lifecycle mutations require expected row versions. Lifecycle transitions are state constrained; unlocking applies only to a locked user and locking applies only to an active user.

Suspension, inactivation, retirement, and lock revoke active sessions in the same transaction and advance the authorization epoch. Role and scope changes also advance the target authorization epoch. The last-active-system-identity-administrator check uses a transaction-scoped PostgreSQL advisory lock plus fresh read-committed state, so concurrent removal attempts cannot both succeed.

Mutation and required `audit.audit_events` evidence commit atomically. A row-version, uniqueness, policy, or audit failure does not report success.

## 5. Roles, scopes, and privileged access

Non-privileged role assignments and exact scope grants are replay-safe through canonical uniqueness, returning an idempotent classification for an existing active semantic equivalent. Stale row versions and incompatible duplicates return deterministic conflicts.

Privileged or elevated-approval roles cannot be assigned directly. I-021 records requests and independent decisions in `identity.privileged_access_requests` and `identity.privileged_access_decisions`. The requester cannot decide their own request, and duplicate/stale decisions conflict. DR-10 and DR-11 remain unresolved; an APPROVE decision records durable approval evidence but does not automatically activate a role or scope. Absence of a decision never grants authority.

Access review reuses I-019 review fields on role assignments and scope grants. I-021 records bounded confirmation evidence and does not create review campaigns or silently renew expired access.

## 6. Authentication administration and I-020 seam

I-021 performs dedicated permission, target-scope, self-administration, and MFA privilege-ceiling checks before invoking `IHumanAuthenticationAdministrationGateway`:

- `human-authentication.credential.reset`
- `human-authentication.session.admin.revoke`
- `human-authentication.mfa.reset`
- `human-authentication.mfa.remove`

MFA reset/removal for a target with active privileged authority additionally requires the acting administrator to hold the active `SYSTEM_RBAC_ADMINISTRATOR` role. I-020 remains responsible for fresh-authentication enforcement, challenge creation, actual session revocation, authenticator mutation, and corresponding authentication/security events.

Until I-020 is integrated, the production registration is `PendingHumanAuthenticationAdministrationGateway`. It fails credential reset, session revoke, revoke-all, MFA reset, and MFA remove closed with HTTP 503. It does not mutate canonical state. I-020 must replace this registration and supply the authenticated ClaimsPrincipal/session middleware before authoritative I-021 mutations are merge-ready.

Session reads expose only reference, audience, status, assurance, safe device/service reference, activity/expiry timestamps, and row version. MFA reads expose only required/enrolled/lifecycle posture and safe timestamps. Neither surface returns tokens, hashes, protected envelopes, provisioning material, TOTP codes, or key references.

## 7. Anti-enumeration, errors, and audit history

Unknown or out-of-scope user, assignment, grant, request, session inventory, MFA status, and audit resources return the same safe not-found posture where existence itself is sensitive. Expected invalid, forbidden, conflict, and integration-unavailable outcomes use bounded classifications and correlation references. Unexpected failures return a generic response; logs record correlation and exception type without raw database diagnostics in the public response.

Audit history is target scoped and privacy safe. It exposes event reference/type/result/reason, actor reference, bounded summary, occurrence time, and correlation reference. It does not expose credentials, authenticator material, session secrets, SQL, or unrestricted security-event internals.

## 8. Validation

Validation uses the canonical I-019 full generated DDL against a disposable PostgreSQL 16 container. Focused proof covers application validation, production-header rejection, safe DTO shape, user invitation and masked metadata, optimistic concurrency, role assignment, Site scope, duplicate-scope replay, explicit GLOBAL denial, lifecycle transitions, credential/session/MFA administration preauthorization, no MFA secret readback, independent privileged decisions, no implicit activation, atomic audit, and concurrent last-active-admin protection.

The I-021 source adds no SQL, migrations, package changes, generated artifacts, or shadow identity persistence. Final validation also includes the Release API build, complete Central PMS unit suite, focused PostgreSQL/API integration tests, security text scans, and `git diff --check`.

## 9. H-007 and integration handoff

H-007 may consume the route and DTO contracts after I-021 merges with I-020. The UI must treat all permissions, scope, row versions, MFA posture, and lifecycle state as server-authoritative. It must not author actor identity or retain authentication secrets.

Required integration order:

1. Integrate latest I-020 authenticated principal and session-revocation/authentication-administration gateway.
2. Rerun I-020 and I-021 authorization, session, freshness, MFA, and PostgreSQL suites together.
3. Merge I-021 only after the pending fail-closed gateway is replaced and production authentication middleware supplies the canonical human actor.
4. Implement H-007 against the integrated API.

Controlled UAT remains unauthorized until I-020, I-021, the staff consumers, and I-022 integration proof are merged and approved.
