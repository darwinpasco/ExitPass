# ExitPass Central PMS APT Session Resolution and Payable Basis Audit

## 1. Audit decision

Decision: READY_WITH_BOUNDED_CENTRAL_PMS_GAP.

Desktop work must not proceed to CASH_RECEIVED enablement yet. Central PMS already has a provider-neutral vendor parking resolution and live tariff path, but the current contracts do not return one APT-facing, authorization-scoped, pre-cash readiness and revalidation posture that covers payable basis, terminal-cash availability, fiscal readiness, and safe state separation.

Most important reason: `src/Services/CentralPms/src/ExitPass.CentralPms.Contracts/TerminalCashPayments/TerminalCashPaymentDtos.cs` requires the APT caller to submit `ParkingSessionId`, `TariffSnapshotId`, site context, POS Server reference, currency, and amount, while the available resolution contracts do not explicitly return "ready for cash acceptance" or "revalidation passed immediately before CASH_RECEIVED".

## 2. Scope and repository boundary

Audit repository: `D:/SourceCodes/ExitPass-APT`.

APT desktop repository: `D:/SourceCodes/ExitPass-AssistedPaymentTerminal`. It owns cashier UI, local SQLite, CASH_RECEIVED, custody, restart recovery, and desktop tests. It was not modified or inspected for implementation.

Primary mainline repository: `D:/SourceCodes/ExitPass`. It was not modified or used.

Central PMS-only HikCentral communication rule: the APT desktop must not communicate directly with HikCentral. The inspected Central PMS path contains HikCentral calls behind `IVendorPmsParkingResolutionClient` and the Vendor PMS adapter, with concrete HikCentral integration in `src/Services/VendorPmsAdapter/src/ExitPass.VendorPmsAdapter.Infrastructure/HikCentral/HikCentralParkingClient.cs`.

## 3. Canonical WebPay reference

Implemented WebPay route: `POST /v1/webpay/parking-session` in `src/Services/PaymentOrchestrator/src/ExitPass.PaymentOrchestrator.Api/Endpoints/WebPayPaymentIntentEndpoints.cs`.

Request DTO: `src/Services/PaymentOrchestrator/src/ExitPass.PaymentOrchestrator.Contracts/WebPay/WebPayParkingSessionResolveRequest.cs`. It accepts site group, site, vendor system, plate, ticket, and correlation values.

Response DTO: `src/Services/PaymentOrchestrator/src/ExitPass.PaymentOrchestrator.Contracts/WebPay/WebPayParkingSessionResolveResponse.cs`. It returns parking session identity, tariff snapshot identity, site context, amount, currency, ticket, plate, entry time, fee calculation time, tariff expiry, parking/payment status, and correlation.

Service path: `WebPayPaymentIntentHandler.ResolveAsync` and `HandleAsync` in `src/Services/PaymentOrchestrator/src/ExitPass.PaymentOrchestrator.Application/UseCases/WebPayPaymentIntents/WebPayPaymentIntentHandler.cs` call `ICentralPmsWebPayClient.ResolveVendorParkingAsync`, then map Central PMS response in `BuildResolveResponse`.

Central PMS client path: `src/Services/PaymentOrchestrator/src/ExitPass.PaymentOrchestrator.Infrastructure/Integrations/CentralPmsWebPayClient.cs` calls Central PMS `v1/vendor-parking/resolve`. This proves WebPay does not calculate tariff locally and does not call HikCentral directly.

Revalidation behavior: WebPay payment-intent creation calls Central PMS resolution again and `ValidatePayableBasis` rejects stale or changed displayed amounts with `PAYABLE_BASIS_LOCKED`. UI tests in `src/Services/WebPayUi/src/App.test.tsx` prove the displayed `tariffSnapshotId` and `expectedAmountMinorUnits` are posted and that `PAYABLE_BASIS_REFRESH_REQUIRED` leads to another parking-session resolve. This is fixture-tested and unit/integration-tested, not live multi-service UAT.

Browser fixture proof: `src/Services/WebPayUi/e2e/webpay-authoritative-sales-invoice.spec.ts` proves authoritative receipt presentation behavior for WebPay receipt availability. It is contract-level fixture proof and is not evidence of live payment-provider or full multi-service UAT.

## 4. Central PMS session-resolution implementation

Central PMS shared route: `POST /v1/vendor-parking/resolve` in `src/Services/CentralPms/src/ExitPass.CentralPms.Api/Controllers/VendorParkingResolutionController.cs`.

Request DTO: `src/Services/CentralPms/src/ExitPass.CentralPms.Contracts/Public/VendorParking/ResolveVendorParkingRequest.cs`. Required fields are `SiteGroupId`, `SiteId`, `VendorSystemId`, `CorrelationId`, and one of `PlateNumber` or `TicketReference`.

Response DTO: `src/Services/CentralPms/src/ExitPass.CentralPms.Contracts/Public/VendorParking/ResolveVendorParkingResponse.cs`. Returned fields include `ParkingSessionId`, `TariffSnapshotId`, site and site group context, ticket, plate, entry time, `CurrentFeeCalculationTime`, `NetPayableMinorUnits`, `Currency`, `TariffExpiresAt`, `FeeValidUntil`, `ParkingStatus`, `PaymentStatus`, vendor system, statutory discount payable-basis fields, and `CorrelationId`.

