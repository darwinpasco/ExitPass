# ExitPass Management Platform UAT User/Role/Permission Seed Result v1.0

## Result

PASSED for source-level seed implementation. The local runtime preflight remains available for `exitpass_v12_dev` and should be run before Management Platform or Operator Console UAT that depends on these identities.

## Seed scripts

| Script | Purpose |
| --- | --- |
| `scripts/management-platform/Seed-ManagementPlatformUatIdentityRbac.sql` | Seeds deterministic local/UAT users, seven role bundles, granular permissions, role-permission bindings, and user-role assignments. |
| `scripts/management-platform/Verify-ManagementPlatformUatIdentityRbac.sql` | Verifies users, roles, permissions, assignments, least-privilege boundaries, and statutory discount requester/reviewer fixture compatibility. |
| `scripts/management-platform/Invoke-ManagementPlatformUatIdentityRbacPreflight.ps1` | Runs the statutory discount pilot fixture seed, Management Platform identity/RBAC seed, both verifiers, and optionally the live inventory API. |

The statutory discount pilot preflight now composes the Management Platform seed and prints requester/reviewer permission profiles.

## UAT users

| User | User ID | Role bundle |
| --- | --- | --- |
| `uat-system-rbac-admin` | `79000000-0000-0000-0000-000000000001` | System / RBAC Administrator |
| `uat-platform-admin` | `79000000-0000-0000-0000-000000000002` | Platform Administrator |
| `uat-operations-supervisor` | `77000000-0000-0000-0000-000000000012` | Operations Supervisor |
| `uat-operator-support` | `77000000-0000-0000-0000-000000000010` | Operator / Support Staff |
| `uat-finance-reconciliation` | `79000000-0000-0000-0000-000000000005` | Finance / Reconciliation Analyst |
| `uat-compliance-policy-admin` | `79000000-0000-0000-0000-000000000006` | Compliance / Policy Administrator |
| `uat-executive-management` | `79000000-0000-0000-0000-000000000007` | Executive / Management |

## Seven role bundles

The seed uses the approved v1.3 role bundles:

- System / RBAC Administrator
- Platform Administrator
- Operations Supervisor
- Operator / Support Staff
- Finance / Reconciliation Analyst
- Compliance / Policy Administrator
- Executive / Management

Roles remain responsibility bundles. Granular access rights are assigned to roles; the seed does not create one role per button/action.

## Permission assignments / header profiles

The seed persists granular permissions in `identity.permissions`, role-permission bindings in `identity.role_permissions`, and user-role assignments in `identity.user_roles`.

Local/dev header profiles are still printed by preflight because current Operator Console runtime authorization can consume headers during UAT. This is local/dev posture only.

Key boundaries:

- System / RBAC Administrator can read identity/RBAC inventory and manage identity/RBAC target permissions, but does not automatically receive statutory discount approval/apply, Sales Invoice void, reconciliation mutation, or payment/fiscal authority.
- Operator / Support Staff can lookup sessions, create drafts, view/capture metadata-only evidence, and read Sales Invoice status, but cannot approve/reject or apply payable basis.
- Operations Supervisor can approve/reject statutory discounts as a different reviewer, apply payable basis, and perform controlled Sales Invoice void where allowed for UAT.
- Compliance / Policy Administrator can read audit and manage policy import/policy surfaces, but does not receive runtime statutory discount approval/apply or Sales Invoice void command authority by default.
- Executive / Management is read-only by default and receives no workflow mutation/export authority.

## Site / device / shift assignments

The seed composes with the existing statutory discount pilot fixture for site-scoped operational context:

| Context | Value |
| --- | --- |
| Site group | `77000000-0000-0000-0000-000000000001` |
| Site | `77000000-0000-0000-0000-000000000002` |
| Operator device binding | `77000000-0000-0000-0000-000000000030` |
| Requester shift | `77000000-0000-0000-0000-000000000050` |
| Reviewer shift | `77000000-0000-0000-0000-000000000052` |

Admin/global scope persistence is still a gap; the current durable assignments cover role bundles and the statutory discount UAT operational site/device/shift posture.

## Inventory API visibility result

After the seed is applied, the read-only inventory endpoint should surface:

- seven UAT users
- seven role bundle assignments through `identity.user_roles`
- persisted permission catalog rows
- statutory discount requester/reviewer site/device/shift context through the existing operational fixture
- gaps for UI, mutation APIs, external IAM mapping, and broader assignment administration

Endpoint:

`GET /v1/ops/management-platform/identity-rbac/inventory`

Required permission:

`management-platform.identity-rbac.inventory.read`

## Statutory discount requester/reviewer support

The requester/reviewer identities are distinct:

- Requester/evidence actor: `uat-operator-support`
- Reviewer/apply actor: `uat-operations-supervisor`

This supports requester-vs-approver segregation for the statutory discount UAT workflow without weakening backend enforcement.

## Gaps

- No Management Platform UI exists yet.
- User/role/permission mutation APIs remain intentionally absent.
- External IAM mapping is not implemented.
- Admin/global site scope assignment persistence is not complete beyond current role assignments and operational fixture shift/device context.
- Runtime permission evaluation may still use local/dev headers in Operator Console UAT until persisted auth integration is completed.

## Validation

Validation performed:

- Focused Management Platform identity/RBAC seed unit tests.
- Focused identity/RBAC inventory unit and integration tests.
- Focused statutory discount RBAC/SoD tests where available.
- Central PMS API build.
- `git diff --check`.

Final command results are recorded in the completion summary for this branch.

## Safety boundaries

This seed does not:

- add user/role/permission mutation APIs
- add Management Platform UI
- weaken RBAC
- give RBAC admin automatic business workflow authority
- give Executive / Management mutation rights by default
- change statutory discount computation
- call payment provider
- call HikCentral
- issue ExitAuthorization
- open gate
- create refund/reversal
- create POS Server Sales Invoice
- allocate fiscal number
- render final BIR artifacts
- expose secrets, passwords, tokens, private keys, raw certificates, or raw evidence
