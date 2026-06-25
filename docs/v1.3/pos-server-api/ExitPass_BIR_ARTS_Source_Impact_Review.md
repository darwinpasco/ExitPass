# ExitPass BIR / ARTS Source Impact Review

## 1. Review Summary

This review checks whether the newer BIR examiner materials and ARTS POSLog references require updates to the approved ExitPass POS/Invoicing BRD v1.0 or the approved POS Server System Design v1.0 before the POS Server API Contract is updated.

Overall, the approved BRD and System Design already preserve the core architecture decisions: platform-wide POS/Invoicing, Sales Invoice as the parking fiscal output, Site-level POS Server authority, Central PMS payment finality and ExitAuthorization authority, simplified printed fiscal outputs, canonical digital fiscal records, and BIR reporting.

The new sources do not require a broad BRD or System Design redesign. They do, however, identify targeted baseline improvements that should be made before the API Contract update:

- Explicitly state reprint coverage for Sales Invoice, X-Read, Z-Read, and Electronic Journal outputs.
- Explicitly require `REPRINT` and `DATE / TIME REPRINTED` labels for reprinted fiscal outputs where BIR requires them.
- Align the System Design with the resolved QR responsibility decision: POS Server returns only the digital Sales Invoice URL; the channel or terminal converts the URL into a QR code where supported.
- Strengthen the System Design posture for ARTS POSLog 6.x-aligned POSLog export support, while preserving Philippine BIR terminology and fiscal report requirements.
- Add structured JSON schema validation/export posture and ONLINE/OFFLINE operational indicator handling to the System Design and downstream API/Engineering work.

No P0 BRD or System Design blockers were found. The recommended path is a targeted BRD/System Design cleanup before applying API Contract updates.

## 2. Overall Recommendation

Recommendation: **perform a targeted BRD and System Design update before the API Contract update**.

The API Contract should not be updated first because several API-level details depend on design-level clarifications:

- QR payload responsibility is now resolved as channel/terminal QR generation from a POS Server-returned URL.
- POSLog export should be treated as ARTS POSLog 6.x-aligned by default where POSLog is required, with local BIR-specific extensions.
- Reprint behavior must explicitly cover SI, X-Read, Z-Read, and EJ before API endpoints/status/error semantics are finalized.
- ONLINE/OFFLINE status should be reflected in the System Design so API status contracts do not invent the behavior independently.

## 3. Source Materials Inspected

### BIR / Examiner Materials

- `D:\Docs\ExitPass\POS\FINAL GAP ANALYSIS - Hikvision AutoPay Machine BIR Accreditation.docx`
- `D:\Docs\ExitPass\POS\BIR Recommended Formats.pptx`
- `D:\Docs\ExitPass\POS\BIR POS Accreditation Requirements.docx`
- `D:\Docs\ExitPass\POS\Hikvision Developer Checklist for BIR-Compliant Autopay Parking Station.docx`
- `D:\Docs\ExitPass\POS\RMO No. 24-2023.pdf`
- `D:\Docs\ExitPass\POS\RMO 24-2023 Annex D-1_Sample X-Reading.pdf`
- `D:\Docs\ExitPass\POS\RMO 24-2023 Annex D-2_Sample Z-Reading.pdf`
- `D:\Docs\ExitPass\POS\RMO 24-2023 Annex E-1 to E-5.xlsx`
- `D:\Docs\ExitPass\POS\RMO 24-2023 ANNEX F_12072022_Functional and Technical Evaluation Checklist_RAF.docx.pdf`
- `D:\Docs\ExitPass\POS\RMO 24-2023 Annex G_Minutes of Meeting_v2_RAF (1).docx`
- `D:\Docs\ExitPass\POS\sampleejournal.txt`

### ARTS POSLog Materials

- `D:\Docs\ExitPass\POS\ARTS POSLog\POSLog_6_readme.txt`
- `D:\Docs\ExitPass\POS\ARTS POSLog\ARTS_POSLog_TechSpec_V6.0_20150203\*.pdf`
- `D:\Docs\ExitPass\POS\ARTS POSLog\ARTS_POSLog_XMLSchema_V6.0_20140204\*.xsd`

### Approved / Downstream Documents Reviewed

