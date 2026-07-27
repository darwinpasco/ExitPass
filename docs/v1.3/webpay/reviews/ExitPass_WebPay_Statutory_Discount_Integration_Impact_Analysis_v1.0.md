# ExitPass WebPay Statutory Discount Integration Impact Analysis

## 1. Executive Verdict

Overall verdict: **READY_WITH_APPLICATION_PREREQUISITE**.

Database verdict: **CANONICAL_DATABASE_ALIGNED_VALIDATION_REQUIRED**. Static canonical source, generated SQL, and promotion evidence are aligned and no WebPay implementation database change is required. Runtime disposable rebuild was not repeated in this read-only audit because direct `psql` was unavailable and Docker API access was denied locally; the canonical promotion report remains the executable rebuild/upgrade evidence.

WebPay implementation may begin. Database work does not need to happen first. Payment Orchestrator work must happen first because the WebPay browser currently calls only WebPay-facing Payment Orchestrator routes and has no browser-safe statutory decision/readback proxy or explicit statutory payable-basis payment gate.

Highest-risk finding: payment initiation must not rely on WebPay UI state alone. The implementation must add a Payment Orchestrator statutory proxy and payment-readiness guard so payment intent creation is blocked until Central PMS readback says `payableBasisReady=true`, the application is applied, an applied tariff snapshot exists, final payable amount exists, and currency exists.

Exact first bounded implementation task: **Add Payment Orchestrator WebPay statutory-discount proxy and payment-readiness gate**.

## 2. Scope and Authority Baseline

This is a read-only implementation impact analysis for WebPay statutory-discount integration. It does not implement WebPay integration, change code, change SQL, or modify canonical database artifacts.

Authority baseline:

- Central PMS owns decision-v2, application-v1, payment finality, fiscal-readiness orchestration, and ExitAuthorization.
- Operator Console reviews service-channel statutory-discount requests and approves or rejects the same canonical decision.
- WebPay is a payment channel. It must not calculate discount, VAT, or final payable amount locally.
- POS Server remains fiscal authority for fiscal issuance, numbering, document persistence, and authoritative Sales Invoice presentation.
- The canonical database authority is `D:\SourceCodes\exitpassdb_v1.2\build\generated\exitpass-full-object.generated.sql` plus object source in `objects/**`. The retired `D:\SourceCodes\ExitPass_DBv1.2` repository was not used.

## 3. Repository and Branch Evidence

| Repository | Path | Branch | HEAD | Upstream commit | Status | Latest baseline inspected |
| --- | --- | --- | --- | --- | --- | --- |
| Primary ExitPass | `D:\SourceCodes\ExitPass` | `docs/webpay-statutory-discount-integration-impact-analysis` | `75b1bf7770a8f944da93d1036e3654a86f861ffc` | `origin/dev` = `75b1bf7770a8f944da93d1036e3654a86f861ffc` | clean before report | `origin/dev` incorporated |
| Canonical DB | `D:\SourceCodes\exitpassdb_v1.2` | `develop` | `636ca9c4b229b1d4e9d517f9251a0d5042950834` | `origin/develop` = `636ca9c4b229b1d4e9d517f9251a0d5042950834` | clean | `origin/develop` current |

Preflight evidence:

- Primary branch status was `## docs/webpay-statutory-discount-integration-impact-analysis`.
- `git log --oneline HEAD..origin/dev` was empty.
- Canonical DB status was `## develop...origin/develop`.
- `git log --oneline HEAD..origin/develop` was empty.
- Retired repository `D:\SourceCodes\ExitPass_DBv1.2` was not accessed or used.

## 4. Active Branch and File-Overlap Inventory

Related local/origin branch inventory in the primary repository:

| Branch | Status | Evidence | Expected overlap |
| --- | --- | --- | --- |
| `dev` / `origin/dev` | integration baseline | both at `75b1bf7770a8f944da93d1036e3654a86f861ffc` | all WebPay statutory work should branch from here |
| `docs/webpay-statutory-discount-integration-impact-analysis` | current audit branch | same HEAD as `origin/dev` before report | adds this report only |
| `feature/statutory-discount-pos-server-runtime-proof` | stale local, upstream gone | `[gone]` at `660636a1128141a20e7f6d3565e94d85ee55f69d` | historical POS runtime proof only |
| `feature/central-pms-fiscal-void-command-runtime-proof` | stale local, upstream gone | `[gone]` at `7ff18a6a3a1bef23095aa9c377d0435c98b42363` | unrelated fiscal void proof |
| `feature/operator-console-read-after-void-manual-browser-smoke-result` | stale local, upstream gone | `[gone]` at `a235d4d571b3ad07070cbfdc0696a4ac1a3f16ad` | unrelated Operator Console void smoke |

Likely implementation conflict areas:

