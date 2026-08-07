# ExitPass User, Role, and Scope Administration Contract v1.0

## 1. Authority and ownership

Management Platform is the administrative user interface. Central PMS is the only runtime authority for user lifecycle, role assignments, permission bindings, Site/Site Group/global grants, access review, and session revocation. Operator Console and APT are consumers and must not become general identity-administration applications.

Authentication establishes the human. Authorization grants actions. Scope grants locations. Each is independently stored, effective-dated, revoked, audited, and re-evaluated.

## 2. Existing baseline

The canonical model already supports:

- users with type, status, effectivity, lifecycle timestamps, actor fields, and row version;
- roles with type, status, privileged/elevated-approval indicators, and effectivity;
- permissions with domain/action, sensitivity, and audit indicators;
- effective-dated user-role assignments with revocation and access-review fields;
- effective-dated role-permission bindings with revocation;
- separate service identities.

It does not have general human Site or Site Group grants. `discounts.statutory_evidence_principal_scope_grants` is deliberately evidence-specific and cannot become platform-wide staff scope. `operator_console.operator_shifts` establishes an operational assignment for a shift but is not a general role scope. `identity.user_roles` alone does not identify where a role applies.

## 3. Minimum scoped-grant model

I-019 should add assignment-scoped grants, provisionally `identity.user_role_scope_grants`, rather than a global scope flag on users or roles.

Each grant binds one `identity.user_roles.user_role_id` to exactly one controlled scope:

- `SITE` plus `site_id`;
- `SITE_GROUP` plus `site_group_id`; or
- `GLOBAL` with neither Site ID and only when the role and assigning actor are eligible.

Required fields include grant ID, user-role assignment ID, scope type, Site/Site Group FK, status, reason, effective window, assignment and revocation actors/timestamps/reasons, last access review, privileged approval reference where required, creation/update actors, and row version. Constraints must prevent ambiguous/global-by-null grants, wrong Site Group membership assumptions, overlapping duplicate active grants, grants outside their parent assignment effectivity, and active grants for revoked assignments.

Site Group grants authorize only current canonical member Sites and only for permissions whose policy allows group inheritance. Site membership is resolved from canonical server data at operation time. Request-supplied Site/Site Group is a resource selector, not authority.

Role definitions stay reusable. Scope is attached to the user-role assignment so a user can hold the same functional role at bounded locations without making the role globally scoped. Direct user-scope grants and role-level universal scope are rejected for v1.3 because they weaken attribution and least privilege.

## 4. Effective authorization

Central PMS permits an operation only when all are true at evaluation time:

1. user is `ACTIVE` and currently effective;
2. human session is active, unexpired, correct audience, and sufficiently fresh;
3. user-role assignment is active and currently effective;
4. role is active and currently effective;
5. role-permission binding and permission are active and currently effective;
6. a matching active assignment-scope grant covers the canonical target;
7. required separation-of-duty, approval, shift, device, custody, and workflow constraints pass;
8. no explicit lifecycle or security prohibition applies.

Global scope is an explicit grant, never the absence of a Site. It is limited to approved central operations/security roles. Site operators, cashiers, ordinary reviewers, and routine support roles are ineligible by default.

Revocation is effective immediately for sensitive operations. Authorization/session epochs invalidate stale cached decisions. Clients may cache presentation data briefly, but a client cache never authorizes.

## 5. Management Platform capabilities

Dedicated permissions and named policies are required for each administrative operation. A broad `admin` role or read permission cannot imply mutation.

