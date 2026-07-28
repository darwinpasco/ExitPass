# ExitPass Statutory Discount Jurisdiction and Operator Console Audit v1.0

## Executive summary

Overall verdict: **NOT_READY_END_TO_END_ENFORCEMENT_GAP**.

The current ExitPass statutory-discount mechanics are durable for Senior Citizen and PWD decision-v2, Operator Console review, application-v1 payable-basis mutation, WebPay/APT readback, replay, and payment-readiness consumption. They do not yet prove or enforce the stricter jurisdiction requirement that parking discounts are offered only when an active applicable city or municipal ordinance covers the parking site and entitlement type.

The strongest existing support is in the legacy Operator Console draft and policy-resolution path. That path reads `sites.sites.lgu_code`, resolves local policy rows, classifies missing jurisdiction and unverified policy as not ready, and has Bruno/manual fixture evidence for `SITE_JURISDICTION_NOT_CONFIGURED` and `STATUTORY_DISCOUNT_POLICY_UNVERIFIED`. The current WebPay/service-channel intake path is not equivalently gated before request creation, and WebPay currently renders the Senior Citizen/PWD request panel after session resolution without a server-owned ordinance availability result.

The audit therefore blocks statutory-discount controlled UAT and treats the current local WebPay walkthrough as blocked as successful evidence until ordinance gating is implemented and proven.

## Examiner requirement

Authoritative requirement audited:

Parking session -> Site -> City or municipality jurisdiction -> Active applicable local ordinance -> Covered entitlement type -> Ordinance-specific requirements -> Operator Console review -> Canonical decision -> Payable-basis application -> Payment and fiscal handoff.

This is fail-closed:

- No applicable ordinance: no WebPay request display, no decision creation, no Operator Console approvable request, no payable-basis adjustment, ordinary payment remains available, and no statutory facts reach payment or fiscal records.
- Applicable ordinance: only covered entitlements may be shown, ordinance requirements govern the request, reviewers must see governing policy facts, and reviewers must not override missing, expired, suspended, ambiguous, or inapplicable ordinance authority.

## Authority boundaries

- WebPay and APT are fact-submission and result-display channels only.
- Operator Console reviewers validate entitlement facts under already established legal authority.
- Reviewers must not create ordinance authority, alter jurisdiction, select applied tariff snapshots, or provide calculated final amounts.
- Central PMS must own eligibility resolution, canonical decision persistence, application, readback, and payment-readiness posture.
- POS/fiscal systems consume finalized safe facts; they do not approve entitlement.
- Ordinary payment must remain available when a discount request is not legally available.

## Repositories and commits inspected

| Repository | Role | Branch/status | Commit inspected |
| --- | --- | --- | --- |
| `D:\SourceCodes\ExitPass-Discounts` | Primary audit repository | `docs/statutory-discount-jurisdiction-operator-console-audit`; aligned with `origin/dev`; pre-existing unrelated untracked Diplomatic VAT audit file present before this audit | `d0a9d948ce7a6afb8b3c41c411fad8fef80c530c` |
| `D:\SourceCodes\ExitPass` | Read-only WebPay and comparison repository | `feature/webpay-statutory-discount-local-walkthrough`; untracked local walkthrough docs/scripts present | `d0a9d948ce7a6afb8b3c41c411fad8fef80c530c` |
| `D:\SourceCodes\exitpassdb_v1.2` | Read-only canonical database source | `develop`; clean; aligned with `origin/develop` | `636ca9c4b229b1d4e9d517f9251a0d5042950834` |

The retired `D:\SourceCodes\ExitPass_DBv1.2` repository and standalone `ExitPass_Full_Database_Creation_DDL_v1.2.sql` were not used as authority.

## Architecture traced

Shared service-channel routes:

- `POST /v1/statutory-discounts/decisions` in `src/Services/CentralPms/src/ExitPass.CentralPms.Api/Endpoints/StatutoryDiscountDecisionEndpoints.cs`
- `GET /v1/statutory-discounts/decisions/{statutoryDiscountDecisionCommandId}` in the same endpoint file

Operator Console policy and review routes:

- `POST /v1/ops/operator-console/statutory-discounts/resolve-policy`
- `POST /v1/ops/operator-console/statutory-discounts/draft`
- `POST /v1/ops/operator-console/statutory-discounts/{draftId}/decision`
- `POST /v1/ops/operator-console/statutory-discounts/{validationId}/apply-payable-basis`
- `GET /v1/ops/operator-console/statutory-discounts/reviews/pending`
- `GET /v1/ops/operator-console/statutory-discounts/reviews/{statutoryDiscountDecisionCommandId}`
- `POST /v1/ops/operator-console/statutory-discounts/reviews/{statutoryDiscountDecisionCommandId}/decision`

Key classes inspected:

- `StatutoryDiscountDecisionFacadeService`
- `StatutoryDiscountStagedCommandService`
- `OperatorConsoleStatutoryDiscountDraftService`
- `OperatorConsoleStatutoryDiscountPolicyResolutionReadRepository`
- `OperatorConsolePolicyReadinessClassifier`
- `OperatorConsoleProductionPolicyImportService`
- `OperatorConsoleServiceChannelStatutoryDiscountReviewService`
- `PostgresStatutoryDiscountServiceChannelReviewRepository`
- `PostgresStatutoryDiscountStagedCommandRepository`
- `OperatorConsoleStatutoryDiscountApplyPayableBasisWriter`
- `VendorParkingResolutionPersistence`
- `AptPayableBasisReadinessService`
- `FiscalSemanticRequestHashCalculator`
- `src/Services/WebPayUi/src/App.tsx`

## Evidence inventory

Positive evidence:

- `OperatorConsoleStatutoryDiscountDraftService` resolves a statutory policy before draft creation and rejects when readiness says draft creation is not allowed.
- `OperatorConsoleStatutoryDiscountPolicyResolutionReadRepository` rejects missing site, site-group mismatch, and missing `sites.sites.lgu_code` with safe policy errors.
- `OperatorConsolePolicyReadinessClassifier` recognizes missing site mapping, missing required policy, unverified policy, missing evidence rule, expired/inactive policy, and sandbox-only policy.
- Canonical DB contains `sites.sites.lgu_code`, `discounts.discount_policy_references`, `discounts.statutory_discount_policy_registry`, `discounts.statutory_discount_validations`, staged decision/application commands, and service-channel review linkage.
- Bruno scenarios prove legacy policy-resolution/draft handling for verified local ordinance fixtures, unverified local policy, and missing jurisdiction.

Negative or incomplete evidence:

- WebPay renders the Senior Citizen/PWD request panel from resolved-session state, not from a server-owned ordinance availability contract.
- WebPay tests assert a Senior Citizen/PWD request can be submitted after session resolution and do not require an active ordinance.
- Shared service-channel decision-v2 intake accepts service facts and creates `AWAITING_REVIEW` before any visible ordinance eligibility gate.
- Service-channel review approval resolves policy only while creating validation linkage and uses `discounts.discount_policy_references`; it may select national fallback when no local ordinance reference is present.
- The service-channel review approval query does not use `sites.sites.lgu_code`, does not filter effectivity windows, and does not require `local_ordinance_reference`.
- Canonical staged decision records carry policy reference IDs and `local_ordinance_applied`, but not a full durable jurisdiction/ordinance/version/evidence-rule snapshot.
- The current local WebPay walkthrough scripts are local/untracked in the comparison repo and do not prove a real active local ordinance for the sandbox site.

## Detailed audit by area

