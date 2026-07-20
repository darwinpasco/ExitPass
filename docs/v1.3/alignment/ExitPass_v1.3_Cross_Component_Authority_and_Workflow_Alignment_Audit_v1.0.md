# ExitPass v1.3 Cross-Component Authority and Workflow Alignment Audit

## 1. Executive Verdict

Overall verdict: ON TRACK WITH CORRECTIONS.

ExitPass v1.3 is preserving the approved v1.2 authority boundaries while adopting the connector-driven parking-session projection workflow. Central PMS owns operational projection, payment finality orchestration, fiscal-readiness orchestration, and ExitAuthorization state. HikCentral or the configured Vendor PMS remains the raw parking-session, live tariff, and physical gate authority. POS Server remains fiscal authority. Management Platform and Operator Console configure and observe; they do not become transaction authority.

Highest-risk issue: the APT repository inventory is ambiguous. The user-listed `D:\SourceCodes\ExitPass-APT` is a stale ExitPass monorepo-style checkout, while the actual standalone APT implementation is `D:\SourceCodes\ExitPass-AssistedPaymentTerminal`. This can cause release, audit, and corrective work to inspect the wrong code.

Immediate corrective work is required for repository inventory clarity and APT cash-custody denomination capture. APT may continue with corrections. POS Server may continue. Management Platform / Operator Console may continue with documentation cleanup. No workstream needs to pause for an active runtime authority-boundary violation based on the audited evidence.

## 2. Baseline and Audit Scope

Approved v1.2 authority model:

- HikCentral or the configured Vendor PMS remains authoritative for raw parking entry/exit events, physical parking-session lifecycle, authoritative live parking-fee and tariff calculation, physical gate/barrier control, gate-device safety, physical action retries, and physical action/passage outcomes.
- Central PMS remains authoritative for connector-driven operational parking-session projection, projection freshness/health, session resolution/routing, payment finality, fiscal-readiness orchestration, ExitAuthorization issuance/validity/expiry/revocation/single-use consumption, audit and reconciliation evidence, and safe outcome records reported by the external gate system.
- POS Server remains authoritative for fiscal document creation, fiscal numbering, Sales Invoice rendering, authoritative Digital Sales Invoice presentation, fiscal document persistence, fiscal idempotency, and fiscal evidence.
- Management Platform / Operator Console configures and observes. It must not fabricate sessions, mark payment final, issue fiscal documents, issue arbitrary ExitAuthorization, or control physical gates.
- Gate Integration or HikCentral consumes or validates ExitAuthorization and owns physical barrier control.

v1.3 projection change:

- Central PMS holds connector-driven operational parking-session projections.
- The projection does not replace HikCentral as the raw session or live tariff authority.
- Normal payment operation should use Central PMS session resolution and live HikCentral tariff calculation.
- Projection data may support centralized visibility, routing, monitoring, continuity, and explicitly governed degraded operation.

Scope audited:

- `D:\SourceCodes\ExitPass`: Central PMS, WebPay, Management Platform, Operator Console.
- `D:\SourceCodes\ExitPass-APT`: user-listed APT path; inspected as inventory evidence.
- `D:\SourceCodes\ExitPass-AssistedPaymentTerminal`: actual standalone APT implementation discovered by local repository search.
- `D:\SourceCodes\ExitPass-PoSServer`: POS Server implementation.
- `D:\SourceCodes\exitpassdb_v1.2`: canonical database repository.

Fetch limitation: the primary audit branch was already at local `origin/dev` commit `02c4e47`. A requested `git fetch origin` could not be completed without mutating Git metadata and was not retried after policy rejection. This audit used existing local refs and checked-out worktrees.

## 3. Repository and Branch Inventory

| Repository | Path | Integration branch | Current branch | HEAD | Clean or dirty | Scope audited |
| --- | --- | --- | --- | --- | --- | --- |
| ExitPass / Central PMS / WebPay / Operator Console / Management Platform | `D:\SourceCodes\ExitPass` | local `origin/dev` | `docs/exitpass-v1.3-cross-component-alignment-audit` | `02c4e47` | Clean before report creation | Merged integration baseline |
| User-listed APT path | `D:\SourceCodes\ExitPass-APT` | `origin/dev` | `dev` | `c6af283` | Clean, behind local `origin/dev` by 2 commits | Inventory only; appears to be a stale monorepo-style checkout |
| Standalone APT implementation | `D:\SourceCodes\ExitPass-AssistedPaymentTerminal` | `origin/develop` | `develop` | `5de4b71` | Clean | Actual APT implementation |
| POS Server | `D:\SourceCodes\ExitPass-PoSServer` | `origin/dev` | `dev` | `46ddd68` | Clean | POS Server implementation |
| Canonical database | `D:\SourceCodes\exitpassdb_v1.2` | `origin/develop` | `develop` | `32cc517` | Clean | Canonical database objects |

