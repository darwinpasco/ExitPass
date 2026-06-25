# ExitPass POS Server API Contract v1.0 Approval-Readiness Review

## 1. Review Summary

This review assesses `ExitPass_POS_Server_API_Contract_v1.0.md` after the technical review and targeted fixes.

The API Contract now preserves the approved POS/Invoicing BRD baseline and approved POS Server System Design baseline. It covers the expected API families, keeps route families provisional, avoids final DTO/database/event-schema commitments, and carries open BIR/accounting/security/privacy questions forward.

The targeted technical review fixes are present:

- Fiscal issuance validation semantics are explicitly stated before Sales Invoice issuance.
- Failed or blocked validation prevents Sales Invoice issuance and prevents Central PMS from issuing ExitAuthorization.
- Timeout and completion-unknown status concepts are included.
- `UNAUTHORIZED_CALLER` and `FISCAL_ACTION_NOT_AUTHORIZED` are included as proposed error codes.
- Customer-facing digital SI URL access is explicitly separated from internal POS Server digital SI/presentation APIs.
- POS/fiscal events are explicitly audit, integration, and observability signals only and do not grant payment finality or ExitAuthorization.

## 2. Approval Recommendation

Recommendation: Ready for architecture/stakeholder review and approval as the POS Server API Contract v1.0 baseline, subject to unresolved open questions remaining assigned to their downstream decision owners.

No P0 approval blockers were found. No P1 should-fix findings were found.

## 3. Blocking Findings

No P0 findings were identified.

| ID | Severity | Section | Finding | Why it matters | Recommended correction |
| --- | --- | --- | --- | --- | --- |
| None | P0 | Not applicable | No approval blocker found. | The API Contract preserves the approved authority model and does not grant POS Server payment finality, ExitAuthorization, gate/exit execution, or refund finality authority. | No blocking correction required. |

## 4. Should-Fix Findings

No P1 findings were identified.

| ID | Severity | Section | Finding | Why it matters | Recommended correction |
| --- | --- | --- | --- | --- | --- |
| None | P1 | Not applicable | No should-fix item found before approval. | The technical review P1 items were addressed and the draft is approval-ready. | No P1 correction required. |

## 5. Non-Blocking Findings

No P2 findings were identified.

| ID | Severity | Section | Finding | Why it matters | Recommended correction |
| --- | --- | --- | --- | --- | --- |
| None | P2 | Not applicable | No non-blocking correction found in this review. | The draft is sufficient for approval as a baseline contract. | No P2 correction required. |

## 6. Editorial Findings

No editorial findings were identified.

| ID | Severity | Section | Finding | Why it matters | Recommended correction |
| --- | --- | --- | --- | --- | --- |
| None | Editorial | Not applicable | No editorial correction found in this review. | The document is readable and consistently marks provisional route families and open questions. | No editorial correction required. |

## 7. BRD and System Design Alignment Review

| Review item | Result | Evidence |
| --- | --- | --- |
| Approved POS/Invoicing BRD baseline | Pass | Sections 2-4 align to the approved BRD authority and fiscal issuance model. |
| Approved POS Server System Design baseline | Pass | Sections 3-5, 10-22, and 23-28 translate System Design API impact areas into contract families. |
| Platform-wide POS/Invoicing | Pass | Sections 2, 5, 12, and 23-28 cover WebPay, APM, Cashier POS, EC/continuity, operator-assisted flows, and future channels. |
| Site-level POS Server fiscal authority | Pass | Sections 4, 10, 13, 14, and 28 preserve resolved Site POS Server authority. |
| Channels/terminals as children | Pass | Sections 5, 13, 23-28 preserve child channel/terminal treatment. |
| Sales Invoice primary fiscal output | Pass | Section 10 centers fiscal issuance on Sales Invoice. |
| Printed/digital SI consistency | Pass | Sections 6, 11, 12, and 19 require consistent canonical fiscal facts. |
| Open decisions remain visible | Pass | Sections 8, 12, 14, 17-20, 22, 30, and Appendix C keep unresolved items open. |

## 8. Authority Boundary Review