| Area | Verdict | Evidence | Gap |
| --- | --- | --- | --- |
| A. Site and jurisdiction model | PARTIALLY_SUPPORTED | `sites.sites` has `city`, `province`, `lgu_code`; policy resolver reads `lgu_code`. Manual fixtures reference `sites.jurisdictions`. | No canonical `sites.jurisdictions` object found in current object source; no proof every parking site has unambiguous city/municipality jurisdiction history. |
| B. Local ordinance policy registry | PARTIALLY_SUPPORTED | Canonical `discounts.statutory_discount_policy_registry` has ordinance, jurisdiction, effectivity, status, verification, entitlement, evidence, and parking-rule fields. Compatibility `discounts.discount_policy_references` also has local ordinance fields. | System of record and production publishing lifecycle are not proven end to end; service-channel approval still relies on compatibility lookup that can fall back to national law. |
| C. Central PMS eligibility resolution | CONTRADICTED_BY_CURRENT_BEHAVIOR | Legacy draft path resolves policy fail-closed; shared service-channel path creates `AWAITING_REVIEW` before policy gate. | No authoritative eligibility resolver blocks WebPay/APT decision creation when no active local ordinance exists. |
| D. WebPay visibility and API enforcement | CONTRADICTED_BY_CURRENT_BEHAVIOR | `App.tsx` renders `StatutoryDiscountRequestPanel` after session resolution; tests submit Senior/PWD without ordinance availability. | WebPay lacks server-owned availability/readback for ordinance-covered entitlement types; backend bypass prevention is incomplete. |
| E. Operator Console queue/detail | PARTIALLY_SUPPORTED | Service-channel review list/detail expose site, site group, entitlement, decision status, evidence, and original tariff. Legacy draft/read models expose policy snapshots. | Service-channel review detail is not proven to display governing ordinance number, jurisdiction, policy version, effectivity, restrictions, or approval reason. |
| F. Reviewer authority boundary | SUPPORTED_NOT_PROVEN | Review service requires site-scoped access, AWAITING_REVIEW state, and validation linkage. | It is not proven that reviewer cannot approve when the only policy is national fallback or when local ordinance applicability is missing/expired/suspended. |
| G. Canonical decision persistence | PARTIALLY_SUPPORTED | Staged decision has `applied_policy_reference_id`, `fallback_policy_reference_id`, `policy_resolution_basis`, `local_ordinance_applied`, amounts, and snapshots. | No direct city/municipality, ordinance number, policy version, effective window, coverage, or evidence-rule snapshot in the decision table. |
| H. Payable-basis application | SUPPORTED_NOT_PROVEN | Application requires approved decision and validation linkage; writer reads policy context from validation. | Application does not independently prove an active local ordinance prerequisite; approved national-fallback decisions can reach payable-basis paths in tests. |
| I. Payment and fiscal handoff | PARTIALLY_SUPPORTED | Vendor parking and fiscal semantic hash carry decision, validation, applied policy reference, amount, and entitlement facts. | Fiscal/payment records do not prove full ordinance identity/version/legal basis snapshot. |
| J. Management Platform/config ownership | PARTIALLY_SUPPORTED | RBAC includes policy-import and statutory-discount-policy permissions; production import service validates ordinance and scope fields. | No full Management Platform UI/system-of-record proof for jurisdiction assignment, ordinance publishing, suspension, retirement, preview, and audit lifecycle. |
| K. Canonical database | PARTIALLY_SUPPORTED | Canonical source contains policy registry, compatibility references, validation, staged commands, and review linkage. | Canonical DB does not by itself enforce local-ordinance-only decision creation/application; no canonical jurisdiction table was found. |
| L. Automated test proof | PARTIALLY_SUPPORTED | Policy-resolution/draft tests and Bruno scenarios cover local-policy fixtures and missing jurisdiction for legacy path. | No proof that WebPay/APT service-channel intake hides/rejects no-ordinance, expired, suspended, unsupported-entitlement, manipulated request, or application-v1 without ordinance. |
| M. Current local walkthrough | CONTRADICTED_BY_CURRENT_BEHAVIOR | WebPay request UI is visible in walkthrough branch; local scripts are untracked and use sandbox pilot seed wrappers. | The walkthrough is blocked as successful statutory evidence until a valid active ordinance fixture and fail-closed visibility/backend proof exist. |

## Operator Console findings

The legacy Operator Console draft workflow is the most mature jurisdiction-aware path. `OperatorConsoleStatutoryDiscountDraftService` resolves policy before writing a draft. `OperatorConsolePolicyReadinessClassifier` can block missing mapping, missing policy, unverified policy, missing evidence rule, expired/inactive policy, and sandbox-only policy.

The service-channel review workflow is not equivalent. `OperatorConsoleServiceChannelStatutoryDiscountReviewService` approves an `AWAITING_REVIEW` decision after `EnsureApprovedValidationLinkageAsync` returns payable-basis facts. That repository policy lookup selects from `discounts.discount_policy_references` by entitlement, site, or site group and can return a row without `local_ordinance_reference`, classifying it as `NATIONAL_LAW_FALLBACK`. It does not prove city/municipality ordinance applicability at review approval time.

Reviewer authority verdict: **SUPPORTED_NOT_PROVEN**. Access and state controls exist, but ordinance non-override controls are not proven.