- `docs/v1.3/pos-invoicing/ExitPass_POS_Invoicing_BRD_v1.0.md`
- `docs/v1.3/pos-server/ExitPass_POS_Server_System_Design_v1.0.md`
- `docs/v1.3/pos-server-api/ExitPass_POS_Server_API_Contract_v1.0.md`
- POS/Invoicing and POS Server decision logs and open question lists
- POS Server API Contract planning, technical review, and approval-readiness context where present

Note: the BIR recommended formats deck appears to be mostly image-based after text extraction. It is treated as support for audit-trail/report-format alignment rather than as a complete standalone textual requirements source.

## 4. BRD Impact Summary

The approved BRD already covers most business requirements from the BIR examiner materials:

- Sales Invoice, not Official Receipt, as the primary parking fiscal output.
- Platform-wide POS/Invoicing, not APM-only scope.
- Site-level POS Server fiscal authority.
- Simplified printed Sales Invoice, X-Read, and Z-Read.
- Canonical digital fiscal records including EJ, POSLog, JSON/PDF/export, reports, and audit records.
- BIR Sales Summary and Annex E reporting.
- Fiscal identity/header/footer metadata.
- Reprints controlled and audited.
- Offline fiscal issuance restricted pending approved model.

Targeted BRD update recommended:

- Add explicit BIR examiner-derived wording that reprint support covers Sales Invoice, X-Read, Z-Read, and Electronic Journal where applicable.
- Add explicit wording that reprinted fiscal outputs must show `REPRINT` and `DATE / TIME REPRINTED` labels where BIR requires them.

The resolved QR responsibility decision does not require a BRD update because the BRD already states that POS Server returns a digital SI URL and that devices/channels may generate or display/print a QR code from that URL. It should be reflected more precisely in the System Design and API Contract.

## 5. System Design Impact Summary

Targeted System Design updates are recommended before the API Contract update:

- Clarify QR responsibility: POS Server returns only the digital SI URL; channels/terminals generate/display/print QR codes where supported.
- Explicitly cover reprint support for Sales Invoice, X-Read, Z-Read, and Electronic Journal.
- Explicitly require reprint labels and reprint timestamp handling where BIR requires them.
- Treat ARTS POSLog 6.x-aligned POSLog export as the default design posture for POSLog export, while preserving BIR terminology and local BIR-specific fields.
- Add design posture for JSON schema validation of structured fiscal exports.
- Add ONLINE/OFFLINE status indicator as an operational/status capability, not as permission for offline fiscal issuance.

These are targeted design clarifications. They do not change the approved authority model.

## 6. API Contract Impact Summary

The API Contract should be updated after the targeted BRD/System Design updates to cover:

- Digital SI response semantics: POS Server returns the digital SI URL only; QR generation/display/printing is channel/terminal responsibility.
- Fiscal document and report reprint request/status semantics for SI, X-Read, Z-Read, and EJ where applicable.
- Reprint label/timestamp status or rendering metadata where needed.
- BIR Sales Summary minimum content and export mode semantics.
- ONLINE/OFFLINE status reporting for POS Server and channel/terminal status APIs.
- ARTS POSLog 6.x-aligned export semantics and BIR extension mapping.
- JSON schema validation status/error semantics for structured exports.
- Audit trail report retrieval/export semantics if exposed through POS Server APIs.

## 7. Database Design Inputs

Carry the following into POS Server Database Design planning:

- Canonical fiscal records must support SI number mapping, transaction identity, transaction sequence, line item sequence, tender, tax, discount, totals, BIR-specific extension fields, and export reconciliation.
- POSLog export mapping should support ARTS POSLog 6.x concepts such as Business Unit/Site identity, Workstation/terminal identity, Business Day Date, transaction sequence numbers, line item sequence numbers, tender, tax, discount, and transaction totals.
- BIR extension fields should support MIN, PTU, Z-counter, reset counter, Grand Total Amount prior/after, taxpayer identity, branch/location identity, and local fiscal metadata.
- Tamper-evident anchoring/recovery needs storage support for latest SI sequence, adjustment sequence, reset counter, Z-counter, Grand Total Amount, latest EJ hash, and last fiscal event timestamp.

## 8. Engineering Pack Inputs

