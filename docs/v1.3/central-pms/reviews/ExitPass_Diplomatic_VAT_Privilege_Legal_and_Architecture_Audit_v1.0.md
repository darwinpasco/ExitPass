# ExitPass Diplomatic VAT Privilege Legal and Architecture Audit v1.0

## 1. Document control

| Field | Value |
| --- | --- |
| Document | ExitPass Diplomatic VAT Privilege Legal and Architecture Audit v1.0 |
| Workstream | ExitPass v1.3 Central PMS statutory privilege research |
| Date accessed | 2026-07-28 |
| Persona | Codex I |
| Output type | Docs-only legal and architecture audit |
| Implementation posture | Not authorized by this report |

## 2. Purpose

This audit determines whether ExitPass v1.3 can safely start a Diplomatic VAT Privilege workstream. It covers current legal authority, parking-service applicability, credential and invoice evidence, fiscal implications, and architecture fit against the existing Senior Citizen/PWD statutory-discount implementation.

This report is not a formal legal opinion.

## 3. Scope

In scope:

- Philippine legal and regulatory source review.
- Current ExitPass Central PMS statutory-discount architecture review.
- Read-only canonical database review.
- Read-only POS Server, WebPay, and APT boundary review where relevant.
- Gap analysis and bounded next-task recommendation.

Out of scope:

- Runtime implementation.
- API, DTO, SQL, test, Bruno, WebPay, APT, Operator Console UI, POS Server, fiscal, payment-finality, ExitAuthorization, gate, privacy-retention, or statutory formula changes.

## 4. Executive summary

Diplomatic VAT treatment must not be modeled as a Senior Citizen or PWD discount extension. The legal theory is an indirect-tax privilege that may produce a VAT zero-rated sale at point of sale, or a VAT-paid sale followed by refund or reimbursement, depending on DFA/BIR-recognized reciprocity and certificate/ruling terms.

The audit found no current official public source proving that ExitPass parking services are categorically eligible for point-of-sale diplomatic VAT zero-rating. The strongest available BIR-related material indicates that VAT privileges for resident foreign missions, qualified personnel, and dependents depend on DFA Office of Protocol reciprocity confirmation, BIR-issued VAT Certificate or VAT Identification Card terms, and conditions such as covered goods or services and minimum invoice amount. Parking therefore requires explicit BIR/DFA confirmation before design or implementation can be authorized.

The current ExitPass Senior Citizen/PWD statutory-discount architecture has reusable infrastructure patterns, but its domain model is materially misleading for diplomatic VAT. A separate Diplomatic VAT bounded context is the safest next architecture posture. It can reuse the staged command, review, idempotency, durable readback, and payable-basis application patterns, but should not reuse the `statutory-discounts` route, discount calculation semantics, or Senior/PWD entitlement type model as-is.

## 5. Disclaimer and legal-review posture

This audit is engineering analysis based on sources available during the review. It does not replace BIR, DFA Office of Protocol, POS accreditation, tax-counsel, privacy-counsel, or site-operator decisions. Any implementation must wait for formal legal and fiscal clarification.

## 6. Repositories and exact commits inspected

| Repository | Branch/status | Commit | Inspection posture |
| --- | --- | --- | --- |
| `D:\SourceCodes\ExitPass-Discounts` | `docs/diplomatic-vat-privilege-legal-architecture-audit`, clean before report | `d0a9d948ce7a6afb8b3c41c411fad8fef80c530c` | Primary audit repository |
| `D:\SourceCodes\exitpassdb_v1.2` | `develop`, clean, equals `origin/develop` | `636ca9c4b229b1d4e9d517f9251a0d5042950834` | Read-only canonical database source |
| `D:\SourceCodes\ExitPass-PoSServer` | `dev`, clean | `46ddd685fcca2a9b9ecac8fb5fddc670207e35b1` | Read-only fiscal/POS capability inspection |
| `D:\SourceCodes\ExitPass-APT` | `dev`, clean | `3708bea0bd3617bfca5e6fec3f53e6cbb6f26841` | Read-only APT contract boundary inspection |
| `D:\SourceCodes\ExitPass-AssistedPaymentTerminal` | `feature/apt-statutory-discount-review-mediated-orchestration`, dirty before inspection | `9d963dfbfacd3c1b3571419a79541821ddbbd695` | Read-only APT implementation evidence; pre-existing local changes not modified |
| `D:\SourceCodes\ExitPass` | `feature/webpay-statutory-discount-local-walkthrough`, clean | `d0a9d948ce7a6afb8b3c41c411fad8fef80c530c` | Read-only WebPay boundary inspection |

## 7. Research methodology

The audit used current official sources first, then non-official copies only to identify source names and unresolved requirements. Browser results that could not retrieve an official BIR PDF were not treated as final authority.

Targeted local inspection used `rg`, `git status`, `git rev-parse`, and selected source reads. No external repository was modified.

## 8. Legal-source hierarchy

| Rank | Source class | Use in this audit |
| --- | --- | --- |
| 1 | Constitution, statute, official BIR/DOF/DFA/Official Gazette/Lawphil/Supreme Court source | Primary authority |
| 2 | Current BIR site service pages and Citizen's Charter | Current public process evidence |
| 3 | Non-official copies or summaries of BIR issuances | Discovery and issue identification only |
| 4 | Blogs, law-firm notes, social posts | Not used as final authority |

