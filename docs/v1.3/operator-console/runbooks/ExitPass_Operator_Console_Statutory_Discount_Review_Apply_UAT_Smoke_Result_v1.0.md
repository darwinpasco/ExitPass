# ExitPass Operator Console Statutory Discount Review/Apply UAT Smoke Result v1.0

## Result

PASSED.

Darwin completed the manual browser rerun for the controlled local Operator Console statutory discount review/apply flow. Ticket Lookup, draft creation, metadata-only evidence capture, approval, and payable-basis application all completed with the expected Senior Citizen statutory discount amounts and without unsafe side effects.

## Proof Level

Browser-executable manual UAT smoke path prepared with focused source fixes and validation.

## Fixture

| Field | Value |
| --- | --- |
| Environment | Local Central PMS validation DB `exitpass_v12_dev` on port 5433 |
| Entitlement | `SENIOR_CITIZEN` |
| Ticket/reference | `E2E-231-SESSION-001` |
| Parking session ID | `23100000-0000-0000-0000-000000000003` |
| Fixture table/columns | `core.parking_sessions.vendor_session_ref` and `core.parking_sessions.ticket_number_masked` |
| Fixture site group | `77000000-0000-0000-0000-000000000001` |
| Fixture site | `77000000-0000-0000-0000-000000000002` |
| Expected original gross | 12500 |
| Expected VAT-exclusive | 11161 |
| Expected VAT | 1339 |
| Expected statutory discount | 2232 |
| Expected final payable | 8929 |

## Root Cause Findings

| Blocker | Root cause | Resolution |
| --- | --- | --- |
| Ticket Lookup showed 10000 instead of 12500 | The UI used the older `/v1/ops/ticket-session-summary` path, which reads the vendor/session summary amount instead of the Operator Console session lookup/current Central PMS payable basis. | Ticket Lookup now uses `POST /v1/ops/operator-console/sessions/lookup` and displays the current payable amount from that read model. |
| `/operator-console/statutory-discounts` showed No drafts | The prepared validation `abe47095-b8c9-451c-a310-a3562863c0fb` was already `APPROVED`/`APPLIED`, so it was not a fresh review item for a happy-path browser smoke. The queue was not a reliable starting point for creating a new review. | Ticket Lookup now provides a safe "Start statutory discount review" flow that creates or opens a metadata-only draft from the looked-up session and navigates directly to the draft detail page. |
| Readiness showed unable to load | The local UI fallback operator context used stale site context, including an empty site group. That can make readiness and operator workflow calls fail in local/dev fixture posture. | Local UI fallback context now uses the controlled fixture site/site group IDs. If services are pointed at a different DB or fixture, readiness can still fail and should be treated as local configuration mismatch. |
| Session lookup returned `SESSION_NOT_FOUND` on rerun | `exitpass_v12_dev`, the DB used by the running local Central PMS API, did not contain the `E2E-231-SESSION-001` pilot rows. `centralpms_feq_retry_uat_local` was also checked and did not contain the fixture. | Reapplied `scripts/operator-console/Seed-StatutoryDiscountPilotFixture.sql` to `exitpass_v12_dev`, then verified the row with `scripts/operator-console/Verify-StatutoryDiscountPilotFixture.sql` and a live endpoint call. |
| Create review draft showed generic request failure | The browser client posted to `POST /v1/ops/operator-console/statutory-discounts/drafts`, but the backend create route is the singular `POST /v1/ops/operator-console/statutory-discounts/draft`. Sending the same payload to the correct route then showed the backend validator was too strict for the controlled masked reference `SC-UAT-****-0001`. The fixture had no existing Senior Citizen validation row, so this was not caused by an approved/applied duplicate. | Updated the UI client to call the singular create route. Relaxed the backend masked-reference validation to accept masked prefix/suffix values while still rejecting raw long numeric identifiers. Focused tests now cover `SC-UAT-****-0001` and the singular route. |
| Ticket not found continued on manual rerun | There was no deterministic local preflight proving the running Central PMS API process was connected to `exitpass_v12_dev` after seed/verify. Browser testing could still hit a stale API process, a wrong DB, or a service started without the required `ConnectionStrings__MainDatabase` override. | Added `scripts/operator-console/Invoke-StatutoryDiscountPilotPreflight.ps1`. It seeds/verifies `exitpass_v12_dev`, calls the live `POST /v1/ops/operator-console/sessions/lookup` endpoint, and fails unless the endpoint returns the fixture with `sessionFound=true`, `sessionEligible=true`, and amount `12500`. |
| Create review draft returned HTTP 500 after lookup was fixed | Runtime logs showed `Npgsql.PostgresException 23503` on `discounts.statutory_discount_validations`, constraint `fk_statutory_discount_validations__applied_policy_reference_`, from `OperatorConsoleStatutoryDiscountDraftWriter.InsertDraftAsync`. The policy resolver selected a dedicated-registry national fallback when the dedicated registry existed, while the validation schema stores policy references through `discounts.discount_policy_references`. The writer also populated `applied_policy_reference_id` during draft creation. | Policy resolution now checks the compatibility local policy table before national fallback even when the dedicated registry exists. Draft persistence resolves the schema-compatible policy reference, stores it in `evaluated_policy_reference_id`, leaves `applied_policy_reference_id` null until apply, and returns a safe 409 if a resolved policy cannot be mapped. The browser-shaped POST now returns HTTP 200 with a persisted draft. |
| Apply payable basis was disabled after approval | The draft detail read model exposed `original_tariff_snapshot_id` only from an already-created payable-basis application. Before application, the active original tariff snapshot was available from the parking session tariff snapshot, but it was not returned to the UI. The UI also added a stricter client-side block even though the apply API can safely derive the original snapshot from the approved validation/session. | Draft detail now returns the active original tariff snapshot before apply and returns applied snapshot/VAT/VAT-exclusive/final-payable fields after apply. The UI now shows `Ready to apply` for approved-but-not-applied drafts and does not block apply solely because the browser lacks an original snapshot ID. Apply API tests cover backend derivation when `originalTariffSnapshotId` is null. |
| Evidence/approval/apply state looked inconsistent | Draft creation creates a metadata-only evidence reference with status `REFERENCED`; that is not the same as captured/satisfied evidence. Approval is correctly blocked while `EvidenceCaptured=false`. The apply panel label also said `Awaiting approval` for approved-but-not-applied rows, which made the final verification and apply panel appear contradictory. | Approval remains fail-closed until metadata-only evidence capture sets evidence satisfied. The apply panel status now distinguishes `Awaiting approval`, `Ready to apply`, and `Applied`. Detail readback after API proof showed `APPROVED`, `evidenceCaptured=true`, `evidenceRequiredSatisfied=true`, `payableBasisApplicationStatus=APPLIED`, and the expected computed amounts. |