- `src/Services/WebPayUi/src/App.tsx`
- `src/Services/WebPayUi/src/webpay.ts`
- `src/Services/WebPayUi/src/types.ts`
- `src/Services/WebPayUi/src/App.test.tsx`
- `src/Services/WebPayUi/src/webpay.test.ts`
- `src/Services/PaymentOrchestrator/src/ExitPass.PaymentOrchestrator.Api/Endpoints/WebPayPaymentIntentEndpoints.cs`
- `src/Services/PaymentOrchestrator/src/ExitPass.PaymentOrchestrator.Application/Abstractions/Integrations/ICentralPmsWebPayClient.cs`
- `src/Services/PaymentOrchestrator/src/ExitPass.PaymentOrchestrator.Infrastructure/Integrations/CentralPmsWebPayClient.cs`
- `src/Services/PaymentOrchestrator/src/ExitPass.PaymentOrchestrator.Application/UseCases/WebPayPaymentIntents/WebPayPaymentIntentHandler.cs`

## 5. Current End-to-End WebPay Flow

Current integration direction remains:

`WebPay UI -> Payment Orchestrator -> Central PMS`.

Code evidence:

- WebPay browser client routes are `"/v1/webpay/parking-session"`, `"/v1/webpay/payment-intents"`, and `"/v1/webpay/payment-attempts/{paymentAttemptId}/receipt-presentation"` in `src/Services/WebPayUi/src/webpay.ts`.
- WebPay session lookup calls `resolveParkingSession()` and payment creation calls `handleCreatePaymentIntent()` in `src/Services/WebPayUi/src/App.tsx`.
- WebPay payment return route is `/webpay/payment-return` in `src/Services/WebPayUi/src/App.tsx`.
- Payment Orchestrator maps `POST /v1/webpay/parking-session`, `POST /v1/webpay/payment-intents`, and `GET /v1/webpay/payment-attempts/{paymentAttemptId:guid}/receipt-presentation` in `src/Services/PaymentOrchestrator/src/ExitPass.PaymentOrchestrator.Api/Endpoints/WebPayPaymentIntentEndpoints.cs`.
- Payment Orchestrator delegates Central PMS calls through `ICentralPmsWebPayClient` and `CentralPmsWebPayClient`.
- WebPay currently has read-only statutory display fields such as `statutoryDiscountStatus` and `statutoryDiscountValidationStatus` in `src/Services/WebPayUi/src/types.ts`; it does not have decision-v2 or application-v1 command client fields yet.
- Current WebPay tests assert that old local statutory validation is absent and that pending operator validation blocks payment (`src/Services/WebPayUi/src/App.test.tsx`).

No direct WebPay browser call to HikCentral was found in the inspected WebPay source. No WebPay browser storage calls to `localStorage`, `sessionStorage`, or IndexedDB were found in `src/Services/WebPayUi/src`.

## 6. Target Statutory-Discount WebPay Flow

Target flow:

1. WebPay resolves the session through Payment Orchestrator and Central PMS.
2. WebPay submits safe entitlement and evidence-reference facts to a new WebPay-facing Payment Orchestrator statutory route.
3. Payment Orchestrator calls Central PMS `POST /v1/statutory-discounts/decisions` as the authenticated `WEBPAY` channel.
4. Central PMS creates or reuses decision-v2 with business identity `statutory-discount-decision:{parkingSessionId}:{entitlementType}`.
5. New service-channel decisions enter `AWAITING_REVIEW` / `NOT_DECIDED`.
6. Operator Console reviews the same canonical decision.
7. WebPay polls durable readback through Payment Orchestrator.
8. After approval, WebPay submits application intent through Payment Orchestrator with equivalent facts and `applyPayableBasis=true`.
9. Central PMS creates or reuses application-v1 with business identity `statutory-discount-payable-basis-application:{statutoryDiscountDecisionCommandId}`.
10. WebPay payment intent remains blocked until durable readback returns `payableBasisReady=true`.
11. WebPay payment intent uses the applied tariff snapshot and approved final payable amount returned by Central PMS.

Documentation evidence:

- `docs/v1.3/central-pms/reviews/ExitPass_Statutory_Discount_Service_Channel_Decision_Authority_Design_Decision_v1.0.md` selects review-mediated decision-v2 and application-v1.
- `docs/v1.3/central-pms/implementation-slices/ExitPass_Central_PMS_Statutory_Discount_Channel_Safe_Readback_Hardening_Implementation_Note_v1.0.md` defines `payableBasisReady` and readiness actions.
- `docs/v1.3/central-pms/reviews/ExitPass_Statutory_Discount_WebPay_APT_Readiness_Reauthorization_Audit_v1.0.md` authorizes WebPay implementation while stating payment initiation is `READY_WITH_CONSTRAINT`.

## 7. Component Impact Matrix

