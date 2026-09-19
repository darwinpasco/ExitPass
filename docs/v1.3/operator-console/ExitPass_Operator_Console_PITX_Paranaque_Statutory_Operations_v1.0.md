# ExitPass Operator Console PITX Parañaque Statutory Operations

Document version: 1.0  
Product version: ExitPass v1.3  
Status: Implementation-aligned

## Site Operator surface

A `SITE_OPERATOR` is an Operator Console user with direct Site scope. Navigation is derived from the effective permission set returned by Central PMS. A normal Site Operator at PITX Level 3 sees:

1. Overview
2. Ticket Lookup
3. Fiscal Status
4. Statutory Discounts

The browser does not infer access from role names. Modules for shifts, fiscal reports, audit, void audit, vendor acknowledgments, projection health, and policy import are visible only when their established permission is present. A direct request for a module without its permission returns a generic unavailable state, while backend authorization remains authoritative.

Ticket Lookup and Fiscal Status are read-only, Site-scoped operations. They require an authenticated Operator Console session, the applicable permission, and an effective direct Site grant. They do not require a trusted device, active shift, or unrelated global readiness result. Cross-Site reads remain denied.

Statutory draft, evidence, and policy reads follow the same Site-scoped posture. Controlled writes such as draft creation and evidence capture retain action-specific readiness checks; a write blocker does not hide otherwise permitted read-only data.

Site Operators may prepare drafts and evidence metadata. Approval and rejection remain segregated to Operations Supervisors with the applicable review and decision permissions.

## Parañaque policy authority

PITX Level 3 (`PITX-LEVEL-3`) resolves City of Parañaque through its canonical Site-jurisdiction assignment. The policy engine resolves policy authority by jurisdiction, so the same policies can apply to another eligible Parañaque Site without a PITX-specific calculation rule.

### Senior Citizen

- Policy: `PH_PARANAQUE_SENIOR_FREE_PARKING`
- Entitlement: `SENIOR_CITIZEN`
- Benefit: `FULL_FEE_EXEMPTION`
- Beneficiary scope: `RESIDENT_ONLY`
- Required evidence: `SENIOR_CITIZEN_ID`
- Verification: `VERIFIED_ACTIVE_OPERATIONAL`
- Publication: `ACTIVE_FOR_TRANSACTION_USE`
- Ordinance number: not asserted because no controlled ordinance number is retained

### Person with Disability

- Policy: `PH_PARANAQUE_PWD_FREE_PARKING`
- Entitlement: `PWD`
- Benefit: `FULL_FEE_EXEMPTION`
- Beneficiary scope: `RESIDENT_ONLY`
- Required evidence: `PWD_ID`
- Verification: `VERIFIED_ACTIVE_OPERATIONAL`
- Publication: `ACTIVE_FOR_TRANSACTION_USE`
- Authority reference: City Ordinance No. 48

Operational verification is distinct from official-source verification. Neither policy is represented as `VERIFIED_OFFICIAL` without the controlled source needed for that classification. Central PMS retains privacy-safe evidence metadata and does not require raw identity-document images for this workflow.

## Review and completion

WebPay and the Assisted Payment Terminal may submit an entitlement request but cannot approve it. Site Operators process Site-scoped draft and evidence work; Operations Supervisors retain decision authority. Central PMS applies the approved payable basis.

For a full fee exemption:

```text
FULL_FEE_EXEMPTION
    -> final payable = PHP 0.00
    -> ZERO_PAYABLE_STATUTORY_FINALITY
    -> no PaymentAttempt
    -> no PaymentConfirmation
    -> required POS fiscal completion
    -> normal ExitPass completion and exit path
```

The original amount, statutory benefit, final payable amount, policy version, jurisdiction, evidence/review references, and application reference remain auditable. WebPay hides payment methods at PHP 0.00 and uses statutory-completion language. The Assisted Payment Terminal does not collect cash or enter `CASH_RECEIVED` for the zero-payable result.

## ExitAuthorization completion authority

`ExitAuthorization` requires an established transaction completion authority. It is not intrinsically dependent on a payment attempt. The supported completion bases are:

- `PAYMENT_FINALITY`: requires the confirmed `PaymentAttempt`, recorded `PaymentConfirmation`, matching tariff/payable basis, and required fiscal completion.
- `ZERO_PAYABLE_STATUTORY_FINALITY`: requires the approved statutory decision, applied zero-payable basis, validation and policy ancestry, matching Site and tariff snapshot, and required fiscal completion. `PaymentAttemptId` and `PaymentConfirmationId` are null.

The authorization stores the completion basis, durable completion-authority reference, parking session, and tariff snapshot. Paid authorizations retain their payment identifiers. Zero-payable authorizations must not fabricate payment identifiers or use empty GUID values.

Issuance and consumption both validate the selected completion basis. A zero-payable authorization cannot be issued before fiscal completion, and its consume path revalidates the statutory payable-basis and fiscal ancestry without looking up a payment attempt. Each authorization remains time-bounded and consumable exactly once.

Central PMS communicates with HikCentral for the approved exit workflow. HikCentral remains the authority that communicates with and controls the physical parking gate. The ExitPass Gate Integration Service and legacy gate-command execution path remain retired and are not used for zero-payable statutory completion.
