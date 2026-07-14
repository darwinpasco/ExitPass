# ExitPass Central PMS Aligned DB Discounted Payment Fiscal ExitAuthorization Readiness Proof Result v1.0

## Result

PASSED for the current v1.3 readiness model.

The proof rebuilt a disposable Central PMS database from the canonical `exitpassdb_v1.2` generated SQL, validated the v1.3 Central PMS database objects, seeded the v1.3 UAT identities and statutory discount fixture, created an approved Senior Citizen discount, recorded discounted payment finality, recorded local POS Server fiscal issuance, evaluated ExitAuthorization fiscal readiness, issued an ExitAuthorization through the canonical typed DB routine, and proved replay was deterministic without gate consumption.

Current model note: fiscal-before-ExitAuthorization enforcement is exposed as readiness/shadow gating. The proof shows `would_allow` after payment finality plus recorded fiscal issuance and `would_block` readiness for missing fiscal, missing payment finality, and unsafe fiscal state. The current application still reports `EnforcementWiredForBlocking=False`, so hard blocking of the issuance routine by fiscal state remains a future enforcement slice.

## Canonical DB Source

| Item | Value |
| --- | --- |
| DB repo generated SQL | `D:\SourceCodes\exitpassdb_v1.2\build\generated\exitpass-full-object.generated.sql` |
| DB repo validation SQL | `D:\SourceCodes\exitpassdb_v1.2\scripts\validation\Validate-V13CentralPmsAlignment.sql` |
| Central PMS disposable DB | `centralpms_aligned_discount_exit_authorization_runtime_local` |
| Central PMS connection | `Host=localhost;Port=5433;Database=centralpms_aligned_discount_exit_authorization_runtime_local;Username=exitpass;Password=change_me;Include Error Detail=true` |
| POS Server DB/runtime | `posserver_api_smoke_validation_local`, `http://localhost:5000` |

## Fixture

| Item | Value |
| --- | --- |
| Ticket/reference | `E2E-231-SESSION-001` |
| Entitlement type | `SENIOR_CITIZEN` |
| Requester/evidence actor | `uat-operator-support` |
| Reviewer/apply actor | `uat-operations-supervisor` |
| Runtime proof run ID | `20260714150500` |
| Upstream finality reference | `STAT-DISCOUNT-EXIT-AUTH-READY:20260714150500:SENIOR_CITIZEN:001` |

## Amount Proof

| Amount | Minor units | Display |
| --- | ---: | --- |
| Original gross | `12500` | PHP 125.00 |
| VAT-exclusive | `11161` | PHP 111.61 |
| VAT | `1339` | PHP 13.39 |
| Statutory discount | `2232` | PHP 22.32 |
| Final payable / tender | `8929` | PHP 89.29 |

## Discount, Payment, And Fiscal Baseline

| Step | Result |
| --- | --- |
| Statutory discount validation | Created, evidence satisfied, and approved; validation ID `612eac8f-3cef-422d-956e-b1f2df9a20c0`. |
| Payable basis application | Applied successfully; PayableBasisApplicationId `77bf8ed1-c8c5-4335-94a3-2d008594774b`. |
| Applied tariff snapshot | `e9341d08-3a50-40e6-b2db-c75104836575`. |
| Payment attempt | `920772c1-76eb-490b-9d49-dba082984602`; created from the applied discounted tariff snapshot and asserted as PHP 89.29, not PHP 125.00. |
| Payment confirmation | `a8d6bfe4-d890-4ce6-9b6f-2f365d1c98b8`; recorded for PHP 89.29. |
| Central PMS fiscal issuance reference | `6e277145-cf07-4868-bd0d-17b7416011cd`; recorded after local POS Server issue. |
| POS Server Sales Invoice | POS fiscal document ID `6b9d75f2-3eb8-41a8-9e8b-0ff767f2f930`; Sales Invoice number `SI-00000015-UAT`; fiscal sequence `15`. |