Management Platform location: no separate local Management Platform repository was found under `D:\SourceCodes`. Evidence inside `D:\SourceCodes\ExitPass` includes `src\Services\ManagementPlatformUi`, `contracts\management-platform`, `scripts\management-platform`, `docs\v1.3\management-platform`, `src\Services\OperatorConsoleUi`, and `docs\v1.3\operator-console`. This audit treats those modules as the Management Platform implementation.

## 4. Cross-System Authority Matrix

| Capability | Authoritative Component | Central PMS | WebPay | APT | POS Server | Management Platform | HikCentral/Gate Integration | Alignment Status | Evidence |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Raw parking-session lifecycle | HikCentral / Vendor PMS | Projects and routes | Requests resolution | Requests Central PMS | None | Observes | Owns raw lifecycle | ALIGNED | `docs/v1.3/ExitPass_System_Design_v1.3.md`; `VendorSessionProjectionSchedulerHostedService` |
| Session projection | Central PMS | Owns projection | Consumes | Consumes | None | Observes health | Source feed | ALIGNED | Central PMS `Program.cs`; `InternalVendorSessionProjectionEndpoints`; `VendorSessionProjectionHealthEndpoints` |
| Live tariff calculation | HikCentral through Central PMS path | Orchestrates | Uses Central PMS | Uses Central PMS facts | None | Observes | Owns live tariff | PARTIALLY ALIGNED | WebPay and APT route through Central PMS; adapter-level live tariff proof remains a coverage point |
| Degraded session/tariff resolution | Central PMS policy | Owns policy | No local fallback found | Blocks stale/non-live cash path | None | Observes | Source unavailable | ALIGNED | APT `CashCapturePanel.tsx`; decision log V13-D027 |
| Payment finality | Central PMS | Owns | Requests | Submits tender through Central PMS | Receives final facts | Observes | None | ALIGNED | `IssueExitAuthorizationHandler`; APT `TerminalCashPaymentSubmissionService` |
| Cash custody | APT local custody plus Central PMS finality | Receives canonical tender | None | Owns local evidence | None | Observes | None | PARTIALLY ALIGNED | APT `CashJournalService`; denomination UI issue in `CashCapturePanel.tsx` |
| Fiscal issuance and numbering | POS Server | Orchestrates readiness | Uses Central PMS | Requests through Central PMS | Owns issuance/numbering | Configures/observes | None | ALIGNED | POS `FiscalDocumentCreationService`; Central PMS fiscal orchestration registrations |
| Receipt presentation | POS Server | Proxies authoritative presentation | Consumes as needed | Retrieves through Central PMS | Owns rendering | Observes | None | ALIGNED | Central PMS `TerminalCashReceiptPresentationService`; POS `DigitalSalesInvoicePresentationEndpoint`; APT `TerminalCashReceiptRetrievalService` |
| ExitAuthorization issuance | Central PMS | Owns | Does not issue | Does not issue | Does not issue | Observes | Consumes/validates | ALIGNED | `IssueExitAuthorizationHandler`; POS boundary docs; APT tests |
| ExitAuthorization consumption | Central PMS + Gate Integration | Owns consumption records | None | None | None | Observes | Calls consume and controls gate | ALIGNED | `GateExitAuthorizationConsumeEndpoints.MapGateExitAuthorizationConsumeEndpoints` |
| Physical gate control | HikCentral/Gate Integration | No normal direct execution path | None | None | None | No control exposed | Owns physical control | ALIGNED WITH CORRECTIONS | `GateExecutionRuntimeRetirementIntegrationTests`; dormant direct execution classes remain |
| Configuration | Management Platform / Operator Console governance | Applies config | None | Terminal config only | POS profile config | Configures/observes | External PMS/gate config remains external | ALIGNED | `ManagementPlatformUi` API client; Operator Console safety copy |
| Reconciliation and audit | Central PMS + POS Server + Management visibility | Owns operational audit | None | Local evidence/readback | Fiscal audit | Observes | Reports outcomes | ALIGNED | Central PMS terminal cash/gate records; POS readback; APT local evidence |

## 5. Central PMS and WebPay Findings

### Finding C-1: Central PMS connector-driven projection is active and aligned

Status: ALIGNED

Code evidence:

- `src/Services/CentralPms/src/ExitPass.CentralPms.Api/Program.cs` registers `IVendorSessionProjectionRepository`, `IVendorSessionProjectionSyncTargetRepository`, `VendorSessionProjectionLookupService`, `VendorSessionProjectionHealthService`, `VendorSessionProjectionSyncOrchestrator`, and `VendorSessionProjectionSchedulerHostedService`.
- `src/Services/CentralPms/src/ExitPass.CentralPms.Api/Endpoints/InternalVendorSessionProjectionEndpoints.cs` maps internal projection sync.
- `src/Services/CentralPms/src/ExitPass.CentralPms.Api/Endpoints/VendorSessionProjectionHealthEndpoints.cs` exposes projection health.
- `src/Services/CentralPms/tests/ExitPass.CentralPms.IntegrationTests/Api/VendorSessionProjectionHealthTests.cs` supports read-only health behavior.