No route family, candidate operation, status, error, or wording grants POS Server authority over payment finality, ExitAuthorization, gate/exit execution, or payment reversal/refund finality.

| Authority area | Result | Evidence |
| --- | --- | --- |
| Central PMS payment finality | Pass | Sections 4, 6, 10, 21, 23-28, and 31 preserve Central PMS authority. |
| Central PMS ExitAuthorization | Pass | Sections 4, 6, 10, 21, 23-28, and 31 prevent POS Server or channels from issuing authorization. |
| POS Server no ExitAuthorization | Pass | Sections 4, 6, 10, 21, 23-28, and risk table explicitly preserve this rule. |
| Payment Orchestrator finality | Pass | Section 4 states Payment Orchestrator must not declare platform payment finality. |
| WebPay finality | Pass | Sections 4 and 23 state WebPay must not declare platform payment finality. |
| Gate/exit execution | Pass | Section 4 states gate/exit execution must not bypass Central PMS. |
| Refund/reversal finality | Pass | Sections 4 and 16 keep money movement finality with Central PMS/payment provider. |
| POS/fiscal events | Pass | Section 22 states events are audit/integration/observability only and do not grant payment finality or ExitAuthorization. |

## 9. Fiscal Issuance Contract Review

Section 10 now includes explicit validation semantics before SI issuance:

- Resolved Site and Site POS Server match.
- Request is scoped to the correct Site POS Server.
- Central PMS payment finality context is present and acceptable.
- Channel/terminal registration is valid where applicable.
- Channel/terminal is active or allowed for the requested operating mode.
- Fiscal identity is configured and active.
- Numbering policy is configured and available.
- Fiscal line basis is present and eligible.
- Entitlement/VAT privilege context is acceptable where applicable.
- Digital SI delivery configuration is valid where digital delivery is requested.
- No recovery, reset, fiscal lock, or continuity block prevents issuance.

Failed validation behavior is also covered:

- POS Server returns blocked or failed semantic response.
- Response identifies validation area at business/error-code level.
- POS Server does not issue the Sales Invoice.
- Central PMS does not issue ExitAuthorization on failed or blocked fiscal issuance response.

Result: Pass.

## 10. Idempotency, Timeout, and Retry Review

The contract supports:

- `Idempotency-Key` for side-effecting operations.
- Duplicate request detection.
- Idempotent replay.
- Idempotency conflict.
- Timeout handling.
- Completion-unknown handling.
- Status lookup before retry.
- No duplicate Sales Invoice on retry.
- Open decisions for idempotency key scope, payload mismatch, sequence reservation, failed issuance, abandoned issuance, and idempotency retention.

Section 29 now includes `Timed out` and `Completion unknown`. It also states these are not successful fiscal issuance states and cannot support ExitAuthorization without status lookup/retry semantics.

Result: Pass.

## 11. Digital SI URL and QR Presentation Review

| Review item | Result | Evidence |
| --- | --- | --- |
| POS Server returns digital SI URL | Pass | Sections 4, 10, 12, and channel integration sections. |
| Digital SI URL points to same issued SI as printed SI | Pass | Section 12. |
| Digital SI URL anti-tampering and data minimization | Pass | Section 12. |
| URL access/expiry/auth/audit open | Pass | Sections 12 and 30. |
| Customer URL is separate trust boundary | Pass | Sections 5, 7, and 12. |
| QR presentation metadata to channels/terminals | Pass | Section 12. |
| QR not APM-only | Pass | Sections 12, 24-28. |
| QR does not create fiscal authority | Pass | Sections 4 and 12. |

Result: Pass.

## 12. API Family Coverage Review

All expected API families are present and coherent.

| API family / integration area | Result |
| --- | --- |
| Fiscal issuance | Pass |
| Fiscal documents | Pass |
| Digital SI URL and presentation | Pass |
| Channel/terminal registry | Pass |
| Fiscal identity configuration | Pass |
| Reprints | Pass |
| Fiscal adjustments | Pass |
| X-read and Z-read | Pass |
| BIR Sales Summary and Annex E | Pass |
| EJ, POSLog, and export | Pass |
| Fiscal reset and recovery | Pass |
| Exception and retry status | Pass |
| Audit and event impact | Pass |
| WebPay integration | Pass |
| APM integration | Pass |
| Cashier POS integration | Pass |
| EC Device / Continuity Terminal integration | Pass |
| Operator-assisted integration | Pass |
| Future channel pattern | Pass |

