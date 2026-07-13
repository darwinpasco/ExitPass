# ExitPass Management Platform Identity/RBAC Inventory API Result v1.0

## Result

PASSED - read-only Central PMS identity/RBAC inventory API added for ExitPass v1.3 Management Platform preparation.

## Endpoint

| Method | Route | Purpose |
| --- | --- | --- |
| GET | `/v1/ops/management-platform/identity-rbac/inventory` | Returns a safe read-only inventory for future Identity & RBAC Administration. |

## Authorization

| Policy | Permission |
| --- | --- |
| `ManagementPlatformIdentityRbacInventoryRead` | `management-platform.identity-rbac.inventory.read` |

Normal Operator Console workflow permissions, statutory discount runtime permissions, fiscal status/void permissions, and policy import review permissions do not grant this inventory access unless the inventory permission is separately assigned.

## Inventory Sections

- users
- roleBundles
- permissions
- policyMappings
- userRoleAssignments
- userSiteScopes
- deviceBindings
- shifts
- gaps

## Role Bundles Surfaced

- System / RBAC Administrator
- Platform Administrator
- Operations Supervisor
- Operator / Support Staff
- Finance / Reconciliation Analyst
- Compliance / Policy Administrator
- Executive / Management

## Safety Boundaries

- No user creation or editing.
- No role or permission creation/editing.
- No assignment mutation.
- No statutory discount workflow mutation.
- No fiscal void or Sales Invoice mutation.
- No payment provider, HikCentral, gate, refund/reversal, or rendering behavior.
- No secrets, password hashes, tokens, private keys, raw certificates, or raw evidence payloads are exposed.

## Key Gaps Surfaced

- Management Platform UI is not implemented yet.
- User/role/permission/assignment mutation APIs are intentionally not implemented in this slice.
- Persisted target role bundle alignment remains to be implemented.
- Local/dev header-driven permissions may still be used by UAT workflows.
- External IAM mapping is not confirmed.
- Admin audit events for future RBAC mutations are not yet confirmed.

## v1.3 Document Findings

Recursive inspection of `docs/v1.3` found no conflict with a read-only Central PMS identity/RBAC inventory API.

Applicable v1.3 findings:

- `docs/v1.3/ExitPass_BRD_v1.3.md`: Central PMS is the core control authority; Operator Console, reporting, fiscal, and payment boundaries must remain separated.
- `docs/v1.3/system-design/input-packs/04_security_trust_and_rbac_input.md`: RBAC, identity, audit, Management Dashboard/Reporting visibility, and Operator Console governance are Central PMS-controlled concerns.
- `docs/v1.3/operator-console/ExitPass_Operator_Console_BRD_v1.1.md`: Operator Console consumes operator/site/device/shift authorization and is not the admin authority.
- `docs/v1.3/operator-console/ExitPass_Operator_Console_System_Design_SDD_v1.0.md`: Operator Console workflows must use RBAC and audit context from Central PMS.
- `docs/v1.3/management-platform/ExitPass_Management_Platform_Current_State_and_Target_Scope_Audit_v1.0.md`: ExitPass Management Platform is the umbrella, Central PMS owns identity/RBAC enforcement, and the seven-role bundle model is the v1.3 target.

## Validation

- Focused Central PMS identity/RBAC inventory unit tests: passed.
  - `dotnet test src\Services\CentralPms\tests\ExitPass.CentralPms.UnitTests\ExitPass.CentralPms.UnitTests.csproj --no-restore --filter "FullyQualifiedName~ManagementPlatformIdentityRbacInventoryServiceTests|FullyQualifiedName~CentralPmsRbacPolicyCatalogTests" -v q`
- Focused Central PMS identity/RBAC inventory integration tests: passed.
  - `dotnet test src\Services\CentralPms\tests\ExitPass.CentralPms.IntegrationTests\ExitPass.CentralPms.IntegrationTests.csproj --no-restore --filter "FullyQualifiedName~ManagementPlatformIdentityRbacInventoryApiIntegrationTests" -v q`
- Central PMS API build: passed.
  - `dotnet build src\Services\CentralPms\src\ExitPass.CentralPms.Api\ExitPass.CentralPms.Api.csproj --no-restore -v q`
- `git diff --check`: passed.