Carry the following into Engineering Pack planning:

- JSON schema validation jobs and tests for BIR/ARTS-aligned exports.
- Export sample generation for SI, X-Read, Z-Read, EJ, POSLog, BIR Sales Summary, and Annex E outputs.
- Reprint label verification tests.
- ONLINE/OFFLINE indicator display and status propagation tests.
- Accreditation package generation and packaging checks.
- Fiscal export reconciliation tests across printed outputs, EJ, POSLog, BIR Sales Summary, Annex E, JSON/PDF exports, and audit records.

## 9. BIR/Accreditation Package Inputs

Carry the following into the BIR/accreditation confirmation package:

- Sample printed Sales Invoice, X-Read, Z-Read.
- Reprinted SI/X/Z/EJ samples showing `REPRINT` and `DATE / TIME REPRINTED` where applicable.
- BIR Sales Summary Annex E-1 sample with minimum contents.
- POSLog ARTS 6.x-aligned JSON/XML samples with BIR local extensions.
- EJ export samples.
- Audit Trail report sample.
- Supplier accreditation footer fields and taxpayer/fiscal identity samples.
- Evidence that APM is a channel/terminal/printer/presenter under the Site POS Server, not an independent fiscal authority.

## 10. No-Update-Needed Items

No BRD/System Design update is needed for these already-covered or intentionally open topics:

- Sales Invoice terminology over Official Receipt.
- Platform-wide applicability and non-APM-only architecture.
- APM as channel/terminal/printer/presenter, not independent POS authority.
- Simplified printed SI/X/Z posture.
- Detailed digital fiscal/audit records.
- BIR Sales Summary and Annex E first-class report posture.
- Supplier accreditation footer support.
- Taxpayer/fiscal identity support.
- Offline fiscal issuance disabled/restricted by default.
- APM-specific non-applicability of Annex E-2 to E-5 does not reduce the platform model.
- Open questions for MIN/PTU/serial/software/supplier assignment, WebPay fiscal identity, numbering, sequence gaps, X/Z scope, VAT/tax treatment, Diplomat VAT Privilege, digital SI URL access, exact export formats, accreditation sample package, recovery anchoring, and final endpoint/DTO/DB/event/RBAC details.

## 11. Topic-by-Topic Impact Matrix

