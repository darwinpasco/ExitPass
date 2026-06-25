# ExitPass POS Server System Design Impact Map

Version: v1.0 planning artifact
Status: Draft for system design planning only
Generated: 2026-06-25

## Purpose

This impact map identifies downstream document, component, integration, security, compliance, and testing impacts for the future `ExitPass POS Server System Design v1.0`.

## Impact Matrix

| Area | Impact |
| --- | --- |
| BRD impact | BRD is approved baseline. System Design must not change business requirements; it must trace design decisions back to the BRD. |
| Core v1.3 document impact | v1.3 documentation set needs POS Server System Design, POS Server API Contract, Database Design updates, API Contract Pack updates, and Engineering Pack updates after design decisions mature. |
| POS Server System Design impact | Must define Site-level boundary, logical components, fiscal lifecycle, digital SI URL, QR presentation, counters, reports, EJ, POSLog, audit, retention, recovery, RBAC, privacy, integrations, events, API/database impacts, and open questions. |
| POS Server API Contract impact | Later contract pack must cover SI issuance, fiscal status, digital SI URL, channel presentation data, terminal registration, reprint, adjustment, X/Z, BIR Summary, Annex E, export, audit, recovery, and exception/retry flows. |
| Database Design v1.3 impact | Later database design must cover fiscal documents, fiscal lines, document sequences, terminal registry, fiscal identity, counters, GTA, EJ, POSLog, reports, exports, audit events, URL access records, recovery anchors, and retention metadata. |
| API Contract Pack v1.3 impact | Contract pack must preserve Central PMS authority and avoid exposing any endpoint that lets channels bypass Central PMS finality or ExitAuthorization. |
| Engineering Pack v1.3 impact | Engineering pack must include implementation sequencing, test strategy, migration strategy, BIR sample outputs, operational runbooks, recovery procedures, and release controls. |
| Central PMS impact | Needs integration to request SI issuance after verified finality, record fiscal reference, block ExitAuthorization on issuance failure, handle retry/exception state, and preserve authorization boundary. |
| Payment Orchestrator impact | Must continue reporting verified provider outcome to Central PMS, not POS Server as finality authority. |
| WebPay impact | Must route fiscal issuance through Central PMS/Site POS Server, display/access digital SI URL where approved, and support non-physical fiscal terminal identity once confirmed. |
| APM impact | Must act as child terminal/channel, present or print POS Server-issued SI, present QR for digital SI URL, and align hardware/printing responsibility with Hikvision/vendor decisions. |
| Cashier POS impact | Must support cashier/session accountability, controlled reprints/adjustments, optional QR presentation, fiscal status display, and RBAC controls. |
| EC Device / Continuity Terminal impact | Must follow Site POS Server authority, keep offline fiscal issuance restricted until approved, and support continuity presentation controls. |
| Operator Console impact | Likely needs screens/workflows for fiscal exceptions, supervisor approval, reprints, adjustments, evidence references, reports, exports, RBAC, recovery, and audit review. |
| Audit/Event impact | Needs fiscal issuance, retry, reprint, adjustment, export, X/Z, reset, recovery, URL access, terminal registration, configuration, and retention events. |
| Security/privacy impact | Needs role separation, digital SI URL access policy, URL expiry/authentication, anti-tamper controls, evidence minimization, fiscal export control, and privileged recovery approval. |
| BIR/accreditation impact | Requires confirmed numbering, layouts, MIN/PTU/serial/software/supplier metadata, X/Z scope, export formats, sample set, Annex E reports, and Diplomat VAT treatment. |
| Testing/certification impact | Needs tests for issuance-before-exit, digital URL/QR, print/digital consistency, X/Z, counters, reset, recovery, reports, EJ/POSLog reconciliation, reprints, adjustments, RBAC, privacy, exports, and accreditation sample output. |

## High-Risk Dependencies

| Dependency | Risk | Mitigation |
| --- | --- | --- |
| Fiscal identity assignment | Wrong MIN/PTU/serial/software/supplier metadata can invalidate outputs. | Keep assignment open and design configurable metadata until confirmed. |
| Numbering and reset-counter rendering | Incorrect sequence format can fail BIR review. | Separate sequence state from rendering and require BIR/accounting signoff. |
| Offline fiscal issuance | Duplicate/skipped sequences and unreconciled counters. | Default to disabled/restricted until approved. |
| Digital SI URL access model | Privacy leak or unauthorized access/modification risk. | Design with least data, immutable view, audit, expiry, and security/privacy review. |
| Recovery continuity | Stale fiscal state can resume issuance incorrectly. | Require tamper-evident state, external anchor candidate, supervised recovery gate, and recovery audit. |

