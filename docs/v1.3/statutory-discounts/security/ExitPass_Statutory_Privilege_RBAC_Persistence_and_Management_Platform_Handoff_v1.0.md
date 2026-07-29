# ExitPass Statutory Privilege RBAC Persistence and Management Platform Handoff v1.0

## Purpose

This handoff gives Codex H the backend contract for Management Platform RBAC administration after the statutory privilege permission catalog freeze. It separates frontend-ready read capabilities from backend-blocked administration behavior.

## Canonical Database Review

Canonical repository inspected read-only: `D:\SourceCodes\exitpassdb_v1.2`, branch `develop`, HEAD `7a785fd93d592b019fbb6ac6bbdf4fc82d8485dc`.

Generated authority inspected: `build\generated\exitpass-full-object.generated.sql`.

Relevant canonical objects found:

| Area | Evidence | Verdict |
|---|---|---|
| Users | `identity.users`, source object near generated SQL line 10315 | PRESENT_AND_USABLE |
| Roles | `identity.roles`, source object near line 9713 | PRESENT_AND_USABLE |
| Permissions | `identity.permissions`, source object near line 9349, unique permission-code support | PRESENT_AND_USABLE |
| Role-permission assignment | `identity.role_permissions`, source object near line 9501 | PRESENT_AND_USABLE for unscoped grants |
| Principal-role assignment | `identity.user_roles`, source object near line 10087 | PRESENT_AND_USABLE for unscoped user roles |
| Service identities | `identity.service_identities`, source object near line 9873 | PRESENT_BUT_INCOMPLETE for lifecycle/admin use |
| Site jurisdiction | `sites.jurisdictions`, source object near line 18429 | PRESENT_AND_USABLE for policy authority |
| Site-jurisdiction assignment | `sites.site_jurisdiction_assignments`, source object near line 18491 | PRESENT_AND_USABLE for statutory ordinance authority |
| Decision policy authority | `discounts.statutory_discount_decision_policy_authorities`, source object near line 23572 | PRESENT_AND_USABLE |
| Service-channel review linkage | `operator_console.statutory_discount_service_channel_reviews`, source object near line 24646 | PRESENT_AND_USABLE for review readback |
| Operator access evaluations | `operator_console.operator_access_evaluations`, source object near line 22887 | PRESENT_BUT_INCOMPLETE for RBAC scope grants |
| Application commands | `discounts.statutory_discount_payable_basis_application_commands`, source object near line 23648 | PRESENT_AND_USABLE for application linkage |

Canonical reference-data gaps found:

- `statutory-discounts.review.queue.read` is not present in the generated canonical permission seed.
- `statutory-discounts.review.detail.read` is not present in the generated canonical permission seed.
- `statutory-discounts.application.read` is not present in the generated canonical permission seed.
- Generated canonical UAT reference data still grants `OPERATIONS_SUPERVISOR` `statutory-discounts.payable-basis.apply`.

No canonical DB files were modified in this task.

## Persistence Readiness Matrix

| Requirement | Verdict | Notes |
|---|---|---|
| Principals or identities | PRESENT_AND_USABLE | `identity.users` and `identity.service_identities` exist. |
| Human versus service classification | PRESENT_BUT_INCOMPLETE | User/service tables and enums exist, but runtime actor-class enforcement is route-specific. |
| Roles | PRESENT_AND_USABLE | `identity.roles` exists. |
| Permissions | PRESENT_AND_USABLE | `identity.permissions` exists. |
| Role-permission assignment | PRESENT_AND_USABLE | `identity.role_permissions` exists for unscoped role permissions. |
| Principal-role assignment | PRESENT_AND_USABLE | `identity.user_roles` exists. |
| Direct principal-permission assignment | ABSENT | No direct grant table confirmed. |
| Site-scoped grants | PRESENT_BUT_INCOMPLETE | Operator shifts/access evaluations carry Site/Site Group facts, but durable RBAC grant scope is not complete. |
| Site Group-scoped grants | PRESENT_BUT_INCOMPLETE | Same as Site scope. |
| Global grants | PRESENT_BUT_INCOMPLETE | Can be represented by broad role assignment, but no explicit global-scope grant model was confirmed. |
| Grant effective dates | PRESENT_AND_USABLE for roles | `user_roles` and `role_permissions` include effectivity/revocation concepts. |
| Revocation | PRESENT_AND_USABLE for roles | Status/effective-to/revoked fields are present in identity assignments. |
| Grant audit history | PRESENT_BUT_INCOMPLETE | Audit events exist generally; no complete RBAC mutation API/audit model exists. |
| Self-review comparison facts | PRESENT_BUT_INCOMPLETE | Legacy drafts persist requester/reviewer ids; service-channel initiator comparison is not fully proven. |
| Policy-administrator separation | PRESENT_BUT_INCOMPLETE | Catalog separates permissions; canonical grant workflows absent. |
| Evidence-access grants | PRESENT_BUT_INCOMPLETE | Evidence permissions exist; protected evidence retrieval is future work. |
| Service identity lifecycle | PRESENT_BUT_INCOMPLETE | `identity.service_identities` exists; management lifecycle APIs are absent. |
| Disabled or revoked service identities | PRESENT_BUT_INCOMPLETE | Status fields exist; runtime checks are limited. |