## 13. API Boundary / Over-Specification Review

The contract remains within API contract scope.

| Boundary | Result | Evidence |
| --- | --- | --- |
| Final endpoint paths | Pass | Route families are explicitly provisional. |
| Final DTO schemas | Pass | Request/response shapes are semantic; exact DTO fields remain pending review. |
| Database schema | Pass | Section 2 and Appendix C avoid final tables, columns, indexes, constraints, and migrations. |
| Final event schemas | Pass | Section 22 identifies event impact without final event schemas. |
| Final enum storage | Pass | Section 29 says status taxonomy is planning-contract level and pending Database Design alignment. |
| Implementation internals | Pass | The document defines contract semantics, not implementation modules. |

## 14. Status and Error Model Review

The proposed status and error models are sufficient for approval-readiness and are clearly marked as pending review/future alignment.

Status concepts include:

- Issued.
- Failed.
- Timed out.
- Completion unknown.
- Retry pending.
- Blocked.
- Duplicate / idempotent replay.
- Recovery check passed/failed.
- Digital SI URL active/expired/revoked.

Proposed error codes include:

- `UNAUTHORIZED_CALLER`.
- `FISCAL_ACTION_NOT_AUTHORIZED`.
- `FISCAL_ISSUANCE_TIMEOUT`.
- `OFFLINE_FISCAL_ISSUANCE_NOT_ALLOWED`.
- Other fiscal issuance, identity, numbering, digital SI, report/export, reset, and recovery errors.

No error/status concept grants wrong authority.

Result: Pass.

## 15. Security/RBAC and Trust Boundary Review

The contract covers:

- Separate internal API and customer digital SI URL trust boundaries.
- Internal service authentication.
- Public/customer digital SI URL access as open for security/privacy review.
- RBAC for privileged fiscal operations.
- Audit for privileged actions.
- Proposed authorization/forbidden error codes.
- High-risk actions: reprint, adjustment, fiscal identity configuration, report/export access, reset, recovery, and privileged configuration.

Final permission matrix, auth mechanism, token format, claims model, and policy enforcement remain open, which is appropriate.

Result: Pass.

## 16. Open Questions Review

The Open Questions section keeps the major unresolved API decisions visible and correctly scoped:

- Final route naming.
- DTO boundaries.
- Idempotency key scope.
- Duplicate issuance handling.
- Sequence-gap behavior.
- Digital SI URL token/access model.
- URL expiry policy.
- Public/customer SI URL authentication/access model.
- QR presentation payload responsibility.
- WebPay fiscal identity.
- APM printing model.
- Terminal/channel registry fields.
- Fiscal identity fields.
- X/Z scope.
- Report/export formats.
- Adjustment workflow sequencing.
- Refund/reversal relationship with Central PMS/provider.
- Recovery continuity API.
- Offline fiscal issuance restriction representation.
- Audit/event publication contracts.
- Status/error model.
- Security/RBAC model.

No decided item is incorrectly listed as open. No unresolved BIR/accounting/security/privacy item is silently decided.

Result: Pass.

## 17. Final Recommendation

Final recommendation: approve the API Contract for architecture/stakeholder review and use it as the POS Server API Contract v1.0 baseline, with unresolved questions carried into the appropriate downstream API, Database Design, Security/Privacy, Engineering Pack, and BIR/accreditation confirmation workstreams.

Approval should not be interpreted as final approval of endpoint paths, DTO schemas, event schemas, database schema, status-code storage, public URL token/auth model, fiscal numbering, WebPay fiscal identity, APM printing model, X/Z scope, export formats, offline fiscal issuance, or security/RBAC matrix.

## 18. Recommended Next Step

Recommended next step: mark `ExitPass_POS_Server_API_Contract_v1.0.md` as approved baseline after stakeholder/architecture acceptance, then proceed with POS Server Database Design planning and Engineering Pack planning using the API open questions and provisional route family summary as inputs.