Validation: `src/Services/CentralPms/src/ExitPass.CentralPms.Api/Validation/ResolveVendorParkingRequestValidator.cs` requires site group, site, vendor system, correlation, and plate or ticket. It validates GUID shape and rejects control characters.

Handler: `src/Services/CentralPms/src/ExitPass.CentralPms.Application/VendorParking/ResolveVendorParkingHandler.cs` calls `IVendorPmsParkingResolutionClient.ResolveSessionAsync`; if the vendor session lacks a tariff quote, it calls `ResolveTariffAsync`. It persists a Central PMS parking session and tariff snapshot through `IVendorParkingResolutionPersistence`.

Persistence: `src/Services/CentralPms/src/ExitPass.CentralPms.Infrastructure/VendorParking/VendorParkingResolutionPersistence.cs` writes `core.parking_sessions` and `core.tariff_snapshots`, retires prior active tariff snapshots, and loads payment status summaries. This is implemented and integration-tested.

Vendor adapter interface: `src/Services/CentralPms/src/ExitPass.CentralPms.Application/VendorParking/IVendorPmsParkingResolutionClient.cs`.

Concrete adapter registration: `src/Services/CentralPms/src/ExitPass.CentralPms.Infrastructure/VendorParking/CentralPmsVendorPmsAdapterRegistration.cs` selects mock or HikCentral provider configuration and registers `HikCentralVendorPmsParkingResolutionClient`.

HikCentral adapter: `src/Services/VendorPmsAdapter/src/ExitPass.VendorPmsAdapter.Infrastructure/HikCentral/HikCentralParkingClient.cs` calls `/artemis/api/vehicle/v1/parkingfee/calculate` for lookup/tariff and `/artemis/api/vehicle/v1/parkingfee/confirm` for confirmation when enabled. Timeout and 5xx responses map to retryable `VENDOR_PMS_UNAVAILABLE`; ambiguous results map to `VENDOR_SESSION_AMBIGUOUS`.

Ops summary route: `POST /v1/ops/ticket-session-summary` in `src/Services/CentralPms/src/ExitPass.CentralPms.Api/Endpoints/TicketSessionSummaryEndpoints.cs` is read-only and explicitly does not confirm fees, mark paid, issue ExitAuthorization, or open gates. It supports ticket/card summary, not the full APT pre-cash contract.

Tests: `src/Services/CentralPms/tests/ExitPass.CentralPms.IntegrationTests/Api/VendorParkingResolutionApiIntegrationTests.cs` covers plate, ticket, not found, ambiguous, vendor unavailable, malformed vendor response, vendor rejected, tariff rejection, idempotent repeated resolution, and no payment/exit truth mutation. `src/Services/CentralPms/tests/ExitPass.CentralPms.ContractTests/Public/VendorParkingResolutionContractTests.cs` covers the contract shape.

## 5. Public-contract assessment

| Contract | Classification | Evidence | APT conclusion |
|---|---|---|---|
| `POST /v1/vendor-parking/resolve` | SHARED_AND_CHANNEL_NEUTRAL | `VendorParkingResolutionController.cs`, `ResolveVendorParkingRequest.cs`, `ResolveVendorParkingResponse.cs` | Usable as an authority source but incomplete for APT CASH_RECEIVED enablement because it lacks explicit pre-cash readiness, fiscal path readiness, terminal-cash availability, and revalidation outcome. |
| `POST /v1/webpay/parking-session` | WEBPAY_SPECIFIC_NOT_SAFE_FOR_APT | `WebPayPaymentIntentEndpoints.cs`, WebPay DTOs | WebPay-specific route in Payment Orchestrator; APT should not call it. |
| `POST /v1/ops/ticket-session-summary` | APT_SPECIFIC_BUT_INCOMPLETE is not proven; operations-specific incomplete | `TicketSessionSummaryEndpoints.cs`, `TicketSessionSummaryDtos.cs` | Read-only summary lacks tariff snapshot ID, tariff expiry, plate lookup contract, payment eligibility, fiscal readiness, and pre-cash readiness. |
| `POST /v1/terminal-cash-payments` | Not a session-resolution contract | `TerminalCashPaymentDtos.cs`, `TerminalCashPaymentEndpoints.cs` | Submission guard only; APT must already know durable payable-basis identity before this command. |

Authorization policy finding: Central PMS has `CentralPmsRbacMiddleware` and `InternalMtlsMiddleware`, but `VendorParkingResolutionController`, `TicketSessionSummaryEndpoints`, and `TerminalCashPaymentEndpoints` do not attach `ReconciliationPolicyMetadata` or `InternalServiceEndpointMetadata` in the inspected files. Route-level APT authorization for a future session-resolution facade is therefore not proven.

## 6. Reference-type matrix