## 9. Official legal and regulatory sources

| Source | Status in audit | Relevance |
| --- | --- | --- |
| 1987 Constitution, Article II, Section 2, Lawphil: `https://lawphil.net/consti/cons1987.html` | Official legal source verified | Incorporates generally accepted international-law principles into Philippine law |
| NIRC VAT zero-rating provisions, including Section 108(B)(3), as quoted in Lawphil/Supreme Court decision `https://lawphil.net/judjuris/juri2011/mar2011/gr_172087_2011.html` | Official judicial source verified | Services to persons/entities whose exemption under special laws or international agreements effectively subjects supply to 0% VAT |
| RA No. 11976, Supreme Court E-Library: `https://elibrary.judiciary.gov.ph/thebookshelf/showdocs/2/96948` | Official judiciary source verified | EOPT invoice and tax-administration changes relevant to current invoice posture |
| RA No. 12066, Supreme Court E-Library: `https://elibrary.judiciary.gov.ph/thebookshelf/showdocs/2/98085` | Official judiciary source found | Later statutory amendments may affect current NIRC text and must be reviewed by tax counsel |
| BIR page, Taxation of Resident Foreign Missions and International Organizations: `https://www.bir.gov.ph/taxationofresidentsforeignmissionembassiesandconsulatesandinternationorganizations` | Official page found, detailed body not rendered by audit tooling | Confirms current BIR public category; detailed source still needed |
| BIR Citizen's Charter: `https://www.bir.gov.ph/citizencharter` | Official page verified | Current BIR service posture; no complete diplomatic VAT procedure extracted |
| RMO No. 10-2019 | Official issuance identified; official full text not retrieved through tooling | Core resident foreign mission VAT privilege rules require official copy before implementation |
| RMO No. 41-2020 | Official issuance identified; official full text not retrieved through tooling | Refund/reimbursement and online purchase clarification require official copy before implementation |
| RMC No. 44-2020 | Historical temporary ECQ guidance only | Electronic VC/VIC treatment cannot be treated as current general authority |
| RR No. 7-2024, RMC No. 77-2024 | Official issuance names identified; official full text not retrieved through tooling | EOPT-era invoice details require confirmation |

## 10. Current legal basis

The legal basis is not a commercial discount. It is a VAT privilege potentially grounded in international law, reciprocity, special law, treaty, or BIR/DFA-recognized exemption, implemented through Philippine tax rules.

The reliable current legal foundation for a service zero-rating theory is the NIRC service zero-rating concept for sales of services to persons or entities whose exemption under special laws or international agreements effectively subjects the supply to zero percent VAT. The resident foreign mission operational rules require official BIR issuance confirmation before implementation.

## 11. Reciprocity and DFA authority

Available RMO 10-2019 digest material states that DFA Office of Protocol provides categorical reciprocity confirmation and a country/jurisdiction list used by BIR ITAD to determine whether a foreign mission, personnel, or dependent receives point-of-sale VAT treatment or refund/reimbursement treatment.

Audit conclusion: reciprocity is mission or jurisdiction specific. ExitPass cannot activate diplomatic VAT treatment from a generic list or customer assertion. A formal DFA/BIR source, ruling, certificate, or approved policy registry is required.

## 12. Eligible party analysis

| Party | Audit finding |
| --- | --- |
| Resident foreign mission | Potentially eligible, subject to DFA/BIR confirmation and VC/ruling terms |
| Embassy | Potentially within resident foreign mission scope |
| Consulate | Potentially within resident foreign mission scope |
| Qualified personnel | Potentially eligible through VC/VIC and DFA ID, subject to terms |
| Qualified dependent | Potentially eligible through separate VC/VIC and dependent status, subject to terms |
| Authorized representative | Potentially valid for official mission purchase with authorization letter or SPA, subject to terms |
| International organization | Separate analysis required; do not assume resident foreign mission rules apply |
| Personnel of international organization | Status unresolved without treaty/organization-specific authority |

## 13. Resident foreign mission analysis

Resident foreign missions may receive VAT privilege based on reciprocity. Official mission purchases appear distinct from personal purchases by personnel. The system would need mission identity, mission accreditation, official-purchase authority, certificate/ruling reference, covered service scope, and effective dates.

## 14. Qualified-personnel analysis

Qualified foreign mission personnel may receive personal-purchase privileges only if the person holds or is covered by current BIR/DFA credentials. A parking transaction must verify identity match, personnel category, validity, service coverage, and any purchase limitations.

## 15. Qualified-dependent analysis

Dependents are not automatically covered by personnel credentials. Available digest material indicates dependents may receive separate VC/VIC documents. ExitPass would need dependent-specific credential evidence and cannot infer eligibility from the principal person's credential.

## 16. International-organization analysis

The BIR public page title includes international organizations, but the audit did not retrieve current detailed rules. International organizations and personnel require treaty, agreement, or BIR/DFA-specific analysis. They should not be silently included in a resident foreign mission program.

## 17. Official versus personal purchase

Official mission purchases and personal purchases have different credential and invoice requirements. ExitPass would need an explicit `purchasePurpose` or `officialOrPersonalPurchase` classification and evidence proving who is purchasing and under what authority.

## 18. Point-of-sale zero-rating