## Missing Tables Or Relationships

The following are required before production RBAC administration writes can be authorized:

- durable direct principal-permission grants or an explicit decision to prohibit direct grants;
- explicit Site/Site Group/global scope table for role or permission assignments;
- scope membership validation relationship used by authorization, not only by operational access evaluation;
- service-principal role/permission assignment lifecycle;
- grant-change approval workflow;
- grant-change audit/history table or canonical audit projection;
- self-review durable initiator-to-reviewer comparison for service-channel-originated requests;
- canonical reference-data rows for new statutory review queue/detail/application-read permissions;
- canonical removal or service-only reclassification of human UAT payable-basis application grants.

## Missing Constraints And Indexes

Required future constraints:

- no duplicate active grant for the same principal, permission/role, scope type, and scope id;
- no overlapping active scoped grants when the scope is meant to be singular;
- valid scope type: `GLOBAL`, `SITE_GROUP`, `SITE`;
- valid actor class for service-only permissions;
- no service principal assigned human review permissions;
- no human principal assigned service-only application permission except under an explicitly modeled break-glass exception;
- no self-review where requester and reviewer are the same durable human actor;
- effective-to greater than effective-from;
- revoked grants cannot remain active.

Required future indexes:

- principal effective grants by actor id, permission code, scope type, scope id, and effective window;
- service identity effective grants by service identity id and permission code;
- grant audit by principal, permission, scope, changed-at;
- Site-to-Site Group membership by site id and effective timestamp.

## Required Backend APIs Before H-002 Writes

Frontend-ready now:

- read the existing Management Platform identity/RBAC inventory endpoint;
- display canonical permission identifiers;
- group permissions by operational domain;
- distinguish implemented versus target-only permissions;
- show role bundle purpose, restrictions, and target surface;
- show safe persisted users, role assignments, Site/Site Group scope readback, device bindings, shifts, and gaps;
- warn that `statutory-discounts.payable-basis.apply` is payment-time service authority and not a normal reviewer permission.

Backend-blocked:

- create roles;
- assign permissions;
- assign Site or Site Group scope;
- revoke grants;
- manage service identities;
- effective-date grants;
- approve grant changes;
- retrieve complete RBAC grant audit history;
- enforce scoped RBAC grants across all Central PMS routes;
- configure WebPay/APT service-principal grants;
- manage evidence-view permissions against protected evidence objects.

## Recommended Implementation Sequence

1. Promote canonical RBAC reference data for the frozen statutory permissions:
   - add `statutory-discounts.review.queue.read`;
   - add `statutory-discounts.review.detail.read`;
   - add `statutory-discounts.application.read`;
   - remove human UAT `OPERATIONS_SUPERVISOR` payable-basis apply mapping or reclassify it as service-only.
2. Add canonical scoped grant model:
   - `GLOBAL`, `SITE_GROUP`, `SITE`;
   - effectivity, revocation, audit, and validation constraints.
3. Add Central PMS effective-permission resolver:
   - actor class;
   - permission;
   - Site/Site Group resource facts;
   - cache invalidation posture.
4. Add service identity lifecycle and service-principal assignment APIs.
5. Add RBAC mutation APIs with audit and approval workflow.
6. Implement H-002 Management Platform RBAC administration UI against those APIs.
7. Add manual validation for grant creation, revocation, scope narrowing, scope broadening, service identity disablement, and SoD conflict warnings.

## H-002 Contract

Codex H may implement read-only Management Platform RBAC displays using:

- `ManagementPlatformIdentityRbacInventoryRead`;
- `ManagementPlatformIdentityRbacInventoryService`;
- `ManagementPlatformIdentityRbacInventoryResponse`;
- permission identifiers frozen in `ExitPass_Statutory_Privilege_Permission_Catalog_and_Enforcement_Contract_v1.0.md`.

Codex H must not implement production write UI against mocks for:

- role creation;
- permission assignment;
- Site/Site Group scope assignment;
- service identity management;
- grant revocation;
- grant effective dating;
- grant approval workflow;
- grant audit history.

## Conditions Required To Fully Unblock H-002

H-002 is fully unblocked only after:

1. canonical RBAC reference data matches the frozen catalog;
2. canonical scoped grant persistence exists;
3. Central PMS can resolve scoped effective permissions server-side;
4. service identities can be managed and disabled;
5. RBAC mutation APIs exist and write audit records;
6. self-review and service/human separation are enforced from durable identity facts;
7. automated access-denial and scope tests pass for the statutory routes.

## Sequencing Decision

Management Platform may build read-only catalog and gap display now.

Management Platform RBAC administration writes remain blocked.
