# ExitPass Statutory Discount System-Wide Baseline Audit v1.0

## 1. Audit purpose and scope

This audit determines what statutory-discount and discount-authorized payable-basis capability already exists in ExitPass, where it exists, and whether it is merged implementation, partial implementation, contract-only, database-structure-only, test-only, documentation-only, research-only, stale/superseded, conflicting, absent, or not verifiable.

This is an audit-only report. It does not implement, repair, refactor, migrate, seed, or activate statutory-discount behavior.

The selected report path is `docs/v1.3/operator-console/reviews/ExitPass_Statutory_Discount_System_Wide_Baseline_Audit_v1.0.md`. This uses the requested path and the existing v1.3 Operator Console review convention.

## 2. Repository and branch examined

Working repository:

- `D:\SourceCodes\ExitPass-Discounts`

Branch examined:

- `docs/statutory-discount-system-wide-baseline-audit`

Baseline branch before audit branch creation:

- `dev`
- upstream: `origin/dev`

Canonical origin:

- `https://github.com/darwinpasco/ExitPass.git`

## 3. origin/dev baseline commit

`origin/dev` baseline incorporated:

- `e3d38ad79779c757be2ebcec3a05a0321272a3e1`

The local `dev` branch and `origin/dev` were equal before creating the audit branch.

## 4. Primary-repository paths inspected read-only

Primary repository inspected read-only:

- `D:\SourceCodes\ExitPass`

Mandatory read-only paths inspected:

- `D:\SourceCodes\ExitPass\docs\operator-console`
- `D:\SourceCodes\ExitPass\docs\v1.3\operator-console`
- other files under `D:\SourceCodes\ExitPass\docs` matching statutory-discount, Senior Citizen, PWD, local ordinance, evidence, VAT, payable-basis, fiscal-discount, Sales Invoice, and audit/reporting concepts.

No writes, branch switches, fetches, pulls, staging, cleaning, stashing, resets, or discards were performed in `D:\SourceCodes\ExitPass`.

## 5. Primary-repository local-only posture

The primary repository has pre-existing local modifications on branch `feature/webpay-authoritative-sales-invoice-presentation`. Those changes were treated as read-only and classified separately as `IN_FLIGHT_OR_LOCAL_ONLY` where relevant.

Observed primary working-tree changes are concentrated in WebPay authoritative Sales Invoice presentation and related Payment Orchestrator/Central PMS receipt presentation files. They are not classified as merged statutory-discount implementation. A read-only comparison of primary `docs` against the clean clone found one primary-only docs file, `docs/hikcentral-sandbox-validation.env.example`, which is unrelated to the statutory-discount audit scope.

## 6. Executive verdict

ExitPass on `origin/dev` contains a real, merged Operator Console statutory-discount workflow and a merged payable-basis application path in Central PMS, but it is not yet a complete shared, channel-neutral statutory-discount service.

The merged implementation supports:

- Operator Console statutory-discount draft creation, evidence metadata capture, decision, readback, audit report, policy resolution, and apply-payable-basis endpoints.
- Central PMS persistence against statutory validation, evidence reference, policy reference, and tariff snapshot tables where the database objects exist.
- An applied statutory-adjusted tariff snapshot read path for WebPay/payment creation.
- Payment-attempt guardrails that reject stale original tariff snapshots after an applied statutory-adjusted basis exists.
- POS Server fiscal handoff DTOs and semantic hash participation for finalized discount facts.
- RBAC policy mappings and UI gating for Operator Console statutory-discount actions.

The merged implementation does not yet provide:

- A shared public/channel-neutral validation API for WebPay or APT.
- WebPay collection/evidence/validation UI.
- APT implementation code.
- Management Platform statutory policy registry administration.
- Production-approved local ordinance activation.
- A verified, complete legal source registry.
- A general discount engine for local-ordinance variants, exemptions, caps, residency, driver/passenger conditions, or stacking.

The most important baseline contradiction is that some older Operator Console documents describe one-step operator approval, entitlement fingerprint assumptions, cropped ID-image capture, and direct/statutory service decision flow, while current v1.3 code and documents have moved toward stricter authority boundaries, requester/reviewer segregation, metadata-only evidence, and backend-owned payable-basis application.

## 7. Complete relevant file and component inventory