## Canonical DB findings

Canonical objects inspected:

- `objects/schemas/sites/tables/sites.sites.sql`
- `objects/schemas/discounts/tables/discounts.discount_policy_references.sql`
- `objects/schemas/discounts/tables/discounts.statutory_discount_policy_registry.sql`
- `objects/schemas/discounts/tables/discounts.statutory_discount_validations.sql`
- `objects/schemas/discounts/tables/discounts.statutory_discount_decision_commands.sql`
- `objects/schemas/discounts/tables/discounts.statutory_discount_payable_basis_application_commands.sql`
- `objects/schemas/operator_console/tables/operator_console.statutory_discount_service_channel_reviews.sql`
- `build/generated/exitpass-full-object.generated.sql`
- `scripts/validation/Validate-V13CentralPmsAlignment.sql`

Supported fields include:

- Site: `site_id`, `site_group_id`, `city`, `province`, `lgu_code`.
- Policy registry: `jurisdiction_id`, `jurisdiction_code`, `jurisdiction_name`, `site_group_id`, `site_id`, `ordinance_reference`, `effective_from`, `effective_to`, `policy_status`, `verification_status`, evidence and parking-rule flags.
- Validation: `applied_policy_reference_id`, `fallback_policy_reference_id`, `policy_resolution_basis`, `local_ordinance_applied`.
- Decision/application commands: policy reference IDs, resolution basis, local-ordinance flag, tariff snapshots, amounts.

Canonical DB gap: object capacity exists, but database constraints do not enforce the business sequence. A decision can exist with no ordinance identity. Application tables can represent local ordinance status but do not make local ordinance authority mandatory.

## Central PMS findings

The shared facade supports `SENIOR_CITIZEN` and `PWD` and derives source channel server-side. For service-channel pending-review intake, it creates or resolves a decision-v2 command and then writes a review linkage row. The service-channel application-intent path requires completed approved decision and validation linkage.

Missing Central PMS control: no fail-closed eligibility resolver runs before service-channel decision creation. The current path allows a pending review request to be created before proving local ordinance authority, contrary to the audit requirement.

## WebPay findings

WebPay currently has browser/client behavior for:

- Senior/PWD request entry
- masked ID validation
- pending-review polling
- application intent
- payable-basis readiness
- browser recovery

It does not have:

- authoritative ordinance availability result
- entitlement list constrained by ordinance
- ordinance-specific requirement display
- no-ordinance hiding based on Central PMS eligibility
- no-sensitive-evidence-before-ordinance guarantee

The UI test `WebPay_WhenSessionResolved_AllowsSeniorCitizenRequestThroughPaymentOrchestratorOnly` proves submission after session resolution, not after ordinance availability.

## Management Platform findings

Management Platform-facing inventory and production policy import support exist in Central PMS:

- `ManagementPlatformIdentityRbacInventoryService` includes compliance/policy administrator and statutory-discount policy permissions.
- `OperatorConsoleProductionPolicyImportService` expects `lgu_code`, `jurisdiction_name`, `policy_resolution_basis`, `ordinance_reference`, effectivity, entitlement, evidence, and scope fields.

Gap: full Management Platform ownership is not proven for site jurisdiction assignment, ordinance registry publishing, legal-source approval, suspension, retirement, affected-site preview, and audit history.

## Payment and fiscal findings

Payment/readback currently can consume applied statutory basis and safe linkage:

- `VendorParkingResolutionPersistence` reads applied statutory tariff snapshots and links decision/application/validation/policy reference.
- `AptPayableBasisReadinessService` consumes canonical statutory readback before cash readiness.
- `FiscalSemanticRequestHashCalculator` includes statutory treatment and applied policy reference.

Gap: payment/fiscal records do not yet prove the exact city/municipality ordinance number, version, and legal-basis snapshot required by this audit.

## Walkthrough finding

The current local WebPay walkthrough is **blocked as successful statutory-discount evidence**. It may remain useful as a mechanical WebPay pending-review/application walkthrough, but not as proof that statutory-discount display and approval are legally eligible for the sandbox site.

The comparison repository contains untracked walkthrough files under `docs/v1.3/webpay/runbooks` and `scripts/v1.3/webpay`. The seed wrapper says it applies after canonical baseline and sandbox pilot seed, while the verified local ordinance evidence is from synthetic manual fixtures marked non-production.