## Required Local Preflight

Before Darwin reruns the browser smoke, run:

```powershell
cd D:\SourceCodes\ExitPass
powershell -ExecutionPolicy Bypass -File scripts/operator-console/Invoke-StatutoryDiscountPilotPreflight.ps1
```

The preflight must print:

- `Expected Central PMS DB: exitpass_v12_dev`
- `DB fixture verified in exitpass_v12_dev.`
- `Live endpoint verified: sessionFound=true, sessionEligible=true, currentPayableAmountMinorUnits=12500.`
- `Preflight PASSED.`
- `Browser URL: http://localhost:5175/operator-console/ticket-lookup`
- `Ticket number: E2E-231-SESSION-001`

If the API is connected to the wrong DB or is not running, the preflight prints the exact Central PMS startup command. The required DB setting is:

```powershell
$env:ConnectionStrings__MainDatabase="Host=localhost;Port=5433;Database=exitpass_v12_dev;Username=exitpass;Password=change_me"
```

## v1.3 Findings

Applicable v1.3 documents under `docs/v1.3` place statutory discount validation, policy resolution, metadata-only evidence, and payable-basis application in Central PMS/Operator Console. POS Server owns Sales Invoice issuance. No v1.3 conflict was found with a local controlled browser smoke that creates a metadata-only review draft, approves it, applies payable basis, and avoids payment provider, HikCentral, gate, refund, POS Server fiscal issuance, and final BIR rendering behavior.

## Corrected Manual Browser Steps

