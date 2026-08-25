# ExitPass Operator Console Deferred Payable-Basis Lifecycle Correction v1.0

## Purpose and provenance

This focused correction addresses the blocking criterion exposed by `OPCON-MVP-ACCEPT-20260824T232009Z-merged-rerun`. That acceptance remains failed and its evidence is unchanged. The failure treated a nullable payable-basis application state after an early approval as a missing result. This record corrects the product contract, presentation, and targeted acceptance criterion; it does not establish whole-console runtime acceptance.

The correction starts from `origin/dev` at `095db7766e274d28c50e6edf30f4e08b16c01a11` on branch `fix/statutory-benefit-deferred-payable-basis-lifecycle` in `D:\wt\StatutoryBenefitDeferredPayableBasisLifecycle`. The baseline contains the merged server-owned operating-context correction and canonical decision-response correction.

## Canonical lifecycle decision

Central PMS owns five separate stages:

1. statutory-benefit request;
2. eligibility review and terminal decision;
3. parking-session eligibility tagging;
4. payable-basis creation;
5. statutory-benefit calculation and application.

The existing locked-v1.2 `discounts.statutory_discount_validations` record is the canonical eligibility tag. An approved service-channel review persists the session, entitlement, frozen policy authority, reviewer attribution, and approved validation without requiring a tariff snapshot, currency, gross amount, discount amount, or net amount. It creates no payable-basis application record. No table, migration, duplicate boolean, or competing registry is added.

When WebPay or APT later supplies a server-created active tariff snapshot through the shared Central PMS statutory-decision facade, Central PMS resolves the approved validation, validates the current tariff and policy linkage, calculates the PHP statutory benefit, persists the immutable application, and creates the applied tariff snapshot. The application retains the originating canonical decision channel. Idempotent replay converges on one application and one applied snapshot; pending and rejected requests cannot create an application.

The shared response and Operator Console detail read monetary facts only from the persisted payable-basis application. Eligibility decisions do not fabricate monetary facts. `payableBasisApplicationStatus = null` remains canonical when no basis exists.

## Operator Console presentation

The canonical review detail separates workflow decision, session eligibility, payable-basis existence, discount-application state, and read-only monetary results. An approval before basis creation displays:

- `Decision: Approved`;
- `Session eligibility: Eligible`;
- `Payable basis: Not yet created`;
- `Discount application: Pending payable-basis creation`;
- explanatory text that Central PMS will calculate and apply the benefit when the basis is created.

No original, discount, or final payable amount is rendered before basis creation. When Central PMS returns an explicit application status and PHP monetary result, the UI preserves that state and renders the read-only original, statutory-benefit, and final payable amounts. Operator Console neither calculates nor applies the benefit.

## Boundaries

Operator Console continues to call Central PMS only. It adds no WebPay, APT, POS Server, Vendor PMS, HikCentral, provider, or Management Platform client. Server-owned device/shift context, H-006 session enforcement, CSRF, RBAC, Site/Site Group concealment, authorization epoch, decision concurrency, and PHP-only handling are unchanged. The locked v1.2 DDL is unchanged.

Late approval after a payable basis already exists has no authoritative rule in the reviewed v1.3 contracts. This correction does not retroactively mutate such a basis; that scenario requires a separate governed decision if it becomes required.

## Validation posture

Targeted validation covers true zero-tariff approval for WebPay and APT, durable eligibility with nullable tariff and monetary columns, later PHP basis creation and application, shared response/readback monetary results, payment/fiscal linkage, replay, concurrency, rejected and pending negative paths, PostgreSQL persistence, RBAC/scope, canonical H-006 audience isolation, frontend contract rendering, and 1440x900, 768x1024, and 390x844 Chromium layouts.

Obsolete local-fallback readiness and pre-device-binding H-006 fixtures are reported separately and are not counted as canonical passes. Production authorization was not weakened to satisfy them.

Review posture: `SELF-REVIEWED`.

Independent review: `NOT_PERFORMED`.

Whole-console integrated runtime and visual acceptance remains pending. The mandatory next task after merge is **Operator Console MVP Whole-Console Integrated Runtime and Visual Acceptance Rerun**.