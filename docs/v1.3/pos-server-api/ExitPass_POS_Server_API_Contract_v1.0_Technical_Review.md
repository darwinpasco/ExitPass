# ExitPass POS Server API Contract v1.0 Technical Review

## 1. Review Summary

This review assesses `ExitPass_POS_Server_API_Contract_v1.0.md` against the approved POS/Invoicing BRD, approved POS Server System Design, and POS Server API Contract planning artifacts.

The API Contract draft preserves the approved authority model and provides complete first-pass coverage of the required API families. The draft correctly treats route families and status/error codes as provisional, avoids final DTO/database/event-schema commitments, and keeps BIR/accounting/security/privacy open questions visible.

Two P1 issues should be addressed before approval-readiness review:

- The Fiscal Issuance API family should explicitly state POS Server validation of resolved Site, Site POS Server authority, fiscal identity readiness, numbering readiness, and fiscal eligibility before issuance.
- The Status Model should include explicit timeout / completion-unknown concepts to support safe retry and status lookup after network or POS Server uncertainty.

No P0 authority or core-decision violations were found.

## 2. Overall Recommendation

Recommendation: Ready for targeted revision, not redesign.

The draft is structurally sound and aligned with the approved BRD/System Design. Apply the P1 targeted edits, then proceed to approval-readiness review.

## 3. Blocking Findings

No P0 findings were identified.

| ID | Severity | Section | Finding | Why it matters | Recommended correction |
| --- | --- | --- | --- | --- | --- |
| None | P0 | Not applicable | No approval blocker found. | The API Contract draft does not violate the Central PMS/POS Server authority split or core fiscal issuance sequence. | No blocking correction required. |

## 4. Should-Fix Findings

| ID | Severity | Section | Finding | Why it matters | Recommended correction |
| --- | --- | --- | --- | --- | --- |
| POS-API-TR-P1-001 | P1 | Section 10 Fiscal Issuance API Family | The Fiscal Issuance API family states that Central PMS requests SI issuance after verified payment finality, but it does not explicitly require POS Server to validate resolved Site, Site POS Server authority, fiscal identity readiness, numbering readiness, and fiscal eligibility before issuance. | The approved System Design requires POS Server validation before issuance. Without explicit contract semantics, the API could accept issuance requests with wrong Site authority or incomplete fiscal configuration. | Add explicit validation semantics for resolved Site/Site POS Server match, fiscal identity readiness, numbering policy readiness, channel/terminal registration where applicable, and fiscal eligibility before any SI is issued. |
| POS-API-TR-P1-002 | P1 | Section 29 Status Model | The Status Model includes `failed`, `retry pending`, and `duplicate / idempotent replay`, but does not explicitly include timeout or completion-unknown status concepts. Section 10 response semantics mentions unknown/timeout state, but Section 29 does not carry it into the taxonomy. | Timeout and unknown completion are central to safe idempotent retry. Central PMS needs a clear status category for "request timed out, issuance state unknown" before deciding to query, retry, or withhold ExitAuthorization. | Add explicit provisional status concepts for `timed out` and/or `completion unknown`, and relate them to status lookup before retry and no duplicate SI behavior. |

## 5. Non-Blocking Findings

| ID | Severity | Section | Finding | Why it matters | Recommended correction |
| --- | --- | --- | --- | --- | --- |
| POS-API-TR-P2-001 | P2 | Section 9 Canonical Error Model | The proposed error model covers fiscal, report/export, recovery, offline, and digital SI failures, but does not include a general authorization/forbidden error for privileged internal APIs. | Security/RBAC is open, but high-risk API families will need a consistent way to report denied privileged actions beyond digital SI public access denial. | Add proposed contract codes such as `UNAUTHORIZED_CALLER` and/or `FISCAL_ACTION_NOT_AUTHORIZED`, marked pending review. |
| POS-API-TR-P2-002 | P2 | Section 12 Digital SI URL and Presentation API Family | The section covers URL access, expiry, authentication/access, audit, and QR metadata, but could more explicitly distinguish customer-facing URL access from internal URL retrieval/presentation metadata APIs. | The trust boundary is already stated in Sections 5 and 7, but repeating it in the API family reduces risk that public/customer access is implemented like an internal service API. | Add one sentence that customer digital SI URL access is a separate trust boundary from internal POS Server presentation APIs. |
| POS-API-TR-P2-003 | P2 | Section 22 Audit and Event API Impact | Audit/event impact lists candidate events but does not explicitly mention that event publication must not become the source of payment finality or ExitAuthorization. | The authority model is preserved elsewhere, but event consumers should not infer authority from event publication. | Add a short statement that fiscal events are informational/audit/integration events and do not grant payment finality or ExitAuthorization. |

## 6. Editorial Findings