Documentation evidence:

- `docs/v1.3/ExitPass_v1.3_Documentation_Decision_Log.md` decisions V13-D024 and V13-D025 establish HCP connector polling and state that projection is operational continuity, not financial truth.

Assessment: Central PMS owns operational projection and health. This aligns with the v1.3 connector-driven projection workflow.

### Finding C-2: WebPay routes through Central PMS rather than direct HikCentral

Status: ALIGNED

Code evidence:

- `src/Services/WebPayUi/src/webpay.ts` defines `parkingSessionResolvePath = "/v1/webpay/parking-session"` and `paymentIntentPath = "/v1/webpay/payment-intents"`.
- `resolveParkingSession` calls `${getApiBaseUrl()}${parkingSessionResolvePath}`.
- `createPaymentIntent` calls `${getApiBaseUrl()}${paymentIntentPath}`.
- `src/Services/WebPayUi/src/webpay.test.ts` verifies Central PMS endpoint use and tariff snapshot/payment status behavior.

Assessment: WebPay does not implement a competing direct HikCentral client in audited runtime code.

### Finding C-3: Fiscal readiness gates remain Central PMS orchestration before ExitAuthorization

Status: ALIGNED

Code evidence:

- `src/Services/CentralPms/src/ExitPass.CentralPms.Application/Payments/IssueExitAuthorizationHandler.cs` uses confirmed payment finality and fiscal gating before issuance, calls `EvaluateFiscalGatingPreflightAsync`, and issues through `IExitAuthorizationGateway`.
- `src/Services/CentralPms/src/ExitPass.CentralPms.Api/Program.cs` registers `IFiscalIssuancePosServerLiveIntegrationService`, `IPosServerFiscalDocumentClient`, `IExitAuthorizationFiscalGatingShadowEvaluator`, and `IFiscalIssuanceOrchestrationService`.

Documentation evidence:

- `docs/v1.3/ExitPass_System_Design_v1.3.md` states POS Server issues Sales Invoice, Central PMS records fiscal reference, and Central PMS issues ExitAuthorization.
- `docs/v1.3/ExitPass_v1.3_Documentation_Decision_Log.md` decision V13-D031 requires fiscal issuance before Central PMS issues ExitAuthorization.

Assessment: Central PMS preserves payment finality and fiscal readiness as prerequisites to ExitAuthorization.

### Finding C-4: Direct physical gate execution is retired from normal runtime composition

Status: ALIGNED WITH CORRECTIONS

Code evidence:

- `src/Services/CentralPms/tests/ExitPass.CentralPms.IntegrationTests/Api/GateExecutionRuntimeRetirementIntegrationTests.cs` contains:
  - `ControlledExecutionRoute_IsNotMappedEvenWhenOldSwitchesArePresent`
  - `NormalComposition_DoesNotResolveLiveHikCentralRuntimeOrFakeFallback`
  - `GateCommandDispatchWorker_IsNotRegisteredAsHostedService`
  - `GateCommandRecoveryWorker_RemainsDisabledByDefaultAndHasNoPhysicalExecutionDependency`
  - `ExitAuthorizationAndGateFacingConsumptionServices_RemainRegisteredWithoutAdapterCoupling`
- `src/Services/CentralPms/src/ExitPass.CentralPms.Api/appsettings.json` contains `GateCommandRecoveryWorker` settings but no production HikCentral gate execution activation section.

Dormant code evidence:

- `src/Services/CentralPms/src/ExitPass.CentralPms.Application/Gates/GateCommandExecutionService.cs`
- `src/Services/CentralPms/src/ExitPass.CentralPms.Application/Gates/GateCommandDispatchCycleService.cs`
- `src/Services/CentralPms/src/ExitPass.CentralPms.Infrastructure/Gates/HikCentralGateActionAdapter.cs`
- `src/Services/CentralPms/src/ExitPass.CentralPms.Infrastructure/Gates/FakeHikCentralGateActionAdapter.cs`

Assessment: Normal runtime does not actively initiate `OPEN_GATE`. Dormant lower-level classes remain and should be treated as retired from runtime, not active behavior.

### Finding C-5: Safe gate-facing consumption validates identity and device context

Status: ALIGNED

Code evidence:

- `src/Services/CentralPms/src/ExitPass.CentralPms.Api/Endpoints/GateExitAuthorizationConsumeEndpoints.cs` maps `/v1/gate/authorizations/{exitAuthorizationId}/consume`, requires internal service mTLS policy, requires `X-Correlation-Id`, `X-Service-Identity-Id`, and `X-Gate-Device-Id`, validates service identity/gate device assignment through `IGateDeviceIdentityValidator`, and calls `IConsumeExitAuthorizationUseCase`.

