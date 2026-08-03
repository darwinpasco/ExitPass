# ExitPass Statutory Cross-Channel End-to-End Lifecycle and Identifier Trace v1.0

## Canonical Lifecycle
| Step | Owner | Initiator | Input | Output | Dedupe | Restart and retry | Ordinary-payment effect | Persistence or contract | Status |
|---:|---|---|---|---|---|---|---|---|---|
| 1 | Central PMS | WebPay, APT, Operator Console | Ticket, plate, or parking session ID | `parkingSessionId`, `siteId`, `siteGroupId` | Session resolver semantics | Requery server state | Preserved when statutory lookup fails but ordinary session/payment remains valid | `core.parking_sessions` | Converged with proof gaps by channel |
| 2 | Central PMS and canonical DB | Channel service | Session/site facts | Canonical Site/Site Group IDs | Server-side scope | Local authority discarded | Preserved unless independent scope failure blocks all payment | `sites.sites`, `sites.site_groups` | Partially converged |
| 3 | Central PMS over canonical DB | WebPay, APT, Management Platform | Site, Site Group, entitlement | Coverage classification | No write | APT revalidates before cash; WebPay gates/recoveries query Central PMS | Preserved on statutory unavailability | Policy registry and coverage views | Partially converged |
| 4 | Channel UI | Customer or attendant | `SENIOR_CITIZEN` or `PWD` | Entitlement request | Controlled enum | Re-select allowed before submit | Preserved | Contracts | Converged |
| 5 | Future evidence service | WebPay, APT, Operator Console | Future opaque evidence refs | Evidence set/item refs | Future upload idempotency | Contract forbids local byte authority | Evidence-service failure must not block ordinary payment | I-003 contract; metadata routes | Not implemented |
| 6 | Central PMS decision-v2 | WebPay, APT, authorized Operator Console human | Session, entitlement, evidence metadata/ref | Decision command ID, request reference | `Idempotency-Key`; semantic hash where implemented | Duplicate submit returns or conflicts deterministically | Pending review preserves ordinary payment | Decision commands and review rows | Converged at Central PMS |
| 7 | Central PMS and Operator Console | Service-channel request | Decision command ID | Pending review | Command identity | WebPay rediscovery returns existing lifecycle | Ordinary payment remains available | Service-channel review table | Converged for readback; evidence incomplete |
| 8 | Operator Console human through Central PMS | Authorized reviewer | Decision command ID | Approved/rejected state | Review service policy and state | Terminal states are read-only | Ordinary payment remains possible if not applied | Decision/review tables | UI converged; backend legacy apply route gap |
| 9 | Central PMS | WebPay/APT/Operator Console | Decision command ID or rediscovery lookup | Approved lifecycle response | Existing decision identity | WebPay recovers same decision and continuation | Approval alone does not change payable amount | Decision readback DTO | Converged |
| 10 | Central PMS application-v1 | WebPay or APT service at payment time | Approved decision ID, current session | Application command ID, applied tariff snapshot | Application command identity | Retry revalidates current session and tariff | Failed statutory application preserves ordinary payment where available | Application commands | Converged in Central PMS; Operator Console route gap |
| 11 | Central PMS | Application-v1 service | Current tariff/session | Final payable basis | Application idempotency | APT pre-cash revalidation checks unchanged state | Ordinary payment can use current basis | Application model | Partially converged end to end |
| 12 | WebPay/Payment Orchestrator or APT/Central PMS | Payment channel | Applied payable basis | Payment or cash readiness | Payment/cash idempotency | APT uses two-stage revalidation | Ordinary path independent | Channel contracts | Partially converged |
| 13 | Payment channel | Customer or cashier | Final amount | Payment confirmation or tender/custody IDs | Payment/cash guards | Restart after cash has irreversible boundary | Ordinary path independent | Payment/cash records | APT statutory cash guarded pending fiscal proof |
| 14 | POS Server | Payment channel | Final fiscal request | Fiscal document ID | Idempotency key + semantic hash | Replay or conflict | Ordinary fiscal documents stay v1 | POS fiscal API | POS boundary implemented; channel linkage needs proof |
| 15 | POS Server | Fiscal request with applied facts | Central PMS applied facts | Immutable child snapshot | Unique decision/request/application refs | Replay preserves snapshot | No effect for ordinary fiscal document | `pos.fiscal_document_applied_statutory_facts` | Implemented at POS boundary |
| 16 | POS Server | Readback client | Fiscal document ID | SI render/presentation | Fiscal document ID | Reprint/readback uses persisted facts | Ordinary SI remains available | POS digital SI models | Implemented; compliance text separate |
| 17 | Central PMS, POS Server, future audit views | Operations/auditors | Decision/application/payment/fiscal refs | Audit/reconciliation records | External refs and semantic hashes | Readback only | No ordinary-payment block | Mixed | Partially converged |

## Identifier Continuity
- `parkingSessionId` is resolved by Central PMS and reused through decision, application, and fiscal facts.
- `statutoryDiscountDecisionCommandId` is the canonical statutory decision identity exposed to WebPay recovery and POS applied facts.
- `statutoryPayableBasisApplicationCommandId` is created only during payment-time application and is required by POS statutory fiscal facts.
- `requestReference` is safe when already canonical and is used by WebPay rediscovery as the opaque continuation reference in current implementation.
- POS `fiscalDocumentId` and semantic hash are POS-local fiscal authorities, not statutory eligibility authorities.

## Missing Lifecycle Proofs
- End-to-end WebPay/APT-to-POS statutory fiscal payload submission is not fully proven in this audit.
- Secure evidence byte upload, preview, retention, and deletion are contract-only.
- Management Platform policy coverage is read-only but not yet fully aligned to canonical I-006 Site-LGU inheritance views.
