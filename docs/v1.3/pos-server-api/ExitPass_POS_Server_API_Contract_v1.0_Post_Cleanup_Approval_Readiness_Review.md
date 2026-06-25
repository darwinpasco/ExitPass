# ExitPass POS Server API Contract v1.0 Post-Cleanup Approval-Readiness Review

## 1. Review Summary

This review checks the updated `ExitPass_POS_Server_API_Contract_v1.0.md` after the BIR/ARTS cleanup updates. The review used the updated POS/Invoicing BRD, updated POS Server System Design, BIR/ARTS Source Impact Review, prior technical review, and prior approval-readiness review as baselines.

The updated API Contract correctly reflects the targeted BRD/System Design cleanup:

- POS Server returns only the digital Sales Invoice URL.
- QR generation, display, and printing are channel/terminal responsibilities.
- Reprint API coverage now includes Sales Invoice, X-read, Z-read, and Electronic Journal where applicable.
- Reprint metadata/status now supports `REPRINT` and `DATE / TIME REPRINTED` behavior where BIR requires it.
- BIR Sales Summary / Annex E-1 minimum content semantics and Print/PDF/JSON output mode semantics are present.
- ARTS POSLog 6.x-aligned export posture is included as a default posture where practical and accepted by BIR/accreditation requirements.
- JSON/POSLog schema-versioning and validation semantics are present.
- ONLINE/OFFLINE status semantics are present and explicitly do not approve offline fiscal issuance.

No P0 or P1 findings were found.

## 2. Approval Recommendation

Approval recommendation: **Ready for commit and use as the POS Server API Contract v1.0 baseline**, subject to normal stakeholder sign-off.

The document remains appropriately provisional for downstream implementation details such as final endpoint paths, DTO schemas, database design, event payloads, RBAC matrix, exact ARTS POSLog profile, exact JSON schema versioning strategy, exact accreditation sample package, and exact print/export layouts.

## 3. Blocking Findings

| ID | Severity | Section | Finding | Why it matters | Recommended correction |
| --- | --- | --- | --- | --- | --- |
| None | P0 | N/A | No approval blockers found. | The updated API Contract preserves the core authority and fiscal design decisions. | None. |

## 4. Should-Fix Findings

| ID | Severity | Section | Finding | Why it matters | Recommended correction |
| --- | --- | --- | --- | --- | --- |
| None | P1 | N/A | No should-fix findings found. | The BIR/ARTS cleanup requirements are covered at API-contract level. | None. |

## 5. Non-Blocking Findings

| ID | Severity | Section | Finding | Why it matters | Recommended correction |
| --- | --- | --- | --- | --- | --- |
| API-PC-P2-001 | P2 | Sections 12, 19, 30, 32 | Downstream design still must resolve exact digital SI URL access model, ARTS POSLog profile, JSON schema versioning strategy, export layouts, and accreditation package. | These are intentionally not finalized in the API Contract, but they remain implementation and accreditation dependencies. | Carry these items into Database Design, Engineering Pack, Security/Privacy Review, and BIR/accreditation package planning. |

## 6. Editorial Findings

| ID | Severity | Section | Finding | Why it matters | Recommended correction |
| --- | --- | --- | --- | --- | --- |
| None | Editorial | N/A | No editorial findings requiring approval delay. | Current wording is clear enough for baseline approval. | None. |

## 7. Updated BRD/System Design Alignment Review

The API Contract aligns with the updated BRD and System Design.

Confirmed alignment:

- Platform-wide POS/Invoicing remains intact.
- Site POS Server remains fiscal issuer for the resolved Site.
- Central PMS remains payment finality and ExitAuthorization authority.
- POS Server fiscal APIs do not issue or imply ExitAuthorization.
- Digital SI URL handling matches the updated System Design: POS Server returns the URL; channels/terminals handle QR generation/display/print.
- Reprint support matches the updated BRD/System Design for Sales Invoice, X-read, Z-read, and Electronic Journal.
- ARTS POSLog 6.x is treated as a structured export posture, not as a replacement for BIR fiscal outputs.
- ONLINE/OFFLINE is treated as observability/status, not offline fiscal issuance approval.

## 8. Authority Boundary Review

The updated API Contract preserves the authority model.