Point-of-sale zero-rating appears possible only where the mission/person/dependent is categorically endorsed and holds the applicable VC/VIC with terms allowing the purchase. This is not currently safe to implement for parking without official BIR/DFA confirmation of service coverage and invoice requirements.

Classification: `DIPLOMATIC_VAT_POINT_OF_SALE_NOT_AUTHORIZED`.

## 19. Refund and reimbursement

Available RMO 10-2019 and RMO 41-2020 discovery material indicates that refund or reimbursement may be the required method for some foreign missions or personnel. ExitPass should not reduce the payable amount when the privilege is refund-only. It may later support ordinary VAT-paid invoice evidence or a read-only refund-document package if legal requirements are confirmed.

Classification: `DIPLOMATIC_VAT_REFUND_DOCUMENTATION_STATUS_UNRESOLVED`.

## 20. Parking-service applicability

No official source retrieved during this audit specifically confirms parking services as eligible for point-of-sale diplomatic VAT zero-rating. General "goods and services" language is not sufficient because VC/VIC terms may include covered or excluded service categories and minimum invoice amounts.

Classification: `PARKING_SERVICE_REQUIRES_EXPLICIT_BIR_DFA_CONFIRMATION`.

## 21. Covered-service and transaction-limit analysis

ExitPass would need enforceable source data for:

- whether parking is covered
- official versus personal scope
- service category limits
- minimum purchase per invoice
- maximum amount, if any
- frequency restrictions, if any
- mission-specific restrictions
- suspension or cancellation

These facts require a governed policy registry sourced from official BIR/DFA evidence or formal legal review.

## 22. Credential and evidence requirements

Candidate evidence:

- BIR VAT Certificate reference.
- BIR VAT Identification Card reference.
- DFA Protocol ID or Certification of Accreditation reference.
- Mission identity and address.
- Qualified personnel or dependent identity evidence.
- Authorization letter or Special Power of Attorney for representative purchases.
- BIR ruling reference for refund/reimbursement mode.
- Certificate terms, effective dates, covered goods/services, and threshold terms.

Operational display should use masked values. Full values may be required only for legally mandated invoice or audit surfaces after privacy review.

## 23. Certificate validity, suspension, and cancellation

Available RMO 10-2019 digest material indicates VC/VIC validity may commonly be two years, but may end earlier when personnel leave, when privilege is cancelled, revoked, or suspended, or when reciprocity changes. Lost or replacement credentials are also governed. ExitPass would need a current registry or verification process; screenshot reuse is not enough.

## 24. Seller verification obligations

Seller verification likely includes:

- presented credential type
- identity match
- mission match
- credential number/reference
- validity and expiration
- original BIR seal or approved electronic equivalent
- no erasures or alterations
- DFA accreditation or Protocol ID
- representative authority for mission purchase
- covered goods/services and threshold terms
- suspended/cancelled privilege check

The audit did not find an official current online verification API.

## 25. Failed-verification treatment

When verification fails, ExitPass should deny only the VAT privilege request, not ordinary payment. The customer should be allowed to pay the ordinary VAT-inclusive parking amount. The system should preserve safe evidence that the privilege was declined and may provide ordinary invoice evidence for external refund where legally appropriate.

## 26. Current invoicing requirements

RMO 10-2019 discovery material indicates zero-rated mission/personnel invoices historically had to include seller details, transaction date, mission or buyer details, DFA ID, VC/VIC number, service description, value without VAT, and a prominent zero-rated label. EOPT-era rules under RA 11976 and related BIR regulations changed invoice terminology and official-receipt posture. The exact current invoice fields, full-versus-masked credential handling, digital invoice handling, printed invoice handling, reprints, and audit exports require official BIR/tax-counsel confirmation.

Surface classification:

| Surface | Data posture |
| --- | --- |
| Screen display | Masked credential references only |
| API payload | Masked/reference-only credential values |
| Application logs | No credential values, no evidence payloads |
| Database operations | Reference, masked, or encrypted audit values only where legally required |
| Digital invoice | Full legal values only if current BIR rules require them |
| Printed invoice | Full legal values only if current BIR rules require them |
| Electronic journal | Legal fiscal values subject to POS/privacy review |
| Audit export | Restricted, role-gated, retention-governed legal evidence |

## 27. Zero-rated versus exempt classification

Point-of-sale diplomatic treatment should be treated as a zero-rated taxable sale when the legal requirements are met, not as a Senior Citizen/PWD discount and not automatically as a VAT-exempt sale. Refund-only treatment remains an ordinary VAT-paid sale at ExitPass point of payment, subject to external refund/reimbursement.

## 28. Payable-amount and VAT-treatment analysis

ExitPass parking prices are generally customer-facing VAT-inclusive amounts. If point-of-sale zero-rating is legally authorized, the likely operational transformation is removal of the VAT component from the same tariff basis, not recording a commercial discount. The final formula, rounding level, and contractual tariff interpretation must be confirmed by tax counsel and fiscal design before implementation.

Do not hard-code a diplomatic VAT formula from this audit.

## 29. Rounding and tariff-snapshot considerations

Any future design must answer:

- whether rounding occurs per line or per transaction
- whether the standard VAT rate is parameterized by effective date
- whether gross displayed parking fees are contractual VAT-inclusive totals
- whether zero-rated payable uses the same original tariff snapshot
- whether POS Server must fiscalize the basis without recalculation
- how mixed VATable/zero-rated components are represented

## 30. Senior Citizen and PWD comparison