| Reference type | Implemented | Route | Evidence | Error posture | APT implication |
|---|---|---|---|---|---|
| Parking ticket | Implemented | `POST /v1/vendor-parking/resolve` | `ResolveVendorParkingRequest.TicketReference`; `VendorParkingResolutionApiIntegrationTests.cs` | 400 invalid, 404 not found, 409 ambiguous/rejected, 502 malformed, 503 retryable unavailable | APT can be served by shared service, but needs APT-specific readiness facade. |
| Plate number | Implemented | `POST /v1/vendor-parking/resolve` | `ResolveVendorParkingRequest.PlateNumber`; `VendorParkingResolutionApiIntegrationTests.cs` | Same as ticket | APT can be served by shared service. |
| Card number | Implemented only in ops summary | `POST /v1/ops/ticket-session-summary` | `TicketSessionSummaryRequest.CardNum` | 400, 404, 409, 502, 503 | Not a complete APT payable-basis contract. |
| Parking-session reference | Not proven as public lookup input | None found | `ResolveVendorParkingRequest` contains plate/ticket only; terminal-cash command requires `ParkingSessionId` after resolution | Not applicable | APT cannot resolve by parking-session reference through a proven public contract. |
| Site-scoped lookup | Implemented | `POST /v1/vendor-parking/resolve` | `SiteId`, `SiteGroupId` validator and handler | 400 invalid site context | APT must supply site context or receive it from terminal profile. |
| Site Group routing | Partial | `POST /v1/vendor-parking/resolve` | `SiteGroupId` is required and persisted | 400 invalid site group | Site group is carried, but per-site vendor adapter selection is not proven beyond configured provider. |
| Ambiguous plate/session | Implemented | `POST /v1/vendor-parking/resolve` | `ResolveVendorParkingOutcome.AmbiguousMatch`; tests | 409 conflict, retryable false | APT can display non-cash-ready ambiguous state. |
| Missing session | Implemented | `POST /v1/vendor-parking/resolve` | `ResolveVendorParkingOutcome.SessionNotFound`; tests | 404, retryable false | APT can display not found. |
| Inactive/closed/already-paid | Partial | `POST /v1/vendor-parking/resolve`, terminal-cash submission | `ParkingStatus`, `PaymentStatus`; `EnsureNoExistingFinalPaymentAsync` | Already-final rejected at terminal-cash submission | APT pre-cash readiness is not explicitly returned. |
| Malformed reference | Implemented for request shape | `POST /v1/vendor-parking/resolve` | `ResolveVendorParkingRequestValidator.cs` | 400 `INVALID_REQUEST` | APT can show validation failure. |
| Cross-site mismatch | Partial | Terminal cash submission guard | `EnsurePayableBasisAsync` checks session/tariff/site relationship | Rejected at payment submission | APT does not receive an explicit pre-cash cross-site readiness result. |

## 7. Payable-basis matrix

| Required field | Available | Authority source | DTO location | Evidence | Gap | Required owner |
|---|---|---|---|---|---|---|
| `parkingSessionId` | Yes | Central PMS persisted session | `ResolveVendorParkingResponse` | `VendorParkingResolutionController.cs` | None | Central PMS |
| Site ID | Yes | Request plus persisted session | `ResolveVendorParkingResponse.SiteId` | `ResolveVendorParkingResponse.cs` | None | Central PMS |
| Site Group ID | Yes | Request plus persisted session | `ResolveVendorParkingResponse.SiteGroupId` | `ResolveVendorParkingResponse.cs` | None | Central PMS |
| Parking location | Partial | Central PMS site summary | `SiteName`, `SiteGroupName` | `VendorParkingResolutionPersistence.cs` | No structured parking location field beyond site names | Central PMS |
| Ticket reference | Yes | Vendor session | `TicketReference` | `ResolveVendorParkingResponse.cs` | None | Central PMS |
| Plate reference | Yes | Vendor session | `PlateNumber` | `ResolveVendorParkingResponse.cs` | None | Central PMS |
| Entry timestamp | Yes | Vendor session | `EntryTime` | `ResolveVendorParkingResponse.cs` | None | Central PMS |
| Calculation timestamp | Yes | Tariff snapshot | `CurrentFeeCalculationTime` | `VendorParkingResolutionController.cs` | None | Central PMS |
| Authoritative payable amount | Yes | Vendor tariff snapshot | `NetPayableMinorUnits` | `ResolveVendorParkingResponse.cs` | None | Central PMS |
| Currency | Yes | Vendor tariff snapshot | `Currency` | `ResolveVendorParkingResponse.cs` | Supported-currency readiness not explicit | Central PMS |
| Tariff calculation ID | Yes | Central PMS tariff snapshot | `TariffSnapshotId` | `ResolveVendorParkingResponse.cs` | None | Central PMS |
| Tariff calculated at | Yes | Central PMS tariff snapshot | `CurrentFeeCalculationTime` | `VendorParkingResolutionController.cs` | None | Central PMS |
| Tariff valid until/expiry | Yes | Central PMS tariff snapshot | `TariffExpiresAt`, `FeeValidUntil` | `ResolveVendorParkingResponse.cs` | None | Central PMS |
| Tariff source | Partial | Vendor system ID | `VendorSystemId` | `ResolveVendorParkingResponse.cs` | No explicit tariff source classification beyond vendor system | Central PMS |
| Calculation status | Partial | Successful response implies resolved; failures use error envelope | `LookupOutcome`, error codes | `VendorParkingResolutionController.cs` | No distinct pending/calculated enum in success DTO | Central PMS |
| Payment eligibility | Partial | PaymentStatus plus terminal-cash submission guards | `PaymentStatus`; `EnsureNoExistingFinalPaymentAsync` | `TerminalCashPaymentRepository.cs` | No explicit `paymentEligibility` or `readyForCashAcceptance` field | Central PMS |
| Safe retryability | Yes on errors | Central PMS error envelope | `ErrorResponse.Retryable` | `VendorParkingResolutionController.cs` | No retry-after guidance proven | Central PMS |
| Safe failure classification | Yes on errors | Central PMS mapping | `ErrorResponse.ErrorCode` | `VendorParkingResolutionController.cs` | APT-specific code taxonomy not complete | Central PMS |
| Correlation ID | Yes | Central PMS correlation | `CorrelationId`, `X-Correlation-Id` | `VendorParkingResolutionController.cs` | None | Central PMS |