| # | Topic | Source basis | Current BRD coverage | Current System Design coverage | Impact classification | Recommended action | Target document | Priority |
|---:|---|---|---|---|---|---|---|---|
| 1 | Sales Invoice terminology versus Official Receipt | Gap analysis says OR must become SI; Annex G supports SI/OR as applicable | Covered: parking fiscal output is Sales Invoice | Covered: SI lifecycle and fiscal document model | No update needed | Preserve SI terminology; do not reintroduce OR as primary output | None | P2 |
| 2 | Platform-wide applicability of BIR examiner findings | Examiner sources are APM-specific but fiscal controls are generally applicable | Covered: platform-wide POS/Invoicing | Covered: Site-level POS Server across channels | No update needed | Continue treating APM materials as fiscal/control inputs, not architecture source of truth | None | P2 |
| 3 | QR presentation responsibility: POS Server returns URL only, channel renders QR | User decision; digital SI requirement | BRD is sufficient: URL returned and devices may generate QR | Needs sharper wording: remove ambiguity around QR payload/rendering ownership | System Design update required | State POS Server returns URL only; channel/terminal generates/displays/prints QR where supported | POS Server System Design, then API Contract | P1 |
| 4 | APM as printer/presenter, not independent fiscal authority | Gap analysis and architecture decision | Covered: channels are children of Site POS Server | Covered: APM integration under POS Server authority | No update needed | Preserve current authority model | None | P2 |
| 5 | Simplified printed Sales Invoice | Gap analysis Gap 4 | Covered: simplified printed output and canonical digital detail | Covered: renderer and fiscal output pipeline | No update needed | Keep printed SI concise and BIR-aligned | None | P2 |
| 6 | Simplified printed X-Read | Gap analysis Gap 4; Annex D-1 | Covered: simplified printed output | Covered: X/Z report model | No update needed | Keep X-Read printout concise and BIR-aligned | None | P2 |
| 7 | Simplified printed Z-Read | Gap analysis Gap 4; Annex D-2 | Covered: simplified printed output | Covered: X/Z report model | No update needed | Keep Z-Read printout concise and BIR-aligned | None | P2 |
| 8 | Detailed digital fiscal/audit records in JSON/EJ/POSLog/PDF/backend exports | Gap analysis, developer checklist, accreditation requirements | Covered: canonical digital records and exports | Covered: fiscal output/reporting pipeline | No update needed | Carry details into API, DB, Engineering, and accreditation samples | Downstream documents | downstream |
| 9 | Reprint support for SI, X-Read, Z-Read, and EJ | Gap analysis Gap 2 | BRD covers audited reprints but is not explicit for X/Z/EJ | System Design covers reprint controls but should name fiscal output types | BRD and System Design update required | Add explicit SI/X/Z/EJ reprint coverage where applicable | BRD and POS Server System Design | P1 |
| 10 | `REPRINT` and `DATE / TIME REPRINTED` labels | Gap analysis Gap 2; Annex G | BRD covers reprints labeled/audited but should state required label/timestamp | System Design should carry rendering/control rule | BRD and System Design update required | Add required reprint label and reprint timestamp handling where BIR requires them | BRD and POS Server System Design | P1 |
| 11 | Reprint activity logging | Gap analysis Gap 2 | Covered: reprints controlled and audited | Covered: Fiscal Audit Service/Reprint Control | No update needed | Preserve audit linkage and operator accountability | None | P2 |
| 12 | BIR Sales Summary / Annex E-1 requirement | Gap analysis Gap 3; Annex F | Covered: first-class report, not analytics | Covered: BIR Sales Summary service | No update needed | No baseline change needed | None | P2 |
| 13 | BIR Sales Summary minimum contents | Gap analysis lists minimum contents | BRD covers reconciliation categories but not exact minimum list | System Design covers report service but not final field list | API Contract update only | Add report semantic contents to API planning without changing BRD-level requirement | POS Server API Contract | P2 |
| 14 | BIR Sales Summary print/PDF/JSON outputs | Gap analysis Gap 3 | BRD covers exports generally | System Design covers exports generally | API Contract update only | Add output mode semantics and keep exact mandatory formats open where needed | POS Server API Contract | P2 |
| 15 | Required fiscal report set: SI, X-Read, Z-Read, EJ, POSLog, BIR Sales Summary | Gap analysis required report set | Covered across fiscal outputs/reporting sections | Covered across component/report/export sections | No update needed | Preserve as accreditation package checklist | BIR/accreditation package | downstream |
| 16 | Supplier accreditation footer fields | Gap analysis Gap 5; Annex G | Covered: supplier accreditation metadata and footer text | Covered: fiscal identity model | BIR/accreditation package input | Use source for exact sample footer evidence and validation | BIR/accreditation package | downstream |
| 17 | Taxpayer information / fiscal identity details | Accreditation requirements; Annex G | Covered | Covered | No update needed | Maintain open assignment questions for fiscal identity fields | None | P2 |
| 18 | Audit trail report support | BIR recommended formats; Annex G | Covered as fiscal audit trail | Covered as Fiscal Audit Service, but API retrieval/export may need expression | API Contract update only | Add audit trail report/export API semantics if POS Server exposes it | POS Server API Contract | P2 |
| 19 | ONLINE/OFFLINE indicator | Gap analysis Gap 6 | BRD covers degraded/offline policy but not indicator detail | Needs operational/status handling | System Design update required | Add ONLINE/OFFLINE status as observability/status indicator, not permission for offline issuance | POS Server System Design, then API/Engineering | P2 |
| 20 | Offline fiscal issuance disabled/restricted by default | Approved BRD/System Design and examiner constraints | Covered | Covered | No update needed | Preserve restriction pending BIR/accounting-approved model | None | P2 |
| 21 | Cash inventory reporting as operational only | Gap analysis says no fiscal report change | Not treated as fiscal requirement | Not treated as fiscal core | Engineering Pack input | If implemented, keep as operational reporting, not BIR fiscal report baseline | Engineering Pack | downstream |
| 22 | Annex E-2 to E-5 treatment: APM not applicable, platform extensible | Gap analysis says APM not applicable; BRD requires platform extensibility | Covered: Senior/PWD immediate, NAAC/Solo future-supported | Covered: extensible fiscal reporting | No update needed | Do not reduce platform requirements because APM scope excludes some reports | None | P2 |
| 23 | ARTS POSLog 6.x-aligned POSLog export default posture | BIR POS Accreditation Requirements; ARTS POSLog folder | BRD mentions POSLog but not ARTS default | System Design leaves exact format open | System Design update required | State POSLog export default posture as ARTS POSLog 6.x-aligned with BIR/local extensions, pending final confirmation | POS Server System Design, then API/DB/Engineering | P1 |
| 24 | JSON schema support and validation | BIR POS Accreditation Requirements includes JSON schemas; ARTS XSDs | BRD covers structured exports generally | System Design should state schema validation capability for structured exports | System Design update required | Add structured export schema validation posture without freezing final schemas | POS Server System Design, then API/Engineering | P1 |
| 25 | Transaction identity model for structured exports | ARTS POSLog TransactionID, BusinessDayDate, WorkstationID, sequence concepts | BRD covers fiscal identity and canonical records | System Design covers canonical fiscal facts | Database Design input | Map SI number, fiscal document identity, site, terminal/workstation, business day, and sequence concepts | POS Server Database Design | downstream |
| 26 | Line item sequence numbers | ARTS POSLog line item sequence; BIR fiscal line needs | BRD covers fiscal line classifications | System Design covers fiscal line model | Database Design input | Ensure canonical fiscal lines can support ordered line item export mapping | POS Server Database Design | downstream |
| 27 | Tender/tax/discount/totals structured data support | ARTS tender/tax/discount/total models; BIR reports | BRD covers fiscal classifications and reporting | System Design covers fiscal line and report model | Database Design input | Ensure canonical records support tender, tax, discounts, totals, and BIR report reconciliation | POS Server Database Design | downstream |
| 28 | Local/BIR-specific extensions to ARTS POSLog-aligned exports | ARTS extension points; BIR-specific fields in accreditation requirements | BRD preserves BIR terminology | System Design should preserve BIR extension posture after ARTS update | Database Design input | Add mapping support for MIN, PTU, Z-counter, reset counter, GTA, taxpayer, branch, and local parking fields | POS Server Database Design | downstream |
| 29 | MIN/PTU/serial/software/supplier assignment | Annex G and accreditation materials | Explicitly open | Explicitly open | No update needed | Keep open for BIR/accounting/accreditation confirmation | None | P2 |
| 30 | WebPay fiscal terminal identity | Platform open question | Explicitly open | Explicitly open | No update needed | Keep open until BIR/accounting confirms WebPay fiscal identity treatment | None | P2 |
| 31 | Sales Invoice numbering pattern | Annex G mentions running digits/reset counter if applicable | Explicitly open | Explicitly open | No update needed | Keep open pending BIR/accounting/API/DB alignment | None | P2 |
| 32 | Adjustment document numbering pattern | Annex G and fiscal adjustment needs | Explicitly open | Explicitly open | No update needed | Keep open pending BIR/accounting confirmation | None | P2 |
| 33 | Sequence gaps, reserved numbers, failed issuance, abandoned issuance | ARTS sequence auditability; BIR fiscal numbering | Explicitly open | Explicitly open | No update needed | Carry to API/DB design without deciding in BRD/SDD | None | P2 |
| 34 | X-read and Z-read aggregation scope | Annex D samples and platform open question | Explicitly open | Explicitly open | No update needed | Keep open pending BIR/accounting and design confirmation | None | P2 |
| 35 | VAT/tax treatment | BIR reports and ARTS tax model | Explicitly open | Explicitly open | No update needed | Preserve finance/accounting confirmation dependency | None | P2 |
| 36 | Diplomat VAT treatment, evidence, wording, reporting, retention | RMO 10-2019 and BRD decisions | Explicitly open except active category decision | Explicitly open | No update needed | Keep open for BIR/accounting/security/privacy confirmation | None | P2 |
| 37 | Digital SI URL token/access/expiry/authentication model | Digital SI BRD requirement and API planning | Explicitly open | Explicitly open | No update needed | Keep open for Security/Privacy/API design | None | P2 |
| 38 | Export exact formats | Gap analysis, accreditation requirements, ARTS POSLog materials | Open at BRD level | Open but should reflect source-supported candidates | API Contract update only | Add candidate formats and validation/error semantics in API Contract while final mandatory list remains open | POS Server API Contract | P1 |
| 39 | Audit trail exact layout | BIR Recommended Formats deck; Annex G | Covered as audit trail support, exact layout not fixed | Covered as audit service, exact layout not fixed | BIR/accreditation package input | Use source materials to prepare sample audit trail report for examiner package | BIR/accreditation package | downstream |
| 40 | Final accreditation sample package | Gap analysis and developer checklist deliverables | Not a BRD baseline detail | Not a system architecture detail | BIR/accreditation package input | Prepare sample outputs, manuals, exports, and evidence separately | BIR/accreditation package | downstream |
| 41 | Tamper-evident anchoring/recovery mechanism | BIR/ARTS sequence auditability; approved recovery requirements | BRD states business control requirement | System Design states design requirement, implementation open | Database Design input | Carry anchoring/recovery mechanism to DB/Engineering design without reopening BRD | POS Server Database Design | downstream |
| 42 | Final endpoint naming / DTO / DB / event / RBAC details | API planning and implementation detail | Out of BRD scope | Out of System Design final detail scope | API Contract update only | Resolve or mark provisional in API Contract; carry DB/event/RBAC details to downstream designs | POS Server API Contract | P1 |