| Component | Current responsibility | Required impact | Expected files | Database impact | Security impact | Test impact | Dependency | Risk |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| WebPay UI | Lookup, display payable basis, create payment intent, return-page receipt display | Add statutory request/readback/application-intent states and block payment until ready | `WebPayUi/src/App.tsx`, `webpay.ts`, `types.ts`, tests | none | no raw IDs/evidence, no local calculations | UI/unit and browser tests | Payment Orchestrator proxy | medium |
| Payment Orchestrator | Browser-facing WebPay API and Central PMS client | Add statutory proxy/readback and authoritative payment gate | endpoint, client, handler contracts/tests | none | keep Central PMS credentials server-side | unit/integration tests | Central PMS statutory API | high |
| Central PMS | Statutory decision/application authority | No implementation change expected; reuse merged routes | existing `StatutoryDiscountDecisionEndpoints.cs` | none | service-channel RBAC already required | focused regressions | canonical DB | low |
| Canonical database | Durable statutory command/review/apply state | No change required | `objects/**`, generated SQL | no change | stores safe linkage, not raw evidence payloads | validation evidence reviewed | none | low |
| POS fiscal handoff | Receives final fiscal facts | No WebPay change should duplicate fiscal authority | fiscal mapper tests | none | POS remains presentation authority | mapper/status regressions | applied tariff facts | low |
| Receipt presentation | WebPay displays POS-owned presentation | No statutory-specific renderer | existing receipt presentation files | none | no local receipt reconstruction | existing WebPay receipt tests | POS Server readback | low |
| Operator Console review | Approves/rejects service-channel decisions | No WebPay slice change; UI availability must remain | existing Operator Console review services | none | reviewer-only facts not from WebPay | review linkage regressions | service-channel review table | low |
| Browser persistence | Currently minimal/in-memory plus URL return refs | Store only non-secret recovery references if needed | WebPay UI | none | no raw evidence/statutory values | refresh/replay tests | proxy state design | medium |
| Authentication/RBAC | Central PMS derives source channel from service identity | Payment Orchestrator must hold WebPay submit/read permissions | configuration/deployment only | none | no browser-to-Central PMS service auth | access-policy tests | service identity | medium |
| Observability | Correlation headers across WebPay and Central PMS | Carry safe correlation only | client/endpoints | none | avoid statutory request-body logging | unit/log-shape tests | existing correlation | low |

## 8. WebPay UI Impact

Required UI states:

- entitlement request for `SENIOR_CITIZEN` and `PWD`
- safe evidence-reference collection only
- pending review
- approved but application not requested
- application processing
- payable basis ready
- rejected
- retryable temporary unavailable
- terminal failure

Reusable UI states:

- existing lookup/loading/error posture in `src/Services/WebPayUi/src/App.tsx`
- existing payable-basis display panel
- existing stale payable-basis refresh state and payment blocking behavior
- existing return-page Sales Invoice presentation retrieval

New UI state needed:

- durable statutory decision/application readback model with `payableBasisReady`, readiness status/action, command IDs, original/applied tariff snapshot IDs, final payable amount, VAT facts, currency, retryability, and safe error code.

The WebPay UI must not use the old local statutory-validation posture. Current tests already assert that `/v1/public/discounts/statutory/validate` is not called and that no "request statutory discount" button appears.

## 9. Payment Orchestrator Impact

Payment Orchestrator is the first implementation dependency.

Required changes:

- Add WebPay-facing statutory decision submit route.
- Add WebPay-facing statutory decision readback route.
- Add application-intent route or explicit action when readback says `SUBMIT_APPLICATION_INTENT`.
- Extend `ICentralPmsWebPayClient` or add a narrow sibling Central PMS statutory client.
- Generate/reuse idempotency keys with the required decision/application identities.
- Preserve `X-Correlation-Id`.
- Block `POST /v1/webpay/payment-intents` if statutory workflow is active and `payableBasisReady=false`.
- Pass the applied tariff snapshot ID and final payable amount into the existing payment-intent path when ready.

Evidence:

- `WebPayPaymentIntentEndpoints.cs` currently maps only parking-session, payment-intent, and receipt-presentation routes.
- `ICentralPmsWebPayClient` currently exposes parking resolution, payment attempt create/reuse/finalize, and receipt presentation.
- `WebPayPaymentIntentHandler.ValidatePayableBasis()` already validates the requested basis against Central PMS parking response and is the correct backend guard extension point.

## 10. Central PMS Impact

No Central PMS implementation change is required before WebPay work starts, based on merged evidence.

Evidence:

- `src/Services/CentralPms/src/ExitPass.CentralPms.Api/Program.cs` maps `app.MapStatutoryDiscountDecisionEndpoints()`.
- `src/Services/CentralPms/src/ExitPass.CentralPms.Api/Endpoints/StatutoryDiscountDecisionEndpoints.cs` maps `POST /v1/statutory-discounts/decisions` and `GET /v1/statutory-discounts/decisions/{statutoryDiscountDecisionCommandId}`.
- `TryResolveAuthenticatedSourceChannel()` derives source channel from authenticated permissions.
- `ValidateChannelFieldMatrix()` rejects service-channel reviewer, device, shift, and decision fields.
- `src/Services/CentralPms/src/ExitPass.CentralPms.Contracts/StatutoryDiscounts/StatutoryDiscountDecisionDtos.cs` exposes durable readback fields including `PayableBasisReady`, `PayableBasisReadinessStatus`, `PayableBasisReadinessAction`, VAT facts, applied snapshot, and application command ID.
- `StatutoryDiscountServiceChannelPostApprovalApplicationIntentApiIntegrationTests.cs` covers WebPay service-channel application intent after approval, cross-channel replay, and one applied tariff snapshot.