## 8. Pre-cash readiness matrix

| Condition | Classification | Evidence | APT implication |
|---|---|---|---|
| Session resolved | EXPLICITLY_RETURNED | `ResolveVendorParkingResponse.ParkingSessionId` | Ready input exists. |
| Session active and payable | DERIVABLE_FROM_SAFE_AUTHORITATIVE_FIELDS | `ParkingStatus`, `PaymentStatus` | APT would still need a rule; explicit readiness is absent. |
| Tariff calculation succeeded | DERIVABLE_FROM_SAFE_AUTHORITATIVE_FIELDS | 200 response plus tariff fields | APT can display success but lacks explicit ready flag. |
| Tariff is current | EXPLICITLY_RETURNED | `TariffExpiresAt`, `FeeValidUntil` | Expiry returned, but authority rule should remain Central PMS-owned. |
| Payable amount is valid | DERIVABLE_FROM_SAFE_AUTHORITATIVE_FIELDS | `NetPayableMinorUnits`, terminal-cash `PAYABLE_BASIS_MISMATCH` guard | No pre-cash eligibility flag. |
| Currency is supported | MISSING | No supported-currency readiness field found in DTO | APT would infer or fail later. |
| No other payment has finalized session | CENTRAL_PMS_ENFORCED_BUT_NOT_RETURNED | `EnsureNoExistingFinalPaymentAsync` throws `PAYMENT_ALREADY_FINAL` | Currently enforced at terminal-cash submission, too late for pre-cash. |
| Terminal-cash submission is enabled | MISSING | Cash rail guard `CASH_PAYMENT_RAIL_NOT_CONFIGURED` only in submission repository | APT cannot know before cash. |
| Central PMS is available | DERIVABLE_FROM_SAFE_AUTHORITATIVE_FIELDS | Successful HTTP response | No explicit readiness dimension. |
| Required fiscal path is ready | MISSING | Fiscal readiness exists in management/fiscal gating areas, not in vendor resolve DTO | APT cannot safely enable cash from current contract. |
| Site POS Server is configured | MISSING | Terminal cash request requires `PosServerId`; profile readiness uses `SitePosServerId` | No APT pre-cash field. |
| Required Sales Invoice configuration is ready | MISSING | `GET /v1/management-platform/sales-invoice-header-profiles/effective-readiness` exists for Management Platform | Not returned in session/payable-basis contract. |
| Transaction not already fiscally completed | MISSING | Fiscal status readback exists for terminal-cash after payment | Not part of pre-cash resolution. |
| Transaction not already authorized for exit under another payment | UNKNOWN | ExitAuthorization issuance/readback exists elsewhere | Not in payable-basis contract. |
| Correlation and audit context are available | EXPLICITLY_RETURNED | `CorrelationId`, `X-Correlation-Id` | Available. |

## 9. Revalidation assessment

Current posture: PARTIAL.

The shared vendor resolution route can be called repeatedly and produces a fresh resolved parking session plus tariff snapshot. WebPay uses this before payment-intent creation and rejects changed displayed amounts with `PAYABLE_BASIS_LOCKED` in `WebPayPaymentIntentHandler.ValidatePayableBasis`.

For APT, there is no dedicated pre-CASH_RECEIVED revalidation endpoint and no APT-facing response that states the existing payable basis is unchanged, the amount changed, the session remains unpaid and active, terminal-cash remains enabled, and fiscal readiness remains acceptable. Terminal-cash submission later validates tariff relationship, staleness, currency, amount, and existing final payment in `TerminalCashPaymentRepository`, but that occurs after the desktop has crossed its local CASH_RECEIVED boundary.

Recommended revalidation owner: Central PMS. It should reuse existing vendor resolution, tariff snapshot, payment status, terminal-cash eligibility, and fiscal-readiness readers, not a desktop-only timestamp comparison.

