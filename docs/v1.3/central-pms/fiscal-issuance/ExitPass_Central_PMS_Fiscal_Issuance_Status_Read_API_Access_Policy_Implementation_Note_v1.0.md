# ExitPass Central PMS Fiscal Issuance Status Read API Access Policy Implementation Note v1.0

## Scope

This slice hardens the read-only fiscal issuance status endpoint:

- `GET /v1/fiscal-issuance/references/{fiscalIssuanceReferenceId}`

The endpoint remains read-only. It does not call POS Server, allocate fiscal numbers, retry fiscal issuance, add FEQ behavior, issue ExitAuthorization, trigger gate behavior, or mutate fiscal/payment state.

## Policy Added

The endpoint now carries Central PMS RBAC metadata with the named policy:

- `FiscalIssuanceStatusRead`

The policy resolves through the existing `CentralPmsRbacPolicyCatalog` to:

- `fiscal-issuance.status.read`
- `reconciliation.view`
- `reconciliation.manage`

This follows the existing Central PMS operational RBAC middleware pattern and keeps access limited to explicitly permissioned internal, support, operator, or audit callers under the current authorization model.

## Response Behavior

When Central PMS RBAC is enabled:

- unauthenticated callers receive `401` with `CENTRAL_PMS_RBAC_UNAUTHENTICATED`;
- authenticated callers without a matching permission receive `403` with `CENTRAL_PMS_RBAC_FORBIDDEN`;
- authorized callers receive the existing fiscal issuance status response when the reference exists;
- authorized callers still receive `404` with `FISCAL_ISSUANCE_REFERENCE_NOT_FOUND` when the reference is missing.

The response fields were not expanded in this slice.

## Current Auth Limitation

The project currently uses Central PMS endpoint metadata plus `CentralPmsRbacMiddleware`. Test and operator permission grants may be supplied by the existing permission header when `CentralPms:Rbac:AllowPermissionHeader` is enabled. This slice does not introduce a new identity provider, role system, mTLS requirement, or production security model.

Production deployment should keep RBAC enabled and wire the existing policy to the approved internal identity/service authorization source.

## Intentionally Not Implemented

- POS Server live read or mutation from the status endpoint.
- Fiscal issuance retry or FEQ behavior.
- Fiscal number allocation or editing.
- Payment finality, ExitAuthorization, gate behavior, refund/reversal, scheduler, or batch retry behavior.
- PDF, HTML, QR, or final BIR statutory receipt wording.

## Validation

Added tests cover:

- policy-to-permission catalog mapping;
- unauthenticated `401` behavior;
- unauthorized `403` behavior;
- authorized read success;
- authorized missing-reference `404`;
- GET-only route behavior;
- endpoint policy metadata.

Recommended next step: keep this status endpoint as a read-only internal/support/audit surface and only broaden access after deployment-specific identity and audit controls are validated.