| ID | Severity | Section | Finding | Why it matters | Recommended correction |
| --- | --- | --- | --- | --- | --- |
| POS-API-TR-ED-001 | Editorial | Section 32 Appendix A | The appendix uses broad route family candidates and marks them provisional. This is acceptable, but a future approval draft may be easier to review if route families are grouped by caller type: Central PMS internal, channel/terminal internal, admin/audit internal, and customer digital SI access. | Grouping by trust boundary helps reviewers distinguish internal APIs from customer-facing access. | Consider adding a trust-boundary column in the route family summary during targeted revision or approval-readiness cleanup. |

## 7. BRD and System Design Alignment Review

| Review item | Result | Notes |
| --- | --- | --- |
| POS/Invoicing platform-wide, not APM-only | Pass | Sections 2, 5, 12, and 23-28 cover WebPay, APM, Cashier POS, EC/continuity, operator-assisted flows, and future channels. |
| Site-level POS Server fiscal authority | Pass | Sections 4, 10, 13, and 28 preserve resolved Site POS Server authority. |
| Channels/terminals as children | Pass | Sections 5, 13, 23-28 model channels/terminals under the Site POS Server. |
| Sales Invoice primary parking fiscal output | Pass | Sections 2 and 10 center on Sales Invoice issuance. |
| Printed/digital SI consistency | Pass | Sections 6, 11, 12, and 19 require consistency from canonical fiscal records. |
| POS Server returns digital SI URL | Pass | Sections 4, 10, 12, and 23-28 cover digital SI URL return/presentation. |
| QR presentation not APM-only | Pass | Sections 12 and 23-28 cover non-APM QR support where approved. |
| Offline fiscal issuance restricted by default | Pass | Sections 20, 26, 30, 31, and Appendix C preserve the restriction. |
| Open compliance/accounting/security/privacy items visible | Pass | Sections 8, 12, 14, 17-20, 30, and Appendix C preserve open items. |

## 8. Authority Boundary Review

No authority boundary violation was found.

The draft correctly states:

- Central PMS owns payment finality.
- Central PMS owns ExitAuthorization.
- POS Server APIs do not issue, approve, create, mutate, or bypass ExitAuthorization.
- Payment Orchestrator and WebPay must not declare platform payment finality.
- Gate/exit execution must not bypass Central PMS.
- Vendor PMS / HikCentral acknowledgment is synchronization only.
- POS Server owns fiscal issuance and fiscal document lifecycle only.

The `Link payment reversal context` candidate operation in Section 16 remains acceptable because the contract also states that Central PMS/payment provider owns refund/reversal finality.

## 9. Fiscal Issuance API Review

The Fiscal Issuance API family covers:

- Central PMS issuance request after verified payment finality.
- Idempotent issuance.
- Sales Invoice issuance.
- Fiscal document identity/status return.
- Digital SI URL return where applicable.
- Central PMS fiscal reference recording and subsequent ExitAuthorization.
- POS Server not issuing ExitAuthorization.
- Failure, pending, retry, blocked, and unknown/timeout response concepts.

Gap: Section 10 should explicitly state validation semantics for resolved Site, Site POS Server authority, fiscal identity readiness, numbering readiness, channel/terminal registration where applicable, and fiscal eligibility before issuance. See `POS-API-TR-P1-001`.

## 10. Idempotency and Retry Review

The draft includes strong first-pass idempotency coverage:

- `Idempotency-Key` required for side-effecting operations.
- Duplicate detection for same fiscal operation.
- Idempotent replay behavior.
- Idempotency conflict behavior.
- Timeout status lookup before retry.
- Retry behavior preventing duplicate fiscal documents.
- Open questions for sequence gaps, reserved numbers, failed issuance, abandoned issuance, idempotency key scope, payload mismatch, and retention period.

Gap: timeout / completion-unknown status should be represented in the Status Model. See `POS-API-TR-P1-002`.

## 11. Digital SI URL and QR Presentation Review

The draft correctly states:

- POS Server returns digital SI URL.
- Digital SI URL points to the same issued SI as the printed SI.
- Digital SI URL must not allow unauthorized modification.
- Digital SI URL must not expose unnecessary sensitive data.
- URL access policy, expiry policy, authentication/access model, and audit treatment remain open.
- QR presentation metadata may be provided to channels/terminals.
- QR presentation is not APM-only.
- QR presentation does not create fiscal authority.
- APM, Cashier POS, EC Device / Continuity Terminal, operator-assisted terminals, and future channels may support QR presentation where approved.

Non-blocking improvement: repeat the customer/internal trust-boundary distinction in Section 12 itself. See `POS-API-TR-P2-002`.

## 12. API Family Coverage Review

All required API families are present.

