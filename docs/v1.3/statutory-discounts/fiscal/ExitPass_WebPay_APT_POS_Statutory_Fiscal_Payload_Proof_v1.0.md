# ExitPass WebPay and APT POS Statutory Fiscal Payload Proof v1.0

## Executive Decision

WebPay and the Assisted Payment Terminal must fiscalize approved and payment-time-applied statutory parking privileges through the same Central PMS to POS Server fiscal payload contract.

Central PMS remains the authority for statutory decision state, payable-basis application, and final payable-basis snapshots. POS Server receives first-class applied statutory fiscal facts only after payment finality or terminal-cash finality is ready for fiscal issuance. POS Server does not adjudicate entitlement, resolve ordinances, review evidence, or apply the benefit.

## Responsibility Boundary

| Concern | Authority |
|---|---|
| Statutory policy coverage | Management Platform and Central PMS read models backed by canonical policy coverage |
| Eligibility review | Operator Console approval or rejection only |
| Payable-basis application | Central PMS application-v1 invoked by WebPay or APT payment-time clients |
| Payment finality | Central PMS payment confirmation or terminal cash confirmation |
| Fiscal document creation | POS Server |
| Fiscal applied statutory fact persistence | POS Server |
| Fiscal readback and Digital Sales Invoice presentation | POS Server |

Operator Console cannot apply payable basis. Approval without application is not enough for fiscal statutory facts.

## POS Server Contract Used

The POS Server create-fiscal-document request supports `appliedStatutoryFiscalFacts` with these governed facts:

- `statutoryDiscountDecisionCommandId`
- `statutoryRequestReference`
- `statutoryPayableBasisApplicationCommandId`
- `statutoryValidationId`
- `parkingSessionId`
- `siteId`
- `siteGroupId`
- `entitlementType`
- `benefitClassification`
- `policyReference`
- `originalTariffSnapshotId`
- `appliedTariffSnapshotId`
- `originalAmountMinorUnits`
- `vatExclusiveBasisAmountMinorUnits`
- `vatAmountMinorUnits`
- `vatTreatment`
- `statutoryDiscountAmountMinorUnits`
- `finalPayableAmountMinorUnits`
- `currency`
- `appliedAt`
- `sourcePaymentChannel`
- `terminalCashTenderId`, for APT cash issuance

Amounts are integer minor units. Display-formatted currency is not authoritative.

## Central PMS Mapping

Central PMS now carries a shared `CentralPmsAppliedStatutoryFiscalFactsContext` inside the existing `CentralPmsFiscalDocumentMappingContext`.

`PosServerFiscalDocumentRequestMapper` maps that context into POS Server `AppliedStatutoryFiscalFacts` and validates before sending:

- final statutory payable amount matches the fiscal payable basis;
- currency matches the payable basis;
- parking session matches the fiscalized parking session;
- decision, application, validation, Site, Site Group, and tariff snapshot references are non-empty;
- original and applied tariff snapshots differ;
- amounts are nonnegative;
- classification fields are present.

The mapper does not query policy coverage and does not calculate statutory entitlement. It only serializes already-authoritative final applied facts.

## WebPay Trace

1. WebPay requests statutory review through Central PMS.
2. Operator Console approves or rejects eligibility.
3. WebPay requests application-v1 only after approval and at payment time.
4. Central PMS applies the benefit to the latest authoritative payable basis.
5. Payment finality is recorded.
6. Central PMS builds the POS fiscal context from the final applied payable-basis snapshot.
7. POS Server receives `sourcePaymentChannel = WEBPAY`.
8. POS Server persists one first-class applied statutory fact set.
9. POS Server readback and Digital Sales Invoice presentation expose the same governed facts.

The WebPay proof uses the existing statutory discount E2E fiscal path and now asserts that `AppliedStatutoryFiscalFacts` is present and reconciled.

## APT Trace

1. APT resolves ordinance availability through Central PMS.
2. APT revalidates before cash acceptance.
3. APT cannot complete statutory fiscal issuance unless Central PMS finds an approved decision and applied application matching the cash payment tariff snapshot.
4. CASH_RECEIVED remains the irreversible cash boundary.
5. Central PMS builds the POS fiscal context from the final applied payable-basis linkage.
6. POS Server receives `sourcePaymentChannel = ASSISTED_PAYMENT_TERMINAL` and the `terminalCashTenderId`.
7. POS Server persists one first-class applied statutory fact set.
8. POS Server readback and Digital Sales Invoice presentation expose the same governed facts.

The APT proof uses the terminal-cash fiscal issuance service and asserts the first-class facts, payment finality, and no exit or gate side effect during fiscal issuance.

## WebPay and APT Parity

Equivalent approved and applied statutory benefits produce equivalent statutory fiscal economics:

- same entitlement type;
- same benefit classification;
- same original amount;
- same VAT-exclusive basis amount;
- same VAT amount;
- same VAT treatment;
- same statutory discount amount;
- same final payable amount;
- same currency;
- same decision/application/snapshot identity semantics.

The only intentional channel differences are `sourcePaymentChannel` and the APT `terminalCashTenderId`.

## Idempotency and Semantic Hashing

Ordinary fiscal payloads continue to use `sha256:v1`.

Payloads with first-class applied statutory facts use `pos-server-fiscal-document-create:sha256:v2`, aligned with the POS Server statutory fiscal document contract. The statutory hash material includes all fiscal-significant statutory facts, including entitlement type, benefit classification, VAT treatment, statutory amounts, final payable amount, decision/application references, tariff snapshot references, source channel, and terminal cash tender reference when applicable.

Correlation identifiers remain transport observability and are not fiscal idempotency identity.

## Privacy Exclusions

The POS boundary must not receive:

- raw Senior Citizen or PWD identifiers;
- beneficiary names;
- reviewer identity;
- reviewer notes;
- evidence images;
- evidence URLs;
- object-storage locators;
- ordinance document content;
- SQL errors or stack traces.

APT statutory fiscal issuance no longer sends beneficiary or evidence references in discount privilege details. The POS payload uses governed statutory decision, application, validation, and tariff-snapshot references only.

## Failure Behavior

Central PMS fails closed before POS submission when applied statutory facts are incomplete, stale, mismatched, or non-reconciling.

POS Server remains the final fiscal validation authority and rejects:

- incomplete statutory facts;
- unsupported entitlement, benefit, VAT, policy-resolution, or source-channel classifications;
- currency mismatch;
- total mismatch;
- prohibited privacy fields;
- non-final statutory application facts.

POS Server failure does not produce a false fiscal success. Exit authorization remains gated by recorded fiscal issuance where fiscal issuance is mandatory.

## Ordinary Payment Preservation

Ordinary WebPay and ordinary APT fiscal contexts have no `AppliedStatutoryFiscalFacts` payload. Ordinary fiscal hashes remain `sha256:v1`, and ordinary payment behavior is unchanged.

## Validation Evidence

- Central PMS fiscal mapper, hash, and live-integration unit tests passed.
- Central PMS terminal-cash and statutory-discount E2E integration tests passed.
- POS Server runtime tests for applied statutory facts, semantic hashing, and Digital SI presentation passed.
- POS Server API tests for applied statutory facts passed.
- POS Server focused PostgreSQL smoke tests for persistence, readback, and presentation passed.

Controlled UAT and production rollout remain unauthorized.