## Gap matrix

| ID | Gap | Severity | Layer | Nature | Dependency | Owner | Blocks |
| --- | --- | --- | --- | --- | --- | --- | --- |
| G1 | No fail-closed service-channel eligibility resolver before decision creation | CRITICAL | Central PMS | missing enforcement | canonical ordinance source | Central PMS / statutory workstream | WebPay, APT, UAT, production |
| G2 | WebPay shows Senior/PWD request without server-owned ordinance availability | CRITICAL | WebPay | contradictory behavior | Central PMS availability contract | WebPay + Central PMS | WebPay UAT, production |
| G3 | Service-channel approval can resolve national fallback rather than requiring local ordinance | CRITICAL | Operator Console/Central PMS | missing enforcement | local-ordinance-only resolver | Central PMS | WebPay, APT, UAT, production |
| G4 | Canonical decision lacks full ordinance/jurisdiction/version snapshot | HIGH | Canonical database/Central PMS | missing persistence | policy model decision | DB + Central PMS | UAT, production |
| G5 | Operator Console service-channel detail lacks proven ordinance display and non-override posture | HIGH | Operator Console | missing display/enforcement proof | G1/G4 | Operator Console + Central PMS | UAT, production |
| G6 | Management Platform ordinance source-of-truth and publish lifecycle not proven | HIGH | Management Platform | missing configuration | canonical model | Management Platform | UAT, production |
| G7 | Application-v1 does not independently prove local ordinance authority | HIGH | Central PMS | missing enforcement | G4 | Central PMS | UAT, production |
| G8 | Payment/fiscal handoff lacks ordinance number/version/legal-basis proof | MEDIUM | Payment/Fiscal | missing persistence/display | G4 | Central PMS + POS/fiscal | production |
| G9 | Automated tests cover legacy draft policy resolution but not service-channel no-ordinance fail-closed flow | HIGH | Testing | missing test | G1/G2/G3 | Central PMS + WebPay | UAT |
| G10 | Secure evidence capture could collect sensitive ID facts before ordinance eligibility | HIGH | Privacy/WebPay/APT | privacy risk | G1/G2 | WebPay/APT + privacy | UAT, production |
| G11 | Canonical `sites.jurisdictions` source was not found while manual fixtures still reference it | MEDIUM | Canonical database | ambiguous authority | DB model decision | canonical DB workstream | UAT |
| G12 | Local walkthrough uses sandbox/manual fixtures and visible request path without ordinance proof | MEDIUM | Runbook/UAT | contradictory behavior | G1/G2 | WebPay + Central PMS | walkthrough/UAT |

## Verdict matrix

| Required decision | Answer |
| --- | --- |
| Is jurisdiction stored for every parking site? | Not proven. `sites.sites` has address and `lgu_code`; no proof every site is populated and unambiguous. |
| Can Central PMS resolve city/municipality from parking session? | Partially. Operator Console policy resolver reads site and `lgu_code`; shared service-channel intake is not gated by that resolver. |
| Is there a canonical local-ordinance registry? | Partially. Canonical policy registry exists; production source-of-truth lifecycle is not proven end to end. |
| Can registry represent effectivity, suspension, supersession, coverage, evidence, and calculation rules? | Partially. Many fields exist in `statutory_discount_policy_registry`; full operation and ownership remain unproven. |
| Does WebPay receive authoritative ordinance eligibility? | No evidence found. |
| Does WebPay hide the request when no ordinance exists? | Contradicted by current UI pattern; only legacy pending status hides the request. |
| Can manipulated WebPay request bypass UI hiding? | Backend fail-closed no-ordinance rejection is not proven. |
| Does Operator Console display ordinance and jurisdiction? | Legacy draft path can expose policy snapshots; service-channel review display is not proven. |
| Can Operator Console approve without an ordinance? | Not safely ruled out; service-channel approval can use national fallback policy. |
| Can reviewer override ordinance inapplicability? | Not proven fail-closed for service-channel flow. |
| Does canonical decision persist ordinance identity and version? | Partially: policy reference and local-ordinance flag only; no full snapshot. |
| Can application-v1 apply a decision lacking ordinance authority? | Not ruled out; application requires approved decision/validation, not local ordinance proof. |
| Are payment and fiscal records safely linked to ordinance decision? | Partially: policy reference and decision linkage exist; ordinance number/version not proven. |
| Does sandbox walkthrough have a valid ordinance fixture? | Not as proof-grade evidence; current fixture evidence is synthetic/manual. |
| Should current manual walkthrough remain blocked? | Yes, blocked as successful statutory-discount legal eligibility evidence. |
| Minimum implementation sequence before secure ID images? | Ordinance eligibility must be resolved before any sensitive ID-image capture. |