Senior Citizen and PWD parking flows are statutory-discount workflows with approved discount amount, VAT treatment, validation ID, evidence references, review, application, and payable-basis mutation. Diplomatic VAT is not a 20% discount. It may be a zero-rated sale or refund/reimbursement posture based on foreign mission reciprocity.

## 31. Stacking and exclusivity analysis

No source reviewed authorizes stacking Senior/PWD statutory discount with diplomatic VAT privilege. Future design must explicitly reject or govern multiple privilege claims for one parking session until BIR/tax counsel and product authority approve a stacking rule.

## 32. Current ExitPass capability inventory

Reusable capabilities:

- canonical command identity pattern
- idempotency and semantic hash pattern
- review-mediated Operator Console pattern
- durable POST/GET readback pattern
- payable-basis application pattern
- applied tariff snapshot pattern
- Site/Site Group scoping pattern
- safe evidence-reference posture
- APT payable-basis readiness dimensions
- payment initiation using effective applied snapshot
- POS Server fiscal document request mapping with tax, totals, and discount/privilege detail structure

Not directly reusable:

- `statutory-discounts` route name as diplomatic API
- Senior/PWD `entitlementType` semantics
- 20% discount formula assumptions
- `statutory_discount_validations` as diplomatic credential validation authority
- Senior/PWD evidence policy
- POS fiscal classification assumptions for discount privilege details

## 33. Current ExitPass semantic gaps

Current gaps include:

- no diplomatic or foreign mission registry
- no reciprocity policy registry
- no VC/VIC/ruling model
- no official/personal purchase model
- no service-category coverage model
- no refund-only result
- no zero-rated fiscal classification dedicated to diplomatic VAT
- no diplomatic invoice buyer/credential data model
- no privacy decision for full credential values on invoice versus storage
- no Management Platform workflow for policy import/activation
- no POS Server proof for diplomatic zero-rated invoice rendering and reporting

## 34. Architecture options

| Option | Summary | Assessment |
| --- | --- | --- |
| A. Extend existing statutory-discount model | Add diplomatic entitlement type | Rejected for initial design; misleading discount semantics and fiscal risk |
| B. General statutory-privilege framework | Refactor Senior/PWD and diplomatic into broader privilege model | Strategically attractive but high migration cost |
| C. Separate diplomatic VAT bounded context | Create separate decision/application/policy model for VAT privilege | Recommended first design path |
| D. Ordinary VAT payment and refund documentation only | No POS zero-rating; assist documentation | Safest operational fallback, still needs invoice/privacy design |

## 35. Recommended architecture

Classification: `CREATE_SEPARATE_DIPLOMATIC_VAT_BOUNDED_CONTEXT`.

The bounded context should reuse infrastructure patterns, not database tables or route names, from statutory discounts. Recommended domain name: `DIPLOMATIC_VAT_PRIVILEGE` as workstream label, with program records that can represent resident foreign missions, qualified personnel, dependents, and separately authorized international organizations.

## 36. Proposed authority model

| Component | Future responsibility |
| --- | --- |
| Central PMS | canonical privilege decision, Site/session/tariff context, payable-basis application, durable readback |
| Operator Console | credential/evidence review, official/personal classification, approval/rejection |
| Management Platform | foreign mission/policy/certificate registry administration and effective dating |
| WebPay | safe submission, polling, result display, payment gating |
| APT | assisted capture, polling, restart recovery, pre-cash gating |
| POS Server | fiscal classification, invoice rendering, X/Z/EJ/reporting, no entitlement approval |
| DFA/BIR | external legal authority |

## 37. Proposed domain concepts

Candidate concepts:

- `privilegeProgramType`
- `privilegeMethod`
- `beneficiaryClass`
- `foreignMissionId`
- `countryOrJurisdictionCode`
- `purchasePurpose`
- `officialOrPersonalPurchase`
- `coveredServiceCategory`
- `certificateType`
- `maskedCertificateNumber`
- `certificateIssuer`
- `certificateExpiresAt`
- `dfaProtocolIdReference`
- `birRulingReference`
- `authorizationLetterReference`
- `reciprocityPolicyReference`
- `zeroRatingAuthorized`
- `refundOnly`
- `originalVatInclusiveAmountMinorUnits`
- `vatExclusiveAmountMinorUnits`
- `vatAmountRemovedMinorUnits`
- `finalPayableAmountMinorUnits`
- `fiscalVatClassification`
- `evidenceReferences`

Full credential values should be avoided unless legally required for invoice or audit, then encrypted and access-governed.

## 38. Canonical identity and idempotency

Candidate business identity should likely include:

- parkingSessionId
- privilege program type
- purchase purpose
- beneficiary or mission reference
- certificate/ruling reference

Source channel should remain attribution, not identity. Cross-channel replay should converge when the same parking session and same privilege claim are represented, but replacement credentials or official-versus-personal changes should produce deterministic conflict or a governed replacement flow.

## 39. Proposed lifecycle

Candidate lifecycle:

```text
SUBMITTED
  -> AWAITING_REVIEW
  -> APPROVED_POINT_OF_SALE
      -> APPLICATION_PROCESSING
      -> APPLIED
  -> APPROVED_REFUND_ONLY
  -> REJECTED
  -> ADDITIONAL_EVIDENCE_REQUIRED
  -> EXPIRED / SUSPENDED / CANCELLED
  -> RETRYABLE_FAILURE / TERMINAL_FAILURE / REQUIRED_FACTS_UNAVAILABLE
```