1. Restart Central PMS from this branch against `exitpass_v12_dev`, the local validation DB that contains the `E2E-231-SESSION-001` fixture.
2. Start Operator Console UI on `http://localhost:5175`.
3. Open `http://localhost:5175/operator-console/ticket-lookup`.
4. Search `E2E-231-SESSION-001`.
5. Confirm Ticket Lookup shows current payable minor units `12500`.
6. In `Start statutory discount review`, keep entitlement `Senior Citizen`.
7. Keep metadata-only defaults or enter equivalent safe masked metadata.
8. Check the operator attestation.
9. Click `Create review draft`.
10. Confirm the draft detail page opens for `E2E-231-SESSION-001`.
11. Capture metadata-only evidence if required by the draft.
12. Approve the statutory discount.
13. Confirm the Apply payable basis panel shows `Ready to apply`.
14. Apply payable basis.
15. Confirm a non-null PayableBasisApplicationId.
16. Confirm amounts: original gross `12500`, VAT-exclusive `11161`, VAT `1339`, discount `2232`, final payable `8929`.
17. Confirm no payment, gate, HikCentral, refund/reversal, replacement Sales Invoice, POS Server fiscal issuance, or final BIR rendering action appears.

## Expected Browser Result

Darwin can now run the browser smoke from Ticket Lookup without relying on a pre-applied validation being visible in the drafts queue.

Expected final manual result after rerun:
- Ticket Lookup uses the current Central PMS payable basis amount `12500`.
- Ticket Lookup response has `sessionFound=true` and `sessionEligible=true`.
- Ticket number is `E2E-231-SESSION-001`, currency is `PHP`, and Sales Invoice number is not available.
- A fresh metadata-only Senior Citizen review draft is reachable from the browser.
- `Create review draft` posts to the singular backend create route and accepts `SC-UAT-****-0001`.
- The create response returns `draftAccepted=true`, `draftPersisted=true`, a non-null draft ID, and metadata-only evidence reference.
- The draft detail read model exposes original tariff snapshot `23100000-0000-0000-0000-000000000004` before payable-basis application.
- Evidence capture, approval, and apply payable basis complete from Operator Console.
- PayableBasisApplicationId is non-null.
- Amounts match `12500 / 11161 / 1339 / 2232 / 8929`.
- Unsafe side effects remain absent.

## Final Manual Browser Observations

Darwin's manual browser rerun passed with these observations:

- Ticket Lookup resolved `E2E-231-SESSION-001`.
- Current payable amount was `12500` / PHP 125.00.
- Ticket number and Sales Invoice number remained distinct.
- Senior Citizen statutory discount review draft was created from Ticket Lookup.
- Metadata-only evidence was captured.
- Approval succeeded.
- Apply payable basis succeeded.
- Application status became `APPLIED`.
- PayableBasisApplicationId: `54128dcc-dfd5-4ec4-9377-5759f202269c`.
- Original tariff snapshot: `23100000-0000-0000-0000-000000000004`.
- Applied tariff snapshot: `5c2a9ad0-84e0-47fb-9f78-4deaa9990396`.

Amount proof:

| Field | PHP | Minor units |
| --- | ---: | ---: |
| Original gross | 125.00 | 12500 |
| VAT-exclusive | 111.61 | 11161 |
| VAT | 13.39 | 1339 |
| Statutory discount | 22.32 | 2232 |
| Final payable | 89.29 | 8929 |

Browser safety message observed:

> This did not create payment, exit authorization, coupon, or gate records.

No payment provider, HikCentral, gate, ExitAuthorization, refund/reversal, Sales Invoice creation, fiscal number allocation, rendering/final BIR artifact, or raw evidence image/bytes appeared.

## Safety Side-Effect Assertions

The corrected path only creates/opens a statutory discount review draft and then uses the existing evidence, approval, and payable-basis application controls. It must not:
- call live payment provider
- call HikCentral
- issue ExitAuthorization
- open gate
- create refund/reversal
- create POS Server Sales Invoice
- allocate fiscal number
- render PDF/HTML/QR/final BIR artifact
- store raw evidence image/bytes

## Remaining Gaps

- This smoke stops at applied payable basis. Downstream payment confirmation and discounted Sales Invoice runtime proof remain covered by the prior backend/runtime proof chain.

## Files Changed

