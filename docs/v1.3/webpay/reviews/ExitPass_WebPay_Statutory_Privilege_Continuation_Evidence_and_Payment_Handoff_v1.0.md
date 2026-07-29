# ExitPass WebPay Statutory Privilege Continuation Evidence and Payment Handoff

## Overall Verdict

PARTIALLY_SUPPORTED.

The merged WebPay statutory workflow supports canonical decision review, application intent, applied payable-basis payment, browser recovery, and authoritative Sales Invoice presentation. It is not ready for controlled UAT because ordinance availability, controlled document type, secure evidence upload/storage, opaque continuation tokens, pay-regular pending-review finality, and approval/payment race behavior are not yet implemented end to end.

## Critical Gaps

| ID | Gap | Required correction |
| --- | --- | --- |
| G-01 | Payment Orchestrator statutory Central PMS calls do not visibly send the Central PMS service identity/permission headers required by RBAC, and customer-facing errors can expose internal auth wording. | Fix service-to-service authentication and customer-safe error mapping first. |
| G-02 | WebPay does not consume authoritative ordinance availability before showing privilege request controls. | Add WebPay-facing availability proxy and hide request when unavailable, unsupported, expired, suspended, ambiguous, or unreachable. |
| G-04 | Secure ID photo capture/upload is absent; WebPay submits `evidenceCaptureRequested: false`. | Add protected evidence-upload/storage/scan contract and require durable evidence before reviewability. |
| G-09 | Approval-versus-ordinary-payment race is not durably arbitrated. | Add Central PMS race/finality command so a completed ordinary payment prevents retroactive privilege application. |

## High Gaps

- G-03: Document type is free-text instead of entitlement-specific controlled values.
- G-05: Operator Console has metadata-only evidence support but no authorized image retrieval.
- G-06: No server-issued opaque continuation token or continuation review page exists.
- G-08: No explicit Pay regular amount action/confirmation while review is pending.
- G-10: Late approval after completed ordinary payment is not classified as inapplicable.
- G-11: File type, size, image dimensions, EXIF stripping, scanning, and unsafe-content handling are unresolved.
- G-15: Manual/controlled UAT is not ready.

## Frozen Decisions

- Supported privileges: Senior Citizen and PWD only.
- Beneficiary presence is optional by default and mandatory only when the frozen applicable ordinance explicitly requires it.
- Senior Citizen document options: OSCA ID or Equivalent document.
- PWD document options: PWD ID or Equivalent document.
- Equivalent document requires a safe description.
- Evidence images must live in protected object storage or evidence vault, not PostgreSQL blobs, Base64 DTOs, browser storage, logs, payment payloads, fiscal records, or Sales Invoice records.
- Public continuation URLs must use opaque server-issued tokens and must not expose internal decision/application/session/evidence/correlation ids.
- Pay regular amount while pending is allowed only by explicit customer action and confirmation.
- Later approval must not mutate a completed ordinary payment or Sales Invoice.
- POS Server remains fiscal and Sales Invoice presentation authority.

## First Implementation Task

Persona: Codex G

Repository: `D:\SourceCodes\ExitPass`

Base branch: `dev`

Proposed branch: `feature/webpay-statutory-service-auth-safe-errors`

Scope:

- Fix Payment Orchestrator statutory calls to authenticate to Central PMS using the existing service identity and RBAC permission convention.
- Preserve `WEBPAY` source-channel attribution server-side.
- Add safe customer-facing error mapping for Central PMS RBAC/auth/service-channel failures.
- Prove no browser response exposes service identity ids, permission headers, raw downstream bodies, internal route details, or `CENTRAL_PMS_AUTHENTICATED_ACTOR_REQUIRED`.

Non-goals:

- No ordinance UI.
- No evidence upload.
- No continuation token.
- No pay-regular flow.
- No database change.
- No APT or POS Server change.

Tests:

- Payment Orchestrator Central PMS client tests.
- Payment Orchestrator statutory endpoint safe-error tests.
- Central PMS statutory RBAC regression tests.
- Security scan over changed files.

Manual testing:

- Not required for this first backend slice if automated validation passes.

## Sequencing Dependencies

1. Service-authentication correction must happen first because availability, decision submit, application intent, and continuation readback all rely on the Payment Orchestrator -> Central PMS service boundary.
2. Availability consumption should precede document/evidence UI because unsupported privileges must not collect ID evidence.
3. Document-type control should precede evidence upload so evidence is tied to the correct entitlement.
4. Evidence upload/storage and Operator Console reviewer access must converge before review is UAT-ready.
5. Opaque continuation tokens depend on recorded decision/evidence references.
6. Pay regular amount and approval/payment race enforcement must be implemented as one Central PMS finality contract.
7. Fiscal linkage follows the payment finality/race contract.
8. Integrated walkthrough and controlled UAT wait for all critical/high gaps.

## WebPay Controlled-UAT Status

WebPay controlled UAT is not authorized.

Required later scenarios include active ordinance, no ordinance, entitlement-specific document controls, camera capture, file upload, invalid/oversized file, scanning/storage failures, continuation redirect, refresh/restart, multiple tabs, approval before payment, rejection, expired request, Pay regular amount, approval/payment race, late approval after ordinary payment, no retroactive adjustment, authoritative Sales Invoice for the amount actually paid, and customer-safe downstream errors.

## Production Status

Production rollout is not authorized.

Production remains blocked by secure evidence handling, legal retention policy, continuation token privacy, payment finality race rules, manual UAT, and customer-safe service-auth failure handling.

## Evidence Anchors

- Central PMS availability route: `src/Services/CentralPms/src/ExitPass.CentralPms.Api/Endpoints/StatutoryDiscountDecisionEndpoints.cs`
- Central PMS statutory DTOs: `src/Services/CentralPms/src/ExitPass.CentralPms.Contracts/StatutoryDiscounts/StatutoryDiscountDecisionDtos.cs`
- Central PMS RBAC catalog: `src/Services/CentralPms/src/ExitPass.CentralPms.Application/Security/CentralPmsRbacPolicyCatalog.cs`
- Payment Orchestrator WebPay statutory routes: `src/Services/PaymentOrchestrator/src/ExitPass.PaymentOrchestrator.Api/Endpoints/WebPayPaymentIntentEndpoints.cs`
- Payment Orchestrator WebPay statutory DTOs: `src/Services/PaymentOrchestrator/src/ExitPass.PaymentOrchestrator.Contracts/WebPay/WebPayStatutoryDiscountDtos.cs`
- Payment Orchestrator Central PMS client: `src/Services/PaymentOrchestrator/src/ExitPass.PaymentOrchestrator.Infrastructure/Integrations/CentralPmsWebPayClient.cs`
- WebPay browser client/form/recovery: `src/Services/WebPayUi/src/webpay.ts`, `src/Services/WebPayUi/src/App.tsx`, `src/Services/WebPayUi/src/statutoryRecovery.ts`
- Operator Console metadata-only evidence DTOs/repository: `src/Services/CentralPms/src/ExitPass.CentralPms.Contracts/OperatorConsole/OperatorConsoleStatutoryDiscountEvidenceDtos.cs`, `src/Services/CentralPms/src/ExitPass.CentralPms.Infrastructure/OperatorConsole/OperatorConsoleStatutoryDiscountEvidenceRepository.cs`
- Canonical DB generated baseline: `D:\SourceCodes\exitpassdb_v1.2\build\generated\exitpass-full-object.generated.sql`