Assessment: The endpoint consumes or validates authorization state. It does not own the physical barrier action.

## 6. Assisted Payment Terminal Findings

### Finding A-1: Actual APT implementation is in `ExitPass-AssistedPaymentTerminal`, not the user-listed `ExitPass-APT`

Status: PARTIALLY ALIGNED

Evidence:

- `D:\SourceCodes\ExitPass-APT` contains a broad ExitPass monorepo-style tree and is behind local `origin/dev` by two commits.
- `D:\SourceCodes\ExitPass-AssistedPaymentTerminal` contains `ExitPass.AssistedPaymentTerminal.sln`, APT application source, and APT tests.
- Standalone APT repository branch and HEAD: branch `develop`, HEAD `5de4b71`.

Impact: The implementation appears aligned, but the known repository list points to a path that is not the standalone APT application. This creates release, audit, and workstream coordination risk.

Smallest recommended correction: record `D:\SourceCodes\ExitPass-AssistedPaymentTerminal` as the actual APT repository and retire or clearly label `D:\SourceCodes\ExitPass-APT` as a stale or legacy monorepo clone if it is not used.

### Finding A-2: APT uses Central PMS for cash payment, fiscal issuance, and receipt presentation

Status: ALIGNED

Code evidence:

- `D:\SourceCodes\ExitPass-AssistedPaymentTerminal\src\AssistedPaymentTerminal.LocalOperations\CentralPmsTerminalCashPaymentClient.cs` calls Central PMS `/v1/terminal-cash-payments` and `/v1/terminal-cash-payments/references/{id}`.
- `D:\SourceCodes\ExitPass-AssistedPaymentTerminal\src\AssistedPaymentTerminal.LocalOperations\CentralPmsTerminalCashFiscalClient.cs` calls Central PMS `/v1/terminal-cash-payments/references/{id}/fiscal-issuance`.
- `D:\SourceCodes\ExitPass-AssistedPaymentTerminal\src\AssistedPaymentTerminal.LocalOperations\CentralPmsTerminalCashReceiptClient.cs` calls Central PMS `/v1/terminal-cash-payments/references/{id}/receipt-presentation`.
- `D:\SourceCodes\ExitPass-AssistedPaymentTerminal\src\AssistedPaymentTerminal.LocalOperations\TerminalCashPaymentSubmissionService.cs` submits and reconciles terminal cash tenders through Central PMS and stores canonical Central PMS payment attempt/confirmation identifiers.
- `D:\SourceCodes\ExitPass-AssistedPaymentTerminal\src\AssistedPaymentTerminal.LocalOperations\TerminalCashFiscalSubmissionService.cs` requires confirmed Central PMS canonical payment state before fiscal issuance.
- `D:\SourceCodes\ExitPass-AssistedPaymentTerminal\src\AssistedPaymentTerminal.LocalOperations\TerminalCashReceiptRetrievalService.cs` requires recorded fiscal state and POS fiscal document ID, then stores POS-owned authoritative receipt presentation payload/hash.

Assessment: APT does not become payment-finality authority, fiscal authority, or receipt-rendering authority.

### Finding A-3: APT does not implement direct HikCentral or gate-control authority

Status: ALIGNED

Code evidence:

- Runtime source searches in `D:\SourceCodes\ExitPass-AssistedPaymentTerminal` found no direct HikCentral client, `OPEN_GATE`, direct gate command execution, or ExitAuthorization issuance path.
- `D:\SourceCodes\ExitPass-AssistedPaymentTerminal\src\AssistedPaymentTerminal.App.Tests\CentralPmsCashFiscalUiProofHostTests.cs` asserts fiscal UI proof state does not issue ExitAuthorization.
- `D:\SourceCodes\ExitPass-AssistedPaymentTerminal\src\AssistedPaymentTerminal.LocalOperations.Contracts\TerminalCashPaymentContracts.cs` treats `ExitAuthorizationIssued` as readback/status, not local issuance authority.

Assessment: APT stays within terminal workflow and Central PMS integration boundaries.

### Finding A-4: APT cash denomination capture appears optional in the UI

Status: PARTIALLY ALIGNED

Code evidence:

- `D:\SourceCodes\ExitPass-AssistedPaymentTerminal\src\AssistedPaymentTerminal.App\src\CashCapturePanel.tsx` renders denomination entry as `Optional denomination inputs`, filters denomination quantities with `quantity > 0`, and requires cashier attestation before `recordCashReceived` but does not present denominations as mandatory.
- `D:\SourceCodes\ExitPass-AssistedPaymentTerminal\src\AssistedPaymentTerminal.LocalOperations\CashJournalService.cs` captures `CashTenderDenominationEntry` records when provided and creates a local terminal cash custody event plus Central PMS outbox command after irreversible `CashReceived`.