The workflow must not leave a vehicle indefinitely blocked. Failed or unresolved privilege verification should fall back to ordinary VAT-inclusive payment.

## 40. Policy-registry requirements

The future registry needs official source evidence for:

- mission or country/jurisdiction
- eligible beneficiary classes
- point-of-sale versus refund treatment
- official versus personal purchase
- covered goods/services
- parking-service eligibility
- minimum or maximum purchase
- effective period
- suspension/cancellation
- DFA endorsement reference
- BIR certificate/ruling reference

Manual controlled entry is acceptable only with maker/checker approval and official source evidence.

## 41. Central PMS impact

Central PMS would need new privilege contracts, decision/application services, durable readback, policy resolution, amount transformation, payment gating, and failure/recovery mapping. Existing statutory-discount APIs should not be overloaded.

## 42. Operator Console impact

Operator Console would need diplomatic credential review queues, safe detail views, evidence controls, official/personal purchase classification, mission/certificate policy resolution, rejection reasons, and reviewer attribution. Existing Senior/PWD drafts should not be fabricated for diplomatic claims.

## 43. Management Platform impact

Management Platform would need administration for foreign missions, jurisdictions, certificate types, covered service categories, official/personal scope, effective dating, suspension/cancellation, and approved source references.

## 44. WebPay impact

WebPay could later submit safe credential references and evidence, poll decisions, display point-of-sale, refund-only, rejected, or ordinary-pay outcomes, and gate payment. It must not calculate VAT removal or approve entitlement.

## 45. APT impact

APT could later support assisted credential capture and restart-safe polling. It must block cash while the privilege is unresolved, refund-only with no applied basis, or not payable-ready. Cash acceptance remains subject to all existing terminal-cash readiness controls.

## 46. POS Server impact

POS Server would need explicit proof for diplomatic zero-rated invoice presentation, buyer/mission credential fields, tax classification, zero-rated totals, X/Z/EJ reporting, reprint, digital invoice, printed invoice, and fiscal export.

## 47. Database impact

Canonical database would likely need new objects for foreign mission registry, certificate/ruling references, reciprocity policy, diplomatic VAT decisions, applications, evidence references, fiscal linkage, and audit retention. Existing `discounts.statutory_discount_*` tables are not semantically sufficient.

## 48. API impact

The route `/v1/statutory-discounts/decisions` is not appropriate for diplomatic VAT because the transaction is not necessarily a discount. A future route such as `/v1/statutory-privileges/decisions` or a dedicated diplomatic VAT route should be evaluated in detailed design.

## 49. Fiscal and invoice impact

Fiscal handling must distinguish VATable, zero-rated, VAT-exempt, refund-only VAT-paid, and discount privilege details. Diplomatic VAT should not be reported as a Senior/PWD discount.

## 50. X/Z/EJ and reporting impact

X reading, Z reading, EJ, sales summaries, fiscal exports, and reconciliation must separate zero-rated sales and output VAT from discount totals. Existing POS scaffolding is generic but not proven diplomatic-ready.

## 51. Reconciliation impact

Reconciliation must match Central PMS privilege decision/application, POS fiscal classification, invoice evidence, tender amount, final payable amount, and any refund-only outcome. It must detect duplicate claims and privilege conflicts.

## 52. Security and privacy

Diplomatic credentials are sensitive. Privacy design must define lawful purpose, data minimization, encryption, masking, role access, logs, browser storage, APT local storage, retention, deletion, audit access, and incident response. Invoice-required full values must be handled separately from operational display and API readback.

## 53. Fraud and misuse controls

Controls needed:

- expiration and suspension checks
- identity and holder matching
- dependent status checks
- official purchase authorization
- excluded service and threshold enforcement
- lost/cancelled credential handling
- duplicate claim prevention
- screenshot/replay resistance
- no Senior/PWD stacking absent legal authority
- invoice buyer mismatch detection

## 54. Controlled-UAT prerequisites

Controlled UAT cannot start until:

- BIR/DFA confirm parking-service treatment
- point-of-sale versus refund mode is known for test data
- credential/invoice/privacy rules are approved
- Central PMS, Operator Console, Management Platform, POS Server, WebPay/APT designs are approved
- POS Server invoice and reporting proof exists
- channel implementations exist
- negative credential tests and ordinary-payment fallback pass

## 55. Production prerequisites

Production requires all UAT prerequisites plus production policy registry, migration, RBAC, monitoring, fiscal certification/signoff where applicable, privacy approval, operational training, reconciliation procedures, and support runbooks.

## 56. Legal authority matrix

| Source | Authority | Status | ExitPass relevance |
| --- | --- | --- | --- |
| 1987 Constitution Art II Sec 2 | Constitution | Current | International-law principle backdrop |
| NIRC Sec 108(B)(3) | Tax Code | Current subject to amendments | Possible zero-rated services basis |
| RA 10963 | Statute | Current/amended | Referenced by RMO discovery material |
| RMO 10-2019 | BIR | Official issuance, official text needed | Core resident foreign mission VAT privilege |
| RMO 41-2020 | BIR | Official issuance, official text needed | Refund/reimbursement procedures |
| RMC 44-2020 | BIR | Historical temporary | ECQ electronic VC/VIC only |
| RA 11976 | Statute | Current | EOPT invoice posture |
| RR 7-2024/RMC 77-2024 | BIR | Official text needed | Current invoice rules |