The proof reused the local POS Server runtime path already used for aligned-DB discounted Sales Invoice creation and asserted gross, VAT-exclusive, VAT, statutory discount, final payable/tender, entitlement, payable-basis, applied tariff, payment attempt, and payment confirmation traceability.

## Positive ExitAuthorization Readiness Proof

| Check | Result |
| --- | --- |
| Payment finality context | Verified by discounted payment confirmation `a8d6bfe4-d890-4ce6-9b6f-2f365d1c98b8`. |
| Fiscal issuance context | Recorded Central PMS fiscal reference `6e277145-cf07-4868-bd0d-17b7416011cd` with POS Server document `6b9d75f2-3eb8-41a8-9e8b-0ff767f2f930`. |
| Readiness evaluation | `would_allow`. |
| Enforcement configuration | `enforcement_configured_readiness_only`; `EnforcementWiredForBlocking=False`. |
| ExitAuthorization issue | Created `f0c09a76-3bf8-4f1c-b666-68643e5215ca` with status `ISSUED`. |
| Payment/session traceability | Authorization references the controlled parking session and discounted payment attempt `920772c1-76eb-490b-9d49-dba082984602`. |
| Gate posture | `ConsumedAt` remained null; gate consumption count was `0`. |

## Replay Proof

| Proof | Result |
| --- | --- |
| Replayed ExitAuthorization request | Returned the same ExitAuthorization ID `f0c09a76-3bf8-4f1c-b666-68643e5215ca`. |
| Duplicate authorization count | Asserted one ExitAuthorization row for the controlled session. |
| Gate side effects | No gate consumption/open was created on replay. |

## Negative Readiness Proofs

| Scenario | Result |
| --- | --- |
| Missing fiscal issuance reference with payment finality present | Readiness `would_block`; blocked reason `fiscal_reference_not_recorded`. |
| Missing payment confirmation/finality with fiscal reference present | Readiness `would_block`; blocked reason `payment_finality_not_verified`. |
| Unsafe fiscal state | Fiscal state changed to conflict/not-assigned for evaluation; readiness `would_block`; blocked reason `fiscal_issuance_conflict`. |

These are readiness/fiscal-gating evaluator proofs. The current issue routine is not yet wired to hard-block on fiscal state, so the proof did not claim that a missing or unsafe fiscal record prevents direct typed DB routine issuance.

## Readback And Status Proof

- ExitAuthorization readback returned status `ISSUED`, the expected parking session, the discounted payment attempt, a non-empty authorization token, and `ConsumedAt = null`.
- Payment attempt readback retained applied tariff snapshot `e9341d08-3a50-40e6-b2db-c75104836575`, amount PHP 89.29, net amount PHP 89.29, and statutory discount PHP 22.32.
- Payment confirmation readback retained amount PHP 89.29.
- Fiscal issuance remained recorded with POS Server document ID, Sales Invoice number `SI-00000015-UAT`, and fiscal sequence `15`.
- Gate/external side-effect count excluding the intended ExitAuthorization row was `0`.

## Safety Assertions

- No live payment provider call was made.
- No live HikCentral call was made.
- No gate open command was sent.
- No gate authorization was consumed.
- No coupon or reconciliation mutation was created.
- No refund or reversal was created.
- No POS Server source code was changed.
- Fiscal number allocation occurred only inside the allowed local POS Server runtime.
- No final BIR PDF/HTML/QR rendering path was invoked.
- No raw statutory evidence bytes or raw evidence payloads were stored or sent.

## v1.3 Alignment Findings

The v1.3 docs and current source inspected for this slice preserve the expected authority boundaries:

- Central PMS owns platform payment finality, fiscal reference recording, and ExitAuthorization.
- POS Server owns fiscal document issuance and fiscal sequence.
- Operator Console and vendor connectors do not own payment finality, fiscal truth, ExitAuthorization, or gate opening.
- Current source explicitly keeps fiscal-before-exit authorization in readiness/shadow posture; no v1.3 documentation was found that permits silently bypassing that current source posture.