Documentation evidence: APT BRD/design material under `D:\SourceCodes\ExitPass-AssistedPaymentTerminal\docs` describes denomination capture as required cash-custody evidence.

Impact: The irreversible `CASH_RECEIVED` custody boundary is preserved, but cash evidence completeness is weaker than the approved operational requirement.

Smallest recommended correction: make denomination capture mandatory or explicitly governed as an exception before `CASH_RECEIVED`.

### Finding A-5: APT refuses unsafe cash/fiscal continuation when Central PMS or fiscal path is unavailable

Status: ALIGNED

Code evidence:

- `D:\SourceCodes\ExitPass-AssistedPaymentTerminal\src\AssistedPaymentTerminal.App\src\CashCapturePanel.tsx` blocks cash capture when terminal configuration is not live, when tariff/session snapshot is stale or non-live, and only shows fiscal controls after Central PMS canonical payment confirmation.
- `TerminalCashFiscalSubmissionService.cs` refuses fiscal issuance before canonical payment confirmation and required identifiers.

Assessment: APT preserves Central PMS and POS Server dependencies before fiscal and exit-readiness progression.

## 7. POS Server Findings

### Finding P-1: POS Server remains Site-scoped fiscal issuance authority

Status: ALIGNED

Code evidence:

- `D:\SourceCodes\ExitPass-PoSServer\src\ExitPass.PosServer.Api\Program.cs` maps fiscal document creation/read/presentation endpoints and Sales Invoice Header Profile Admin endpoints, and does not map parking-session projection, ExitAuthorization, or gate-control endpoints.
- `D:\SourceCodes\ExitPass-PoSServer\src\ExitPass.PosServer.Application\FiscalDocuments\FiscalDocumentCreationService.cs` validates site/POS fiscal context, uses upstream payment/finality references as inputs, resolves fiscal idempotency through `FiscalIssuanceIdempotencyResolver`, and persists POS fiscal document state.
- `D:\SourceCodes\ExitPass-PoSServer\src\ExitPass.PosServer.Api\Endpoints\FiscalDocumentCreationEndpoint.cs` exposes fiscal document creation and returns fiscal document identity/status fields.

Documentation evidence:

- `D:\SourceCodes\ExitPass-PoSServer\docs\REPOSITORY_BOUNDARY.md` states Central PMS sends verified payment finality context, POS Server issues Sales Invoice and returns fiscal document identity/status/Digital SI URL, and Central PMS remains the only ExitAuthorization authority.

Assessment: POS Server remains fiscal authority and does not become parking-session, payment-finality, or gate authority.

### Finding P-2: POS Server owns authoritative Digital Sales Invoice presentation

Status: ALIGNED

Code evidence:

- `D:\SourceCodes\ExitPass-PoSServer\src\ExitPass.PosServer.Api\Endpoints\DigitalSalesInvoicePresentationEndpoint.cs` serves Digital Sales Invoice presentation from POS Server fiscal documents.
- `D:\SourceCodes\ExitPass-PoSServer\src\ExitPass.PosServer.Application\FiscalDocuments\DigitalSalesInvoiceRenderService.cs` renders Digital Sales Invoice content from fiscal document data.
- `D:\SourceCodes\ExitPass-PoSServer\tests\ExitPass.PosServer.Api.Tests\DigitalSalesInvoiceEndpointTests.cs` verifies endpoint behavior and absence of exit/gate payload authority.

Assessment: Receipt presentation authority is preserved in POS Server and consumed by Central PMS/APT as authoritative presentation.

### Finding P-3: POS Server tests guard against gate and ExitAuthorization leakage

Status: ALIGNED

Test evidence:

- `D:\SourceCodes\ExitPass-PoSServer\tests\ExitPass.PosServer.Api.Tests\FiscalDocumentCreationEndpointTests.cs` includes `NoPaymentFinalityOrGateExitBehaviorIsExposed`.
- `D:\SourceCodes\ExitPass-PoSServer\tests\ExitPass.PosServer.Api.Tests\FiscalDocumentReadEndpointTests.cs` guards against forbidden names.
- `D:\SourceCodes\ExitPass-PoSServer\tests\ExitPass.PosServer.Runtime.Tests\SalesInvoiceHeaderProfileServiceTests.cs` includes `ProfileModelDoesNotExposePrintExitGateCentralPmsOrAptBehavior`.
- `D:\SourceCodes\ExitPass-PoSServer\tests\ExitPass.PosServer.Runtime.Tests\SalesInvoiceHeaderProfileAdminServiceTests.cs` guards profile admin service types from Print/APT/Central PMS/Exit/Gate authority leakage.

Assessment: Tests support the authority boundary, while endpoint/service evidence proves the runtime shape.

## 8. Management Platform / Operator Console Findings

### Finding M-1: Management Platform is inside the primary ExitPass repository

Status: ALIGNED

