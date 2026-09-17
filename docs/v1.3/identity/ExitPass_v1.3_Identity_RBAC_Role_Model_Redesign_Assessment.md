# ExitPass v1.3 Identity/RBAC role-model redesign

## Decision

Proceed with the approved eight-role non-hierarchical model. A role grants only its functional authority; no role implies or inherits another role.

## Current state and gaps found before implementation

- The Management Platform inventory exposed seven older bundles. It split System/RBAC Administrator from Platform Administrator and omitted Parking Attendant and APT / Cashier Operator.
- The UAT seed depended on an externally generated canonical catalog, contained eight definitions but only seven assigned fixture users, and included obsolete Platform Administrator, Head Office Statutory Benefit Reviewer, and Operator / Support Staff bundles.
- Persisted role listing trusted `CANONICAL_ROLE` and `human_assignable` flags but did not enforce the approved catalog.
- Identity administration rejected every Global grant. It did not require Executive / Management to be Global or reject other incompatible role/scope combinations.
- Last-administrator and delegation safeguards referenced the obsolete `SYSTEM_RBAC_ADMINISTRATOR` code.
- Effective authorization is the union of persisted role-permission bindings. There is no hierarchy mechanism, but an over-broad binding can still leak authority.
- Human authentication validates the requested audience but does not currently establish application eligibility from effective role codes. Login/session issuance remains H2-owned; H1 now exposes the complete role/application contract for H2 to consume.
- Scope grants apply to an entire role assignment, not individual permission groups. The Operations Supervisor assignment therefore remains Site-scoped, while the Management Platform statutory-review repository expands only its statutory supervisor capability to all Sites.
- Statutory supervisor permissions were previously audience-neutral in the common authorization path, allowing an Operations Supervisor permission union to reach Operator Console supervisory endpoints. That was a surface-boundary defect.
- The production clean-build generator is not owned by this repository. It is generated in `D:\SourceCodes\exitpassdb_v1.2` from `objects/reference-data/identity.v13-management-platform-role-permission-bundles.sql`, included by `objects/exitpass-full-object-apply-order.txt`, and emitted as `build/generated/exitpass-full-object.generated.sql`. That source still contains the superseded role catalog.

## Implemented Identity/RBAC corrections

- Added one approved catalog and fail-closed role/application/scope policy in application code. System Administrator is Global-only; Operations Supervisor is Site-only for its ordinary assignment; Executive / Management is Global-only.
- Added `Policies`, `TryGetPolicy`, `IsApplicationEligible`, and `IsUserEligibleForApplication` as H2's authoritative application-eligibility contract.
- Classified statutory review/approval/rejection/payable-basis supervisor permissions as Management Platform-only. The common RBAC middleware denies those permissions for an authenticated Operator Console audience before considering claims or database fallback.
- Restricted the all-site Management Platform statutory resolver to its explicit statutory-review permission allowlist. It does not expand ordinary operational permissions.
- Replaced the inventory bundles with the exact eight approved roles.
- Restricted the persisted role catalog and assignment paths to approved role codes.
- Enforced Executive / Management as Global-only and role/scope compatibility across direct creation, grants, privileged requests, and approval revalidation.
- Renamed administrator safety checks to `SYSTEM_ADMINISTRATOR`.
- Added an idempotent reference-data patch that retires non-approved human-assignable roles, merges platform administration into System Administrator, replaces the System/Operations/Site bundles exactly, and adds narrow Parking App and APT permissions.
- Added a fail-fast database validator and wired patch plus validator into both initial and replay passes of every disposable canonical integration database.
- Updated local/UAT fixtures to the eight-role model.

## Cross-workstream dependencies

1. H2 must call `ApprovedIdentityRoleCatalog.IsUserEligibleForApplication(effectiveRoleCodes, requestedAudience)` before normal session issuance. It may call `IsApplicationEligible(roleCode, requestedAudience)` per role, but must not maintain a second role/audience switch or infer eligibility from user type.
2. The external database owner at `D:\SourceCodes\exitpassdb_v1.2` must replace its source role catalog in `objects/reference-data/identity.v13-management-platform-role-permission-bundles.sql`, rebuild `build/generated/exitpass-full-object.generated.sql`, and provide an equivalent migration. This worktree safely composes and validates the v1.3 patch, but cannot change that external repository.
3. Native Parking App and APT consumers must enforce assigned device/terminal context in addition to the Site grant; the current generic identity scope model stores Site/Site Group/Global only.