- `src/Services/OperatorConsoleUi/src/App.tsx`
- `src/Services/OperatorConsoleUi/src/apiClient.ts`
- `src/Services/OperatorConsoleUi/src/types.ts`
- `src/Services/OperatorConsoleUi/src/styles.css`
- `src/Services/OperatorConsoleUi/src/App.test.tsx`
- `src/Services/CentralPms/src/ExitPass.CentralPms.Infrastructure/Operations/TicketSessionSummaryReadRepository.cs`
- `src/Services/CentralPms/src/ExitPass.CentralPms.Application/OperatorConsole/OperatorConsoleAccessEvaluationService.cs`
- `src/Services/CentralPms/src/ExitPass.CentralPms.Application/OperatorConsole/OperatorConsoleStatutoryDiscountDraftModels.cs`
- `src/Services/CentralPms/src/ExitPass.CentralPms.Application/OperatorConsole/OperatorConsoleStatutoryDiscountReadModels.cs`
- `src/Services/CentralPms/src/ExitPass.CentralPms.Api/Endpoints/OperatorConsoleStatutoryDiscountDraftEndpoints.cs`
- `src/Services/CentralPms/src/ExitPass.CentralPms.Contracts/OperatorConsole/OperatorConsoleStatutoryDiscountReadDtos.cs`
- `src/Services/CentralPms/src/ExitPass.CentralPms.Infrastructure/OperatorConsole/OperatorConsoleStatutoryDiscountDraftWriter.cs`
- `src/Services/CentralPms/src/ExitPass.CentralPms.Infrastructure/OperatorConsole/OperatorConsoleStatutoryDiscountPolicyResolutionReadRepository.cs`
- `src/Services/CentralPms/src/ExitPass.CentralPms.Infrastructure/OperatorConsole/OperatorConsoleStatutoryDiscountReadRepository.cs`
- `src/Services/CentralPms/tests/ExitPass.CentralPms.IntegrationTests/Api/OperatorConsoleStatutoryDiscountDraftApiIntegrationTests.cs`
- `src/Services/CentralPms/tests/ExitPass.CentralPms.IntegrationTests/Api/OperatorConsoleStatutoryDiscountE2EIntegrationTests.cs`
- `src/Services/CentralPms/tests/ExitPass.CentralPms.UnitTests/Application/OperatorConsoleAccessEvaluationServiceTests.cs`
- `src/Services/CentralPms/tests/ExitPass.CentralPms.UnitTests/Application/OperatorConsoleStatutoryDiscountDraftServiceTests.cs`
- `scripts/operator-console/Seed-StatutoryDiscountPilotFixture.sql`
- `scripts/operator-console/Invoke-StatutoryDiscountPilotPreflight.ps1`
- `docs/v1.3/operator-console/runbooks/ExitPass_Operator_Console_Statutory_Discount_Review_Apply_UAT_Smoke_Result_v1.0.md`

## Validation