Evidence:

- No separate Management Platform repository was found under `D:\SourceCodes`.
- Implementation evidence in `D:\SourceCodes\ExitPass` includes `src\Services\ManagementPlatformUi`, `contracts\management-platform`, `scripts\management-platform`, `docs\v1.3\management-platform`, and `src\Services\OperatorConsoleUi`.

Assessment: Management Platform should be audited as primary-repo implementation, not as a separate repository.

### Finding M-2: Management Platform API client is scoped to Central PMS management endpoints

Status: ALIGNED

Code evidence:

- `src/Services/ManagementPlatformUi/src/apiClient.ts` creates a Central PMS API client, restricts paths through `toCentralPmsPath`, and rejects absolute URLs and non-management API roots.
- `src/Services/ManagementPlatformUi/src/apiClient.test.ts` verifies path-scope behavior.

Assessment: Management Platform calls Central PMS management endpoints and does not expose direct Vendor PMS, POS Server, or gate authority by itself.

### Finding M-3: Operator Console visibility pages state non-authoritative behavior

Status: ALIGNED

Code evidence:

- `src/Services/OperatorConsoleUi/src/App.tsx` includes fiscal void UI copy stating the page does not refund payment, open gates, call HikCentral, or create replacement Sales Invoices.
- The same file displays vendor payment acknowledgments as external acknowledgments, not Central PMS payment finality.
- The HikCentral projection health page states Vendor PMS remains session/tariff authority and does not expose sync trigger, fallback enablement, tariff, payment, paid-state, or exit controls.
- Review draft UI states it does not authorize exits, open gates, or call HikCentral.

Assessment: Operator Console observes and configures. It does not become transaction authority in audited code.

### Finding M-4: Management Platform README appears stale relative to profile approve/retire UI

Status: PARTIALLY ALIGNED

Evidence:

- `src/Services/ManagementPlatformUi/README.md` describes the module as read-only and says it does not create/edit/activate/retire registered businesses or Sales Invoice setups.
- `contracts/management-platform/sales-invoice-profile-approve-retire-ui.v1.json` and current merge history include management profile approve/retire UI work.

Impact: The code direction may still be valid configuration governance, but documentation can mislead auditors about what Management Platform exposes.

Smallest recommended correction: update the README to distinguish read-only transaction status views from approved configuration governance actions.

## 9. Cross-Component Contract Findings

### Finding X-1: Core identifiers and authority boundaries are mostly consistent

Status: ALIGNED

Evidence:

- Central PMS code uses `parkingSessionId`, `vendorSessionRef`, `paymentConfirmationId`, `fiscalIssuanceReferenceId`, `exitAuthorizationId`, and `gateAuthorizationConsumptionId` in payment, fiscal, and gate-facing flows.
- APT local operations use `TerminalCashTenderId`, canonical payment attempt/confirmation references from Central PMS, and POS fiscal document identifiers from Central PMS/POS readback.
- POS Server uses fiscal document ID/status and Central PMS upstream finality/fiscal context as inputs.

File evidence:

- `src/Services/CentralPms/src/ExitPass.CentralPms.Application/TerminalCashPayments/*`
- `D:\SourceCodes\ExitPass-AssistedPaymentTerminal\src\AssistedPaymentTerminal.LocalOperations\TerminalCashPaymentSubmissionService.cs`
- `D:\SourceCodes\ExitPass-AssistedPaymentTerminal\src\AssistedPaymentTerminal.LocalOperations\TerminalCashFiscalSubmissionService.cs`
- `D:\SourceCodes\ExitPass-PoSServer\src\ExitPass.PosServer.Application\FiscalDocuments\FiscalDocumentCreationService.cs`

Assessment: No audited payment channel bypasses Central PMS for finality, POS Server for fiscal issuance, or HikCentral/Gate Integration for physical control.

### Finding X-2: Receipt-presentation contract preserves POS Server authority

Status: ALIGNED

Evidence:

- Central PMS `TerminalCashReceiptPresentationService` resolves terminal cash tenders to POS Server-owned presentation and does not reconstruct fiscal content.
- APT `TerminalCashReceiptRetrievalService` stores `AuthoritativePresentationJson` and `AuthoritativePayloadHash`.
- POS Server `DigitalSalesInvoicePresentationEndpoint` owns authoritative presentation.

Assessment: Receipt presentation is properly opaque/authoritative from POS Server through Central PMS to APT.

### Finding X-3: Normal live tariff path is directionally aligned but final adapter-level proof remains a coverage gap

Status: PARTIALLY ALIGNED

Evidence:

- WebPay and APT both route through Central PMS.
- v1.3 docs establish HikCentral as live tariff authority.
- Central PMS contains projection and session-resolution components.

Gap: This audit did not fully prove the concrete live HikCentral tariff adapter registration and request path for every normal payment channel. The channel direction is correct; the remaining proof point is connector implementation coverage, not evidence of an APT/WebPay bypass.