Verified:

- Central PMS owns payment finality.
- Central PMS owns ExitAuthorization.
- POS Server APIs must not issue, approve, create, mutate, or bypass ExitAuthorization.
- Payment Orchestrator and WebPay must not declare platform payment finality.
- Site POS Server remains fiscal issuer.
- Channels/terminals are payment/presentation endpoints, not independent fiscal authorities.
- Vendor PMS / HikCentral acknowledgment remains synchronization only.
- POS/fiscal events are audit, integration, and observability signals only; they do not grant payment finality or ExitAuthorization.

No authority-boundary weakening was found.

## 9. Digital SI URL and QR Responsibility Review

The updated API Contract correctly resolves QR responsibility.

Verified:

- Section 12 states POS Server returns only the digital Sales Invoice URL.
- Section 12 states POS Server does not generate the QR image as a required API responsibility.
- Section 12 states the channel or terminal converts the POS Server-returned URL into a QR code where supported.
- Section 12 states QR generation, display, and printing are channel/terminal presentation responsibilities.
- Sections 24 through 28 align APM, Cashier POS, EC Device / Continuity Terminal, operator-assisted flows, and future channels to channel-side QR generation.
- QR presentation does not make the channel/terminal fiscal issuer.

Resolved QR responsibility is no longer listed as a major open question.

## 10. Reprint API Review

The Reprint API Family now covers the BIR/ARTS cleanup requirements.

Verified:

- Section 15 covers controlled reprints for Sales Invoice, X-read, Z-read, and Electronic Journal outputs where applicable.
- Reprint semantics include original document/report/output linkage.
- Reprint semantics include reprint type, reason, actor/service identity, authorization/approval where required, timestamp, status/history, audit reference, and no mutation of original facts.
- Section 15 states reprinted fiscal outputs shall show `REPRINT` and `DATE / TIME REPRINTED` at the bottom where BIR requires them.
- Section 15 states POS Server shall preserve or return enough metadata/status for renderer/channel/terminal application of required labels and timestamps.

No reprint API approval blockers were found.

## 11. BIR Sales Summary and Report Output Review

The BIR Sales Summary / Annex E API family now includes the required minimum content semantics and output modes.

Verified minimum content semantics:

- Report Date.
- Beginning SI Number.
- Ending SI Number.
- Previous Grand Total.
- Present Grand Total.
- Sales for the Day.
- Gross Sales.
- Net Sales.
- VATable Sales.
- VAT Amount.
- VAT Exempt Sales.
- Zero-Rated Sales.
- Discounts.
- Voids.
- Returns.
- Reset Counter.
- Z Counter.

Verified output/export mode semantics:

- Print.
- PDF.
- JSON.

Exact mandatory formats and final layouts remain open for BIR/accounting/accreditation confirmation, which is appropriate.

## 12. ARTS POSLog and Structured Export Review

The API Contract now reflects the ARTS POSLog source impact correctly.

Verified:

- Section 19 states POSLog exports may use an ARTS POSLog 6.x-aligned export where practical and accepted by BIR/accreditation requirements.
- ARTS POSLog is treated as a structured export/schema interoperability reference.
- ARTS POSLog does not replace Philippine BIR fiscal document/report requirements.
- ExitPass Sales Invoice, SI, and Sales Invoice Number terminology is preserved.
- BIR-required outputs such as Sales Invoice, X-read, Z-read, EJ, POSLog, and BIR Sales Summary are preserved.
- Local/BIR-specific fields may be represented as local extensions or mapped fields.

Verified local/BIR mapping concepts include:

- Sales Invoice Number.
- Ticket Number / Plate Number.
- Site / branch / business unit identity.
- Channel / terminal / workstation identity.
- Business Day Date.
- MIN.
- PTU.
- Serial Number.
- Supplier/accreditation metadata.
- Reset Counter.
- Z Counter.
- Grand Total Amount.
- Digital SI URL.
- Parking session timestamps and duration.
- Fiscal audit references.

The final ARTS POSLog profile and schema mapping remain open, which is correct.

## 13. JSON/POSLog Validation Review

The structured export validation posture is covered.

Verified:

- JSON fiscal and audit records should remain complete even when printed outputs are simplified.
- JSON and POSLog exports should be schema-versioned.
- JSON and POSLog exports should support validation against approved BIR/ARTS-aligned schemas where applicable.
- Export validation success, failure, and pending states must be auditable and visible to operational/support workflows.
- Status concepts include export validation pending, passed, and failed.
- Error model includes `EXPORT_VALIDATION_FAILED`.

The document does not define final JSON schema, XSD mapping, validation job implementation, storage model, or packaging format.

## 14. ONLINE/OFFLINE Status Review

ONLINE/OFFLINE semantics are correctly added and bounded.

Verified:

- Channel/terminal registry APIs include ONLINE/OFFLINE or equivalent reachability/health state.
- POS Server administrative/status APIs should support ONLINE/OFFLINE where required.
- Channel/terminal status APIs should support ONLINE/OFFLINE or equivalent reachability/health state where applicable.
- ONLINE/OFFLINE is operational and observability information.
- ONLINE/OFFLINE does not approve offline fiscal issuance.
- Offline fiscal issuance remains disabled/restricted unless BIR/accounting approves a compliant sequence, counter, evidence, reconciliation, and recovery model.
- Status model includes POS Server online/offline and channel/terminal online/offline concepts.

No offline issuance approval was introduced.

## 15. Status and Error Model Review

The status and error model is sufficient for approval-readiness and remains provisional.

Verified status concepts include:

- Reprint requested.
- Reprint completed.
- Reprint failed.
- Export validation pending.
- Export validation passed.
- Export validation failed.
- POS Server online.
- POS Server offline.
- Channel/terminal online.
- Channel/terminal offline.

Verified error concepts include:

- `EXPORT_VALIDATION_FAILED`.
- `OFFLINE_FISCAL_ISSUANCE_NOT_ALLOWED`.
- Existing authorization, issuance, report, export, reset, recovery, idempotency, and digital SI errors remain intact.

The status and error model does not finalize database enum values.

## 16. Open Questions and Non-Decisions Review

Open question hygiene is acceptable.

Resolved items are no longer left as major open questions:

- QR presentation payload responsibility.
- POS Server QR generation responsibility.
- Reprint coverage for SI/X/Z/EJ.
- Reprint label/timestamp requirement.
- BIR Sales Summary minimum content baseline.
- ARTS POSLog 6.x-aligned export default posture.
- JSON/POSLog validation posture.
- ONLINE/OFFLINE indicator as observability/status information.

Still-open items are appropriately preserved:

- Final endpoint route family naming.
- Request/response DTO boundaries.
- Final database tables/columns.
- Final event payloads.
- Final RBAC matrix.
- Exact ARTS POSLog profile/schema mapping.
- Exact JSON schema versioning strategy.
- Exact accreditation sample package.
- Exact export formats/layouts.
- Digital SI URL token/access/expiry/authentication model.
- Offline fiscal issuance approval, if any.
- Sequence gaps, reserved numbers, failed issuance, abandoned issuance.
- X/Z aggregation scope.
- MIN/PTU/serial/software/supplier assignment.
- WebPay fiscal terminal identity.
- VAT/tax treatment.
- Diplomat VAT treatment/evidence/reporting/retention.

## 17. Over-Specification Review

The updated API Contract remains at an appropriate contract-planning level.

No premature finalization was found for:

- Final endpoint paths.
- Final DTO schemas.
- Final database tables/columns.
- Final event payloads.
- Final RBAC matrix.
- Final ARTS POSLog profile/schema mapping.
- Final JSON schema versioning strategy.
- Final accreditation sample package.
- Exact print/export layouts.
- Offline fiscal issuance approval.

Route families, statuses, and error concepts are clearly provisional.

## 18. Final Recommendation

Final recommendation: **Approve the updated POS Server API Contract v1.0 as ready for commit and baseline use**.

The document is ready to support downstream POS Server Database Design, Engineering Pack, Security/Privacy Review, BIR/accreditation package preparation, and implementation planning.

## 19. Recommended Next Step

Recommended next step:

1. Commit the updated API Contract and this post-cleanup approval-readiness review when instructed.
2. Start POS Server Database Design planning using the approved BRD, approved System Design, updated API Contract, BIR/ARTS impact review, and remaining open API/database/security/accreditation questions.
