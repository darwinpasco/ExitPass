# ExitPass Central PMS Terminal Cash Statutory Fiscal Linkage Implementation Note

## Scope

This implementation slice extends the existing Central PMS terminal-cash fiscal issuance path so a confirmed terminal-cash payment governed by an applied statutory-discount payable basis carries canonical statutory linkage into the POS Server fiscal request.

The slice depends on the APT statutory cash-acceptance readiness authorization audit decision `READY_WITH_BOUNDED_FISCAL_LINKAGE_GAP`. It closes only that Central PMS fiscal-linkage gap.

The public terminal-cash contract is unchanged. APT does not submit statutory discount amounts, VAT values, reviewer facts, fiscal content, receipt content, POS Server payloads, HikCentral data, ExitAuthorization data, or gate commands.

## Authority Boundaries

Central PMS remains authoritative for:

- statutory decision and application readback;
- applied tariff snapshot ownership;
- statutory validation linkage;
- approved VAT, discount, original amount, and final payable amount facts;
- terminal-cash payment and fiscal orchestration.

POS Server remains authoritative for Sales Invoice fiscalization, numbering, persistence, and presentation.

APT remains outside this slice. Statutory Continue to Cash and statutory `CASH_RECEIVED` remain not authorized until a separate desktop task enables them against this linkage.

## Linkage Anchor

The linkage reader uses the terminal-cash payment attempt tariff snapshot as the smallest authoritative anchor. When that snapshot is non-statutory, fiscal issuance preserves the existing request shape and sends empty POS Server statutory discount reference and privilege-detail collections.

When the snapshot is statutory-applied, the reader resolves exactly one canonical applied statutory application and validates:

- approved decision command;
- applied application command;
- statutory validation;
- applied tariff snapshot;
- parking session;
- Site and Site Group where available;
- final amount and currency;
- approved VAT and discount facts.

Missing, ambiguous, mismatched, pending, rejected, or incomplete statutory linkage blocks POS Server fiscal creation before any POS Server side effect is attempted.

## POS Server Mapping

For complete approved statutory context, Central PMS populates the existing POS Server-compatible fiscal request structures:

- `DiscountReferences` receives approved statutory reference linkage, including the validation reference, decision command reference, application command reference, entitlement type, original and applied tariff snapshot references, original amount, VAT-exclusive amount, VAT treatment, statutory discount amount, final payable amount, currency, source channel, and decision timestamp.
- `DiscountPrivilegeDetails` receives safe approved privilege facts, including the existing POS Server statutory discount privilege controlled-code reference `10000000-0000-0000-0000-000000000501`, entitlement type, masked beneficiary reference, validation evidence reference, approval reference, VAT-exclusive basis amount, discount amount, VAT privilege amount, final amount, currency, and statutory command context.

The mapping does not send reviewer identity, reviewer notes, full statutory ID, raw evidence, Base64 evidence, OCR text, credentials, authorization headers, raw SQL, stack traces, or downstream response bodies.

Null canonical facts remain unavailable. The service does not substitute zero values for required missing statutory facts.

## Failure Classification

Transient database or readback unavailability maps to retryable fiscal linkage posture and preserves existing fiscal retry/reconciliation handling.

Terminal or support-required posture is used for structural inconsistencies, including missing application linkage, ambiguous linkage, unapproved decision, application not applied, missing validation, session mismatch, Site mismatch, Site Group mismatch, amount/currency mismatch, and incomplete approved facts.

Invalid statutory linkage suppresses the POS Server call and does not create a fiscal issuance reference.

## Idempotency

The implementation preserves existing terminal-cash fiscal issuance idempotency. Replays for the same terminal-cash tender reuse the same fiscal issuance reference path and do not create statutory decisions, statutory applications, payment attempts, duplicate POS Server fiscal documents, duplicate discount references, or duplicate privilege details.

## Database Posture

No database change is introduced. The implementation reads the existing canonical statutory decision, statutory application, statutory validation, service-channel review, and tariff snapshot relationships.

## Validation

Focused validation covers:

- non-statutory terminal-cash fiscal issuance remains unchanged;
- non-statutory fiscal requests keep statutory collections empty;
- approved applied statutory context populates POS Server discount references;
- approved applied statutory context populates POS Server discount privilege details;
- sensitive statutory/reviewer evidence is excluded;
- invalid statutory linkage blocks before POS Server fiscal creation;
- existing fiscal idempotency and readback behavior remain intact.

Proof script:

`scripts/Invoke-CentralPmsTerminalCashStatutoryFiscalLinkageProof.ps1`

## Deferred Work

Deferred to separate bounded tasks:

- Windows APT statutory Continue to Cash;
- Windows APT statutory `CASH_RECEIVED`;
- controlled UAT;
- cash controlled UAT;
- production rollout;
- receipt rendering changes, unless future POS Server evidence requires them;
- ExitAuthorization readback;
- gate integration.