## 11. Payment-Gating Impact

Required additional gate:

- decision approved
- application-v1 requested and applied
- `payableBasisReady=true`
- `AppliedTariffSnapshotId` present
- final payable amount present
- currency present

Layering:

- WebPay UI: convenience gate, user state, no payment button while pending.
- Payment Orchestrator: required browser-facing backend gate before payment intent creation.
- Central PMS: authoritative payment attempt enforcement; existing tests prove applied snapshot use and stale original snapshot rejection.

Evidence:

- `docs/v1.3/central-pms/implementation-slices/ExitPass_Central_PMS_Statutory_Discount_Channel_Safe_Readback_Hardening_Implementation_Note_v1.0.md` says payment initiation must wait for `payableBasisReady=true`.
- `src/Services/CentralPms/tests/ExitPass.CentralPms.IntegrationTests/Api/VendorParkingResolutionApiIntegrationTests.cs` contains tests for applied statutory payable basis, payment using effective applied tariff snapshot, and rejecting stale original snapshot.

## 12. Fiscal and Receipt Impact

No WebPay-local fiscal rendering is required or allowed.

Evidence:

- Central PMS fiscal mapper tests (`PosServerFiscalDocumentRequestMapperTests.cs`) include statutory discount metadata such as applied tariff snapshot reference.
- Operator Console statutory E2E tests assert payment/fiscal references use the applied snapshot.
- WebPay already retrieves the POS-owned Sales Invoice presentation through `GET /v1/webpay/payment-attempts/{paymentAttemptId}/receipt-presentation`.
- `WebPayReceiptPresentationService` and `WebPayReceiptPresentationResponse` preserve POS Server ownership.

Implementation impact:

- WebPay must keep the current authoritative Sales Invoice presentation path.
- WebPay must not generate a statutory receipt from payment fields.
- Payment Orchestrator/WebPay should expose fiscal/presentation state only after Central PMS records fiscal state.

## 13. Security and Privacy Impact

Prohibited in WebPay browser and Payment Orchestrator browser-facing DTOs:

- raw ID images
- raw evidence payloads
- Base64 evidence
- full statutory ID numbers
- reviewer identity
- reviewer notes
- Operator Console device or shift identity
- Central PMS service credentials
- authorization headers
- database details
- calculated discount, VAT, or final payable amount
- caller-selected applied tariff snapshot or application status

Code evidence:

- Service-channel source is server-derived in Central PMS, not browser authority.
- Service-channel field matrix rejects reviewer/device/shift fields.
- WebPay source currently has no `console.*`, `localStorage`, `sessionStorage`, or IndexedDB usage in `src/Services/WebPayUi/src`.

Risk:

- Payment Orchestrator must avoid general-purpose logging of statutory request bodies. Add focused tests/assertions for safe problem mapping and no raw downstream body exposure.

## 14. Idempotency, Replay, and Concurrency Impact

Decision identity:

- Business identity: `statutory-discount-decision:{parkingSessionId}:{entitlementType}`.
- Semantic source: `statutory-discount-decision:sha256:v2`.
- Source channel is attribution only.

Application identity:

- Business identity: `statutory-discount-payable-basis-application:{statutoryDiscountDecisionCommandId}`.
- Semantic source: `statutory-discount-payable-basis-application:sha256:v1`.

Payment intent:

- Continue current WebPay payment idempotency in `WebPayPaymentIntentHandler`.

Browser refresh/restart:

- Required durable recovery should use command IDs and readback. Browser storage, if introduced, must be non-authoritative and limited to safe command IDs/request references. It must not store full IDs, evidence payloads, statutory amounts as authority, or service credentials.

Concurrency:

- Existing Central PMS tests prove cross-channel replay converges on one canonical decision/application and one applied tariff snapshot.
- Payment Orchestrator must preserve original idempotency key reuse on temporary unavailable recovery action `WAIT_THEN_RETRY_ORIGINAL_IDEMPOTENCY_KEY`.

## 15. Canonical Database Object Inventory

### discounts.statutory_discount_decision_commands

- Canonical source: `objects/schemas/discounts/tables/discounts.statutory_discount_decision_commands.sql`.
- Generated SQL: `build/generated/exitpass-full-object.generated.sql` line 22956.
- Primary key: `statutory_discount_decision_command_id`.
- Business identity fields: `parking_session_id`, `entitlement_type`, `business_identity`.
- Idempotency fields: `idempotency_scope`, `idempotency_key`.
- Semantic fields: `semantic_request_hash`, `semantic_hash_source_version`.
- Status fields: `decision_status`, `command_status`, `decision_result_status`, `result_classification`.
- Retry/recovery: `retryable`, `recovery_classification`, `error_code`.
- Linkage: `statutory_discount_validation_id`, `payable_basis_application_id`, `original_tariff_snapshot_id`, `applied_tariff_snapshot_id`.
- Amount/VAT/currency: `gross_amount_minor_units`, `vat_exclusive_amount_minor_units`, `vat_amount_minor_units`, `statutory_discount_amount_minor_units`, `net_payable_amount_minor_units`, `currency_code`.
- Correlation/timestamps: `original_correlation_id`, `created_at`, `decided_at`, `applied_at`, `completed_at`, `failed_at`, `updated_at`.
- Constraints: source channel limited to `OPERATOR_CONSOLE`, `WEBPAY`, `ASSISTED_PAYMENT_TERMINAL`; entitlement limited to `SENIOR_CITIZEN`, `PWD`; command status includes `AWAITING_REVIEW`; decision result includes `NOT_DECIDED`.
- Unique indexes: `ux_statutory_discount_decision_commands__business_identity`, `ux_statutory_discount_decision_commands__business_identity_text`, `ux_statutory_discount_decision_commands__idempotency`, `ux_statutory_discount_decision_commands__request_reference`.