| Area | Paths | Classification | Current posture |
| --- | --- | --- | --- |
| Central PMS endpoint registration | `src/Services/CentralPms/src/ExitPass.CentralPms.Api/Program.cs` | `MERGED_IMPLEMENTATION` | Registers Operator Console statutory-discount draft, policy-resolution, and policy-import endpoint groups. |
| Operator Console statutory endpoints | `src/Services/CentralPms/src/ExitPass.CentralPms.Api/Endpoints/OperatorConsoleStatutoryDiscountDraftEndpoints.cs`; `OperatorConsoleStatutoryDiscountPolicyResolutionEndpoints.cs`; `OperatorConsoleProductionPolicyImportEndpoints.cs` | `MERGED_IMPLEMENTATION` | Access-gated API surface for drafts, evidence, decisions, audit, policy resolution, payable-basis application, and policy import review. |
| Operator Console statutory application services | `src/Services/CentralPms/src/ExitPass.CentralPms.Application/OperatorConsole/OperatorConsoleStatutoryDiscount*` | `MERGED_IMPLEMENTATION` / `PARTIAL_IMPLEMENTATION` | Implements draft/evidence/decision/read/policy-resolution/apply services. Computation remains narrow and hard-coded to current Senior/PWD VAT-exclusive 20 percent support. |
| Statutory computation contract | `src/Services/CentralPms/src/ExitPass.CentralPms.Application/OperatorConsole/OperatorConsoleStatutoryDiscountComputationContract.cs` | `PARTIAL_IMPLEMENTATION` | Computes VAT-exclusive Senior/PWD discount using 12 percent VAT divisor and 20 percent statutory discount rate. Not a general policy engine. |
| Operator Console repositories/writers | `src/Services/CentralPms/src/ExitPass.CentralPms.Infrastructure/OperatorConsole/OperatorConsoleStatutoryDiscount*` | `MERGED_IMPLEMENTATION` / `PARTIAL_IMPLEMENTATION` | PostgreSQL readers/writers for validations, evidence references, policy resolution, and payable-basis application; uses locks and idempotency where implemented. |
| Policy import dry run/review | `src/Services/CentralPms/src/ExitPass.CentralPms.Application/OperatorConsole/OperatorConsoleProductionPolicyImport*`; `src/Services/CentralPms/src/ExitPass.CentralPms.Infrastructure/OperatorConsole/OperatorConsoleProductionPolicyImportReviewQueueRepository.cs` | `MERGED_IMPLEMENTATION` / `CONTRACT_ONLY` | Dry-run and review queue exist; import approval does not activate policy rows and stops at repository-alignment review state. |
| Operator Console UI | `src/Services/OperatorConsoleUi/src/App.tsx`; `apiClient.ts`; `types.ts`; `App.test.tsx` | `MERGED_IMPLEMENTATION` | Provides UI workflow for session lookup, statutory draft/review/evidence metadata, decisions, apply payable basis, audit, and policy import review. |
| WebPay UI | `src/Services/WebPayUi/src/App.tsx`; `types.ts`; `webpay.ts`; `App.test.tsx`; `webpay.test.ts` | `PARTIAL_IMPLEMENTATION` | Displays readback/status for approved or pending statutory discount effects. No WebPay-side entitlement approval, evidence capture, or authoritative calculation was found. |
| Payment Orchestrator WebPay contracts | `src/Services/PaymentOrchestrator/src/ExitPass.PaymentOrchestrator.Contracts/WebPay/WebPayPaymentIntentRequest.cs`; `WebPayPaymentIntentHandler.cs` | `MERGED_IMPLEMENTATION` | WebPay payment intent expects final approved tariff snapshot and fails closed on stale pre-coupon/pre-statutory payable basis. |
| Tariff snapshot domain/read model | `src/Services/CentralPms/src/ExitPass.CentralPms.Domain/Tariffs/TariffSnapshot.cs`; `TariffSnapshotSourceType.cs`; `TariffSnapshotStatus.cs`; `src/Services/CentralPms/src/ExitPass.CentralPms.Infrastructure/PaymentAttempts/TariffSnapshotReadRepository.cs` | `MERGED_IMPLEMENTATION` | Represents `STATUTORY_ADJUSTED` applied tariff snapshot and rejects invalid/expired/superseded consumed bases. |
| Vendor parking resolution readback | `src/Services/CentralPms/src/ExitPass.CentralPms.Contracts/Public/VendorParking/ResolveVendorParkingResponse.cs`; `src/Services/CentralPms/src/ExitPass.CentralPms.Infrastructure/VendorParking/VendorParkingResolutionPersistence.cs` | `MERGED_IMPLEMENTATION` | Returns statutory discount flags, validation/application IDs, policy resolution basis, and applied payable basis where present. |
| Payment attempt guardrails | `src/Services/CentralPms/src/ExitPass.CentralPms.Application/PaymentAttempts/CreateOrReusePaymentAttemptHandler.cs`; `PaymentAttemptsController.cs`; tests under `CreateOrReusePaymentAttemptHandlerTests.cs` | `MERGED_IMPLEMENTATION` | Rejects stale/original tariff snapshots after an applied statutory-discount basis exists. |
| POS Server fiscal handoff | `src/Services/CentralPms/src/ExitPass.CentralPms.Application/FiscalIssuance/PosServerFiscalDocumentClientModels.cs`; `PosServerFiscalDocumentRequestMapper.cs`; `FiscalSemanticRequestHashCalculator.cs` | `MERGED_IMPLEMENTATION` | Carries payable basis, discount references, discount privilege details, beneficiary/evidence refs, idempotency fields, and semantic hash inputs. |
| RBAC policy mappings | `src/Services/CentralPms/src/ExitPass.CentralPms.Application/Security/CentralPmsRbacPolicyCatalog.cs`; `src/Services/CentralPms/tests/ExitPass.CentralPms.UnitTests/Application/CentralPmsRbacPolicyCatalogTests.cs` | `MERGED_IMPLEMENTATION` | Defines statutory-discount view/create/evidence/decision/apply/policy/audit permissions. |
| Management Platform identity/RBAC inventory | `src/Services/CentralPms/src/ExitPass.CentralPms.Application/ManagementPlatform/ManagementPlatformIdentityRbacInventoryService.cs`; tests | `MERGED_IMPLEMENTATION` | Read-only identity/RBAC inventory includes statutory-discount UAT role/permission context. No statutory policy admin CRUD. |
| Management Platform UI | `src/Services/ManagementPlatformUi` | `ABSENT` for statutory policy admin; `MERGED_IMPLEMENTATION` for Sales Invoice setup | Current UI is Sales Invoice configuration; statutory policy registry/configuration surfaces are not implemented. |
| Database v1.2 baseline | `ExitPass_Full_Database_Creation_DDL_v1.2.sql`; `ExitPass_Reference_Data_v1.2.sql`; `infra/db/seed/ExitPass_Reference_Data_v1.2.sql` | `DATABASE_STRUCTURE_ONLY` / `STALE_OR_SUPERSEDED` for runtime migration posture | Defines statutory tables/enums and seed placeholders, but v1.2 DDL must not be assumed active in current v1.3 runtime. |
| App-local statutory DB patches | `infra/db/patches/ExitPass_StatutoryDiscountPolicyRegistrySchema_v1.2.sql`; `ExitPass_StatutoryDiscountPayableBasisApplicationSchema_v1.2.sql`; `ExitPass_StatutoryDiscountAppliedTariffSnapshotLifecycle_v1.2.sql`; validation SQL files | `DATABASE_STRUCTURE_ONLY` / `PARTIAL_IMPLEMENTATION` | Patch scripts define dedicated registry, payable-basis application, idempotency, locks, and applied tariff lifecycle. Canonical migration pipeline is not proven by `infra/db/migrations`, which contains only `.gitkeep`. |
| Bruno statutory smoke scenarios | `bruno/operator-console-statutory-discount-draft/*` | `TEST_ONLY` | Manual/API scenario coverage for draft, replay, evidence, decision, apply, readback, payment finality, exit authorization, and gate consume boundaries. |
| Operator Console scripts/runbooks | `scripts/operator-console/*StatutoryDiscount*`; `docs/v1.3/operator-console/runbooks/*Statutory*`; `docs/operator-console/*statutory*` | `TEST_ONLY` / `DOCUMENTATION_ONLY` | UAT/preflight/readiness scripts and historical/current design notes. Some older assumptions are stale relative to current v1.3 code. |
| APT references | `docs/v1.3/assisted-payment-terminal/*`; `docs/v1.3/assisted-payment-terminal/diagrams/*` | `DOCUMENTATION_ONLY` | Defines APT as capture/display/payment-capable terminal; no APT code in this repo. |
| POS Server docs | `docs/v1.3/pos-server-api/*`; `docs/v1.3/pos-invoicing/*`; `docs/v1.3/pos-server/*` | `DOCUMENTATION_ONLY` plus Central PMS handoff implementation | POS Server is fiscal issuer and Sales Invoice renderer; not entitlement authority. |
| Legal/ordinance research | `docs/operator-console/Philippine_Parking_Statutory_Discount_Local_Ordinances_Detailed_List.docx`; policy readiness/import docs | `RESEARCH_ONLY` unless specific row has repository proof of official review | Matrix explicitly says it is operational research and not legal opinion. |

## 8. Current end-to-end statutory-discount flow

Current merged flow is Operator Console centered:

1. Operator Console resolves a parking session through Central PMS.
2. Central PMS/Vendor PMS resolution exposes an authoritative tariff snapshot/payable basis.
3. Operator Console creates a statutory-discount draft through `POST /v1/ops/operator-console/statutory-discounts/draft`.
4. Central PMS resolves policy context through the Operator Console statutory policy resolver.
5. Operator Console captures metadata-only evidence through `POST /v1/ops/operator-console/statutory-discounts/{draftId}/evidence`.
6. A reviewer submits approve/reject through `POST /v1/ops/operator-console/statutory-discounts/{draftId}/decision`.
7. If approved and evidence requirements are satisfied, Central PMS applies payable basis through `POST /v1/ops/operator-console/statutory-discounts/{validationId}/apply-payable-basis`.
8. The apply writer creates or reuses a statutory payable-basis application and applied `core.tariff_snapshots` row, superseding the original active snapshot where appropriate.
9. WebPay/vendor session readback can show the effective statutory-adjusted payable basis.
10. Payment creation uses the effective approved tariff snapshot and rejects stale pre-discount snapshot IDs.
11. POS Server fiscal issuance receives finalized payable and discount facts from Central PMS after payment finality.

No merged WebPay or APT collection flow was found.

## 9. Authority-boundary assessment

| Boundary | Assessment | Evidence |
| --- | --- | --- |
| Vendor PMS/HikCentral owns raw session lifecycle and normal live tariff computation | Supported | v1.3 system design review states Vendor PMS/HCP remains authority for raw parking session lifecycle and normal tariff computation. APT docs route session/payable-basis lookup through Central PMS/Vendor PMS. |
| Central PMS/Discount workflow owns policy resolution, validation, approved computation, payable-basis effect, evidence refs, and audit | Partially supported | Operator Console statutory endpoints/services are implemented in Central PMS. The implementation is Operator Console scoped and not channel-neutral. |
| WebPay and APT collect/display, not approve/calculate | Supported for WebPay and documented for APT | WebPay has no validation request UI/API call and displays backend results only. APT BRD says the terminal must not independently approve statutory entitlement or mutate payable basis directly. |
| Operator Console provides controlled workflow/governance | Supported, with stale older docs | Current code provides workflow; v1.3 BRD positions Operator Console as review/governance. Older docs still mention one-step operator approval and cropped image evidence as MVP patterns. |
| POS Server fiscalizes finalized facts only | Supported by docs and Central PMS handoff contracts | POS/Invoicing BRD says Central PMS/Discount workflow remains policy/validation/payable-basis authority and POS Server owns fiscal treatment on Sales Invoice/reports. |
| No statutory component marks payment final, issues ExitAuthorization, or controls the gate | Supported by endpoint descriptions and smoke scenarios | Operator Console endpoint descriptions and Bruno collection state no payment provider, finality, ExitAuthorization, gate, coupon, or reconciliation mutation. |

No merged code path was found where WebPay, APT, or POS Server independently approves entitlement or calculates the authoritative statutory discount.

## 10. Central PMS assessment

Classification: `PARTIAL_IMPLEMENTATION`.

Central PMS has real merged implementation for Operator Console statutory-discount operations:

- API routes under `/v1/ops/operator-console`.
- DTOs in `src/Services/CentralPms/src/ExitPass.CentralPms.Contracts/OperatorConsole`.
- Application services and models under `Application/OperatorConsole`.
- PostgreSQL readers/writers under `Infrastructure/OperatorConsole`.
- Access/RBAC policy mapping in `CentralPmsRbacPolicyCatalog`.
- Unit tests for draft, evidence, decision, read, policy resolution, payable-basis application, computation, RBAC, Management Platform RBAC inventory, payment attempts, and fiscal handoff.

Central PMS does not yet have a complete shared, channel-neutral statutory-discount capability:

- Route names and services are Operator Console scoped.
- The computation contract is not a general rule engine.
- Local ordinance policy resolution exists, but payable-basis computation remains constrained to VAT-exclusive Senior/PWD 20 percent behavior.
- No public WebPay/APT statutory validation endpoint was found.
- No shared Discount Service endpoint or contract was found outside Operator Console naming.
- Outbox publication for payable-basis application remains a documented/open item rather than verified merged behavior.

## 11. Operator Console assessment

Classification: `MERGED_IMPLEMENTATION` for the current workflow, with `PARTIAL_IMPLEMENTATION` and `STALE_OR_SUPERSEDED` documentation areas.

Current implementation supports:

- Session lookup prerequisite through Operator Console access/session APIs.
- Draft initiation.
- Beneficiary/entitlement facts in draft DTOs/models.
- Metadata-only evidence references.
- Operator/reviewer attestation fields.
- Approve/reject decision path.
- Reviewer/apply actor segregation in UI and tests.
- Payable-basis application request.
- Audit/report readback with safe/masked fields.
- RBAC permission gating.
- Site/device/shift/action access evaluation through Central PMS access evaluation.
- Policy resolution and policy import dry-run/review UI/API.

Current implementation does not support:

- Raw evidence upload or raw ID image storage.
- Full ID-number exposure.
- WebPay-side statutory request initiation.
- Supervisor override as a distinct implemented statutory entitlement override endpoint.
- Production policy activation from import review.
- A complete policy-admin UI.

Older Operator Console docs contain stale or only partially approved assumptions:

- `docs/operator-console/statutory-validation-and-access-contract.md` says the MVP uses one-step operator approval and describes supervisor review/override as later scope.
- The same document contains an example `cropped_id_image` evidence reference and entitlement fingerprint behavior.
- `docs/operator-console/operator-console-schema-extension-design.md` describes entitlement fingerprint storage and cropped image paths.
- Current UI and endpoint descriptions instead emphasize metadata-only evidence and "do not upload or enter raw ID numbers".
- Current UAT runbook uses a requester profile and a reviewer/apply profile, which is stricter than direct one-step operator approval.

These older statements should not be treated as current production approval.

## 12. WebPay assessment

Classification: `PARTIAL_IMPLEMENTATION`.

Merged WebPay capabilities found:

- Displays backend statutory discount status/amounts in the parking session summary where Central PMS returns them.
- Shows pending review / no approved statutory discount status.
- Tests verify WebPay does not expose a statutory-discount request button and does not call `/v1/public/discounts/statutory/validate`.
- Payment Orchestrator WebPay intent contracts expect the final approved tariff snapshot and fail closed on stale pre-discount basis.

Absent WebPay capabilities:

- Statutory-discount collection UI.
- Beneficiary input.
- Evidence input.
- API client for statutory validation submission.
- Local approval or authoritative calculation.
- Retry flow for statutory validation requests.

WebPay currently preserves the intended boundary: it does not independently approve entitlement or calculate the authoritative statutory discount.

## 13. APT contract and documentation assessment

Classification: `DOCUMENTATION_ONLY`.

No APT implementation code was inspected in this repository. The user explicitly prohibited access to `D:\SourceCodes\ExitPass-APT` and `D:\SourceCodes\ExitPass-APT-NewVersion`.

Relevant in-repo APT documents state:

- APT supports cashier-facing statutory-discount validation capture.
- APT submits entitlement details, evidence, and attestation to Central PMS/Discount workflow.
- APT receives refreshed payable basis from Central PMS.
- APT must not independently approve statutory entitlement.
- APT must not bypass Central PMS/Discount workflow.
- APT must not mutate payable basis directly.
- APT must not issue ExitAuthorization or open gates.
- Continuity-mode statutory handling must fail closed or route to supervisor/manual review when policy, evidence, projection freshness, or payable basis is unsafe.

