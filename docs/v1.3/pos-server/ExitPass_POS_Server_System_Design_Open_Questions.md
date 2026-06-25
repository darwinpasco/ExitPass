# ExitPass POS Server System Design Open Questions

Version: v1.0 planning artifact
Status: Draft for system design planning only
Generated: 2026-06-25

## Purpose

This artifact reclassifies BRD and POS/Invoicing open questions for POS Server System Design impact. It does not close or remove BRD open questions.

## Open Questions Matrix

| ID | Question | Source / reason | Decision owner | Blocks BRD? | Blocks System Design? | Blocks API Contract? | Blocks Database Design? | Blocks implementation? | Proposed default, if safe | Risk if decided incorrectly |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| PSD-OQ-001 | What exact Sales Invoice numbering pattern is required? | BRD OQ-012; BIR references mention sequence and reset counter patterns. | BIR/accounting, compliance | No | Yes | Yes | Yes | Yes | Make numbering configurable pending signoff. | Non-compliant SI sequence or failed accreditation samples. |
| PSD-OQ-002 | What exact adjustment document numbering pattern is required? | BRD OQ-013; adjustment documents remain fiscal design consideration. | BIR/accounting, compliance | No | Yes | Yes | Yes | Yes | Separate configurable sequences per confirmed document type. | Incorrect void/refund/cancel/return fiscal documents. |
| PSD-OQ-003 | Should reset counter print separately, append to fiscal number, or both? | BRD OQ-014; Annex E reset counter reporting. | BIR/accounting, compliance | No | Yes | Yes | Yes | Yes | Store separately and render per confirmed layout. | Incorrect print/report layout. |
| PSD-OQ-004 | How are MIN, PTU, serial, terminal number, software version, and supplier accreditation metadata assigned across Site POS Server and terminals/channels? | BRD OQ-001; accreditation metadata required. | BIR/accounting, compliance, architecture | No | Yes | Yes | Yes | Yes | Model metadata at both Site POS Server and terminal/channel levels until confirmed. | Wrong fiscal identity and footer data. |
| PSD-OQ-005 | What fiscal terminal identity should WebPay use without physical printer or hardware serial? | BRD OQ-002. | BIR/accounting, security, architecture | No | Yes | Yes | Yes | Yes | Treat WebPay as non-physical channel with assigned fiscal identity pending confirmation. | WebPay SI issuance may be non-compliant. |
| PSD-OQ-006 | Can APM print a POS Server-issued SI payload, or must APM be treated differently for print purposes? | BRD OQ-003; Hikvision hardware assumptions. | BIR/accounting, Hikvision/APM vendor, architecture | No | Yes | Yes | Yes | Yes | POS Server issues; APM renders/prints POS Server-issued output pending confirmation. | Split fiscal authority or unusable APM print integration. |
| PSD-OQ-007 | What X-read and Z-read scope is approved? | BRD OQ-015. | BIR/accounting, finance, operations | No | Yes | Yes | Yes | Yes | Site-level Z-close with terminal/cashier X-read views unless BIR requires otherwise. | Reports fail cashier/site reconciliation. |
| PSD-OQ-008 | Is offline fiscal issuance allowed? | BRD OQ-008. | BIR/accounting, operations, architecture | No | Yes | Yes | Yes | Yes | Disable offline SI issuance unless approved. | Duplicate or skipped fiscal sequences. |
| PSD-OQ-009 | How should refund/void sequencing work between Central PMS/provider and POS Server fiscal adjustment? | Decision recommendations POS-Q014. | Architecture, payments, finance, compliance | No | Yes | Yes | Yes | Yes | Coordinate payment finality and fiscal adjustment with reconciliation links. | Money movement and fiscal documents diverge. |
| PSD-OQ-010 | What exact VAT/tax treatment applies by Site, taxpayer, transaction type, entitlement, and line item? | BRD OQ-004. | Finance/accounting, BIR advisor | No | Yes | Yes | Yes | Yes | Support explicit configurable fiscal line classification. | Incorrect VAT/exempt/zero-rated/non-VAT reporting. |
| PSD-OQ-011 | What exact Diplomat VAT Privilege / VAT Exemption treatment, evidence, wording, reporting, and retention are required? | BRD OQ-005/OQ-006; RMO No. 10-2019. | Finance/accounting, compliance, privacy | No | Yes | Yes | Yes | Yes | Model as VAT privilege/exemption at fiscal line/evidence-reference level pending confirmation. | Invalid VAT exemption or privacy overcollection. |
| PSD-OQ-012 | Should NAAC and Solo Parent report structures be active in v1.3? | BRD OQ-007. | Product, finance, compliance | No | No | Maybe | Maybe | Maybe | Include extensible structures; defer workflows unless activated. | Redesign if future statutory categories are omitted. |
| PSD-OQ-013 | What mandatory export formats are required for EJ, SI, X/Z, BIR Summary, Annex E, and POSLog? | BRD OQ-010. | BIR/accounting, compliance, architecture | No | Yes | Yes | Yes | Yes | Support TXT EJ, PDF/JSON, and POSLog/ARTS candidates until narrowed. | Missing accreditation or audit export. |
| PSD-OQ-014 | What is the final accreditation sample set? | BRD OQ-011. | BIR/accounting, compliance | No | No | No | No | Yes | Prepare broad sample set including discounts, adjustments, X/Z, EJ, POSLog, BIR Summary, Annex E, Diplomat. | Accreditation delays. |
| PSD-OQ-015 | Who is software supplier/applicant and POS user/PTU applicant? | BRD OQ-016. | Legal, compliance, vendor management, BIR advisor | No | Yes | Maybe | Maybe | Yes | Keep supplier/applicant fields configurable pending responsibility matrix. | Wrong legal/accreditation entity in output. |
| PSD-OQ-016 | What Sales Invoice URL access policy, expiry policy, authentication/access model, and audit treatment are required? | BRD OQ-017. | POS Server System Design, security, privacy, compliance | No | Yes | Yes | Yes | Yes | Use secure, auditable, least-data access with configurable expiry pending confirmation. | Unauthorized access, privacy exposure, or inability to retrieve SI. |
| PSD-OQ-017 | Are QR code presentation rules mandatory for non-APM assisted channels? | BRD channel requirements use "may" for non-APM QR. | Product, operations, security, compliance | No | Yes | Yes | No | Yes | Treat QR as optional presentation capability unless channel scope makes it mandatory. | Incomplete UX or overbuilt terminal requirements. |
| PSD-OQ-018 | How should sequence gaps, reserved numbers, failed issuance, and retry idempotency be handled? | Decision recommendations POS-Q023. | BIR/accounting, architecture | No | Yes | Yes | Yes | Yes | Use idempotent issuance and explicit states pending BIR signoff. | Duplicate or missing SI numbers. |
| PSD-OQ-019 | How should tamper-evident state and external anchoring be implemented? | BRD DR/restore requirements. | Architecture, security, compliance | No | Yes | Yes | Yes | Yes | Use append-only fiscal state plus external anchor candidate for last state. | Rollback or continuity failure. |
| PSD-OQ-020 | What recovery procedure is approved after restore/failover/counter continuity failure? | BRD recovery requirement. | Architecture, security, operations, compliance | No | Yes | Yes | Yes | Yes | Block issuance and require supervised recovery audit if continuity cannot be proven. | Stale counters resume fiscal issuance. |
| PSD-OQ-021 | What fiscal roles and permissions are required? | BRD RBAC; decision recommendations POS-Q025. | Security, compliance, operations | No | Yes | Yes | Yes | Yes | Separate cashier, supervisor, fiscal admin, compliance auditor, recovery approver, system admin. | Unauthorized fiscal actions. |
| PSD-OQ-022 | What clock authority and time rollback controls are required? | Hikvision checklist and BIR anti-tamper expectations. | Architecture, security, operations | No | Yes | Yes | Yes | Yes | Trusted time source, drift logging, rollback prevention. | Invalid fiscal timestamps. |
| PSD-OQ-023 | Where should entitlement/evidence data live for Annex E and Diplomat support? | BRD privacy and evidence requirements. | Privacy, compliance, architecture | No | Yes | Yes | Yes | Yes | Prefer references to evidence/compliance vault unless storage is required. | Overcollection or missing report fields. |

## Open Question Handling Rule

The full System Design should include a section that explicitly carries these questions forward. A design may propose defaults or option sets, but it must not silently finalize BIR/accounting, security/privacy, or legal/accreditation decisions.