### discounts.statutory_discount_payable_basis_application_commands

- Canonical source: `objects/schemas/discounts/tables/discounts.statutory_discount_payable_basis_application_commands.sql`.
- Generated SQL: `build/generated/exitpass-full-object.generated.sql` line 23094.
- Primary key: `statutory_discount_payable_basis_application_command_id`.
- Business identity: `business_identity`.
- Idempotency fields: `idempotency_scope`, `idempotency_key`.
- Semantic fields: `semantic_request_hash`, `semantic_hash_source_version`.
- Status fields: `command_status`, `result_classification`.
- Retry/recovery: `retryable`, `recovery_classification`, `safe_error_code`.
- Linkage: `statutory_discount_decision_command_id`, `parking_session_id`, `site_id`, `statutory_discount_validation_id`, `statutory_discount_payable_basis_application_id`, original/target/applied tariff snapshot IDs.
- Amount/VAT/currency: approved discount, VAT-exclusive, VAT, final payable amount, and `currency_code`.
- Unique indexes: `ux_stat_discount_pba_commands__business_identity`, `ux_stat_discount_pba_commands__decision_command`, `ux_stat_discount_pba_commands__idempotency`, `ux_stat_discount_pba_commands__request_reference`.

### operator_console.statutory_discount_service_channel_reviews

- Canonical source: `objects/schemas/operator_console/tables/operator_console.statutory_discount_service_channel_reviews.sql`.
- Generated SQL: `build/generated/exitpass-full-object.generated.sql` line 24073.
- Primary key: `statutory_discount_decision_command_id`.
- Linkage: decision command, parking session, site, site group, original tariff snapshot, validation.
- Safe submitted facts: ticket, plate, entitlement type, document type, issuing authority, expiry date, masked ID reference, evidence references, requester attestation, attestation notes.
- Reviewer fields: reviewer user/device/shift/access evaluation, reviewer decision, decision reason, review timestamps.
- Status constraint: `PENDING_REVIEW`, `APPROVED`, `REJECTED`, `REVIEW_FACTS_UNAVAILABLE`.
- This table is a review read model/linkage row, not the authoritative statutory decision.

### discounts.statutory_discount_validations

- Canonical source: `objects/schemas/discounts/tables/discounts.statutory_discount_validations.sql`.
- Generated SQL: `build/generated/exitpass-full-object.generated.sql` line 5663.
- Primary key: `statutory_discount_validation_id`.
- Linkage: parking session, tariff snapshot, policy references, actor/service identities.
- Status enum: `REQUESTED`, `PENDING_OPERATOR_REVIEW`, `APPROVED`, `REJECTED`, `FAILED`, `EXPIRED`, `CANCELLED`.
- Contains safe evidence metadata, masked ID reference, attestation fields, amounts, currency, correlation, and timestamps.

### discounts.statutory_discount_payable_basis_applications

- Canonical source: `objects/schemas/discounts/tables/discounts.statutory_discount_payable_basis_applications.sql`.
- Generated SQL: present through object source and generated baseline; the table stores legacy payable-basis application rows linked from application-v1 commands.
- Primary key: `statutory_discount_payable_basis_application_id`.
- Linkage: validation, parking session, original tariff snapshot, applied tariff snapshot.
- Amount/VAT/currency: gross, VAT, VAT-exclusive, statutory discount, final payable, currency.
- Constraints enforce non-negative amounts, gross components, final not greater than gross, discount not greater than VAT-exclusive basis, currency format, applied fields, distinct snapshots, and positive row version.
- Indexes include status, parking session, original/applied tariff snapshot, validation, correlation, idempotency, and active-session uniqueness.

### core.tariff_snapshots

- Canonical source: `objects/schemas/core/tables/core.tariff_snapshots.sql`.
- Generated SQL: `build/generated/exitpass-full-object.generated.sql` line 3121.
- Primary key: `tariff_snapshot_id`.
- Linkage: parking session, vendor system, statutory validation, coupon application, superseded snapshot.
- Amount fields: gross, statutory discount, coupon discount, net, currency.
- Status/timestamps: snapshot status, calculated/expires/consumed timestamps, correlation, row version.
- Applied statutory support: `objects/schemas/core/indexes/core.ux_tariff_snapshots__statutory_discount_validation_applied.sql`; generated SQL line 23378.