No document in the inspected APT set was found that gives APT authoritative legal interpretation, entitlement approval, or calculation authority.

## 14. POS Server fiscal-handoff assessment

Classification: `MERGED_IMPLEMENTATION` for Central PMS handoff DTO/hash participation; `DOCUMENTATION_ONLY` for POS Server behavior inside this repository.

Central PMS contains POS Server fiscal handoff models with:

- Payable basis reference and amount.
- Discount references with `DiscountValidationRef`, status, and `AppliesStatutoryDiscountTreatment`.
- Discount privilege details with discount type code, line sequence, discount amount, VAT privilege amount, basis amount, currency, beneficiary reference, evidence reference, approval reference, and context dictionary.
- Idempotency fields and semantic request hash fields.
- Semantic hash canonicalization that includes payable basis discount references and discount privilege detail facts.

POS/Invoicing documents define:

- Sales Invoice as primary parking fiscal output.
- POS Server as fiscal issuance authority.
- Central PMS/Discount workflow as statutory policy resolution, validation persistence, and payable-basis update authority.
- Senior Citizen and PWD as immediate statutory entitlement workflows for fiscal support.
- NAAC and Solo Parent as future-supported fiscal-report categories.
- Diplomat VAT Privilege / VAT Exemption as an active VAT privilege/exemption category, not ordinary commercial discount.
- Local ordinance production application requiring official ordinance/policy review.

No read-only access to `D:\SourceCodes\ExitPass-PoSServer` was needed because the required fiscal handoff contracts and POS boundary documentation were verifiable inside the ExitPass repository.

## 15. Management Platform and configuration assessment

Classification: `ABSENT` for statutory policy administration; `MERGED_IMPLEMENTATION` for identity/RBAC inventory and Sales Invoice configuration surfaces.

Implemented:

- Management Platform UI for Sales Invoice configuration and effective readiness.
- Central PMS identity/RBAC inventory service exposing role/permission posture, including statutory-discount UAT compatibility.

Documented but not implemented:

- Policy registry administration.
- Jurisdiction/Site/Site Group applicability admin UI.
- Effective-dated statutory policy CRUD.
- Beneficiary type/residency/driver-passenger condition configuration.
- Initial free period, full exemption, capped exemption, overnight/valet/standalone parking exclusions configuration UI.
- Evidence requirement configuration UI.
- Legal verification status and approval status workflow.
- Maker-checker activation/suspension/supersession controls for statutory policies.
- Audit provenance and activation history for statutory policy admin.

The policy import review surface exists in Operator Console as a temporary governed route, but it does not activate production rules.

## 16. Database and migration assessment

Classification: `DATABASE_STRUCTURE_ONLY` plus `PARTIAL_IMPLEMENTATION` where current writers consume the objects.

Relevant database objects found:

- `discounts.discount_policy_references`
- `discounts.statutory_discount_validations`
- `discounts.discount_evidence_references`
- `core.tariff_snapshots`
- `discounts.statutory_discount_payable_basis_applications`
- `discounts.statutory_discount_policy_registry`
- applied tariff snapshot lifecycle patch objects, including unique applied-snapshot constraints and lock-based routine logic.

Relevant fields found:

- `statutory_discount_validation_id`
- `applied_policy_reference_id`
- `fallback_policy_reference_id`
- `policy_resolution_basis`
- `local_ordinance_applied`
- `gross_amount_at_validation`
- `statutory_discount_amount`
- `net_amount_after_discount`
- evidence classifications and retention fields
- effective periods, policy level/type/status, jurisdiction/source fields
- row version / lifecycle fields
- idempotency keys and uniqueness in payable-basis application patch validation

Important alignment finding:

- Historical v1.2 DDL and seed files define baseline statutory structures and placeholder policies.
- App-local v1.2 patch files define dedicated registry and payable-basis lifecycle additions.
- `infra/db/migrations` does not show an executable migration sequence for these statutory objects; it contains only `.gitkeep`.
- Current Central PMS writers assume the objects exist in the target database.
- Therefore the database layer is not proven as a canonical current v1.3 migration pipeline from repository evidence alone.

The v1.2 seed contains national fallback and development local policy placeholders. Development placeholders must not be treated as production-approved legal rules.

## 17. API and contract assessment

Classification: `PARTIAL_IMPLEMENTATION`.

Merged statutory-discount API surface:

- `GET /v1/ops/operator-console/statutory-discounts/drafts`
- `GET /v1/ops/operator-console/statutory-discounts/drafts/{draftId}`
- `GET /v1/ops/operator-console/audit/statutory-discounts`
- `POST /v1/ops/operator-console/statutory-discounts/draft`
- `POST /v1/ops/operator-console/statutory-discounts/{draftId}/decision`
- `POST /v1/ops/operator-console/statutory-discounts/{draftId}/evidence`
- `GET /v1/ops/operator-console/statutory-discounts/{draftId}/evidence`
- `POST /v1/ops/operator-console/statutory-discounts/{validationId}/apply-payable-basis`
- `POST /v1/ops/operator-console/statutory-discounts/resolve-policy`
- policy import dry-run/review endpoints under `/v1/ops/operator-console/statutory-discounts/policies/import/*`

Contract posture:

- Operator Console DTOs are implemented.
- POS Server fiscal handoff DTOs include discount facts.
- WebPay response/readback contracts include statutory discount status and applied payable basis fields.
- APT contracts are documentation-only in this repository.
- No public/channel-neutral statutory validation contract was found.
- No Management Platform statutory policy admin API was found.

## 18. Idempotency, replay, semantic-conflict, and concurrency assessment

Classification: `PARTIAL_IMPLEMENTATION`.

Implemented/verified by source:

- Operator Console draft, evidence, decision, policy resolution, and payable-basis services require idempotency keys.
- Payable-basis writer uses row locking and idempotency-aware persistence.
- Applied tariff snapshot lifecycle patch uses `FOR UPDATE` lock posture and uniqueness constraints.
- Bruno scenarios cover duplicate draft replay, duplicate evidence metadata replay, apply replay, no duplicate application row, no duplicate applied tariff snapshot, payment attempt replay, exit authorization replay, and gate consume duplicate boundaries.
- Fiscal semantic request hash includes payable basis, discount references, and discount privilege details.
- POS fiscal idempotency source is tied to payable-basis upstream finality reference in Central PMS fiscal readiness code.

Gaps/not verifiable:

- A full shared channel-neutral semantic conflict contract for statutory validation requests was not found.
- WebPay/APT statutory validation retry/replay behavior is absent because those channel submission flows do not exist.
- Outbox behavior for statutory payable-basis application is documented as open/not yet verified.
- Canonical DB migration installation of all uniqueness and concurrency constraints is not proven by `infra/db/migrations`.

## 19. Security, privacy, evidence, and retention assessment