## 12. Recommended Document Update Sequence

1. **Targeted BRD update**
   - Add explicit reprint coverage for Sales Invoice, X-Read, Z-Read, and Electronic Journal where applicable.
   - Add explicit `REPRINT` and `DATE / TIME REPRINTED` label requirements where BIR requires them.

2. **Targeted POS Server System Design update**
   - Apply the resolved QR responsibility decision.
   - Add SI/X/Z/EJ reprint coverage and required reprint label/timestamp behavior.
   - Add ARTS POSLog 6.x-aligned POSLog export posture with BIR/local extensions.
   - Add structured JSON schema validation posture.
   - Add ONLINE/OFFLINE operational/status indicator handling.

3. **POS Server API Contract update**
   - Update digital SI/QR contract semantics to URL-only from POS Server.
   - Add reprint/report/export/status semantics from the targeted BRD/System Design update.
   - Add ARTS POSLog-aligned export and JSON validation semantics.
   - Add BIR Sales Summary minimum content/output semantics.
   - Keep final DTO, endpoint, DB, event, RBAC, and exact export format decisions provisional where still open.

4. **Database Design / Engineering Pack / BIR package**
   - Map canonical fiscal records to ARTS/BIR structured exports.
   - Add validation/generation tests.
   - Prepare accreditation sample outputs and evidence package.

