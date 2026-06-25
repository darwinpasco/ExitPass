# ExitPass POS Server System Design v1.0 Outline

Version: v1.0 planning artifact
Status: Draft outline only
Generated: 2026-06-25

## Purpose

This outline proposes the structure for the future `ExitPass POS Server System Design v1.0`. It does not draft the full design and does not define final database schema, API contracts, or implementation internals.

## Proposed Document Structure

1. Document Control
2. Purpose and Scope
3. Reference Baseline
4. Architecture Principles
5. POS Server Context
6. Site-level POS Server Boundary
7. Authority Model
8. Component Architecture
9. Channel and Terminal Registration
10. Fiscal Identity Model
11. Sales Invoice Lifecycle
12. Printed and Digital Sales Invoice Delivery
13. Digital SI URL and QR Code Model
14. Fiscal Document Numbering
15. Fiscal Line Model
16. Entitlement and VAT Privilege Handling
17. X-read and Z-read
18. Reset Counter, Z-counter, and Grand Total Amount
19. BIR Sales Summary and Annex E Reporting
20. Electronic Journal
21. POSLog
22. Fiscal Exports
23. Reprints
24. Void/Refund/Cancel/Return Adjustment Documents
25. Fiscal Audit Trail
26. Security, RBAC, and Segregation of Duties
27. Privacy and Evidence Handling
28. Fiscal State Integrity and Tamper Evidence
29. Backup, Restore, Failover, and Recovery
30. Exception and Retry Handling
31. Integration With Central PMS
32. Integration With Payment Orchestrator
33. Integration With WebPay
34. Integration With APM
35. Integration With Cashier POS
36. Integration With EC Device / Continuity Terminal
37. Integration With Operator-assisted Payment
38. Eventing and Outbox Impact
39. Database Design Impact
40. API Contract Impact
41. Observability and Operations
42. Testing and Certification Considerations
43. Diagrams
44. Open Questions
45. Risks and Mitigations
46. Appendices

## Section Planning Notes

| Section | Planning note |
| --- | --- |
| Architecture Principles | Preserve BRD authority boundaries, canonical fiscal facts, auditability, non-bypass, and compliance-first sequencing. |
| Component Architecture | Describe logical components only; avoid claiming final code modules. |
| Channel and Terminal Registration | Cover WebPay, APM, Cashier POS, EC/continuity, operator-assisted, and future channels under Site POS Server. |
| Fiscal Identity Model | Keep MIN/PTU/serial/software/supplier assignment visibly open. |
| Sales Invoice Lifecycle | Include idempotency, status, retry, digital URL, print/digital consistency, and fiscal reference return. |
| Printed and Digital Delivery | Cover printed output, digital SI URL, QR presentation, and access/privacy controls. |
| Counters and Recovery | Preserve reset/Z distinction and continuity proof requirements. |
| Integration Sections | Define responsibilities and flow impacts without drafting final API paths. |
| Database/API Impact | Provide later design impacts, not schema or endpoint definitions in the outline. |
| Testing and Certification | Include BIR sample outputs, X/Z, EJ, POSLog, digital SI, recovery, and channel routing scenarios. |

