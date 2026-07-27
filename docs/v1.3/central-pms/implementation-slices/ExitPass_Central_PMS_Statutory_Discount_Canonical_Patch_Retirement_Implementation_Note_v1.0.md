# ExitPass Central PMS Statutory Discount Canonical Patch Retirement Implementation Note v1.0

## Purpose

This bounded application slice retires the statutory-discount application-local database patches superseded by the merged canonical promotion in `D:\SourceCodes\exitpassdb_v1.2`.

No Central PMS runtime behavior, public DTO, API route, statutory calculation, VAT treatment, payment finality, fiscal issuance, ExitAuthorization, gate behavior, WebPay, APT, Operator Console UI, POS Server, or Management Platform behavior changed.

## Canonical Promotion Dependency

The retirement depends on canonical DB repository `D:\SourceCodes\exitpassdb_v1.2` branch `develop` at commit `636ca9c4b229b1d4e9d517f9251a0d5042950834`.

The canonical generated SQL now contains:

- `discounts.statutory_discount_decision_commands`
- `discounts.statutory_discount_payable_basis_application_commands`
- `operator_console.statutory_discount_service_channel_reviews`
- `discounts.statutory_discount_validations` decision-v2 metadata columns
- `AWAITING_REVIEW` decision command status support
- `NOT_DECIDED` decision result support
- service-channel review-to-validation linkage

## Retired Patches

These files moved from `infra/db/patches` to `infra/db/patches/retired` and are classified `RETIRED_CANONICAL_SUPERSEDED`:

- `ExitPass_OperatorConsoleStatutoryDiscountDecisionConvergence_v1.3.sql`
- `ExitPass_StatutoryDiscountDecisionFacade_v1.3.sql`
- `ExitPass_StatutoryDiscountStagedCanonicalCommands_v1.3.sql`
- `ExitPass_StatutoryDiscountServiceChannelPendingReviewIntake_v1.3.sql`
- `ExitPass_StatutoryDiscountServiceChannelOperatorConsoleReviewLinkage_v1.3.sql`
- `ExitPass_StatutoryDiscountServiceChannelPostApprovalApplicationIntent_v1.3.sql`

Their validation scripts moved to `infra/db/patches/retired/validation`. The current canonical validation authority is `D:\SourceCodes\exitpassdb_v1.2\scripts\validation\Validate-V13CentralPmsAlignment.sql`.

## Fixture Posture

Statutory integration fixtures no longer unconditionally execute the retired promoted patches or the already-retired payable-basis schema patch.

`StatutoryDiscountCanonicalSchemaPrerequisite` verifies the canonical schema before affected tests run. If the target database is missing the promoted canonical objects or constraints, tests fail with a setup error instructing the operator to rebuild or upgrade from the canonical database source. The helper does not apply retired patches as fallback.

## Historical Upgrade Compatibility

The retired scripts remain available as historical upgrade evidence under `infra/db/patches/retired`. Environments that already applied those application-local patches are handled by the canonical DB migration `20260727090000_statutory_discount_staged_service_channel_canonical_promotion.sql`.

## Validation Guard

`infra/db/patches/validation/Validate_RetiredStatutoryDiscountCanonicalPatches.ps1` verifies:

- the six promoted patches are absent from active top-level patch inventory
- retired patch and validation files are retained under retired inventory
- the retirement manifest maps the files to canonical replacements
- canonical generated SQL contains the promoted statutory tables, columns, and status vocabulary
- active statutory test fixtures do not reference the retired patch paths

## Deferred Work

- Align statutory-discount tests to canonical generated SQL disposable fixtures.
- Complete the broader documentation correction sweep for stale app-local database-source claims.
- Run proof-grade canonical-only runtime revalidation after fixture alignment.

## Authorization

This patch-retirement slice does not authorize WebPay or APT integration.

WebPay integration: not authorized yet
APT integration: not authorized yet