| API family / integration area | Result |
| --- | --- |
| Fiscal Issuance API Family | Present; P1 validation clarity needed. |
| Fiscal Document API Family | Present. |
| Digital SI URL and Presentation API Family | Present. |
| Channel and Terminal Registry API Family | Present. |
| Fiscal Identity Configuration API Family | Present. |
| Reprint API Family | Present. |
| Fiscal Adjustment API Family | Present. |
| X-read and Z-read API Family | Present. |
| BIR Sales Summary and Annex E Report API Family | Present. |
| EJ, POSLog, and Export API Family | Present. |
| Fiscal Reset and Recovery API Family | Present. |
| Exception and Retry Status API Family | Present. |
| Audit and Event API Impact | Present. |
| WebPay Integration Contract | Present. |
| APM Integration Contract | Present. |
| Cashier POS Integration Contract | Present. |
| EC Device / Continuity Terminal Integration Contract | Present. |
| Operator-assisted Integration Contract | Present. |
| Future Channel Contract Pattern | Present. |

## 13. API Over-Specification Review

The draft does not over-specify implementation.

| Boundary | Result | Notes |
| --- | --- | --- |
| Final endpoint paths | Pass | Route families are explicitly provisional. |
| Final DTO schemas | Pass | Request/response content is semantic, not schema-level. |
| Database schema | Pass | The draft explicitly avoids final tables, columns, indexes, constraints, and migrations. |
| Final event schemas | Pass | Section 22 identifies event impact only. |
| Final status-code storage | Pass | Status model is explicitly planning-contract level. |
| Implementation internals | Pass | No code module or storage implementation is defined. |

## 14. Status and Error Model Review

The proposed status taxonomy is useful and clearly provisional. It covers issuance, retry, recovery, reprint, adjustment, report/export, reset, recovery check, and digital SI URL lifecycle concepts.

The proposed error codes cover:

- POS Server unavailable.
- Fiscal issuance failed.
- Fiscal issuance timeout.
- Fiscal document already issued.
- Idempotency conflict.
- Invalid Site POS Server.
- Fiscal identity not configured.
- Numbering policy not configured.
- Digital SI URL unavailable.
- Digital SI access denied.
- Report/export failure.
- Reset approval requirement.
- Recovery continuity failure.
- Offline fiscal issuance not allowed.

Should-fix gap: Section 29 should include explicit timeout / completion-unknown status concepts to match Section 10 and the retry model.

Non-blocking improvement: add general internal authorization/forbidden error concepts for privileged fiscal APIs.

## 15. Fiscal Adjustment and Refund/Reversal Boundary Review

The Fiscal Adjustment API family preserves the correct boundary:

- POS Server owns fiscal adjustment documents.
- Central PMS/payment provider owns refund/reversal money movement finality.
- Void/refund/cancel/return workflows link to original fiscal documents.
- Adjustment document identity/status is returned by POS Server.
- Workflow sequencing remains open.
- The API does not silently reverse payments or mark refunds final.

Result: Pass.

## 16. Reports, Exports, X/Z, EJ, POSLog Review

The draft covers:

- X-read and Z-read.
- Z-counter advancement.
- Reset counter not advancing per Z-read.
- BIR Sales Summary.
- Annex E-1 to E-5.
- Senior/PWD immediate workflows.
- NAAC/Solo Parent future-supported categories.
- Diplomat VAT Privilege / VAT Exemption active but exact treatment open.
- EJ export.
- POSLog export.
- Fiscal exports.
- Export format open questions.
- Reconciliation to canonical fiscal records.

Result: Pass.

## 17. Security/RBAC and Trust Boundary Review

The draft correctly separates:

- Internal APIs.
- Public/customer digital SI URL access.
- Service identity.
- Actor identity.
- Channel/terminal identity.
- High-risk fiscal operations requiring RBAC and audit.

Privileged operations covered include:

- Reprints.
- Fiscal adjustments.
- Fiscal identity configuration.
- X/Z close operations where required.
- Fiscal reset.
- Recovery continuity override or supervised recovery.
- Fiscal exports and compliance access.

Final permission matrix, auth mechanism, token format, claims model, and policy enforcement remain open, which is appropriate.

## 18. Open Questions Review

The Open Questions section preserves all major API open items:

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

No decided item was found incorrectly reopened as a blocker. No unresolved BIR/accounting/security/privacy item was silently decided.

## 19. Recommended Targeted Edits

Apply these edits before approval-readiness review:

1. Add explicit fiscal issuance validation semantics in Section 10:
   - resolved Site and Site POS Server match,
   - channel/terminal registration where applicable,
   - fiscal identity readiness,
   - numbering policy readiness,
   - fiscal eligibility and fiscal line readiness,
   - blocked response when validation fails.
2. Add provisional status concepts in Section 29 for timeout and completion unknown.
3. Add proposed authorization/forbidden error codes in Section 9 for high-risk fiscal APIs.
4. Add one clarifying sentence in Section 12 that customer digital SI URL access is a separate trust boundary from internal digital SI/presentation APIs.
5. Add one clarifying sentence in Section 22 that audit/event publication does not grant payment finality or ExitAuthorization authority.

## 20. Recommended Next Step

Recommended next step: perform a targeted revision of `ExitPass_POS_Server_API_Contract_v1.0.md` for the P1/P2 findings, then run approval-readiness review.

No redesign pass is needed.
