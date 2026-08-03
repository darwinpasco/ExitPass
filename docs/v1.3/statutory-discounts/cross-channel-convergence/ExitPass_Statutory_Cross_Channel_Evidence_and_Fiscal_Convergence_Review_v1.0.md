# ExitPass Statutory Cross-Channel Evidence and Fiscal Convergence Review v1.0

## Evidence Convergence
| Concern | Current implementation | Status | Required next step |
|---|---|---|---|
| WebPay evidence reference submission | Decision contracts can carry evidence-related safe metadata; secure byte upload is not implemented | CONTRACT_FROZEN_NOT_IMPLEMENTED | Implement evidence upload authorization and opaque references. |
| APT evidence reference submission | APT docs prohibit durable image storage; runtime secure upload not implemented | CONTRACT_FROZEN_NOT_IMPLEMENTED | Implement desktop upload consumer with no SQLite image persistence. |
| Operator Console evidence capture | Evidence metadata routes exist under Operator Console draft endpoints | PARTIAL_METADATA_ONLY | Add secure object storage and reviewer preview. |
| Raw evidence storage | Contract prohibits PostgreSQL/browser/APT/POS raw evidence bytes | CONVERGED_BY_CONTRACT | Add automated proof when runtime exists. |
| Protected reviewer preview | Not implemented | NOT_IMPLEMENTED | Implement short-lived server-authorized preview. |
| Evidence retention/deletion | I-003 contract only | NOT_IMPLEMENTED | Implement retention, hold, deletion, and reconciliation worker. |
| Evidence access logging | Contract only | NOT_IMPLEMENTED | Add audit event catalog runtime. |
| POS evidence privacy | POS runtime rejects sensitive evidence markers and does not need evidence | IMPLEMENTED_AT_POS_BOUNDARY | Keep evidence-free fiscal payload tests. |

## Fiscal Convergence
| Concern | Current implementation | Status | Required next step |
|---|---|---|---|
| Applied statutory fiscal facts contract | POS Server API has `AppliedStatutoryFiscalFactsRequest` | IMPLEMENTED | Keep contract versioned. |
| Runtime validation | POS validates entitlement, benefit, VAT, policy reference, source channel, Site/session scope, totals, and privacy-sensitive fields | IMPLEMENTED | Review allowed source channels. |
| Semantic hash | Ordinary requests use `sha256:v1`; statutory requests use `pos-server-fiscal-document-create:sha256:v2` | IMPLEMENTED | Keep replay/conflict tests. |
| Immutable persistence | `pos.fiscal_document_applied_statutory_facts` stores one immutable row per statutory fiscal document with unique decision/request/application refs | IMPLEMENTED | Keep DB validation. |
| Digital Sales Invoice presentation | POS render/presentation includes applied statutory facts where present | IMPLEMENTED | Compliance approval for final text remains separate. |
| WebPay to POS linkage | Not fully proven in this audit | PARTIALLY_CONVERGED | Add WebPay statutory fiscal end-to-end test. |
| APT to POS linkage | APT docs state statutory cash remains guarded pending complete fiscal linkage | PARTIALLY_CONVERGED | Complete APT statutory cash enablement only after POS payload proof. |
| Ordinary fiscal flow | Remains v1 and does not require statutory facts | CONVERGED | None. |
| Duplicate fiscalization | POS idempotency and unique applied facts prevent duplicate fiscal snapshots for same application | IMPLEMENTED_AT_POS_BOUNDARY | Prove across WebPay/APT retries. |

## Key Fiscal Gap
POS Server is ready to accept and persist final applied statutory facts, but cross-channel payment integrations must prove they supply those facts whenever a statutory benefit is applied. Without that proof, WebPay/APT statutory payment readiness remains partial even though POS boundary validation is strong.