## 10. State-separation matrix

| State | Classification | Evidence |
|---|---|---|
| Reference not entered | NOT_APPLICABLE | Desktop-owned UI state. |
| Resolution pending | NOT_APPLICABLE | Desktop/UI transport state. |
| Session resolved | ALIGNED | `ResolveVendorParkingResponse.LookupOutcome`. |
| Session not found | ALIGNED | `SessionNotFound` maps 404. |
| Session ambiguous | ALIGNED | `AmbiguousMatch` maps 409. |
| Session inactive | PARTIAL | `ParkingStatus` returned; explicit cash readiness absent. |
| Session closed | PARTIAL | `ParkingStatus` returned; explicit cash readiness absent. |
| Session already paid | PARTIAL | `PaymentStatus` returned; `PAYMENT_ALREADY_FINAL` enforced on submission. |
| Tariff calculation pending | MISSING | No pending tariff state in public DTO. |
| Tariff calculated | ALIGNED | 200 response includes amount, currency, timestamps. |
| Tariff expired | PARTIAL | Expiry returned and submission rejects `STALE_TARIFF`; no explicit resolution status. |
| Tariff recalculation required | PARTIAL | WebPay handles refresh-required locally through error flows; shared APT posture not explicit. |
| Tariff changed | PARTIAL | WebPay payment-intent path detects amount mismatch; APT revalidation output missing. |
| Vendor PMS temporarily unavailable | ALIGNED | 503 retryable error from `VendorParkingResolutionController`. |
| Vendor PMS terminal failure | ALIGNED | 409/502 non-retryable vendor/malformed mappings. |
| Payable basis valid | PARTIAL | Fields exist; explicit readiness absent. |
| Payment unavailable | MISSING | No terminal-cash availability field. |
| Fiscal path unavailable | MISSING | Not in session/payable-basis contract. |
| Fiscal configuration incomplete | MISSING | Profile readiness exists elsewhere, not returned here. |
| Ready for cash acceptance | MISSING | No explicit field or route found. |
| Revalidation pending | NOT_APPLICABLE | Desktop/UI transport state. |
| Revalidation passed | MISSING | No APT revalidation contract. |
| Revalidation failed | MISSING | No APT revalidation contract. |
| Amount changed before cash acceptance | PARTIAL | WebPay detects `PAYABLE_BASIS_LOCKED`; APT route absent. |

## 11. Error and retry matrix

| Condition | HTTP/status evidence | Safe code evidence | Retryable | APT implication |
|---|---|---|---|---|
| Malformed reference | 400 | `INVALID_REQUEST` | false | Show validation error, do not enable cash. |
| Unknown reference | 404 | `SESSION_NOT_FOUND` or mapped not-found code | false | Show not found, do not enable cash. |
| Ambiguous plate | 409 | `VENDOR_SESSION_AMBIGUOUS` | false | Cash blocked until clarified. |
| Inactive session | 200 with `ParkingStatus` or not proven error | Not explicit | Unknown | Needs explicit APT readiness rule. |
| Session already paid | 200 with `PaymentStatus`; terminal cash rejects `PAYMENT_ALREADY_FINAL` | `PAYMENT_ALREADY_FINAL` on submission | false | Needs pre-cash result. |
| Tariff service timeout | 503 from adapter | `VENDOR_PMS_UNAVAILABLE` | true | Retryable lookup. |
| HikCentral unavailable | 503 | `VENDOR_PMS_UNAVAILABLE` | true | Retryable lookup. |
| HikCentral authentication failure | Adapter error | Sanitized vendor adapter code | false unless mapped otherwise | No raw credentials exposed in inspected mapping. |
| Throttling | Not specifically proven | Not specifically proven | Unknown | Gap for APT user-facing retry guidance. |
| Configuration disabled | Confirm path disabled applies to vendor confirm, not resolve | `VENDOR_CONFIRMATION_DISABLED` for confirm path | false | Not part of APT pre-cash resolve. |
| Site routing failure | 400 invalid site fields | `INVALID_REQUEST` | false | Cash blocked. |
| Malformed Vendor PMS response | 502 | `MALFORMED_VENDOR_SESSION` or `MALFORMED_VENDOR_RESPONSE` | false | Cash blocked. |
| Unsupported currency | Terminal cash submission guard only | `PAYABLE_BASIS_MISMATCH` or related guard | false | Needs pre-cash readiness. |
| Fiscal readiness unavailable | Not in resolve contract | None in payable-basis DTO | Unknown | Blocking gap. |
| Central PMS internal failure | 502 fallback mapping for unexpected outcome | Generic safe error | true by default only in WebPay fallback | Needs APT-specific taxonomy. |

Raw downstream exposure posture: `VendorParkingResolutionController` returns `ErrorResponse` with safe message and retryable flag. `HikCentralParkingClient` sanitizes adapter error codes and maps timeouts/5xx to `VENDOR_PMS_UNAVAILABLE`; no HikCentral credentials, signing material, endpoints, or raw stack traces were found in the public response DTOs.