## Validation Commands And Results

| Command | Result |
| --- | --- |
| `git -C D:\SourceCodes\exitpassdb_v1.2 switch develop; git -C D:\SourceCodes\exitpassdb_v1.2 pull origin develop` | PASSED; canonical DB repo was already up to date on `develop`. |
| `powershell -NoProfile -ExecutionPolicy Bypass -File scripts\central-pms\Invoke-AlignedDbDiscountedPaymentExitAuthorizationReadinessProof.ps1 -RunId 20260714150000` | PASSED. Rebuilt the disposable Central PMS DB from canonical SQL, ran `Validate-V13CentralPmsAlignment.sql`, seeded/verified UAT identity/RBAC, seeded/verified statutory pilot fixture, checked local POS Server runtime, and ran the opt-in proof. |
| `dotnet test src\Services\CentralPms\tests\ExitPass.CentralPms.IntegrationTests\ExitPass.CentralPms.IntegrationTests.csproj --no-restore --filter "FullyQualifiedName~LocalRuntime_WhenEnabled_DiscountedPaymentAndFiscalIssuanceAreReadyForExitAuthorization" --logger "console;verbosity=detailed" -m:1 /p:UseSharedCompilation=false` | PASSED, 1/1. Captured the runtime identifiers in this result note. |
| `dotnet test src\Services\CentralPms\tests\ExitPass.CentralPms.UnitTests\ExitPass.CentralPms.UnitTests.csproj --no-restore --filter "FullyQualifiedName~ManagementPlatformUatIdentityRbacSeedTests\|FullyQualifiedName~FiscalIssuanceExitAuthorizationGateEvaluatorTests\|FullyQualifiedName~FiscalIssuanceExitAuthorizationPreflightTests\|FullyQualifiedName~IssueExitAuthorizationHandlerTests" -m:1 /p:UseSharedCompilation=false` | PASSED. |
| `dotnet test src\Services\CentralPms\tests\ExitPass.CentralPms.IntegrationTests\ExitPass.CentralPms.IntegrationTests.csproj --no-restore --filter "FullyQualifiedName~IssueExitAuthorizationIntegrationTests" -m:1 /p:UseSharedCompilation=false` | PASSED, 4/4. |
| `dotnet build src\Services\CentralPms\src\ExitPass.CentralPms.Api\ExitPass.CentralPms.Api.csproj --no-restore -m:1 /p:UseSharedCompilation=false` | PASSED. |

## Files Changed

- `scripts/central-pms/Invoke-AlignedDbDiscountedPaymentExitAuthorizationReadinessProof.ps1`
- `scripts/management-platform/Seed-ManagementPlatformUatIdentityRbac.sql`
- `scripts/management-platform/Verify-ManagementPlatformUatIdentityRbac.sql`
- `src/Services/CentralPms/tests/ExitPass.CentralPms.IntegrationTests/Api/OperatorConsoleStatutoryDiscountE2EIntegrationTests.cs`
- `src/Services/CentralPms/tests/ExitPass.CentralPms.UnitTests/Application/ManagementPlatformUatIdentityRbacSeedTests.cs`
- `docs/v1.3/central-pms/runbooks/ExitPass_Central_PMS_Aligned_DB_Discounted_Payment_Fiscal_ExitAuthorization_Readiness_Proof_Result_v1.0.md`

## Remaining Gaps

- Fiscal-before-ExitAuthorization enforcement is not yet wired as a blocking issue-time control. The current v1.3 source exposes readiness/shadow evaluation and reports `EnforcementWiredForBlocking=False`.
- A future hard-enforcement slice should wire the readiness result into the issue path and then repeat the missing-fiscal, missing-payment-finality, and unsafe-fiscal tests as true issuance-blocking integration cases.

