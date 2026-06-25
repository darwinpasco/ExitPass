# ExitPass POS Server API Contract Outline

Status: Initial API contract planning artifact only

This outline proposes the structure for `ExitPass POS Server API Contract v1.0`. It does not define endpoint paths, DTOs, schemas, database design, or final event payloads.

## Proposed Document Structure

1. Document Control
2. Purpose and Scope
3. Reference Baseline
4. API Ownership and Authority Model
5. API Consumers and Trust Boundaries
6. Common Contract Rules
7. Authentication and Authorization
8. Idempotency and Retry Model
9. Canonical Error Model
10. Fiscal Issuance API Family
11. Fiscal Document API Family
12. Digital SI URL and Presentation API Family
13. Channel and Terminal Registry API Family
14. Fiscal Identity Configuration API Family
15. Reprint API Family
16. Fiscal Adjustment API Family
17. X-read and Z-read API Family
18. BIR Sales Summary and Annex E Report API Family
19. EJ, POSLog, and Export API Family
20. Fiscal Reset and Recovery API Family
21. Exception and Retry Status API Family
22. Audit and Event API Impact
23. WebPay Integration Contract
24. APM Integration Contract
25. Cashier POS Integration Contract
26. EC Device / Continuity Terminal Integration Contract
27. Operator-assisted Integration Contract
28. Future Channel Contract Pattern
29. Status Model
30. Open Questions
31. Risks and Mitigations
32. Appendices

## Section Intent

| Section | Intended content |
| --- | --- |
| Document Control | Version, status, baseline references, approval notes. |
| Purpose and Scope | POS Server API boundary, included route families, excluded database/schema/design topics. |
| Reference Baseline | Approved POS/Invoicing BRD, approved POS Server System Design, v1.2 API Contract Pack, and supporting BIR/POS references. |
| API Ownership and Authority Model | Central PMS payment finality/ExitAuthorization authority and POS Server fiscal authority. |
| API Consumers and Trust Boundaries | Central PMS, WebPay, APM, Cashier POS, EC/continuity, operator-assisted workflows, future channels, admin/audit consumers, and public/customer SI URL access boundary. |
| Common Contract Rules | Correlation, Site context, actor context, timestamps, canonical fiscal reference handling, audit expectations, and no ExitAuthorization from POS Server. |
| Authentication and Authorization | Internal caller authorization, fiscal RBAC, high-risk action approvals, and customer SI access model placeholder. |
| Idempotency and Retry Model | Idempotent issuance, duplicate handling, timeout/retry semantics, sequence gaps, and open BIR/accounting dependencies. |
| Canonical Error Model | Error category planning without final status codes. |
| Fiscal Issuance API Family | Sales Invoice issuance request/status, fiscal identity/status return, digital SI URL return, Central PMS usage. |
| Fiscal Document API Family | Document lookup/status, printed/digital consistency, original document references. |
| Digital SI URL and Presentation API Family | Digital SI URL, access model, re-access, QR presentation metadata, channel/terminal capabilities. |
| Channel and Terminal Registry API Family | Channel/terminal registration, capability, status, Site association, and audit. |
| Fiscal Identity Configuration API Family | Taxpayer, Site/branch, MIN/PTU/serial/software/supplier metadata once confirmed. |
| Reprint API Family | Reprint request/status, authorization, labeling, and audit. |
| Fiscal Adjustment API Family | Void/refund/cancel/return request/status, original document linkage, sequencing with Central PMS/provider. |
| X-read and Z-read API Family | X/Z request/status/export and approved fiscal scope. |
| BIR Sales Summary and Annex E Report API Family | Report request/status/export, entitlement/tax category support, format open items. |
| EJ, POSLog, and Export API Family | EJ/POSLog/fiscal export request/status/export and final format dependencies. |
| Fiscal Reset and Recovery API Family | Reset request/approval/status, recovery continuity check/status, supervised recovery. |
| Exception and Retry Status API Family | Fiscal issuance pending/failure/retry/closure visibility for Central PMS and operations. |
| Audit and Event API Impact | Audit/event publication expectations without final event schema. |
| Channel integration sections | Per-channel contract responsibilities while preserving Site POS Server fiscal authority. |
| Future Channel Contract Pattern | Registration and presentation pattern for future channels. |
| Status Model | Fiscal issuance, document, report/export, adjustment, reset/recovery, and exception status categories. |
| Open Questions | API-specific unresolved decisions and owners. |
| Risks and Mitigations | Contract risks and mitigation posture. |
| Appendices | Glossary, acronyms, source mapping, and traceability. |

## Boundary Notes

The full API Contract must not:

- Move payment finality from Central PMS to POS Server.
- Move ExitAuthorization from Central PMS to POS Server.
- Allow Payment Orchestrator or WebPay to declare platform finality.
- Allow Gate/exit execution to bypass Central PMS.
- Treat APM, WebPay, Cashier POS, EC/continuity, operator-assisted flow, or future channels as independent POS systems.
- Approve offline fiscal issuance without BIR/accounting confirmation.
- Finalize BIR/accounting/security/privacy questions without the proper decision owner.