## 12. HikCentral authority boundary

Implemented boundary: `IVendorPmsParkingResolutionClient` in Central PMS defines provider-neutral `ResolveSessionAsync`, `ResolveTariffAsync`, and `ConfirmParkingFeeAsync`.

Concrete HikCentral ownership: `CentralPmsVendorPmsAdapterRegistration.cs` registers `HikCentralVendorPmsParkingResolutionClient`, which delegates to `HikCentralParkingClient`.

HikCentral route evidence: `HikCentralParkingClient.cs` defines `/artemis/api/vehicle/v1/parkingfee/calculate` and `/artemis/api/vehicle/v1/parkingfee/confirm`. Lookup/tariff resolution uses calculate; confirm is separately guarded by configuration.

APT does not need HikCentral credentials, signing, endpoint information, or tariff interpretation when consuming a Central PMS APT-facing facade. Correctly contained Central PMS adapter code is not an APT boundary violation.

## 13. Fiscal-readiness boundary

Fiscal evidence and ExitAuthorization gating exist after payment/fiscal issuance. `FiscalIssuanceExitAuthorizationGateEvaluator.cs` blocks normal ExitAuthorization when payment finality is not verified, fiscal reference is missing, fiscal issuance is pending/requested/failed/unknown/manual-review, or fiscal numbering evidence is incomplete.

Sales Invoice profile readiness exists in Management Platform scope through `GET /v1/management-platform/sales-invoice-header-profiles/effective-readiness` in `ManagementPlatformSalesInvoiceProfileAdministrationEndpoints.cs` and the DTO `ManagementPlatformSalesInvoiceHeaderProfileReadinessDto`.

Gap: neither `ResolveVendorParkingResponse` nor `TicketSessionSummaryResponse` returns POS Server configuration readiness, Sales Invoice profile readiness, fiscal issuance capability, or a pre-cash fiscal readiness classification. Terminal-cash fiscal issuance readback exists after terminal-cash payment, not before CASH_RECEIVED.

## 14. Findings

### Finding APT-PB-001

Severity: Critical.

Condition: No complete APT-facing session-resolution/payable-basis/pre-cash readiness contract is present.

Evidence: `ResolveVendorParkingResponse.cs` returns authoritative payable-basis fields but not `readyForCashAcceptance`, terminal-cash availability, fiscal readiness, or revalidation result. `TerminalCashPaymentRequest` in `TerminalCashPaymentDtos.cs` requires `ParkingSessionId` and `TariffSnapshotId`, proving the APT caller needs those before payment submission.

APT impact: the desktop would have to infer cash enablement from partial fields or wait for terminal-cash submission rejection after local CASH_RECEIVED.

Repository owner: `D:/SourceCodes/ExitPass-APT`.

Bounded correction: add a thin APT-facing Central PMS facade over existing vendor parking resolution, payment status, terminal-cash eligibility, fiscal configuration readiness, and revalidation services.

Blocks desktop implementation: yes.

### Finding APT-PB-002

Severity: High.

Condition: Immediate pre-CASH_RECEIVED revalidation is only partially supported.

Evidence: WebPay re-resolves vendor parking and rejects changed expected amount in `WebPayPaymentIntentHandler.ValidatePayableBasis`, while terminal-cash submission validates `STALE_TARIFF`, `PAYABLE_BASIS_MISMATCH`, and `PAYMENT_ALREADY_FINAL` in `TerminalCashPaymentRepository.cs`.

APT impact: APT cannot receive an explicit "revalidation passed" or "amount changed before cash acceptance" result before accepting cash.

Repository owner: `D:/SourceCodes/ExitPass-APT`.

Bounded correction: include APT revalidation semantics in the same facade or companion endpoint, using Central PMS authority.

Blocks desktop implementation: yes.

### Finding APT-PB-003

Severity: High.

Condition: Fiscal readiness is implemented in separate management/fiscal areas but is not returned in the payable-basis response.

Evidence: `ManagementPlatformSalesInvoiceHeaderProfileReadinessDto` exposes profile readiness; `FiscalIssuanceExitAuthorizationGateEvaluator.cs` evaluates fiscal evidence after payment. `ResolveVendorParkingResponse.cs` has no POS Server readiness, Sales Invoice profile readiness, or fiscal capability fields.

APT impact: the desktop cannot safely enable CASH_RECEIVED only when required fiscal path preconditions are satisfied.

Repository owner: `D:/SourceCodes/ExitPass-APT`.

Bounded correction: include a safe fiscal-readiness dimension in the APT facade, reusing existing Management Platform/profile readiness and Central PMS fiscal policy readers.

Blocks desktop implementation: yes.

### Finding APT-PB-004

Severity: Medium.

Condition: Authorization policy for the future APT session-resolution/readiness route is not established by the inspected shared endpoints.

Evidence: `CentralPmsRbacMiddleware.cs` enforces only endpoints with `ReconciliationPolicyMetadata`; `VendorParkingResolutionController.cs`, `TicketSessionSummaryEndpoints.cs`, and `TerminalCashPaymentEndpoints.cs` do not attach that metadata in the inspected files.