Validation commands and results:
- `npm.cmd --prefix src\Services\OperatorConsoleUi test -- --run` - passed, 82 tests.
- `npm.cmd --prefix src\Services\OperatorConsoleUi run build` - passed.
- `dotnet test src\Services\CentralPms\tests\ExitPass.CentralPms.UnitTests\ExitPass.CentralPms.UnitTests.csproj --filter "FullyQualifiedName~OperatorConsoleStatutoryDiscountDraftServiceTests|FullyQualifiedName~OperatorConsoleAccessEvaluationServiceTests|FullyQualifiedName~OperatorConsoleStatutoryDiscountPolicyResolutionServiceTests" --logger "console;verbosity=minimal"` - passed, 54 tests.
- `dotnet test src\Services\CentralPms\tests\ExitPass.CentralPms.IntegrationTests\ExitPass.CentralPms.IntegrationTests.csproj --filter "FullyQualifiedName~OperatorConsoleSessionLookupApiIntegrationTests|FullyQualifiedName~OperatorConsoleStatutoryDiscountDraftApiIntegrationTests|FullyQualifiedName~OperatorConsoleStatutoryDiscountE2EIntegrationTests|FullyQualifiedName~OperatorConsoleStatutoryDiscountPolicyResolutionApiIntegrationTests|FullyQualifiedName~OperatorConsoleDedicatedPolicyRegistryIntegrationTests" --logger "console;verbosity=minimal"` - passed, 41 tests.
- `dotnet build src\Services\CentralPms\src\ExitPass.CentralPms.Api\ExitPass.CentralPms.Api.csproj` - passed.
- `git diff --check` - passed; Git reported line-ending normalization warnings only.
- `scripts/operator-console/Seed-StatutoryDiscountPilotFixture.sql` applied to local `exitpass_v12_dev` - passed.
- `scripts/operator-console/Verify-StatutoryDiscountPilotFixture.sql` against local `exitpass_v12_dev` - passed; active session, tariff, site/site group, policy, operator, and no unsafe side-effect rows verified.
- `POST http://localhost:56065/v1/ops/operator-console/sessions/lookup` for `E2E-231-SESSION-001` with the browser-equivalent idempotency key - passed; `sessionFound=true`, `sessionEligible=true`, `currentPayableAmountMinorUnits=12500`.
- `powershell -ExecutionPolicy Bypass -File scripts/operator-console/Invoke-StatutoryDiscountPilotPreflight.ps1` - passed; seeded and verified `exitpass_v12_dev`, then the live endpoint returned `sessionFound=true`, `sessionEligible=true`, `currentPayableAmountMinorUnits=12500`.
- `POST http://localhost:56065/v1/ops/operator-console/statutory-discounts/draft` with the browser-equivalent payload for `E2E-231-SESSION-001` - passed; HTTP 200, `draftAccepted=true`, `draftPersisted=true`, `draftId=dc89514b-1b7f-439f-bd20-33bc8c059faa`, `evidenceReferenceId=9c40c875-a44b-4d9f-909a-46da656ab0ad`, policy `SANDBOX_OC_SD_REQUIRED_EVIDENCE_POLICY_235A`.
- DB verification after the draft POST - passed; `evaluated_policy_reference_id=23100000-0000-0000-0000-000000000002`, `applied_policy_reference_id` is null, evidence required true, evidence captured false.
- `dotnet test src\Services\CentralPms\tests\ExitPass.CentralPms.UnitTests\ExitPass.CentralPms.UnitTests.csproj --filter "FullyQualifiedName~OperatorConsoleStatutoryDiscountDecisionServiceTests|FullyQualifiedName~OperatorConsoleStatutoryDiscountEvidenceServiceTests|FullyQualifiedName~OperatorConsoleStatutoryDiscountApplyPayableBasisServiceTests|FullyQualifiedName~OperatorConsoleStatutoryDiscountDraftServiceTests|FullyQualifiedName~OperatorConsoleAccessEvaluationServiceTests" --logger "console;verbosity=minimal"` - passed.
- `dotnet test src\Services\CentralPms\tests\ExitPass.CentralPms.IntegrationTests\ExitPass.CentralPms.IntegrationTests.csproj --filter "FullyQualifiedName~OperatorConsoleStatutoryDiscountReadApiIntegrationTests|FullyQualifiedName~OperatorConsoleStatutoryDiscountEvidenceApiIntegrationTests|FullyQualifiedName~OperatorConsoleStatutoryDiscountDecisionApiIntegrationTests|FullyQualifiedName~OperatorConsoleStatutoryDiscountApplyPayableBasisApiIntegrationTests|FullyQualifiedName~OperatorConsoleStatutoryDiscountE2EIntegrationTests|FullyQualifiedName~OperatorConsoleStatutoryDiscountDraftApiIntegrationTests" --logger "console;verbosity=minimal"` - passed, 56 tests.
- `npm.cmd --prefix src\Services\OperatorConsoleUi test -- --run` - passed, 83 tests.
- `npm.cmd --prefix src\Services\OperatorConsoleUi run build` - passed.
- `dotnet build src\Services\CentralPms\src\ExitPass.CentralPms.Api\ExitPass.CentralPms.Api.csproj` - passed.
- Browser-equivalent API proof against local `exitpass_v12_dev` - passed for create draft, evidence-blocked approval, metadata-only evidence capture, approval, and apply payable basis with `originalTariffSnapshotId=null`; result returned non-null PayableBasisApplicationId, original snapshot `23100000-0000-0000-0000-000000000004`, applied snapshot, and amounts `12500 / 11161 / 1339 / 2232 / 8929`.
- Draft detail read with Operator Console identity headers - passed; pre-apply detail returned original tariff snapshot `23100000-0000-0000-0000-000000000004`, evidence required true, evidence captured false, evidence satisfied false, latest evidence status `REFERENCED`, and original amount `12500`.
- Final `powershell -ExecutionPolicy Bypass -File scripts\operator-console\Invoke-StatutoryDiscountPilotPreflight.ps1` after proof - passed; fixture was reset and live session lookup returned `sessionFound=true`, `sessionEligible=true`, `currentPayableAmountMinorUnits=12500`.