## 57. Eligibility matrix

| Eligible class | POS zero-rating | Refund/reimbursement | Status |
| --- | --- | --- | --- |
| Resident foreign mission | Possible | Possible | Requires certificate/ruling terms |
| Qualified personnel | Possible | Possible | Requires VC/VIC and DFA ID |
| Qualified dependent | Possible | Possible | Requires dependent-specific proof |
| International organization | Unresolved | Unresolved | Requires separate authority |
| Authorized representative | Possible for official purchase | Possible | Requires authorization evidence |

## 58. Point-of-sale versus refund matrix

| Mode | ExitPass payable effect | Required authority | Current status |
| --- | --- | --- | --- |
| Point-of-sale zero-rating | VAT component may be removed if authorized | VC/VIC, DFA/BIR terms, service coverage | Not authorized |
| Refund/reimbursement | Customer pays VAT-inclusive amount | BIR ruling/refund evidence | Status unresolved |
| Not eligible | Ordinary VAT-inclusive payment | Failed verification | Required fallback |

## 59. Credential and evidence matrix

| Evidence | Operational use | Storage posture |
| --- | --- | --- |
| VC/VIC reference | Eligibility proof | Masked/reference, full only if legally required |
| DFA Protocol ID | Identity/accreditation proof | Masked/reference |
| Mission accreditation | Mission validity | Reference |
| Authorization letter/SPA | Official purchase proof | Evidence reference, restricted access |
| BIR ruling | Refund/reimbursement proof | Reference and source document control |
| Credential image | High risk | Avoid unless required; encrypted/restricted if allowed |

## 60. Parking-service applicability matrix

| Question | Result |
| --- | --- |
| Does general law say all services always qualify? | No verified current official source supports that broad conclusion |
| Does parking have specific confirmed coverage? | Not found |
| Can VC/VIC terms limit service coverage? | Yes, based on available RMO 10-2019 digest |
| Required next authority | BIR ITAD and DFA Office of Protocol confirmation |

## 61. Invoice and fiscal matrix

| Requirement | Current ExitPass posture | Gap |
| --- | --- | --- |
| Zero-rated label | POS Server has invoice presentation scaffolding | Diplomatic-specific proof missing |
| Buyer/mission fields | Sales Invoice profile exists | Buyer/mission credential fields unresolved |
| Tax classification | POS tax details support generic classification | Diplomatic zero-rated codes not proven |
| X/Z/EJ totals | POS state has reporting scaffolds | Diplomatic zero-rated reporting proof missing |
| Refund-only invoice | Ordinary invoice possible | Refund package requirements unresolved |

## 62. Privacy classification matrix

| Data | Class | Default action |
| --- | --- | --- |
| Mission name | Business/legal identity | Store/display where required |
| Credential number | Sensitive identifier | Mask by default; full only for legal invoice/audit |
| Credential image | Sensitive evidence | Avoid or encrypt/restrict if mandated |
| DFA Protocol ID | Sensitive identifier | Mask by default |
| Authorization letter | Sensitive evidence | Evidence reference, restricted access |
| Invoice-required full value | Legal fiscal data | Separate controlled fiscal surface |

## 63. Component responsibility matrix

| Component | Ready for diplomatic VAT? |
| --- | --- |
| Central PMS | PARTIALLY_READY infrastructure, NOT_READY domain |
| Operator Console | PARTIALLY_READY review pattern, NOT_READY diplomatic workflow |
| Management Platform | NOT_READY registry |
| WebPay | NOT_READY diplomatic client |
| APT | NOT_READY diplomatic client |
| POS Server | PARTIALLY_READY fiscal scaffolding, NOT_READY diplomatic proof |
| Canonical DB | NOT_READY diplomatic objects |

## 64. Capability gap matrix

| Gap | Severity | Owner |
| --- | --- | --- |
| Parking-service eligibility unresolved | CRITICAL | BIR/DFA/tax counsel |
| Point-of-sale versus refund mode unresolved | CRITICAL | BIR/DFA/tax counsel |
| Diplomatic domain model absent | HIGH | Central PMS/database |
| POS diplomatic invoice/reporting proof absent | HIGH | POS/fiscal workstream |
| Credential privacy policy absent | HIGH | DPO/privacy counsel |
| Management policy registry absent | MEDIUM | Management Platform |
| WebPay/APT clients absent | MEDIUM | Channel teams |

## 65. Architecture option matrix

| Option | Fit | Risk | Decision |
| --- | --- | --- | --- |
| Extend statutory discount | Low | Misclassification | Reject |
| General privilege framework | Medium/long-term | Migration complexity | Defer |
| Separate diplomatic VAT bounded context | High for first design | Some duplicated infrastructure | Recommend |
| Refund documentation only | High as fallback | Limited benefit | Keep as fallback design path |

## 66. Channel readiness matrix

| Channel | Point-of-sale readiness | Refund-documentation readiness | Notes |
| --- | --- | --- | --- |
| WebPay | NOT_READY | STATUS_UNRESOLVED | Needs legal authority and client design |
| Cashier-assisted APT | NOT_READY | STATUS_UNRESOLVED | Needs credential capture and cash gating |
| Continuity APT | NOT_READY | NOT_READY | Higher risk; defer |
| APM | NOT_READY | NOT_READY | No human credential review |
| Cashier POS | STATUS_UNRESOLVED | STATUS_UNRESOLVED | Depends on POS fiscal model |
| Operator Console-assisted flow | PARTIALLY_READY pattern only | PARTIALLY_READY pattern only | Needs diplomatic workflow |