APT impact: a new APT-facing facade must define narrow authorization instead of copying an unprotected or not-proven shared route posture.

Repository owner: `D:/SourceCodes/ExitPass-APT`.

Bounded correction: add or reuse the narrowest existing APT/terminal read policy when implementing the facade.

Blocks desktop implementation: yes, as part of the bounded Central PMS slice.

### Finding APT-PB-005

Severity: Informational.

Condition: HikCentral tariff communication is correctly contained in Central PMS/Vendor PMS adapter code.

Evidence: `CentralPmsVendorPmsAdapterRegistration.cs`, `HikCentralVendorPmsParkingResolutionClient.cs`, and `HikCentralParkingClient.cs` contain adapter wiring and HikCentral calls; WebPay UI code calls WebPay/Central PMS routes, not HikCentral.

APT impact: the desktop should consume Central PMS only.

Repository owner: `D:/SourceCodes/ExitPass-APT`.

Bounded correction: none for this audit.

Blocks desktop implementation: no.

Low findings: none.

## 15. Required next task

Implement exactly one bounded Central PMS task in `D:/SourceCodes/ExitPass-APT`:

Add an APT-facing session-resolution, payable-basis readiness, and immediate pre-CASH_RECEIVED revalidation facade that reuses the existing vendor parking resolution, tariff snapshot persistence, terminal-cash eligibility guards, payment status readers, fiscal/Sales Invoice readiness readers, correlation, safe error taxonomy, and route authorization conventions.

The task must return explicit APT-safe fields for session resolution, live tariff/payable basis, tariff validity, payment eligibility, terminal-cash availability, fiscal readiness, and revalidation outcomes. It must not add desktop behavior, HikCentral client code, tariff calculation in APT, payment/fiscal mutation redesign, ExitAuthorization, or gate behavior.

## 16. Deferred work

Deferred: degraded/offline tariff policy, discounts, cash submission changes, fiscal issuance changes, receipt changes, printing changes, ExitAuthorization, gate integration, and full multi-service UAT.

## 17. Evidence inventory

Source files inspected:

- `src/Services/CentralPms/src/ExitPass.CentralPms.Api/Controllers/VendorParkingResolutionController.cs`
- `src/Services/CentralPms/src/ExitPass.CentralPms.Api/Endpoints/TicketSessionSummaryEndpoints.cs`
- `src/Services/CentralPms/src/ExitPass.CentralPms.Api/Endpoints/TerminalCashPaymentEndpoints.cs`
- `src/Services/CentralPms/src/ExitPass.CentralPms.Api/Endpoints/ManagementPlatformSalesInvoiceProfileAdministrationEndpoints.cs`
- `src/Services/CentralPms/src/ExitPass.CentralPms.Api/Program.cs`
- `src/Services/CentralPms/src/ExitPass.CentralPms.Api/Security/CentralPmsRbacMiddleware.cs`
- `src/Services/CentralPms/src/ExitPass.CentralPms.Api/Security/InternalMtlsMiddleware.cs`
- `src/Services/CentralPms/src/ExitPass.CentralPms.Api/Security/ReconciliationPolicyMetadata.cs`
- `src/Services/CentralPms/src/ExitPass.CentralPms.Contracts/Public/VendorParking/ResolveVendorParkingRequest.cs`
- `src/Services/CentralPms/src/ExitPass.CentralPms.Contracts/Public/VendorParking/ResolveVendorParkingResponse.cs`
- `src/Services/CentralPms/src/ExitPass.CentralPms.Contracts/Operations/TicketSessionSummaryDtos.cs`
- `src/Services/CentralPms/src/ExitPass.CentralPms.Contracts/TerminalCashPayments/TerminalCashPaymentDtos.cs`
- `src/Services/CentralPms/src/ExitPass.CentralPms.Contracts/ManagementPlatform/PosServerSalesInvoiceProfileAdministrationDtos.cs`
- `src/Services/CentralPms/src/ExitPass.CentralPms.Application/VendorParking/ResolveVendorParkingHandler.cs`
- `src/Services/CentralPms/src/ExitPass.CentralPms.Application/VendorParking/IVendorPmsParkingResolutionClient.cs`
- `src/Services/CentralPms/src/ExitPass.CentralPms.Application/Operations/TicketSessionSummaryService.cs`
- `src/Services/CentralPms/src/ExitPass.CentralPms.Application/Operations/TicketSessionSummaryModels.cs`
- `src/Services/CentralPms/src/ExitPass.CentralPms.Application/FiscalIssuance/FiscalIssuanceExitAuthorizationGateEvaluator.cs`
- `src/Services/CentralPms/src/ExitPass.CentralPms.Application/FiscalIssuance/FiscalIssuanceExitAuthorizationGatingReadiness.cs`
- `src/Services/CentralPms/src/ExitPass.CentralPms.Application/ManagementPlatform/PosServerSalesInvoiceProfileAdministrationModels.cs`
- `src/Services/CentralPms/src/ExitPass.CentralPms.Infrastructure/VendorParking/CentralPmsVendorPmsAdapterRegistration.cs`
- `src/Services/CentralPms/src/ExitPass.CentralPms.Infrastructure/VendorParking/HikCentralVendorPmsParkingResolutionClient.cs`
- `src/Services/CentralPms/src/ExitPass.CentralPms.Infrastructure/VendorParking/VendorParkingResolutionPersistence.cs`
- `src/Services/CentralPms/src/ExitPass.CentralPms.Infrastructure/PaymentAttempts/ParkingSessionReadRepository.cs`
- `src/Services/CentralPms/src/ExitPass.CentralPms.Infrastructure/PaymentAttempts/TariffSnapshotReadRepository.cs`
- `src/Services/CentralPms/src/ExitPass.CentralPms.Infrastructure/TerminalCashPayments/TerminalCashPaymentRepository.cs`
- `src/Services/CentralPms/src/ExitPass.CentralPms.Domain/Tariffs/TariffSnapshot.cs`
- `src/Services/VendorPmsAdapter/src/ExitPass.VendorPmsAdapter.Infrastructure/HikCentral/HikCentralParkingClient.cs`
- `src/Services/VendorPmsAdapter/src/ExitPass.VendorPmsAdapter.Contracts/Parking/VendorParkingSessionDto.cs`
- `src/Services/VendorPmsAdapter/src/ExitPass.VendorPmsAdapter.Contracts/Parking/VendorTariffQuoteDto.cs`
- `src/Services/VendorPmsAdapter/src/ExitPass.VendorPmsAdapter.Contracts/Parking/VendorParkingLookupStatus.cs`
- `src/Services/PaymentOrchestrator/src/ExitPass.PaymentOrchestrator.Api/Endpoints/WebPayPaymentIntentEndpoints.cs`
- `src/Services/PaymentOrchestrator/src/ExitPass.PaymentOrchestrator.Application/UseCases/WebPayPaymentIntents/WebPayPaymentIntentHandler.cs`
- `src/Services/PaymentOrchestrator/src/ExitPass.PaymentOrchestrator.Infrastructure/Integrations/CentralPmsWebPayClient.cs`
- `src/Services/PaymentOrchestrator/src/ExitPass.PaymentOrchestrator.Contracts/WebPay/WebPayParkingSessionResolveRequest.cs`
- `src/Services/PaymentOrchestrator/src/ExitPass.PaymentOrchestrator.Contracts/WebPay/WebPayParkingSessionResolveResponse.cs`
- `src/Services/WebPayUi/src/webpay.ts`
- `src/Services/WebPayUi/src/types.ts`