## Blocking risks

- A discount request may be created for a site with no applicable ordinance.
- WebPay may collect masked ID facts for an ineligible site.
- Operator Console may approve under national fallback instead of local parking ordinance authority.
- Payment and fiscal records may carry statutory adjustment facts without enough local-ordinance audit basis.
- A manual walkthrough could falsely demonstrate compliance because the mechanics work even when legal eligibility is unproven.

## Minimum target architecture

Target sequence:

1. Resolve parking session to durable site and site group.
2. Resolve site to city/municipality jurisdiction and canonical jurisdiction code.
3. Resolve active approved local ordinance for the entitlement and service category on the transaction date.
4. Return channel-safe availability with covered entitlement types and evidence requirements.
5. Create a decision only when eligibility is resolved.
6. Display ordinance, jurisdiction, version, effectivity, restrictions, and evidence requirements to Operator Console.
7. Persist ordinance/policy identity and version with the canonical decision and validation.
8. Permit approval only under that resolved authority.
9. Permit application-v1 only when the approved decision carries local-ordinance authority.
10. Carry safe ordinance linkage into payment, fiscal hash, reporting, and audit projections.

## Recommended implementation sequence

1. Canonical jurisdiction and ordinance policy model confirmation/promotion.
2. Management Platform ordinance configuration/import and publishing lifecycle.
3. Central PMS fail-closed eligibility resolver before decision creation.
4. Shared channel-safe statutory availability/readback contract.
5. WebPay request visibility gate and manipulated-request backend proof.
6. Operator Console service-channel ordinance display and approval enforcement.
7. Decision/application persistence linkage to ordinance identity and version.
8. APT channel integration with the same availability contract.
9. Secure ID evidence capture and review, only after ordinance eligibility is known.
10. Payment/fiscal audit linkage for ordinance-based adjustments.
11. Integrated walkthrough and controlled UAT proof.

Recommended first implementation task:

**Implement Central PMS statutory-discount local-ordinance eligibility resolver and availability contract design, including canonical policy-source gap closure.**

This should explicitly decide whether the current canonical policy registry is sufficient or whether `sites.jurisdictions`/jurisdiction-history source promotion is required before runtime enforcement.

## Parallelization guidance

Can run in parallel after the first model decision:

- Management Platform ordinance import UI/configuration and Central PMS resolver implementation, if the canonical schema contract is frozen.
- Operator Console display work and WebPay visibility work, after the shared availability/readback DTO is frozen.
- Payment/fiscal audit-linkage work and APT integration, after decision/application ordinance persistence is frozen.

Must wait:

- Secure ID-image capture.
- Controlled UAT.
- Production rollout.
- Treating local walkthrough as successful compliance evidence.

## ID evidence sequencing

Applicable ordinance must be resolved before collecting sensitive evidence.

No ordinance:

- no discount request
- no ID fields
- no image capture
- no Operator Console approvable request

Applicable ordinance:

- collect only evidence required by the ordinance and approved evidence policy
- keep evidence reference-only unless a later secure evidence task authorizes image capture

## Manual test requirements

Significant manual testing required for this audit merge: **No**.

Significant manual testing required after remediation: **Yes**.

Required later scenarios:

1. Active ordinance: session resolves to site/jurisdiction; active ordinance is resolved; only covered entitlements appear; Operator Console sees and approves under the ordinance; application uses ordinance and policy version.
2. No ordinance: request absent; no sensitive ID details collected; manipulated API request rejected; Operator Console receives no approvable request; ordinary payment remains available.
3. Expired or suspended ordinance: request absent or rejected; reviewer cannot override; ordinary payment remains available.
4. Mismatched entitlement: unsupported entitlement absent; manipulated request rejected.
5. Ambiguous or missing jurisdiction: fail closed with safe operational error and remediation guidance.