| Capability | Required behavior |
|---|---|
| List/read users | Scoped, paginated, masked contacts, anti-enumerating detail |
| Create/invite | Create identity only; activation challenge is separate |
| Edit profile | Optimistic version; username/email rules; audit diff |
| Activate | Requires valid credential/provider binding and effectivity |
| Suspend/inactivate/retire | Reason required; revoke sessions; retirement terminal |
| Lock/unlock | Security-specific permission; no self-unlock |
| Credential reset | Issue challenge only; admin never sees password |
| MFA status/readiness | Show controlled status, requirement, enrollment/revocation timestamps, and safe audit references; never show secret, code, or QR payload |
| TOTP require/enroll | Required only for privileged Management Platform administrators under v1.3 policy; enrollment is completed by the user through the authentication boundary |
| TOTP reset/remove | Explicit privileged authority, fresh authentication, reason, independent ceiling checks, old-authenticator invalidation, session-policy enforcement, and audit |
| Assign/revoke role | Explicit role-assignment permission and scope ceiling |
| Assign/revoke Site/Site Group | Explicit scope permission; canonical membership validation |
| Grant global scope | Privileged approval and eligible role/user type |
| Change role-permission binding | Separate role-definition authority; elevated approval for sensitive permissions |
| Access review | Reviewer, outcome, date, next-review posture, stale-grant action |
| Sessions | Safe list and revoke; no raw token or impersonation |
| Audit history | Read-only controlled events and support references |

The authenticated administrator is derived from the session. `created_by`, `assigned_by`, `revoked_by`, `approved_by`, and audit actor fields cannot be supplied as authority by the browser.

## 6. Safeguards required for v1.3

### 6.1 Self and last-administrator protections

- No self-role assignment, self-scope grant, self-privileged approval, self-unlock, or self-approval of a pending access change.
- No administrator can retrieve, reveal, export, or generate a TOTP secret/code for another user. Management Platform is not an OTP generator.
- A lower-privilege administrator cannot reset/remove MFA from a higher-privilege account unless an explicit delegated policy authorizes it; removing MFA from a privileged administrator cannot leave privileged access active without the required authenticator.
- An administrator may change their own non-authoritative profile fields but not status, privilege, scope, or credential-recovery channel without governed verification.
- Disabling, retiring, or removing the final active eligible identity administrator is rejected transactionally.
- Global logout remains available to the user; administrative forced self-session revocation is not a privilege escalation.

### 6.2 Privilege ceiling

An administrator may assign only roles, permissions, and scopes inside the administrator's own current delegation ceiling. Possessing a Site Group grant does not permit granting global scope. Privileged roles and sensitive permission bindings require a different approver with current elevated-approval authority.

### 6.3 Separation of duties

The following remain independently permissioned:

- role definition versus role assignment;
- user/role assignment request versus privileged approval;
- statutory approve versus reject where policy requires distinct authority;
- evidence view/review versus capture/replace/delete/hold;
- payable-basis apply versus statutory review;
- cash collection versus supervisor custody takeover;
- fiscal operation versus fiscal void/approval;
- security unlock/reset versus ordinary user editing.

I-019/I-021 must implement a controlled incompatible-duty catalog or policy checks for combinations that are prohibited. At minimum, the privileged approver cannot approve their own request.

### 6.4 Grant hygiene

- All grants have explicit status and effectivity.
- Expired and revoked grants are retained as history but ignored by authorization.
- Future grants do not activate early.
- Changes use optimistic concurrency and one transaction for assignment, approval, audit, and authorization-epoch update.
- Privileged and global grants require periodic review; recommended default is 90 days. Other staff grants default to 180 days pending approval.
- Overdue review does not silently extend privilege. Privileged grants fail closed or are suspended according to approved policy; ordinary grants produce an actionable stale state before expiry.

## 7. Administration workflows

### 7.1 Create and activate

`administrator -> create/invite identity -> assign bounded role/scope -> issue activation challenge -> user establishes credential/provider binding -> privileged Management Platform user enrolls TOTP when required -> activate -> new session`

Creation does not imply activation. Role/scope assignment can be prepared while invited but cannot authorize until all effectivity and status checks pass. Ordinary Management Platform, Operator Console, and APT users are not automatically enrolled in MFA.

### 7.2 Privileged assignment

`requester -> proposed role/scope with reason/effectivity -> policy validation -> independent approver fresh privileged authentication -> atomic activation/audit/epoch update -> affected session re-evaluation`

No email, ticket, or frontend flag substitutes for the durable approval.

### 7.3 Revoke or suspend

`authorized admin/security action -> reason -> atomic status/grant change -> authorization epoch -> session revocation or reduction -> audit/security event`