## 67. Open questions

- Does BIR/DFA treat parking as a covered service for any mission or personnel class?
- Which missions/personnel receive point-of-sale zero-rating versus refund/reimbursement?
- Are electronic VC/VIC presentations currently valid outside temporary ECQ guidance?
- What invoice fields are mandatory under EOPT for diplomatic zero-rated service sales?
- Must credential numbers be printed in full?
- What retention period applies to credential evidence and invoice data?
- Can Senior/PWD and diplomatic VAT privilege ever coexist for one parking session?

## 68. Required BIR/DFA clarifications

Required from BIR/DFA:

- parking-service eligibility
- point-of-sale versus refund-only treatment
- service-category and transaction-limit handling
- current VC/VIC format and electronic presentation validity
- seller verification procedure
- suspension/cancellation lookup procedure
- invoice wording and data fields

## 69. Required tax-counsel review

Tax counsel must review:

- zero-rated versus exempt classification
- payable amount formula
- rounding
- invoice and fiscal totals
- refund-only fallback
- interaction with Senior/PWD claims
- POS Server reporting and accreditation implications

## 70. Required privacy review

Privacy counsel or DPO must review:

- credential data classification
- full value versus masked value surfaces
- evidence image retention
- APT local storage
- browser storage
- logs
- audit exports
- deletion and incident response

## 71. Point-of-sale design decision

`DIPLOMATIC_VAT_POINT_OF_SALE_NOT_AUTHORIZED`

The point-of-sale design cannot proceed until parking-service eligibility, method, credential, invoice, and fiscal rules are officially confirmed.

## 72. Refund-documentation design decision

`DIPLOMATIC_VAT_REFUND_DOCUMENTATION_STATUS_UNRESOLVED`

Refund/reimbursement support may be safer than point-of-sale payable reduction, but the current refund evidence and invoice requirements remain unresolved.

## 73. Parking-service applicability decision

`PARKING_SERVICE_REQUIRES_EXPLICIT_BIR_DFA_CONFIRMATION`

## 74. Architecture decision

`CREATE_SEPARATE_DIPLOMATIC_VAT_BOUNDED_CONTEXT`

This decision authorizes only the next design direction after legal clarification. It does not authorize implementation.

## 75. Overall sequencing decision

`PROCEED_TO_FORMAL_BIR_DFA_CLARIFICATION`

The next step is not implementation. It is a formal authority clarification package and tax/privacy review request.

## 76. Exact next bounded task

Create a docs-only BIR/DFA clarification package for diplomatic VAT parking-service applicability:

- repository: `D:\SourceCodes\ExitPass-Discounts`
- branch: `docs/diplomatic-vat-bir-dfa-clarification-package`
- output: one clarification memo listing exact legal questions, sample parking scenarios, invoice samples, credential evidence assumptions, and desired yes/no answers
- validation: source-link verification and stakeholder review checklist

## 77. Tasks that must wait

- Central PMS diplomatic VAT API design.
- Canonical database design.
- Operator Console diplomatic review workflow.
- Management Platform mission registry.
- POS Server zero-rated diplomatic invoice implementation.
- WebPay or APT diplomatic VAT client implementation.
- Controlled UAT.
- Production rollout.

## 78. Known limitations

- Official full-text BIR RMO 10-2019, RMO 41-2020, RR 7-2024, and RMC 77-2024 were not retrieved through the audit tooling.
- Public DFA Office of Protocol reciprocity matrix was not found.
- No live BIR/DFA confirmation was obtained.
- External repositories were inspected read-only and may contain unmerged feature work.

## 79. Evidence appendix

Local files/components inspected:

- `src/Services/CentralPms/src/ExitPass.CentralPms.Api/Endpoints/StatutoryDiscountDecisionEndpoints.cs`
- `src/Services/CentralPms/src/ExitPass.CentralPms.Contracts/StatutoryDiscounts/StatutoryDiscountDecisionDtos.cs`
- `src/Services/CentralPms/src/ExitPass.CentralPms.Application/StatutoryDiscounts/StatutoryDiscountDecisionFacadeModels.cs`
- `src/Services/CentralPms/src/ExitPass.CentralPms.Application/StatutoryDiscounts/StagedStatutoryDiscountCommandModels.cs`
- `src/Services/CentralPms/src/ExitPass.CentralPms.Application/TerminalCashPayments/AptPayableBasisReadinessService.cs`
- `src/Services/CentralPms/src/ExitPass.CentralPms.Contracts/TerminalCashPayments/AptPayableBasisDtos.cs`
- `src/Services/CentralPms/src/ExitPass.CentralPms.Application/FiscalIssuance/PosServerFiscalDocumentRequestMapper.cs`
- `src/Services/CentralPms/tests/ExitPass.CentralPms.ContractTests/Public/StatutoryDiscountDecisionContractTests.cs`
- `D:\SourceCodes\exitpassdb_v1.2\build\generated\exitpass-full-object.generated.sql`
- `D:\SourceCodes\exitpassdb_v1.2\scripts\validation\Validate-V13CentralPmsAlignment.sql`
- `D:\SourceCodes\ExitPass-PoSServer\contracts\pos-server\sales-invoice-header-profile.v1.json`
- `D:\SourceCodes\ExitPass-PoSServer\contracts\pos-server\fiscal-document-presentation.v1.json`
- `D:\SourceCodes\ExitPass-PoSServer\db\state\tables\pos.fiscal_tax_details.sql`
- `D:\SourceCodes\ExitPass-PoSServer\db\state\tables\pos.fiscal_discount_privilege_details.sql`
- `D:\SourceCodes\ExitPass-PoSServer\db\state\tables\pos.fiscal_totals.sql`
- `D:\SourceCodes\ExitPass-PoSServer\db\state\tables\pos.x_z_reports.sql`
- `D:\SourceCodes\ExitPass-APT\contracts\central-pms\apt-session-payable-basis-readiness.v1.json`
- `D:\SourceCodes\ExitPass-AssistedPaymentTerminal\docs\decisions\ADR-0001-cashier-assisted-terminal-authority-boundary.md`
- `D:\SourceCodes\ExitPass-AssistedPaymentTerminal\docs\product\ExitPass_Assisted_Payment_Terminal_BRD_v1.1.md`
- `D:\SourceCodes\ExitPass\src\Services\WebPayUi\e2e\webpay-authoritative-sales-invoice.spec.ts`

Web/legal sources inspected:

- Lawphil 1987 Constitution: `https://lawphil.net/consti/cons1987.html`
- Lawphil Supreme Court decision quoting NIRC Section 108(B)(3): `https://lawphil.net/judjuris/juri2011/mar2011/gr_172087_2011.html`
- RA 11976, Supreme Court E-Library: `https://elibrary.judiciary.gov.ph/thebookshelf/showdocs/2/96948`
- RA 12066, Supreme Court E-Library: `https://elibrary.judiciary.gov.ph/thebookshelf/showdocs/2/98085`
- BIR resident foreign missions/international organizations page: `https://www.bir.gov.ph/taxationofresidentsforeignmissionembassiesandconsulatesandinternationorganizations`
- BIR Citizen's Charter: `https://www.bir.gov.ph/citizencharter`
- BIR Form 2550M/2550Q guidance pages showing zero-rated/exempt reporting lines: `https://efps.bir.gov.ph/efps-war/EFPSWeb_war/help/proc2550m2006.html`
- Non-official RMO 10-2019 digest used only as discovery evidence: `https://www.scribd.com/document/464341083/RMO-No-10-2019-Digest`
- Non-official RMO 41-2020 digest used only as discovery evidence: `https://www.scribd.com/document/979517635/Digest-Revenue-Memorandum-Order-No-41-2020`
- Non-official RMC 44-2020 summaries used only to confirm temporary ECQ posture: `https://www.bworldonline.com/economy/2020/04/21/290576/foreign-missions-issued-digital-vat-exemption-cards-during-lockdown/`

Validation commands:

```powershell
git branch --show-current
git status --short --branch --untracked-files=all
git fetch origin --prune
git log --oneline HEAD..origin/dev
git switch dev
git pull --ff-only origin dev
git rev-parse HEAD
git rev-parse origin/dev
git switch -c docs/diplomatic-vat-privilege-legal-architecture-audit
rg -n "StatutoryDiscountDecisionResponse|StatutoryDiscountDecisionRequest|PayableBasisReady|payableBasisReady|vatExclusive|vatAmount|VatTreatment|Senior|PWD|DIPLO|statutory-discounts" src/Services/CentralPms/src src/Services/CentralPms/tests docs/v1.3/central-pms/reviews docs/v1.3/central-pms/implementation-slices
rg -n "Invoice|SalesInvoice|OfficialReceipt|receipt|fiscal|XReading|ZReading|ElectronicJournal|EJ|SalesSummary|E-1|zero" src/Services/CentralPms/src src/Services/CentralPms/tests
rg -n "exitpass_v12_dev|ExitPass_Full_Database_Creation_DDL_v1.2.sql|ExecuteSqlFileAsync|EnsurePatchAppliedAndValidatedAsync|EnsureSchemaAsync|StatutoryDiscountCanonicalSchemaPrerequisite|EXITPASS_INTEGRATION_DB|ConnectionStrings__MainDatabase" src/Services/CentralPms/tests
rg -n "statutory_discount_decision_commands|statutory_discount_payable_basis_application_commands|statutory_discount_service_channel_reviews|vat_exclusive|vat_amount|zero|invoice|fiscal|foreign|mission|diplomat" objects/schemas build/generated/exitpass-full-object.generated.sql scripts/validation
rg -n --hidden --glob '!bin/**' --glob '!obj/**' --glob '!node_modules/**' --glob '!dist/**' --glob '!build/**' "zero|ZERO|VAT|vat|exempt|invoice|Invoice|receipt|Receipt|XReading|ZReading|ElectronicJournal|EJ|SalesSummary|discount|statutory|Senior|PWD|diplomat|mission|foreign" .
git diff --check
```

## 80. Final authorization lines

Diplomatic VAT point-of-sale implementation: not authorized yet
Diplomatic VAT refund-documentation implementation: not authorized yet
WebPay diplomatic VAT integration: not authorized yet
APT diplomatic VAT integration: not authorized yet