Tests, fixtures, proofs, and documents inspected:

- `src/Services/CentralPms/tests/ExitPass.CentralPms.ContractTests/Public/VendorParkingResolutionContractTests.cs`
- `src/Services/CentralPms/tests/ExitPass.CentralPms.ContractTests/Public/PaymentAttemptsContractTests.cs`
- `src/Services/CentralPms/tests/ExitPass.CentralPms.IntegrationTests/Api/VendorParkingResolutionApiIntegrationTests.cs`
- `src/Services/CentralPms/tests/ExitPass.CentralPms.IntegrationTests/Api/ManagementPlatformSalesInvoiceProfileApiIntegrationTests.cs`
- `src/Services/CentralPms/tests/ExitPass.CentralPms.IntegrationTests/Api/GateExecutionRuntimeRetirementIntegrationTests.cs`
- `src/Services/PaymentOrchestrator/tests/ExitPass.PaymentOrchestrator.IntegrationTests/WebPay/WebPayPaymentIntentEndpointIntegrationTests.cs`
- `src/Services/WebPayUi/src/App.test.tsx`
- `src/Services/WebPayUi/e2e/webpay-authoritative-sales-invoice.spec.ts`
- `docs/v1.3/central-pms/runbooks/ExitPass_Central_PMS_ExitAuthorization_Gate_Command_Boundary_Audit_v1.0.md`

Routes inspected:

- `POST /v1/vendor-parking/resolve`
- `POST /v1/webpay/parking-session`
- `POST /v1/webpay/payment-intents`
- `POST /v1/ops/ticket-session-summary`
- `POST /v1/terminal-cash-payments`
- `GET /v1/terminal-cash-payments/references/{terminalCashTenderId:guid}`
- `GET /v1/management-platform/sales-invoice-header-profiles/effective-readiness`
- `POST /v1/internal/gates/commands/{gateCommandId}/execute` as retired/not mapped evidence

Evidence quality summary:

- Live HikCentral/Vendor PMS tariff path: implemented and integration-tested with controlled adapter/test doubles; live multi-service UAT not proven by this audit.
- WebPay reference: implemented, integration-tested, and browser-fixture-tested; not live payment-provider UAT.
- APT terminal-cash contracts: implemented and present.
- Complete APT pre-cash readiness and revalidation contract: absent.
- Direct ExitPass-owned gate execution retirement: integration-tested by `GateExecutionRuntimeRetirementIntegrationTests.cs`.