## 16. Application-to-Database Alignment Matrix

| Application expectation | Canonical object/column | Alignment | Evidence | Required action |
| --- | --- | --- | --- | --- |
| WebPay/APT source channels supported | `source_channel` checks on decision/application commands | aligned | generated SQL constraints include `WEBPAY` and `ASSISTED_PAYMENT_TERMINAL` | none |
| Decision-v2 pending review | `command_status`, `decision_result_status` | aligned | `AWAITING_REVIEW`, `NOT_DECIDED` constraints | none |
| Decision identity excludes source channel | `ux_statutory_discount_decision_commands__business_identity` on session + entitlement | aligned | index source file and design decision doc | none |
| Application-v1 one per decision | `ux_stat_discount_pba_commands__decision_command` | aligned | generated SQL line 23162 | none |
| Application applied status | `command_status` includes `APPLIED` | aligned | generated SQL line 23144 | none |
| Retry original key posture | `recovery_classification` includes `WAIT_THEN_RETRY_ORIGINAL_IDEMPOTENCY_KEY` | aligned | decision/application command constraints | none |
| Review queue durability | `operator_console.statutory_discount_service_channel_reviews` | aligned | source table keyed by decision command | none |
| Applied tariff snapshot | `core.tariff_snapshots` and applied snapshot FK/indexes | aligned | tariff snapshot table and applied validation unique index | none |
| Fiscal handoff can see applied basis | Central PMS applied tariff snapshot resolution | aligned | `VendorParkingResolutionApiIntegrationTests.cs`, `PosServerFiscalDocumentRequestMapperTests.cs` | none |
| WebPay payment gate | Payment Orchestrator handler | partially aligned | current `ValidatePayableBasis()` has amount/snapshot checks, lacks statutory readiness readback input | implement application prerequisite |

## 17. Database Change Assessment

| Change state | Required? | Reason | Canonical file area | Migration impact | Validation impact | Blocks implementation? |
| --- | --- | --- | --- | --- | --- | --- |
| NO_DATABASE_CHANGE_REQUIRED | yes | Current canonical objects support decision-v2, review linkage, application-v1, applied snapshots, VAT, idempotency, and recovery | existing `objects/**` and generated SQL | none | static evidence plus prior promotion proof | no |
| APPLICATION_READ_PATH_ONLY | yes | WebPay needs Payment Orchestrator/Central PMS readback integration only | application repos | none | app tests | no |
| ADDITIVE_CANONICAL_COLUMN_REQUIRED | no | No missing field was found for required readback | none | none | none | no |
| ADDITIVE_CANONICAL_INDEX_REQUIRED | no | Business identity/idempotency/readback indexes exist | decision/application/review indexes | none | none | no |
| ADDITIVE_CANONICAL_CONSTRAINT_REQUIRED | no | Required source/status/semantic/recovery constraints exist | decision/application/review constraints | none | none | no |
| CANONICAL_MIGRATION_REQUIRED | no | No schema blocker found | none | none | none | no |
| VALIDATION_ONLY | yes | Runtime rebuild was not repeated locally | validation scripts and promotion report | none | repeat disposable rebuild before merge if environment available | no |
| DOCUMENTATION_ONLY | no | This report is the only audit artifact | this file | none | git diff/status | no |

## 18. Canonical Build, Upgrade, and Validation Evidence

Reviewed evidence:

- `D:\SourceCodes\exitpassdb_v1.2\docs\ExitPass_Statutory_Discount_Staged_Service_Channel_Canonical_DB_Promotion_Result_v1.0.md`
- `D:\SourceCodes\exitpassdb_v1.2\build\generated\exitpass-full-object.generated.sql`
- `D:\SourceCodes\exitpassdb_v1.2\scripts\validation\Validate-V13CentralPmsAlignment.sql`
- `D:\SourceCodes\exitpassdb_v1.2\scripts\validation\Invoke-DbObjectSourceCiCheck.ps1`

The promotion result records:

- clean canonical rebuild from generated SQL passed Central PMS alignment validation and promoted-object checks
- pre-promotion canonical upgrade passed
- app-local-patched environment upgrade passed
- focused Central PMS statutory, payment, TerminalCash, fiscal mapper, and fiscal semantic-hash tests passed against a disposable PostgreSQL 16.14 database

Runtime rebuild in this audit:

- Not repeated. `psql` was not available locally and Docker API access failed with permission denial.
- No repository files in the canonical DB repo were changed.
- No local environment database such as `exitpass_v12_dev` was used.

## 19. Testing Impact

Minimum implementation tests:

- Central PMS: rerun `StatutoryDiscountDecisionApiAccessPolicyIntegrationTests`, `StatutoryDiscountServiceChannelPostApprovalApplicationIntentApiIntegrationTests`, `VendorParkingResolutionApiIntegrationTests`, and affected fiscal mapper tests.
- Payment Orchestrator unit: extend `CentralPmsWebPayClientTests` for statutory decision submit/readback/application intent and headers.
- Payment Orchestrator application: extend `WebPayPaymentIntentHandlerTests` for `payableBasisReady=false`, applied snapshot required, final amount required, and retryable/terminal readiness states.
- Payment Orchestrator integration: extend `WebPayPaymentIntentEndpointIntegrationTests` for browser-safe statutory routes and payment gate.
- WebPay UI: extend `App.test.tsx` and `webpay.test.ts` for entitlement request, pending review, approval, application intent, applied ready, retryable failure, terminal failure, no local discount/VAT calculation, and payment disabled until ready.
- WebPay browser: add controlled flow coverage for pending review, approved application, applied payment, refresh recovery, and no duplicate payment intent.
- Canonical DB: rerun existing DB CI/disposable apply when Docker or PostgreSQL access is available; no new DB object tests expected.

## 20. Deployment and Configuration Impact

Expected deployment/configuration impact:

- Payment Orchestrator service identity must be authorized to submit/read WebPay statutory decisions through Central PMS.
- No browser-to-Central PMS service authentication should be added.
- No new database migration ordering is required.
- No POS Server configuration change is required.
- No new WebPay feature flag is required by evidence, but rollout can still be operationally controlled outside this implementation.
- Preserve existing correlation and idempotency configuration.

## 21. Risks and Findings

### Critical

None.

### High

Finding: Payment intent can become unsafe if statutory readiness is only enforced in the browser.

- Impact: WebPay could submit payment against original or not-yet-applied tariff basis.
- Evidence: `docs/v1.3/central-pms/reviews/ExitPass_Statutory_Discount_WebPay_APT_Readiness_Reauthorization_Audit_v1.0.md` marks payment readiness `READY_WITH_CONSTRAINT`; `WebPayPaymentIntentHandler.ValidatePayableBasis()` is the current Payment Orchestrator guard but does not yet consume statutory decision readback.
- Affected components: WebPay UI, Payment Orchestrator, Central PMS payment attempt.
- Blocks WebPay implementation? No, but it dictates the first implementation slice.
- Smallest correction: add Payment Orchestrator statutory proxy/readback and backend payment gate before UI payment enablement.

### Medium

Finding: WebPay currently lacks durable statutory decision/application recovery state.

- Impact: refresh or simultaneous tabs could repeat decision/application commands incorrectly or lose the original idempotency posture.
- Evidence: WebPay source currently has no decision/application command fields in `types.ts` and no browser storage convention in `src/Services/WebPayUi/src`.
- Affected components: WebPay UI, Payment Orchestrator.
- Blocks WebPay implementation? No.
- Smallest correction: expose safe command IDs/readiness through Payment Orchestrator and store only non-authoritative safe recovery references if browser persistence is required.

Finding: Runtime canonical rebuild was not repeated in this audit.

- Impact: database alignment relies on committed generated SQL and prior promotion proof instead of fresh local apply.
- Evidence: `psql` was unavailable; Docker API access was denied; canonical promotion report records prior clean rebuild and upgrade validation.
- Affected components: canonical DB validation.
- Blocks WebPay implementation? No.
- Smallest correction: rerun `scripts/validation/Invoke-DbObjectSourceCiCheck.ps1 -RunDbApply -ValidationDatabase <unique_disposable_name>` in an environment with Docker or PostgreSQL access before merge if current validation policy requires fresh DB apply evidence.

### Low

Finding: WebPay UI already has legacy statutory display text and pending-operator validation behavior that will need replacement.

- Impact: user flow may be confusing if old status text remains after review-mediated workflow is added.
- Evidence: `src/Services/WebPayUi/src/App.tsx` renders `Statutory discount`; `src/Services/WebPayUi/src/App.test.tsx` asserts pending operator validation text.
- Affected components: WebPay UI.
- Blocks WebPay implementation? No.
- Smallest correction: replace old read-only/pending status handling as part of WebPay statutory state model.

### Informational

- POS Server changes are not required for WebPay statutory-discount integration based on current Central PMS fiscal mapper and receipt-presentation evidence.
- APT integration is authorized separately, but APT cash acceptance and UAT are not authorized.
- Retired database repository `D:\SourceCodes\ExitPass_DBv1.2` was not used.

## 22. Exact Recommended Implementation Sequence

1. Task name: Add Payment Orchestrator WebPay statutory-discount proxy and payment-readiness gate
   - Owning persona: Codex G
   - Repository: `D:\SourceCodes\ExitPass`
   - Proposed branch: `feature/webpay-statutory-discount-payment-orchestrator-proxy`
   - Exact concern: expose browser-safe decision submit/readback/application-intent routes and block payment intent when statutory payable basis is not ready
   - Expected file areas: Payment Orchestrator endpoints, contracts, Central PMS client, handler tests
   - Off-limits areas: POS Server, APT, canonical DB, fiscal rendering
   - Required validation: Payment Orchestrator unit/integration tests, focused Central PMS statutory regressions
   - Dependency: current `origin/dev`