Smallest recommended correction: add or preserve contract tests proving normal WebPay and APT resolution call Central PMS live tariff calculation and do not use projection-only tariff data except under explicit degraded policy.

## 10. Retired and Dormant Gate-Execution Findings

What was retired:

- Central PMS direct controlled gate execution runtime endpoint `/v1/internal/gates/commands/{id}/execute`.
- Hosted direct gate command dispatch worker registration.
- Normal runtime live/fake HikCentral gate action adapter resolution.
- Configuration sections that previously could activate direct physical gate execution.

Evidence:

- `src/Services/CentralPms/tests/ExitPass.CentralPms.IntegrationTests/Api/GateExecutionRuntimeRetirementIntegrationTests.cs`
- `src/Services/CentralPms/src/ExitPass.CentralPms.Api/appsettings.json`
- `src/Services/CentralPms/src/ExitPass.CentralPms.Api/Program.cs`

What remains dormant:

- `GateCommandExecutionService`
- `GateCommandDispatchCycleService`
- `GateCommandDispatchCandidateRepository`
- `HikCentralGateActionAdapter`
- `FakeHikCentralGateActionAdapter`
- HikCentral signing, transport, and planning classes under Central PMS gate infrastructure.

Canonical database objects that remain:

- `D:\SourceCodes\exitpassdb_v1.2\objects\schemas\gates\comments\gates.gate_commands.comments.sql`
- `D:\SourceCodes\exitpassdb_v1.2\objects\schemas\gates\comments\gates.gate_authorization_consumptions.comments.sql`
- `D:\SourceCodes\exitpassdb_v1.2\objects\schemas\gates\comments\gates.hikcentral_gate_action_audits.column-comments.17.sql`
- `D:\SourceCodes\exitpassdb_v1.2\objects\schemas\gates\comments\gates.hikcentral_gate_action_audits.column-comments.18.sql`

Runtime reachability assessment:

- No audited normal Central PMS runtime path maps the direct execution endpoint.
- No audited normal composition resolves live/fake HikCentral physical gate action adapters.
- No audited APT, WebPay, POS Server, Management Platform, or Operator Console code directly performs physical `OPEN_GATE`.

Disposition: retain dormant code only while non-reachability tests remain mandatory. Later relocation or deletion is advisable after Gate Integration boundary contracts are finalized. This audit does not recommend immediate broad deletion because dormant request-planning/signing code may be reusable for external Gate Integration adapters or test harnesses.

## 11. Risks and Gaps

### Critical

None found.

### High

#### H-1: APT repository path ambiguity

Finding: the user-listed `D:\SourceCodes\ExitPass-APT` does not appear to be the standalone APT implementation. The actual APT implementation is `D:\SourceCodes\ExitPass-AssistedPaymentTerminal`.

Impact: audits, releases, or corrective work may inspect or build the wrong repository, leading to stale conclusions or missed APT defects.

Evidence:

- `D:\SourceCodes\ExitPass-APT` branch `dev`, HEAD `c6af283`, behind local `origin/dev` by two commits, contains broad ExitPass monorepo structure.
- `D:\SourceCodes\ExitPass-AssistedPaymentTerminal` branch `develop`, HEAD `5de4b71`, contains `ExitPass.AssistedPaymentTerminal.sln` and APT source/tests.

Affected components: APT and cross-component release coordination.

Blocks continued work: no, if the team confirms and uses `D:\SourceCodes\ExitPass-AssistedPaymentTerminal`.

Smallest recommended correction: update repository inventory and team task templates to name the actual standalone APT repository path.

#### H-2: APT denomination capture is presented as optional

Finding: APT cash capture UI labels denomination inputs as optional.

Impact: cash custody evidence may be incomplete for irreversible `CASH_RECEIVED` events.

Evidence:

- `D:\SourceCodes\ExitPass-AssistedPaymentTerminal\src\AssistedPaymentTerminal.App\src\CashCapturePanel.tsx` renders `Optional denomination inputs`.
- `CashJournalService.CommitCashReceivedAsync` records denominations when supplied but the UI wording does not enforce them as required.

Affected components: APT, Central PMS terminal cash reconciliation, audit and cash custody workflows.

Blocks continued work: no, but it should be corrected before Private Beta cash handling acceptance.

Smallest recommended correction: make denomination entry mandatory or explicitly require a governed exception before `CASH_RECEIVED`.

### Medium

#### M-1: Dormant direct gate execution code remains in Central PMS

Finding: direct gate execution services/adapters remain in code even though normal runtime registration has been retired.

Impact: future composition changes could accidentally reactivate direct physical control if regression tests are weakened.

Evidence:

- `GateCommandExecutionService.cs`
- `GateCommandDispatchCycleService.cs`
- `HikCentralGateActionAdapter.cs`
- `FakeHikCentralGateActionAdapter.cs`
- `GateExecutionRuntimeRetirementIntegrationTests.cs`

