# ExitPass POS Server API Contract Impact Map

Status: Initial API contract planning artifact only

This impact map identifies affected documents, components, and workstreams for `ExitPass POS Server API Contract v1.0`. It does not define final endpoint paths, DTOs, schemas, database tables, or implementation plans.

## 1. Document and Design Impact

| Area | Impact |
| --- | --- |
| POS Server API Contract | New companion contract must define route families, common rules, auth, idempotency, error/status model, request/response semantics, and integration responsibilities. |
| POS Server Database Design | API decisions will drive persistence needs for fiscal documents, sequences, idempotency, URL access, registry, identity config, audit, reports, exports, reset/recovery, and event/outbox state. |
| ExitPass API Contract Pack v1.3 | The POS Server API Contract must align with platform contract conventions and Central PMS authority model. |
| Engineering Pack v1.3 | Implementation plan, test plan, runbooks, certification support, eventing, observability, and rollout controls must reflect API decisions. |
| POS/Invoicing BRD | No BRD changes expected; API Contract implements approved business requirements. |
| POS Server System Design | No System Design changes expected unless API planning exposes a design gap. |

## 2. Core Platform Impact

| System/component | API contract impact |
| --- | --- |
| Central PMS | Primary caller for payment-linked SI issuance; records fiscal reference; withholds ExitAuthorization until SI success or controlled exception closure. |
| Payment Orchestrator | Reports verified provider outcome to Central PMS only; does not call POS Server as finality authority unless a future design explicitly routes through Central PMS control. |
| WebPay | Receives or displays issued SI/digital SI URL through approved Central PMS/POS Server flow; must not declare payment finality. |
| APM | Presents/prints POS Server-issued SI and QR where supported; APM printing model remains open. |
| Cashier POS | Uses Site POS Server fiscal APIs for issuance presentation, reprint, adjustment, cashier/session context, and status where authorized. |
| EC Device / Continuity Terminal | Uses same Site POS Server fiscal authority when activated; offline issuance remains restricted unless approved. |
| Operator-assisted payment | Uses resolved Site POS Server for SI presentation and exception workflows; operator cannot bypass Central PMS finality/authorization. |
| Future channels | Must follow child channel/terminal registry and presentation pattern. |

## 3. API Route Family Impact

| Route family | Impact / planning need |
| --- | --- |
| Fiscal issuance | Idempotent Sales Invoice issuance, fiscal identity/status return, digital SI URL return, failure/retry status. |
| Fiscal document | Document lookup/status, printed/digital consistency, original document linkage, re-access support. |
| Digital SI URL and presentation | URL access policy, expiry, authentication/access, audit, QR presentation metadata, channel/terminal display/print capability. |
| Channel/terminal registry | Site association, channel type, identity, capability, status, continuity state, audit. |
| Fiscal identity configuration | Taxpayer, Site/branch, POS Server identity, channel/terminal identity, MIN/PTU/serial/software/supplier metadata once confirmed. |
| Reprint | Authorization, reason, repeated-output labeling, audit, original document reference. |
| Fiscal adjustment | Void/refund/cancel/return request/status, original document linkage, value handling, payment reversal context, audit. |
| X-read/Z-read | Request/status/export and approved fiscal scope. |
| BIR Sales Summary/Annex E | Report request/status/export and statutory category support. |
| EJ/POSLog/fiscal exports | Export request/status/output metadata and final format support. |
| Fiscal reset/recovery | Reset request/approval/status, continuity check/status, supervised recovery. |
| Exception/retry status | Pending issuance, failure, retry, manual exception closure, reconciliation tags. |
| Audit/event impact | Event publication and audit retrieval/status impact without final event schema in planning. |

## 4. Audit/Event Impact

API Contract planning affects:

- Fiscal issuance requested/issued/failed/timed out.
- Digital SI URL created/accessed where required.
- Reprint requested/completed.
- Adjustment requested/issued.
- X/Z read generated.
- BIR Summary/Annex E generated.
- EJ/POSLog/export generated.
- Fiscal reset requested/approved/completed.
- Terminal/channel registered/updated.
- Fiscal identity changed.
- Recovery continuity check passed/failed.
- Supervised recovery approved/completed.

Final event names, payloads, outbox ownership, delivery guarantees, replay behavior, and retention remain open for API Contract Pack and Engineering Pack.

## 5. Security and Privacy Impact

| Area | Impact |
| --- | --- |
| Authentication | Internal API consumers and customer SI URL access require separate treatment. |
| Authorization | High-risk fiscal APIs need role separation and approval controls. |
| Digital SI URL | Access model, expiry, least-data exposure, anti-tampering, and audit treatment remain open. |
| Evidence handling | Diplomat and entitlement evidence references may affect API boundaries and privacy review. |
| Fiscal identity configuration | Changes must be privileged, auditable, and probably approval-controlled. |
| Recovery/reset | Recovery and fiscal reset APIs must require strong authorization and audit. |

## 6. BIR/Accreditation Impact

API decisions are affected by:

- Sales Invoice numbering and sequence-gap behavior.
- Adjustment document numbering.
- Reset counter display/append behavior.
- MIN/PTU/serial/software/supplier assignment.
- X/Z scope.
- Report/export formats.
- EJ/POSLog format and export requirements.
- APM printing model.
- WebPay fiscal identity.
- Accreditation sample set.
- Offline fiscal issuance approval or restriction.

The API Contract must keep these open until confirmed by the proper decision owners.

## 7. Testing and Certification Impact

Future test planning should cover:

- Idempotent SI issuance and duplicate request behavior.
- Fiscal issuance success before ExitAuthorization.
- Fiscal issuance failure blocking ExitAuthorization.
- Digital SI URL return and access controls.
- QR presentation metadata for APM, Cashier POS, EC/continuity, operator-assisted, and future channels where supported.
- Printed/digital SI consistency.
- X/Z, BIR Summary, Annex E, EJ, POSLog, and exports.
- Reprint and adjustment controls.
- Reset/recovery continuity.
- Offline issuance disabled/restricted behavior.
- Security/RBAC enforcement for high-risk APIs.

## 8. Operations and Runbook Impact

Operational planning should cover:

- Monitoring issuance failures and retries.
- Tracking pending fiscal exceptions.
- Digital SI URL access health.
- Channel/terminal registration and availability.
- X/Z close status.
- Report/export generation.
- Counter/GTA/EJ hash continuity alerts.
- Recovery/failover status.
- Privileged fiscal action audit.
- API versioning and deprecation posture.

## 9. Risk Summary

| Risk | Impact | Mitigation |
| --- | --- | --- |
| API contract accidentally grants ExitAuthorization authority to POS Server | Core authority violation. | Keep ExitAuthorization out of POS Server API families. |
| Idempotency is weak | Duplicate SI or inconsistent fiscal reference. | Define explicit idempotency/retry model in full API Contract. |
| Digital SI URL access is overexposed | Privacy/security breach. | Resolve access, expiry, auth, and audit model in security/privacy review. |
| QR presentation contract is APM-only | Future channels and assisted flows diverge. | Keep QR presentation as channel/terminal capability. |
| Offline issuance path is implied | Unapproved fiscal sequence risk. | Keep offline issuance disabled/restricted unless approved. |
| API finalizes BIR/accounting questions too early | Accreditation or compliance failure. | Keep unresolved fiscal items open until confirmed. |
| Database design assumptions leak into API planning | Tight coupling before schema design. | Define contract responsibilities without tables/columns. |