Classification: `PARTIAL_IMPLEMENTATION`.

Supported:

- Current Operator Console UI says raw ID numbers and raw evidence files are not displayed.
- Evidence capture UI says metadata-only evidence capture and "Do not upload or enter raw ID numbers."
- Endpoint descriptions say evidence APIs do not return raw evidence, OCR data, raw ID numbers, or document verification results.
- Evidence references include masked reference number support.
- POS/Operator Console documents require evidence minimization and retention policy separation.
- APT documents say the terminal must not retain unmanaged entitlement evidence outside approved workflows.

Unresolved:

- Exact evidence retention periods by jurisdiction/policy remain open.
- Raw/cropped ID-image capture appears in older documents as configurable, but current implementation does not store raw bytes.
- Official evidence storage owner, redaction, export, purge, and retention workflow remain governed/documented items, not fully implemented statutory policy administration.
- Entitlement fingerprint storage is documented historically, but current table evidence and docs caution that no dedicated `entitlement_fingerprint` column exists on `discounts.statutory_discount_validations`.

## 20. Legal and ordinance source classification

| Source or source family | Classification | Basis |
| --- | --- | --- |
| RA 9994 Senior Citizen national fallback references in policy resolver/seed | `NOT_DETERMINABLE` for legal approval; `CONTRACT_ONLY` / `DATABASE_STRUCTURE_ONLY` for repository usage | Repository contains fallback code/seed references but this audit found no separate approved legal interpretation artifact. |
| RA 10754 PWD national fallback references in policy resolver/seed | `NOT_DETERMINABLE` for legal approval; `CONTRACT_ONLY` / `DATABASE_STRUCTURE_ONLY` for repository usage | Same posture as RA 9994. |
| `Philippine_Parking_Statutory_Discount_Local_Ordinances_Detailed_List.docx` | `RESEARCH_LEAD` / `UNVERIFIED` | The document explicitly says it is operational research, not legal opinion, and official ordinance text must be obtained/reviewed before production configuration. |
| Local ordinance rows in v1.2 seed and policy docs marked development/test/sandbox | `RESEARCH_ONLY` / `UNVERIFIED` / `STALE_OR_SUPERSEDED` for production use | Development placeholders and sandbox rows are not production authority. |
| Production policy import CSV/review flow | `CONTRACT_ONLY` / `PARTIAL_IMPLEMENTATION` | Dry-run/review validates source metadata and review decisions but does not insert/activate production policy rows. |
| POS/Invoicing BIR RMO No. 10-2019 Diplomat references | `SECONDARY_SOURCE_ONLY` / `NOT_DETERMINABLE` for final implementation | Docs identify a fiscal input and open questions; exact treatment remains open for BIR/accounting confirmation. |

No new legal research was performed. No local ordinance should be activated from public summaries, social posts, ordinance indexes, or the research matrix without official source review and approval.

## 21. Cross-channel compatibility matrix

| Channel/component | Current capability | Compatibility with authority model | Classification |
| --- | --- | --- | --- |
| Central PMS Operator Console workflow | Draft/evidence/decision/apply/audit/policy resolution | Compatible but Operator Console scoped | `PARTIAL_IMPLEMENTATION` |
| Shared Discount Service | Not found as channel-neutral API/service | Missing | `ABSENT` |
| WebPay | Readback/display and stale payable-basis guardrails | Compatible; no independent approval/calculation | `PARTIAL_IMPLEMENTATION` |
| APT | Documentation-only capture/display expectations | Compatible in docs | `DOCUMENTATION_ONLY` |
| POS Server handoff | Central PMS request models and fiscal semantic hash include discount facts | Compatible; POS remains fiscal authority | `MERGED_IMPLEMENTATION` |
| Operator Console UI | Controlled workflow and governance surface | Compatible, with stale older docs to retire/qualify | `MERGED_IMPLEMENTATION` |
| Management Platform | Sales Invoice config and RBAC inventory only | Statutory policy admin missing | `ABSENT` for statutory config |
| Vendor PMS/HikCentral | Normal tariff/session authority in docs/code integration | Compatible | `MERGED_IMPLEMENTATION` outside discount engine |
| Gate/ExitAuthorization | No statutory component control found | Compatible | `MERGED_IMPLEMENTATION` outside discount engine |

## 22. Contradictions and stale assumptions

1. Older Operator Console contract describes one-step operator approval; current v1.3 UI/UAT posture uses requester/reviewer segregation and current documents define supervisor review where policy allows.
2. Older evidence examples include `cropped_id_image`; current UI/endpoints emphasize metadata-only evidence and no raw ID/evidence bytes.
3. Older documents discuss entitlement fingerprint generation/storage; current documentation cautions that v1.2 DDL lacks a dedicated `entitlement_fingerprint` column on statutory validations.
4. Payable-basis design docs originally said no implemented routine existed; current code and patches now include an apply-payable-basis writer and applied tariff snapshot lifecycle.
5. Policy resolution docs state apply-payable-basis should consume resolved policy snapshot fields; current computation contract remains fixed to the supported Senior/PWD VAT-exclusive 20 percent calculation.
6. Local ordinance research lists contain production-candidate language, but the same document says official ordinance text must be reviewed before production configuration.
7. Management Platform target docs describe policy/admin surfaces, but current Management Platform UI does not implement statutory policy administration.

## 23. Missing capabilities and risks

Missing or unresolved capabilities:

- Shared channel-neutral statutory validation API.
- Policy-snapshot-driven calculation for local ordinance variants.
- Production-approved legal source registry.
- Management Platform statutory policy admin and maker-checker activation workflow.
- WebPay statutory beneficiary/evidence collection and validation request flow.
- APT implementation contracts/API clients.
- Explicit coupon/statutory stacking and exclusivity enforcement beyond documented boundaries.
- General handling for multiple eligible beneficiaries, shared transactions, caps, full exemptions, initial free periods, residency, non-residency, driver/passenger conditions, overnight/valet/standalone parking exclusions, and facility restrictions.
- Canonical executable migration proof for all statutory objects.
- Statutory validation semantic conflict contract shared across channels.
- Approved evidence retention matrix.

Main risks:

- A channel-neutral capability could be incorrectly inferred from Operator Console-scoped endpoints.
- Hard-coded VAT-exclusive 20 percent computation could be misapplied to local ordinance benefits that require free initial periods, caps, full exemptions, or exclusions.
- Development/sandbox policy rows could be mistaken for production authority.
- Older cropped-image/fingerprint assumptions could lead to over-collection of sensitive evidence.
- Fiscal handoff can carry discount facts, but fiscal correctness still depends on upstream approved discount facts and unresolved BIR/accounting tax-treatment decisions.

## 24. Ranked findings

### BLOCKER