Affected components: Central PMS and Gate Integration boundary.

Blocks continued work: no, because runtime evidence shows the path is unreachable.

Smallest recommended correction: keep non-reachability tests mandatory and later decide whether to relocate or remove dormant execution code.

#### M-2: Live tariff adapter proof should be strengthened across channels

Finding: the audited channel direction is correct, but adapter-level proof that normal operation always invokes live HikCentral tariff calculation was not fully established in this audit.

Impact: projection data could be confused with live tariff authority if degraded-mode controls are not explicit.

Evidence: WebPay and APT route through Central PMS; v1.3 docs state HikCentral live tariff authority; remaining proof point is Central PMS connector/live tariff adapter path and channel tests.

Affected components: Central PMS, WebPay, and APT.

Blocks continued work: no, but should be strengthened before final v1.3 acceptance.

Smallest recommended correction: add or retain tests proving normal WebPay/APT session resolution obtains live tariff through Central PMS and only uses projection tariffs under explicit degraded policy.

#### M-3: Management Platform README is stale

Finding: the README still describes Management Platform as strictly read-only, while current contracts include Sales Invoice profile approve/retire UI.

Impact: audit readers may misclassify configuration governance actions as runtime transaction authority or assume current UI does not exist.

Evidence:

- `src/Services/ManagementPlatformUi/README.md`
- `contracts/management-platform/sales-invoice-profile-approve-retire-ui.v1.json`

Affected components: Management Platform and Operator Console.

Blocks continued work: no.

Smallest recommended correction: update README wording to distinguish configuration governance from transaction authority.

### Low

- Canonical database still contains gate command/action audit objects from the retired direct execution era. They are not runtime authority by themselves but should be documented as audit/dormant support.
- `D:\SourceCodes\ExitPass-APT` being behind `origin/dev` may confuse future local audits if left in place without a label.

### Informational

- Passing tests support several authority boundaries, especially direct gate runtime retirement and POS Server fiscal-only behavior, but runtime registration and endpoint evidence remain the primary proof.

## 12. On-Track Assessment by Workstream

| Workstream | Assessment | Rationale |
| --- | --- | --- |
| Central PMS / WebPay | ON TRACK WITH CORRECTIONS | Projection, payment/fiscal gating, WebPay routing, and gate consumption align. Dormant direct gate code and live tariff adapter proof need continued guardrails. |
| APT | ON TRACK WITH CORRECTIONS | Actual APT routes through Central PMS and POS-owned receipt presentation. Repository path ambiguity and optional denomination UI need correction. |
| POS Server | ON TRACK | POS Server remains fiscal authority and does not own parking sessions, payment finality, ExitAuthorization, or gate control. |
| Management Platform / Operator Console | ON TRACK WITH CORRECTIONS | Configures and observes without transaction authority. README staleness should be corrected. |
| Gate Integration boundary | ON TRACK WITH CORRECTIONS | Direct Central PMS gate execution is retired from normal runtime; dormant code remains and must stay guarded. |
| Canonical database alignment | ON TRACK WITH CORRECTIONS | Database supports audit/reconciliation evidence and central/fiscal boundaries. Gate command/action audit objects need clear retired/dormant context. |

## 13. Recommended Next Bounded Tasks

1. Correct APT repository inventory and task templates to use `D:\SourceCodes\ExitPass-AssistedPaymentTerminal` as the standalone APT implementation path.
2. Make APT denomination capture mandatory or require an explicit governed exception before irreversible `CASH_RECEIVED`.
3. Add or verify end-to-end Central PMS tests proving normal WebPay and APT session resolution use live HikCentral tariff calculation and only use projection-derived tariff data under explicit degraded policy.
4. Preserve and extend Central PMS direct-gate-retirement guard tests so dormant `OPEN_GATE` code cannot be reactivated through configuration or hosted-service registration.
5. Update `src/Services/ManagementPlatformUi/README.md` to reflect current configuration governance capabilities without implying transaction authority.
6. Add a short canonical database note classifying legacy gate command/action audit objects as retained audit/dormant support, not active physical gate authority.

## 14. Final Conclusion

ExitPass v1.3 is preserving the v1.2 working baseline while implementing the connector-driven session-projection workflow consistently across the audited channels.

No active runtime path was found where WebPay, APT, POS Server, Management Platform, or Operator Console bypasses Central PMS to create payment finality, issues ExitAuthorization outside Central PMS, acts as fiscal authority outside POS Server, or controls physical gates outside HikCentral/Gate Integration.

The system is not fully clean. Repository inventory for APT must be corrected, APT denomination capture should be hardened, live tariff adapter proof should be explicit across WebPay and APT, and dormant direct gate execution code must remain guarded or later retired. These are bounded corrections, not evidence that the v1.3 architecture is off track.