## 13. Recommended Next Codex Task

Recommended next task:

**Apply targeted BRD and POS Server System Design updates from `ExitPass_BIR_ARTS_Source_Impact_Review.md`, without editing the API Contract yet.**

The next task should update only the approved BRD and approved System Design for the P1/P2 baseline impacts identified here, then run validation. After that, proceed to the POS Server API Contract update.

## 14. Open Risks and Downstream Confirmations

- BIR/accreditation reviewers may require exact printed and exported layouts that differ from the current sample interpretation.
- ARTS POSLog 6.x alignment must not replace BIR Sales Invoice, X-Read, Z-Read, EJ, BIR Sales Summary, Annex E, and footer/header requirements.
- POSLog JSON/XML choice and schema validation obligations require confirmation before implementation.
- QR code display/printing responsibility is resolved, but API response shape and terminal display behavior still require API/Engineering design.
- ONLINE/OFFLINE status must not be interpreted as permission for offline fiscal issuance.
- Sequence gap, reserved number, failed issuance, abandoned issuance, and idempotency behavior remain high-risk downstream API/DB decisions.
- Supplier/applicant/accreditation responsibility remains open and can affect footer fields, manuals, package evidence, and PTU/accreditation submissions.
- Diplomat VAT Privilege / VAT Exemption remains active but its evidence, wording, reporting, and retention details still require BIR/accounting/security/privacy confirmation.