| ID | Finding | Classification | Evidence |
| --- | --- | --- | --- |
| B-01 | No repository evidence proves production-approved local ordinance rules; local ordinance research must remain research-only. | `RESEARCH_ONLY` / `UNVERIFIED` | Local ordinance DOCX caveat says official ordinance text must be obtained/reviewed before production configuration. |
| B-02 | No shared channel-neutral statutory validation API/service exists for WebPay/APT/Operator Console parity. | `ABSENT` | Existing routes are under `/v1/ops/operator-console`; WebPay tests verify no public statutory validation call. |
| B-03 | Canonical current v1.3 migration path for statutory structures is not proven by `infra/db/migrations`. | `DATABASE_STRUCTURE_ONLY` / `NOT_VERIFIABLE` | Statutory structures exist in v1.2 DDL/patch files; migrations folder has no active sequence. |

### HIGH

| ID | Finding | Classification | Evidence |
| --- | --- | --- | --- |
| H-01 | Payable-basis computation remains hard-coded to current Senior/PWD VAT-exclusive 20 percent behavior, not a general policy-snapshot engine. | `PARTIAL_IMPLEMENTATION` | `OperatorConsoleStatutoryDiscountComputationContract.cs`; policy resolution docs say apply should use resolved policy snapshot. |
| H-02 | Operator Console implementation is real but scoped; treating it as a shared Discount Service would create channel inconsistency. | `PARTIAL_IMPLEMENTATION` | Operator Console route/service names and UI only. |
| H-03 | Management Platform statutory policy admin, maker-checker activation, suspension, and supersession are absent. | `ABSENT` | Management Platform UI currently implements Sales Invoice setup; docs identify policy admin as target/gap. |
| H-04 | Older one-step approval/cropped-ID/fingerprint documents can mislead future implementation if not qualified as stale or superseded. | `STALE_OR_SUPERSEDED` / `CONFLICTING` | `docs/operator-console/statutory-validation-and-access-contract.md`; current UI/endpoints use metadata-only evidence and requester/reviewer separation. |

### MEDIUM

| ID | Finding | Classification | Evidence |
| --- | --- | --- | --- |
| M-01 | POS fiscal handoff facts are structurally present, but exact VAT/tax treatment remains open. | `PARTIAL_IMPLEMENTATION` | POS/Invoicing BRD open questions for VAT/tax treatment; semantic hash includes discount facts. |
| M-02 | WebPay readback is present, but collection and validation submission are absent. | `PARTIAL_IMPLEMENTATION` / `ABSENT` | WebPay UI/tests display status and do not call public validation endpoint. |
| M-03 | APT docs are compatible, but implementation contracts/code are absent in this repo. | `DOCUMENTATION_ONLY` | APT BRD/diagrams only. |
| M-04 | Outbox/audit event publication for payable-basis application is not verified. | `NOT_VERIFIABLE` | Design docs list outbox/audit publication as open/later item. |

### LOW

| ID | Finding | Classification | Evidence |
| --- | --- | --- | --- |
| L-01 | Policy import review endpoint lives under Operator Console although Management Platform is the target admin surface. | `PARTIAL_IMPLEMENTATION` | Management Platform audit treats Operator Console route as temporary acceptable route. |
| L-02 | Bruno/manual scenarios are extensive but remain manual/test-only coverage. | `TEST_ONLY` | `bruno/operator-console-statutory-discount-draft/*`. |
| L-03 | Primary repository local WebPay receipt presentation work exists but is not merged statutory-discount implementation. | `IN_FLIGHT_OR_LOCAL_ONLY` | Primary repo status shows uncommitted WebPay receipt/Sales Invoice presentation files. |

## 25. Exact smallest recommended next implementation slice

The smallest correct next implementation slice is Central PMS only:

Create a shared, channel-neutral statutory-discount validation/application contract and internal application-service boundary that reuses the existing Operator Console policy-resolution, validation, evidence-reference, and payable-basis application components, but fails closed unless the resolved policy snapshot is verified and supported by the current computation contract.

The slice should not add new rules. It should first make the existing merged Operator Console path consume a single shared policy-snapshot-driven computation/application boundary and expose a channel-neutral contract shape that WebPay and APT can later call. It should explicitly preserve the existing prohibitions on payment finality, fiscal issuance, ExitAuthorization, gate control, raw evidence storage, and ordinance activation.

Acceptance for that slice should include:

- One shared request/response model for validation/apply readback facts.
- Explicit unsupported-policy and unverified-policy failures.
- Deterministic semantic request hash/conflict posture for validation requests.
- Preservation of existing Operator Console behavior through the shared service.
- No WebPay/APT UI changes.
- No new local ordinance seed data.
- No POS Server entitlement logic.

## 26. Explicit items that must not be implemented yet

Do not implement yet:

- Production activation of any local ordinance.
- Local ordinance seed data based on research-only sources.
- WebPay beneficiary/evidence UI.
- APT implementation or offline statutory-discount handling.
- POS Server entitlement approval, policy resolution, or authoritative discount calculation.
- Generic coupon/statutory stacking behavior without approved precedence rules.
- Raw ID-image storage or full ID-number exposure.
- Management Platform policy activation without maker-checker/legal approval design.
- Multiple-beneficiary/shared-transaction allocation.
- Caps, full exemptions, initial free periods, or exclusions without approved source and policy model.
- New fiscal/tax conclusions.
- Payment finality, ExitAuthorization, or gate behavior changes.

## 27. Manual verification requirements

Before production or broad UAT:

- Verify canonical database migration state in the actual aligned v1.3 database, not only v1.2 DDL/patch files.
- Confirm every active policy row has approved official/legal source evidence.
- Run the full Operator Console statutory discount aligned DB UAT preflight against a disposable or approved UAT database.
- Verify requester/reviewer segregation and RBAC in a real identity/shift/device context.
- Verify no raw evidence or full ID values are logged, returned, or stored.
- Verify POS Server Sales Invoice rendering with finalized discount facts in the actual POS Server implementation.
- Verify WebPay payable-basis readback against an applied statutory-adjusted tariff snapshot.
- Confirm BIR/accounting treatment for statutory discount, VAT privilege/exemption, and Sales Invoice presentation fields.

## 28. Evidence appendix

### Central PMS endpoints and services