## UAT and production authorization posture

- WebPay statutory-discount controlled UAT: **not authorized** until fail-closed ordinance eligibility is implemented and proven.
- APT statutory-discount controlled UAT: **not authorized** until the same backend and channel proof exists.
- Production rollout: **not authorized**.
- Current local statutory walkthrough: **blocked as successful evidence**.

This audit does not rescind the prior channel implementation authorization for building client integration mechanics. It blocks controlled statutory-discount UAT and production claims until jurisdiction and ordinance enforcement are complete.

## Appendices

### Source files inspected

- `src/Services/CentralPms/src/ExitPass.CentralPms.Api/Endpoints/StatutoryDiscountDecisionEndpoints.cs`
- `src/Services/CentralPms/src/ExitPass.CentralPms.Api/Endpoints/OperatorConsoleStatutoryDiscountDraftEndpoints.cs`
- `src/Services/CentralPms/src/ExitPass.CentralPms.Api/Endpoints/OperatorConsoleStatutoryDiscountPolicyResolutionEndpoints.cs`
- `src/Services/CentralPms/src/ExitPass.CentralPms.Api/Endpoints/OperatorConsoleProductionPolicyImportEndpoints.cs`
- `src/Services/CentralPms/src/ExitPass.CentralPms.Application/StatutoryDiscounts/StatutoryDiscountDecisionFacadeService.cs`
- `src/Services/CentralPms/src/ExitPass.CentralPms.Application/StatutoryDiscounts/StatutoryDiscountStagedCommandService.cs`
- `src/Services/CentralPms/src/ExitPass.CentralPms.Application/OperatorConsole/OperatorConsoleStatutoryDiscountDraftService.cs`
- `src/Services/CentralPms/src/ExitPass.CentralPms.Application/OperatorConsole/OperatorConsoleServiceChannelStatutoryDiscountReviewService.cs`
- `src/Services/CentralPms/src/ExitPass.CentralPms.Application/OperatorConsole/OperatorConsolePolicyReadinessClassifier.cs`
- `src/Services/CentralPms/src/ExitPass.CentralPms.Application/OperatorConsole/OperatorConsoleProductionPolicyImportService.cs`
- `src/Services/CentralPms/src/ExitPass.CentralPms.Infrastructure/OperatorConsole/OperatorConsoleStatutoryDiscountPolicyResolutionReadRepository.cs`
- `src/Services/CentralPms/src/ExitPass.CentralPms.Infrastructure/StatutoryDiscounts/PostgresStatutoryDiscountServiceChannelReviewRepository.cs`
- `src/Services/CentralPms/src/ExitPass.CentralPms.Infrastructure/StatutoryDiscounts/PostgresStatutoryDiscountStagedCommandRepository.cs`
- `src/Services/CentralPms/src/ExitPass.CentralPms.Infrastructure/OperatorConsole/OperatorConsoleStatutoryDiscountApplyPayableBasisWriter.cs`
- `src/Services/CentralPms/src/ExitPass.CentralPms.Infrastructure/VendorParking/VendorParkingResolutionPersistence.cs`
- `src/Services/CentralPms/src/ExitPass.CentralPms.Application/TerminalCashPayments/AptPayableBasisReadinessService.cs`
- `src/Services/CentralPms/src/ExitPass.CentralPms.Application/FiscalIssuance/FiscalSemanticRequestHashCalculator.cs`
- `src/Services/WebPayUi/src/App.tsx`
- `src/Services/WebPayUi/src/App.test.tsx`
- `src/Services/WebPayUi/e2e/webpay-authoritative-sales-invoice.spec.ts`

### Database files inspected

- `D:\SourceCodes\exitpassdb_v1.2\objects\schemas\sites\tables\sites.sites.sql`
- `D:\SourceCodes\exitpassdb_v1.2\objects\schemas\discounts\tables\discounts.discount_policy_references.sql`
- `D:\SourceCodes\exitpassdb_v1.2\objects\schemas\discounts\tables\discounts.statutory_discount_policy_registry.sql`
- `D:\SourceCodes\exitpassdb_v1.2\objects\schemas\discounts\tables\discounts.statutory_discount_validations.sql`
- `D:\SourceCodes\exitpassdb_v1.2\objects\schemas\discounts\tables\discounts.statutory_discount_decision_commands.sql`
- `D:\SourceCodes\exitpassdb_v1.2\objects\schemas\discounts\tables\discounts.statutory_discount_payable_basis_application_commands.sql`
- `D:\SourceCodes\exitpassdb_v1.2\objects\schemas\operator_console\tables\operator_console.statutory_discount_service_channel_reviews.sql`
- `D:\SourceCodes\exitpassdb_v1.2\build\generated\exitpass-full-object.generated.sql`
- `D:\SourceCodes\exitpassdb_v1.2\scripts\validation\Validate-V13CentralPmsAlignment.sql`