2. Task name: Add WebPay statutory-discount state client and pending-review UI
   - Owning persona: Codex G
   - Repository: `D:\SourceCodes\ExitPass`
   - Proposed branch: `feature/webpay-statutory-discount-pending-review-ui`
   - Exact concern: collect safe entitlement/evidence-reference facts and display pending/rejected/readback states
   - Expected file areas: WebPay `App.tsx`, `webpay.ts`, `types.ts`, tests
   - Off-limits areas: Central PMS decision rules, POS Server, APT
   - Required validation: WebPay typecheck/unit/build
   - Dependency: task 1

3. Task name: Add WebPay post-approval application-intent flow
   - Owning persona: Codex G
   - Repository: `D:\SourceCodes\ExitPass`
   - Proposed branch: `feature/webpay-statutory-discount-application-intent-ui`
   - Exact concern: submit application intent after approved readback and handle processing/retry/terminal states
   - Expected file areas: WebPay UI/client/tests, Payment Orchestrator tests if needed
   - Off-limits areas: DB, POS Server, APT
   - Required validation: WebPay and Payment Orchestrator tests
   - Dependency: tasks 1 and 2

4. Task name: Wire WebPay payment initiation to applied payable basis
   - Owning persona: Codex G
   - Repository: `D:\SourceCodes\ExitPass`
   - Proposed branch: `feature/webpay-statutory-discount-applied-payment-gate`
   - Exact concern: initiate payment only after `payableBasisReady=true` using applied snapshot/final amount/currency
   - Expected file areas: WebPay payment state, Payment Orchestrator handler tests
   - Off-limits areas: payment provider internals unless directly needed
   - Required validation: WebPay unit/browser tests, Payment Orchestrator payment-intent tests, Central PMS applied tariff tests
   - Dependency: tasks 1-3

5. Task name: Add WebPay statutory-discount browser recovery tests
   - Owning persona: Codex G
   - Repository: `D:\SourceCodes\ExitPass`
   - Proposed branch: `feature/webpay-statutory-discount-browser-proof`
   - Exact concern: controlled browser proof for refresh, replay, pending review, application processing, applied payment, and no duplicate payment
   - Expected file areas: WebPay browser/e2e tests
   - Off-limits areas: APT, POS Server
   - Required validation: browser smoke/e2e plus unit/build
   - Dependency: tasks 1-4

6. Task name: Run canonical DB disposable validation
   - Owning persona: Codex G
   - Repository: `D:\SourceCodes\exitpassdb_v1.2`
   - Proposed branch: none, validation only
   - Exact concern: rerun existing DB object-source CI check against a uniquely named disposable database
   - Expected file areas: none
   - Off-limits areas: `exitpass_v12_dev`, retired DB repo, migrations
   - Required validation: `Invoke-DbObjectSourceCiCheck.ps1 -RunDbApply`
   - Dependency: Docker/PostgreSQL access

7. Task name: Prepare controlled WebPay UAT checklist
   - Owning persona: Codex G
   - Repository: `D:\SourceCodes\ExitPass`
   - Proposed branch: docs only after implementation
   - Exact concern: document UAT steps after implementation passes automation
   - Expected file areas: targeted WebPay UAT note only
   - Off-limits areas: production rollout, APT cash acceptance
   - Required validation: no runtime validation
   - Dependency: implementation complete; UAT authorization still separate

## 23. First Authorized Implementation Task

Task name: **Add Payment Orchestrator WebPay statutory-discount proxy and payment-readiness gate**

- Repository: `D:\SourceCodes\ExitPass`
- Base branch: `dev`
- Proposed branch: `feature/webpay-statutory-discount-payment-orchestrator-proxy`
- Scope: add WebPay-facing Payment Orchestrator routes/contracts/client calls for Central PMS statutory decision submit, readback, and application intent; add backend payment-intent gate based on authoritative Central PMS readiness; preserve current WebPay route direction.
- Non-goals: WebPay UI styling, APT integration, APT cash acceptance, POS Server changes, DB changes, fiscal rendering, UAT, production rollout.
- Completion criteria: Payment Orchestrator tests prove source-channel service identity, decision/readback/application calls, idempotency posture, payment blocked until ready, payment allowed only with applied snapshot/final amount/currency, safe errors, and no direct browser Central PMS credential exposure.

## 24. Deferred Work

- APT integration: authorized but separate from WebPay implementation.
- APT cash acceptance: not authorized.
- Controlled WebPay UAT: not authorized yet.
- Controlled APT UAT: not authorized yet.
- Production rollout: not authorized yet.
- Local ordinance support: deferred; current scope supports only `SENIOR_CITIZEN` and `PWD`.
- Management Platform policy configuration: deferred.
- Privacy retention policy: deferred; do not add raw evidence storage in WebPay.
- Bruno environment execution: deferred.

## 25. Final Authorization Statement

`WEBPAY_IMPLEMENTATION_IMPACT_ANALYSIS_COMPLETE`

WebPay integration implementation remains authorized.

APT integration implementation remains authorized.

APT cash acceptance remains not authorized.

WebPay controlled UAT remains not authorized yet.

APT controlled UAT remains not authorized yet.

Production rollout remains not authorized yet.