- `src/Services/CentralPms/src/ExitPass.CentralPms.Api/Program.cs`: maps Operator Console statutory-discount draft, policy-resolution, and policy-import endpoints.
- `src/Services/CentralPms/src/ExitPass.CentralPms.Api/Endpoints/OperatorConsoleStatutoryDiscountDraftEndpoints.cs:46`: `GET /statutory-discounts/drafts`.
- `src/Services/CentralPms/src/ExitPass.CentralPms.Api/Endpoints/OperatorConsoleStatutoryDiscountDraftEndpoints.cs:67`: `GET /audit/statutory-discounts`.
- `src/Services/CentralPms/src/ExitPass.CentralPms.Api/Endpoints/OperatorConsoleStatutoryDiscountDraftEndpoints.cs:78`: `POST /statutory-discounts/draft`.
- `src/Services/CentralPms/src/ExitPass.CentralPms.Api/Endpoints/OperatorConsoleStatutoryDiscountDraftEndpoints.cs:90`: `POST /statutory-discounts/{draftId}/decision`.
- `src/Services/CentralPms/src/ExitPass.CentralPms.Api/Endpoints/OperatorConsoleStatutoryDiscountDraftEndpoints.cs:103`: `POST /statutory-discounts/{draftId}/evidence`.
- `src/Services/CentralPms/src/ExitPass.CentralPms.Api/Endpoints/OperatorConsoleStatutoryDiscountDraftEndpoints.cs:115`: `GET /statutory-discounts/{draftId}/evidence`.
- `src/Services/CentralPms/src/ExitPass.CentralPms.Api/Endpoints/OperatorConsoleStatutoryDiscountDraftEndpoints.cs:126`: `POST /statutory-discounts/{validationId}/apply-payable-basis`.
- `src/Services/CentralPms/src/ExitPass.CentralPms.Api/Endpoints/OperatorConsoleStatutoryDiscountPolicyResolutionEndpoints.cs:31`: `POST /statutory-discounts/resolve-policy`.
- `src/Services/CentralPms/src/ExitPass.CentralPms.Application/OperatorConsole/OperatorConsoleStatutoryDiscountComputationContract.cs:15`: VAT-exclusive computation contract summary.
- `src/Services/CentralPms/src/ExitPass.CentralPms.Application/OperatorConsole/OperatorConsoleStatutoryDiscountComputationContract.cs:42`: VAT-exclusive calculation.
- `src/Services/CentralPms/src/ExitPass.CentralPms.Application/OperatorConsole/OperatorConsoleStatutoryDiscountComputationContract.cs:48`: 20 percent statutory discount calculation.
- `src/Services/CentralPms/src/ExitPass.CentralPms.Application/OperatorConsole/OperatorConsoleStatutoryDiscountDraftService.cs:244`: idempotency key required.
- `src/Services/CentralPms/src/ExitPass.CentralPms.Application/OperatorConsole/OperatorConsoleStatutoryDiscountDecisionService.cs:126`: idempotency key required.
- `src/Services/CentralPms/src/ExitPass.CentralPms.Application/OperatorConsole/OperatorConsoleStatutoryDiscountEvidenceService.cs:188`: idempotency key required.
- `src/Services/CentralPms/src/ExitPass.CentralPms.Application/OperatorConsole/OperatorConsoleStatutoryDiscountApplyPayableBasisService.cs:148`: idempotency key required.

### Policy resolution and import

- `src/Services/CentralPms/src/ExitPass.CentralPms.Infrastructure/OperatorConsole/OperatorConsoleStatutoryDiscountPolicyResolutionReadRepository.cs:20`: Senior national fallback code.
- `src/Services/CentralPms/src/ExitPass.CentralPms.Infrastructure/OperatorConsole/OperatorConsoleStatutoryDiscountPolicyResolutionReadRepository.cs:21`: PWD national fallback code.
- `src/Services/CentralPms/src/ExitPass.CentralPms.Infrastructure/OperatorConsole/OperatorConsoleStatutoryDiscountPolicyResolutionReadRepository.cs:91`: unverified policy error posture.
- `src/Services/CentralPms/src/ExitPass.CentralPms.Application/OperatorConsole/OperatorConsolePolicyReadinessClassifier.cs:64`: configured-but-unverified classifier.
- `src/Services/CentralPms/src/ExitPass.CentralPms.Application/OperatorConsole/OperatorConsoleProductionPolicyImportService.cs:517`: import candidate columns start.
- `src/Services/CentralPms/src/ExitPass.CentralPms.Application/OperatorConsole/OperatorConsoleProductionPolicyImportService.cs:529`: free-duration column.
- `src/Services/CentralPms/src/ExitPass.CentralPms.Application/OperatorConsole/OperatorConsoleProductionPolicyImportService.cs:532`: overnight exclusion column.
- `src/Services/CentralPms/src/ExitPass.CentralPms.Application/OperatorConsole/OperatorConsoleProductionPolicyImportService.cs:535`: driver/passenger required column.
- `src/Services/CentralPms/src/ExitPass.CentralPms.Application/OperatorConsole/OperatorConsoleProductionPolicyImportService.cs:536`: beneficiary residency scope column.
- `src/Services/CentralPms/src/ExitPass.CentralPms.Application/OperatorConsole/OperatorConsoleProductionPolicyImportReviewService.cs:281`: review hash computation.

### Database objects

- `ExitPass_Full_Database_Creation_DDL_v1.2.sql`: historical v1.2 baseline.
- `ExitPass_Reference_Data_v1.2.sql`: historical v1.2 reference seed.
- `infra/db/seed/ExitPass_Reference_Data_v1.2.sql:722`: `discounts.discount_policy_references` seed block.
- `infra/db/patches/ExitPass_StatutoryDiscountPolicyRegistrySchema_v1.2.sql`: dedicated policy registry patch.
- `infra/db/patches/ExitPass_StatutoryDiscountPayableBasisApplicationSchema_v1.2.sql`: payable-basis application patch.
- `infra/db/patches/ExitPass_StatutoryDiscountAppliedTariffSnapshotLifecycle_v1.2.sql`: applied tariff snapshot lifecycle patch.
- `infra/db/patches/validation/Validate_StatutoryDiscountPayableBasisApplicationSchema_v1.2.sql:101`: idempotency key validation.
- `infra/db/patches/validation/Validate_StatutoryDiscountPayableBasisApplicationSchema_v1.2.sql:166`: payable-basis idempotency unique index validation.
- `infra/db/migrations/.gitkeep`: no active migration sequence was found in the migrations folder.

### Tariff, WebPay, and payment basis

- `src/Services/CentralPms/src/ExitPass.CentralPms.Domain/Tariffs/TariffSnapshotSourceType.cs:14`: statutory adjusted tariff source type.
- `src/Services/CentralPms/src/ExitPass.CentralPms.Domain/Tariffs/TariffSnapshot.cs:32`: statutory discount amount in quote.
- `src/Services/CentralPms/src/ExitPass.CentralPms.Infrastructure/PaymentAttempts/TariffSnapshotReadRepository.cs`: effective applied statutory-adjusted snapshot read model.
- `src/Services/CentralPms/src/ExitPass.CentralPms.Contracts/Public/VendorParking/ResolveVendorParkingResponse.cs:96`: statutory discount applied response field.
- `src/Services/CentralPms/src/ExitPass.CentralPms.Contracts/Public/VendorParking/ResolveVendorParkingResponse.cs:101`: statutory discount validation ID response field.
- `src/Services/CentralPms/src/ExitPass.CentralPms.Contracts/Public/VendorParking/ResolveVendorParkingResponse.cs:124`: policy resolution basis response field.
- `src/Services/PaymentOrchestrator/src/ExitPass.PaymentOrchestrator.Contracts/WebPay/WebPayPaymentIntentRequest.cs:44`: final approved tariff snapshot expectation.
- `src/Services/WebPayUi/src/App.test.tsx:211`: WebPay pending statutory validation test area.
- `src/Services/WebPayUi/src/App.test.tsx:226`: WebPay does not call public statutory validate route.