Suspension and inactivation revoke all sessions. A bounded role/scope revocation may preserve the session only if its remaining access is safe; the next sensitive operation always re-evaluates.

## 8. Required APIs

Repository-consistent final naming is an I-021 concern. The frozen capabilities and proposed routes are:

### 8.1 Users

- `GET /v1/management-platform/identity/users`
- `POST /v1/management-platform/identity/users`
- `GET /v1/management-platform/identity/users/{userReference}`
- `PATCH /v1/management-platform/identity/users/{userReference}`
- `POST /v1/management-platform/identity/users/{userReference}/activate`
- `POST /v1/management-platform/identity/users/{userReference}/suspend`
- `POST /v1/management-platform/identity/users/{userReference}/inactivate`
- `POST /v1/management-platform/identity/users/{userReference}/retire`
- `POST /v1/management-platform/identity/users/{userReference}/lock`
- `POST /v1/management-platform/identity/users/{userReference}/unlock`
- `POST /v1/management-platform/identity/users/{userReference}/credential-reset-challenges`
- `GET /v1/management-platform/identity/users/{userReference}/mfa-status`
- `POST /v1/management-platform/identity/users/{userReference}/mfa-requirements`
- `POST /v1/management-platform/identity/users/{userReference}/mfa-authenticators/reset`
- `POST /v1/management-platform/identity/users/{userReference}/mfa-authenticators/remove`

The administration surface exposes safe MFA status/readiness and governs requirement/reset/removal. TOTP provisioning and confirmation occur through the authenticated user's bounded I-020 enrollment ceremony. No administration response returns TOTP secret, provisioning URI/QR payload, submitted code, protected ciphertext, or credential reference.

### 8.2 Role, permission, and scope

- `GET /v1/management-platform/identity/roles`
- `GET /v1/management-platform/identity/permissions`
- `POST /v1/management-platform/identity/users/{userReference}/role-assignments`
- `POST /v1/management-platform/identity/users/{userReference}/role-assignments/{assignmentReference}/revoke`
- `POST /v1/management-platform/identity/users/{userReference}/role-assignments/{assignmentReference}/scope-grants`
- `POST /v1/management-platform/identity/users/{userReference}/role-assignments/{assignmentReference}/scope-grants/{grantReference}/revoke`
- `POST /v1/management-platform/identity/privileged-access-requests/{requestReference}/decision`
- `POST /v1/management-platform/identity/users/{userReference}/access-reviews`

### 8.3 Sessions and audit

- `GET /v1/management-platform/identity/users/{userReference}/sessions`
- `POST /v1/management-platform/identity/users/{userReference}/sessions/{sessionReference}/revoke`
- `POST /v1/management-platform/identity/users/{userReference}/sessions/revoke-all`
- `GET /v1/management-platform/identity/users/{userReference}/audit-events`

Mutation requests carry operation/idempotency key, expected row version, controlled reason, effectivity, target references, and correlation reference. Responses contain safe opaque references and current state, never secrets.

## 9. Decision gates

| Gate | Options | Recommendation | Default if not approved | Runtime impact |
|---|---|---|---|---|
| Privileged assignment | one admin; two-person; external approval | Two-person durable approval | Deny privileged activation | Privileged grant runtime blocked |
| Global eligibility | any admin; role allowlist; no global | Central security/operations allowlist | No global grants | General scoped work can proceed |
| Access review | advisory; suspend on overdue; expire | 90-day privileged expiry/reapproval, 180-day ordinary review | Privileged grant does not renew | Review automation needs approval |
| Last admin | allow with warning; prohibit | Transactionally prohibit | Prohibit | Can implement immediately |
| Self changes | broad self-service; bounded; none | Bounded profile/password only | Deny authority changes | Can implement immediately |

## 10. Deferred items

WebAuthn, FIDO2, passkeys, security/hardware-key distribution, passkey recovery, recovery codes, automated HR provisioning, SCIM, dynamic IdP-group mapping, just-in-time privileged access, emergency break-glass automation, and generalized delegation are deferred. Any future implementation must preserve Central PMS authorization and explicit scoped grants.