### Tests and scenarios inspected

- `src/Services/CentralPms/tests/ExitPass.CentralPms.UnitTests/Application/OperatorConsoleStatutoryDiscountDraftServiceTests.cs`
- `src/Services/CentralPms/tests/ExitPass.CentralPms.UnitTests/Application/OperatorConsoleStatutoryDiscountPolicyResolutionServiceTests.cs`
- `src/Services/CentralPms/tests/ExitPass.CentralPms.IntegrationTests/Api/OperatorConsoleStatutoryDiscountPolicyResolutionApiIntegrationTests.cs`
- `src/Services/CentralPms/tests/ExitPass.CentralPms.IntegrationTests/Api/OperatorConsoleDedicatedPolicyRegistryIntegrationTests.cs`
- `src/Services/CentralPms/tests/ExitPass.CentralPms.IntegrationTests/Api/OperatorConsoleServiceChannelStatutoryDiscountReviewApiIntegrationTests.cs`
- `src/Services/CentralPms/tests/ExitPass.CentralPms.IntegrationTests/Api/StatutoryDiscountServiceChannelPostApprovalApplicationIntentApiIntegrationTests.cs`
- `src/Services/CentralPms/tests/ExitPass.CentralPms.ContractTests/Public/StatutoryDiscountDecisionContractTests.cs`
- `src/Services/WebPayUi/src/App.test.tsx`
- `src/Services/WebPayUi/e2e/webpay-authoritative-sales-invoice.spec.ts`
- `bruno/operator-console-statutory-discount-draft/37-resolve-policy-senior-national-fallback.bru`
- `bruno/operator-console-statutory-discount-draft/39-resolve-policy-verified-local-policy.bru`
- `bruno/operator-console-statutory-discount-draft/40-resolve-policy-unverified-local-policy-blocked.bru`
- `bruno/operator-console-statutory-discount-draft/41-resolve-policy-missing-site-jurisdiction.bru`
- `bruno/operator-console-statutory-discount-draft/47-create-draft-verified-local-policy-snapshot.bru`
- `bruno/operator-console-statutory-discount-draft/50-create-draft-missing-site-jurisdiction-blocked.bru`
- `bruno/operator-console-statutory-discount-draft/165-service-channel-review-approve-webpay.bru`
- `bruno/operator-console-statutory-discount-draft/176-shared-statutory-decision-webpay-post-approval-application-intent.bru`

### Comparison repository files inspected

- `D:\SourceCodes\ExitPass\src\Services\WebPayUi\src\App.tsx`
- `D:\SourceCodes\ExitPass\src\Services\WebPayUi\src\webpay.ts`
- `D:\SourceCodes\ExitPass\src\Services\WebPayUi\e2e\webpay-authoritative-sales-invoice.spec.ts`
- `D:\SourceCodes\ExitPass\docs\v1.3\webpay\runbooks\ExitPass_WebPay_Statutory_Discount_Local_Walkthrough_v1.0.md`
- `D:\SourceCodes\ExitPass\scripts\v1.3\webpay\Seed-WebPayStatutoryDiscountWalkthrough.sql`
- `D:\SourceCodes\ExitPass\scripts\v1.3\webpay\Start-WebPayStatutoryDiscountWalkthrough.ps1`
- `D:\SourceCodes\ExitPass\scripts\v1.3\webpay\Verify-WebPayStatutoryDiscountWalkthrough.sql`

### Final authorization lines

WebPay integration implementation: authorized with ordinance-gate remediation constraints; WebPay statutory-discount controlled UAT is not authorized.

APT integration implementation: authorized with ordinance-gate remediation constraints; APT statutory-discount controlled UAT is not authorized.
