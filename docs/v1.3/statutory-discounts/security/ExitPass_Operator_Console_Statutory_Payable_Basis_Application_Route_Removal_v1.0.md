# ExitPass Operator Console Statutory Payable-Basis Application Route Removal v1.0

## Executive Decision

The legacy Operator Console payable-basis application route is removed:

`POST /v1/ops/operator-console/statutory-discounts/{validationId}/apply-payable-basis`

Operator Console remains an eligibility review surface. It may approve or reject statutory parking privilege requests, but it must not apply an approved privilege to the payable basis. Payment-time statutory application remains owned by the WebPay and Assisted Payment Terminal service-channel flows through the Central PMS statutory decision/application contract.

## Compatibility Decision

No active compatibility requirement was retained for the Operator Console route. The route is removed rather than kept as a compatibility hard-denial endpoint.

The removed route was still externally callable and mapped to the former `OperatorConsoleStatutoryDiscountPayableBasisApply` named policy. That mapping could reach the payable-basis application service and create application command and tariff evidence. Removing the route is the narrowest correction because the Operator Console UI already presents approved and rejected reviews as read-only and no longer exposes an amount-application action.

## Preserved Authority

- Operator Console review queue, detail, approve, and reject endpoints remain available.
- Terminal decision readback remains available.
- Central PMS application-v1 and payable-basis mutation remain available to the service-channel application flow.
- WebPay and Assisted Payment Terminal application paths remain separate from Operator Console review authority.
- Ordinary payment remains independent from statutory review and application.

## Permission Boundary

The former `OperatorConsoleStatutoryDiscountPayableBasisApply` policy mapping is removed from the Central PMS RBAC policy catalog.

The permission identifier `statutory-discounts.payable-basis.apply` remains in the Management Platform inventory as a target-only payment-time permission until a governed service-channel route maps it. It is not mapped to any Operator Console route and is not assigned to the Operations Supervisor target bundle.

`reconciliation.manage` does not bypass the removal because there is no Operator Console route to authorize.

## No-Workflow-Write Posture

Calls to the legacy Operator Console path return route-not-found before reaching application services. They cannot create or update:

- statutory decision commands
- Operator Console reviews
- statutory payable-basis application commands
- tariff snapshots
- payment attempts
- payment confirmations
- terminal-cash records
- fiscal issuance references

The payable-basis application service remains registered because the shared statutory decision/application facade still uses the Central PMS application machinery for payment-time service-channel application.

## Validation Scope

Focused validation covers:

- route absence from endpoint metadata
- route absence from Swagger/OpenAPI
- anonymous, human reviewer, apply-permission, reconciliation, and service-principal requests returning not found
- preserved Operator Console approve and reject behavior
- preserved WebPay/APT service-channel application behavior
- no application command or applied tariff snapshot through the legacy Operator Console route
- no Operator Console browser client method for payable-basis application

## Exclusions

This slice does not implement new WebPay, APT, POS Server, Management Platform, RBAC administration, evidence storage, payment, fiscal, statutory policy, or canonical database behavior.