### POS fiscal handoff

- `src/Services/CentralPms/src/ExitPass.CentralPms.Application/FiscalIssuance/PosServerFiscalDocumentClientModels.cs:47`: payable basis context.
- `src/Services/CentralPms/src/ExitPass.CentralPms.Application/FiscalIssuance/PosServerFiscalDocumentClientModels.cs:55`: discount reference context.
- `src/Services/CentralPms/src/ExitPass.CentralPms.Application/FiscalIssuance/PosServerFiscalDocumentClientModels.cs:96`: discount privilege detail context.
- `src/Services/CentralPms/src/ExitPass.CentralPms.Application/FiscalIssuance/PosServerFiscalDocumentClientModels.cs:103`: beneficiary reference.
- `src/Services/CentralPms/src/ExitPass.CentralPms.Application/FiscalIssuance/PosServerFiscalDocumentClientModels.cs:104`: evidence reference.
- `src/Services/CentralPms/src/ExitPass.CentralPms.Application/FiscalIssuance/PosServerFiscalDocumentClientModels.cs:246`: idempotency fields.
- `src/Services/CentralPms/src/ExitPass.CentralPms.Application/FiscalIssuance/PosServerFiscalDocumentClientModels.cs:249`: semantic request hash field.
- `src/Services/CentralPms/src/ExitPass.CentralPms.Application/FiscalIssuance/FiscalSemanticRequestHashCalculator.cs:78`: payable basis ref required for semantic source.
- `src/Services/CentralPms/src/ExitPass.CentralPms.Application/FiscalIssuance/FiscalSemanticRequestHashCalculator.cs:156`: discount references written into hash source.
- `src/Services/CentralPms/src/ExitPass.CentralPms.Application/FiscalIssuance/FiscalSemanticRequestHashCalculator.cs:242`: discount privilege details written into hash source.

### Operator Console UI and docs

- `src/Services/OperatorConsoleUi/src/App.tsx:317`: raw ID numbers and raw evidence files not displayed.
- `src/Services/OperatorConsoleUi/src/App.tsx:4003`: metadata-only evidence notice.
- `src/Services/OperatorConsoleUi/src/App.tsx:4022`: raw ID or evidence bytes must not be entered/uploaded.
- `src/Services/OperatorConsoleUi/src/App.tsx:4235`: VAT-exclusive payable-basis display.
- `src/Services/OperatorConsoleUi/src/apiClient.ts:696`: create draft API client route.
- `src/Services/OperatorConsoleUi/src/apiClient.ts:757`: capture evidence API client route.
- `src/Services/OperatorConsoleUi/src/apiClient.ts:797`: decision API client route.
- `src/Services/OperatorConsoleUi/src/apiClient.ts:829`: apply payable basis API client route.
- `src/Services/OperatorConsoleUi/src/App.test.tsx:622`: raw ID/evidence file display test.
- `src/Services/OperatorConsoleUi/src/App.test.tsx:903`: no raw ID entry/upload notice test.
- `docs/operator-console/statutory-validation-and-access-contract.md:21`: one-step operator approval MVP statement.
- `docs/operator-console/statutory-validation-and-access-contract.md:23`: image capture/fingerprint/cropped ID statement.
- `docs/operator-console/statutory-validation-and-access-contract.md:302`: cropped ID image example.
- `docs/operator-console/statutory-validation-and-access-contract.md:491`: frontend must not generate entitlement fingerprint.
- `docs/operator-console/statutory-validation-and-access-contract.md:493`: no dedicated entitlement fingerprint column.
- `docs/operator-console/statutory-validation-and-access-contract.md:497`: payable-basis update backend boundary.
- `docs/operator-console/statutory-discount-jurisdiction-policy-resolution-design.md:46`: fixed national computation gap statement.
- `docs/operator-console/statutory-discount-jurisdiction-policy-resolution-design.md:482`: payable-basis application should use resolved policy snapshot.
- `docs/operator-console/statutory-discount-applied-tariff-snapshot-lifecycle-design.md:37`: fail closed if payment attempt exists.
- `docs/operator-console/statutory-discount-applied-tariff-snapshot-lifecycle-design.md:669`: outbox/audit event open item.

### APT and POS documentation

- `docs/v1.3/assisted-payment-terminal/ExitPass_Assisted_Payment_Terminal_BRD_v1.0.md:53`: APT shall not declare finality, issue ExitAuthorization, become discount policy engine, or become fiscal authority.
- `docs/v1.3/assisted-payment-terminal/ExitPass_Assisted_Payment_Terminal_BRD_v1.0.md:113`: APT shall not independently approve statutory entitlement.
- `docs/v1.3/assisted-payment-terminal/ExitPass_Assisted_Payment_Terminal_BRD_v1.0.md:115`: APT shall not mutate payable basis directly.
- `docs/v1.3/assisted-payment-terminal/ExitPass_Assisted_Payment_Terminal_BRD_v1.0.md:320`: APT submits validation request to Central PMS/Discount workflow.
- `docs/v1.3/assisted-payment-terminal/diagrams/D-03_Cashier_Assisted_Payment_with_Statutory_Discount_Validation_Flow.puml:23`: terminal submits entitlement/evidence/attestation.
- `docs/v1.3/assisted-payment-terminal/diagrams/D-03_Cashier_Assisted_Payment_with_Statutory_Discount_Validation_Flow.puml:37`: terminal does not mutate payable basis/finality.
- `docs/v1.3/pos-invoicing/ExitPass_POS_Invoicing_BRD_v1.0.md:173`: statutory policy resolution owned by Central PMS/Discount workflow.
- `docs/v1.3/pos-invoicing/ExitPass_POS_Invoicing_BRD_v1.0.md:176`: fiscal treatment and Sales Invoice issuance owned by resolved Site POS Server.
- `docs/v1.3/pos-invoicing/ExitPass_POS_Invoicing_BRD_v1.0.md:388`: Central PMS/Discount workflow remains policy/validation/payable-basis authority.
- `docs/v1.3/pos-invoicing/ExitPass_POS_Invoicing_BRD_v1.0.md:390`: local parking statutory benefits require official ordinance/policy review.
- `docs/v1.3/pos-invoicing/ExitPass_POS_Invoicing_BRD_v1.0.md:575`: exact VAT/tax treatment open question.

### Legal/source posture

- `docs/operator-console/Philippine_Parking_Statutory_Discount_Local_Ordinances_Detailed_List.docx`: operational research matrix; extracted text states it is not legal opinion and official ordinance text must be obtained/reviewed before production configuration.
- `docs/operator-console/OperatorConsole_Production_Policy_Registry_Readiness_v1.md`: production registry readiness package and sandbox policy warning.
- `docs/operator-console/OperatorConsole_Production_Policy_Dedicated_Registry_Test_Matrix_v1.md`: no-go/conditional-go posture and evidence/legal verification tests.
- `scripts/operator-console/Verify-ProductionPolicyRegistryReadiness.sql`: read-only policy readiness checks for unverified/dev/source posture.
