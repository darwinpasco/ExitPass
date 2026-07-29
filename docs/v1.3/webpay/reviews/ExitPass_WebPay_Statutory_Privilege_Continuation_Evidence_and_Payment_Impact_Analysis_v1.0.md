# ExitPass WebPay Statutory Privilege Continuation Evidence and Payment Impact Analysis

## 1. Executive Summary

Overall verdict: PARTIALLY_SUPPORTED.

The merged ExitPass WebPay statutory-discount implementation supports a canonical review-mediated Senior Citizen/PWD flow through Payment Orchestrator and Central PMS, including decision-v2, Operator Console review linkage, application-v1, applied payable-basis readback, WebPay payment initiation using applied Central PMS facts, browser recovery metadata, and POS Server-owned Sales Invoice presentation after payment.

The implementation does not yet support the frozen parking-privilege continuation and secure-evidence contract required before controlled UAT. The blocking gaps are:

- WebPay does not consume Central PMS ordinance availability before showing the request.
- WebPay still uses a free-text ID document field instead of entitlement-specific document-type options.
- WebPay does not require secure ID photo capture/upload and currently submits `evidenceCaptureRequested: false`.
- Payment Orchestrator has no WebPay availability proxy and no evidence-upload/continuation-token boundary.
- Current WebPay recovery persists internal Central PMS identifiers in localStorage; it is not an opaque server-issued continuation URL.
- Payment Orchestrator statutory Central PMS client sets `SourceChannel = "WEBPAY"` but does not add the Central PMS service identity/permission headers required by the current RBAC policy.
- The customer-facing error path can expose Central PMS internal authorization wording.
- There is no explicit Pay regular amount command, no payment-without-privilege finality classification, and no durable race rule that makes a later approval inapplicable to a completed ordinary payment.

WebPay implementation may continue, but WebPay controlled UAT is not authorized. The exact first bounded implementation task is a Payment Orchestrator service-authentication and customer-safe statutory error correction.

## 2. Frozen Product Decisions

- Supported privilege types are `SENIOR_CITIZEN` and `PWD` only.
- Beneficiary presence is optional by default and mandatory only when the frozen applicable ordinance explicitly requires beneficiary presence, beneficiary-as-driver, beneficiary-as-passenger, or another explicitly modeled presence condition.
- Senior Citizen document type must be controlled to `OSCA_ID` or `EQUIVALENT_DOCUMENT`.
- PWD document type must be controlled to `PWD_ID` or `EQUIVALENT_DOCUMENT`.
- Equivalent document requires a safe document-description field.
- Raw ID images must be uploaded or captured only after ordinance availability is confirmed.
- Images must not be stored in PostgreSQL blobs, Base64 request bodies, browser storage, logs, payment intent payloads, fiscal records, or Sales Invoice records.
- Browser state is recovery metadata only and never authority.
- The customer may explicitly pay the regular amount while review is pending, but that payment freezes the ordinary payable basis for that payment and prevents later approval from mutating the completed payment.
- POS Server remains authoritative for fiscal issuance, fiscal numbering, Sales Invoice rendering, and authoritative Digital Sales Invoice presentation.

## 3. Repositories and Commits Inspected

Primary repository/worktree:

- Path: `D:\SourceCodes\ExitPass-G-StatutoryPrivilege`
- Branch: `docs/webpay-statutory-privilege-continuation-evidence-impact-analysis`
- HEAD: `19315cb90442732c13d466bf7897ae59b6df2eea`
- Upstream inspected: `origin/dev` at `19315cb90442732c13d466bf7897ae59b6df2eea`
- Status before edits: clean.

Canonical database repository:

- Path: `D:\SourceCodes\exitpassdb_v1.2`
- Branch: `develop`
- HEAD: `7a785fd93d592b019fbb6ac6bbdf4fc82d8485dc`
- Upstream inspected: `origin/develop` at `7a785fd93d592b019fbb6ac6bbdf4fc82d8485dc`
- Status: clean and aligned.
- Generated baseline inspected: `build/generated/exitpass-full-object.generated.sql`

Not used:

- Retired database repository `D:\SourceCodes\ExitPass_DBv1.2`
- Historical standalone full DDL as sole authority
- `D:\SourceCodes\ExitPass-G-LocalHarness`
- `D:\SourceCodes\ExitPass`
- `D:\SourceCodes\ExitPass-Discounts`
- APT repositories
- POS Server repository

## 4. Current Architecture

Current WebPay statutory-discount direction:

`WebPay UI -> Payment Orchestrator -> Central PMS`

Evidence:

- WebPay browser routes are declared in `src/Services/WebPayUi/src/webpay.ts`.
- Payment Orchestrator maps WebPay routes in `src/Services/PaymentOrchestrator/src/ExitPass.PaymentOrchestrator.Api/Endpoints/WebPayPaymentIntentEndpoints.cs`.
- Central PMS maps shared statutory routes in `src/Services/CentralPms/src/ExitPass.CentralPms.Api/Endpoints/StatutoryDiscountDecisionEndpoints.cs`.

Current WebPay statutory flow:

1. WebPay resolves the parking session through `POST /v1/webpay/parking-session`.
2. WebPay can submit a statutory decision through `POST /v1/webpay/statutory-discounts/decisions`.
3. WebPay polls `GET /v1/webpay/statutory-discounts/decisions/{statutoryDiscountDecisionCommandId}`.
4. WebPay can call `POST /v1/webpay/statutory-discounts/decisions/{statutoryDiscountDecisionCommandId}/apply-payable-basis`.
5. WebPay payment intent uses `POST /v1/webpay/payment-intents`.
6. WebPay receipt readback uses `GET /v1/webpay/payment-attempts/{paymentAttemptId}/receipt-presentation`.

Current Central PMS authority:

- Central PMS owns ordinance availability through `POST /v1/statutory-discounts/decisions/availability`.
- Central PMS owns decision-v2 and application-v1.
- Central PMS RBAC enforces source-channel submit/read permissions.
- Central PMS stores canonical decision/application/readback state in canonical PostgreSQL objects.

## 5. Current Routes and DTOs

### WebPay Browser and Payment Orchestrator Routes

| Route | Owner | Evidence | Current posture |
| --- | --- | --- | --- |
| `POST /v1/webpay/parking-session` | Payment Orchestrator | `WebPayPaymentIntentEndpoints.cs`, `webpay.ts` | Supported |
| `POST /v1/webpay/payment-intents` | Payment Orchestrator | `WebPayPaymentIntentEndpoints.cs`, `WebPayPaymentIntentRequest.cs` | Supported |
| `POST /v1/webpay/statutory-discounts/decisions` | Payment Orchestrator | `WebPayPaymentIntentEndpoints.cs`, `WebPayStatutoryDiscountDtos.cs` | Supported, but lacks availability/evidence gating |
| `GET /v1/webpay/statutory-discounts/decisions/{id}` | Payment Orchestrator | `WebPayPaymentIntentEndpoints.cs`, `ICentralPmsWebPayClient.GetStatutoryDiscountDecisionAsync` | Supported |
| `POST /v1/webpay/statutory-discounts/decisions/{id}/apply-payable-basis` | Payment Orchestrator | `WebPayPaymentIntentEndpoints.cs`, `ICentralPmsWebPayClient.ApplyStatutoryDiscountPayableBasisAsync` | Supported |
| `GET /v1/webpay/payment-attempts/{paymentAttemptId}/receipt-presentation` | Payment Orchestrator -> Central PMS | `WebPayPaymentIntentEndpoints.cs`, `WebPayReceiptPresentationEndpoints.cs` | Supported |

Missing WebPay-facing routes:

- `POST /v1/webpay/statutory-discounts/decisions/availability`
- Evidence upload/capture route
- Continuation-token issue route
- Continuation-token resolve route
- Pay-regular-amount while privilege pending route/command

### Central PMS Routes

| Route | Policy | Evidence | Current posture |
| --- | --- | --- | --- |
| `POST /v1/statutory-discounts/decisions` | `CentralPmsStatutoryDiscountDecisionSubmit` | `StatutoryDiscountDecisionEndpoints.cs` | Supported |
| `GET /v1/statutory-discounts/decisions/{id}` | `CentralPmsStatutoryDiscountDecisionRead` | `StatutoryDiscountDecisionEndpoints.cs` | Supported |
| `POST /v1/statutory-discounts/decisions/availability` | `CentralPmsStatutoryDiscountDecisionRead` | `StatutoryDiscountDecisionEndpoints.cs` | Supported |
| `POST /v1/ops/operator-console/statutory-discounts/{draftId}/evidence` | `statutory-discounts.evidence.capture` | `OperatorConsoleStatutoryDiscountDraftEndpoints.cs` | Metadata-only, Operator Console draft path |
| `GET /v1/ops/operator-console/statutory-discounts/{draftId}/evidence` | `statutory-discounts.evidence.view` | `OperatorConsoleStatutoryDiscountDraftEndpoints.cs` | Metadata-only, no raw image access |

### DTO Inventory

| DTO | Path | Finding |
| --- | --- | --- |
| `StatutoryDiscountDecisionRequest` | `src/Services/CentralPms/src/ExitPass.CentralPms.Contracts/StatutoryDiscounts/StatutoryDiscountDecisionDtos.cs` | Includes `SourceChannel`, safe evidence references, ID metadata, `ApplyPayableBasis`, and tariff snapshot ids. |
| `StatutoryDiscountParkingAvailabilityRequestDto` | same file | Central PMS availability request exists. |
| `StatutoryDiscountParkingAvailabilityResponse` | same file | Contains availability, covered entitlements, policy identity, evidence requirements, benefit effect support, safe reason/remediation. |
| `StatutoryDiscountEvidenceReferenceRequest` | same file | Metadata/reference-only request, not raw image upload. |
| `WebPayStatutoryDiscountDecisionRequest` | `src/Services/PaymentOrchestrator/src/ExitPass.PaymentOrchestrator.Contracts/WebPay/WebPayStatutoryDiscountDtos.cs` | Browser-safe shape exists, but has free-text `IdDocumentType` and no availability or upload contract. |
| `WebPayStatutoryDiscountDecisionResponse` | same file | Durable readback shape exists. |
| `WebPayPaymentIntentRequest` | `src/Services/PaymentOrchestrator/src/ExitPass.PaymentOrchestrator.Contracts/WebPay/WebPayPaymentIntentRequest.cs` | Carries statutory decision/application ids for applied payment, but no pay-regular pending-review classification. |
| `OperatorConsoleStatutoryDiscountEvidenceCaptureRequest` | `src/Services/CentralPms/src/ExitPass.CentralPms.Contracts/OperatorConsole/OperatorConsoleStatutoryDiscountEvidenceDtos.cs` | Metadata-only evidence capture request. |
| `OperatorConsoleStatutoryDiscountEvidenceListResponse` | same file | Metadata-only evidence list response. |

## 6. Current Authentication Posture

Central PMS RBAC evidence:

- `CentralPmsRbacPolicyCatalog` defines `X-ExitPass-Permissions`, `X-ExitPass-User-Id`, and `X-ExitPass-Service-Identity-Id`.
- `CentralPmsStatutoryDiscountDecisionSubmit` includes `statutory-discounts.decision.submit.webpay`.
- `CentralPmsStatutoryDiscountDecisionRead` includes `statutory-discounts.decision.read`.
- `StatutoryDiscountDecisionApiAccessPolicyIntegrationTests.cs` proves service-channel submit succeeds only when service identity and matching permission are supplied.

Payment Orchestrator evidence:

- `CentralPmsWebPayClient.SendStatutoryDiscountDecisionAsync` sets `SourceChannel: "WEBPAY"` and forwards `Idempotency-Key` plus `X-Correlation-Id`.
- The same client does not add `X-ExitPass-Service-Identity-Id`, `X-ExitPass-Permissions`, or another visible Central PMS statutory service-auth credential in the inspected statutory request path.
- `StatutoryDiscountDecisionEndpoints.cs` returns `CENTRAL_PMS_AUTHENTICATED_ACTOR_REQUIRED` when an actor/service identity is absent.

Finding:

The local WebPay internal Central PMS identity error is most consistent with Payment Orchestrator calling the correct Central PMS route with `SourceChannel = WEBPAY` but without a Central PMS service identity/permission binding accepted by RBAC. This is a contract/configuration defect at the Payment Orchestrator-to-Central PMS service boundary, not a browser-authentication requirement. The customer browser must never authenticate as a Central PMS operator.

Customer-safe error required:

`We could not submit your request right now. Please try again or continue with the regular parking amount.`

## 7. Current Evidence Capability

Supported:

- Canonical DB has `discounts.discount_evidence_references`.
- The table has `evidence_storage_type`, `evidence_storage_ref`, `evidence_hash`, `evidence_capture_status`, `access_classification`, `redaction_status`, `retention_policy_code`, `retention_expires_at`, capture actor fields, purge actor fields, and correlation/audit timestamps.
- `OperatorConsoleStatutoryDiscountEvidenceRepository` writes metadata-only rows and states it does not store raw evidence bytes, OCR results, ID numbers, payment, provider, gate, coupon, or reconciliation data.
- Operator Console evidence DTOs list/capture metadata only.
- `operator_console.statutory_discount_service_channel_reviews.evidence_references` is JSONB reference-only metadata; canonical comments prohibit raw images, Base64 evidence, raw bytes, and full statutory IDs.

Not supported:

- No WebPay evidence-upload route.
- No browser camera/file upload flow for ID images.
- No protected object-storage integration was found in the WebPay/Payment Orchestrator statutory path.
- No signed upload URL or signed reviewer download URL contract was found.
- No malware scanning, MIME validation, image dimension validation, EXIF stripping, or unsafe-content scanning seam was found for WebPay statutory ID evidence.
- No Operator Console reviewer image retrieval route was found; current evidence access is metadata-only.

## 8. Continuation and Recovery Findings

Current WebPay recovery:

- `src/Services/WebPayUi/src/statutoryRecovery.ts` defines `statutoryRecoveryStorageKey = "exitpass:webpay:statutory-discount-recovery:v1"`.
- The record stores `parkingSessionId`, `entitlementType`, `statutoryDiscountDecisionCommandId`, `statutoryDiscountPayableBasisApplicationCommandId`, decision/application idempotency keys, request/correlation ids, stage, optional `paymentAttemptId`, and a six-hour expiry.
- Recovery uses localStorage, validates schema version, rejects malformed/expired records, and prefers GET readback when a decision command id exists.

Gap against frozen continuation URL:

- There is no server-issued opaque continuation token.
- There is no WebPay continuation page route that resolves an opaque token.
- Current recovery metadata contains internal Central PMS command IDs and the parking-session ID. That can remain internal browser metadata for the current implementation, but it cannot be used as the public continuation URL contract.
- Payment-return has provider-facing continuation URLs such as `resumePaymentUrl`, and the PayMongo checkout request tests show return URLs with `paymentAttemptId` and `correlationId`; this is not the statutory privilege review continuation token model.

Frozen continuation target:

- Payment Orchestrator should issue and resolve the opaque WebPay public continuation token.
- Payment Orchestrator should own token expiry/revocation for the public browser boundary.
- Payment Orchestrator should resolve the token to safe internal references, call Central PMS GET readback, and reconstruct the page from authoritative state.
- Central PMS remains authoritative for decision/application/payment state; the token is not authority.

## 9. Payment-Without-Privilege Findings

Current supported normal payment:

- WebPay no-discount payment remains supported through `POST /v1/webpay/payment-intents`.
- Central PMS `core.payment_attempts` records `parking_session_id`, `tariff_snapshot_id`, `idempotency_key`, `payment_rail_id`, `currency_code`, `amount`, `attempt_status`, `requested_at`, `expires_at`, `finalized_at`, and correlation/service identity fields.
- Payment Orchestrator cross-instance provider-session replay safety is merged, and `payments.provider_sessions` records provider-session idempotency and checkout handoff state.

Missing pending-review regular-payment contract:

- No WebPay UI action or contract was found for explicit `Pay regular amount` while a statutory request is awaiting review.
- No confirmation copy was found for `Proceed without the parking privilege?`.
- No Payment Orchestrator request field or Central PMS command was found that links an ordinary payment attempt to an active pending statutory review and classifies that review as no longer applicable to that completed payment.
- No database status currently represents `NO_LONGER_APPLICABLE_TO_PAYMENT` or equivalent for `discounts.statutory_discount_decision_commands` or `operator_console.statutory_discount_service_channel_reviews`.

Target:

- The ordinary payment attempt uses the ordinary payable basis and remains durable/final.
- Later review approval must not mutate the completed payment, trigger a refund, apply the privilege retroactively, or alter the Sales Invoice.
- Central PMS must own the payment-versus-approval race arbitration because it owns payment finality, canonical decision state, and payable-basis application state.

## 10. Race and Idempotency Findings

Existing idempotency support:

- Decision command canonical business identity is `statutory-discount-decision:{parkingSessionId}:{entitlementType}`.
- Decision command unique indexes include idempotency scope/key, business identity, request reference, and business identity text.
- Application command canonical business identity is `statutory-discount-payable-basis-application:{statutoryDiscountDecisionCommandId}`.
- Application command unique indexes include business identity, decision command, idempotency scope/key, and request reference.
- Payment attempts use an idempotency key and active-by-session uniqueness.
- Provider sessions use provider-session idempotency and active-by-attempt/rail uniqueness.

Missing race support:

- There is no explicit command that says the customer intentionally paid ordinary amount while a review was pending.
- There is no durable inapplicability state for a pending decision after ordinary payment finality.
- There is no documented transaction ordering between Operator Console approval and WebPay ordinary payment.
- There is no late-approval guard that prevents a newly approved decision from applying payable basis after the associated parking session already has a finalized ordinary payment.

Required race rule:

- If Central PMS records ordinary payment finality first, the pending statutory request becomes inapplicable to that completed payment. Review history remains auditable, but payable-basis application must not proceed for that payment.
- If Operator Console approval and payable-basis application complete first, WebPay payment must use the applied authoritative payable basis.
- If the two operations race, Central PMS must make one state transition authoritative under a database transaction or command-level compare-and-set and return a deterministic conflict/retry/inapplicable result to the losing operation.

## 11. Privacy and Security Findings

Resolved or partially resolved:

- Central PMS source-channel field matrix rejects operator-only facts from WebPay and APT.
- WebPay masked ID validation blocks obvious full ID-like input in current tests.
- Current WebPay statutory recovery does not persist raw evidence or monetary authority facts.
- Canonical service-channel review comments prohibit raw images, Base64 evidence, raw bytes, and full statutory ID values.

Unresolved:

- Allowed file types, maximum file size, minimum/maximum image dimensions, camera capture constraints, EXIF stripping, duplicate-image hash behavior, malware scanning, storage outage behavior, and retention duration are not implemented or frozen in runtime code.
- Retention policy exists as a database field, but the legal retention period and deletion/redaction operation for customer-uploaded ID images remain a policy decision. This report does not invent a retention period.
- Signed reviewer access URLs are not implemented; reviewer evidence access is metadata-only.
- Payment Orchestrator statutory error mapping should not pass raw Central PMS authorization wording to the browser.

Frozen safe evidence posture:

- Browser may upload JPEG, PNG, or HEIC/HEIF only if product/security later approve the exact set.
- Maximum size and dimensions remain unresolved policy/implementation decisions.
- Camera capture is preferred; existing file upload remains allowed.
- EXIF and unnecessary metadata must be stripped before protected storage or before reviewer access.
- Malware/unsafe-content scanning must fail closed for reviewability. Storage/scanning unavailable must allow ordinary payment but must not create an approvable statutory request.
- Duplicate-image hashes may support duplicate detection only; they must not become entitlement authority.
- Reviewer access must be short-lived, non-reusable where possible, scoped to the assigned review permission, and audit logged.

## 12. Persistence Findings

Canonical objects currently aligned:

- `discounts.statutory_discount_decision_commands`
- `discounts.statutory_discount_payable_basis_application_commands`
- `operator_console.statutory_discount_service_channel_reviews`
- `discounts.discount_evidence_references`
- `discounts.statutory_discount_validations`
- `discounts.statutory_discount_payable_basis_applications`
- `core.tariff_snapshots`
- `core.payment_attempts`
- `payments.provider_sessions`

Likely persistence additions:

- Public continuation token table or equivalent command/read model. Store token hash, decision command id, parking session id, scope, expiry, revocation state, created/used timestamps, and correlation/audit fields. Do not store raw token.
- Evidence upload session table or equivalent. Store upload token hash/session id, decision draft/intake correlation, object key/reference, content metadata, scan state, expiry, and audit fields. Do not store image bytes.
- Payment-without-privilege command or decision classification. Store link between pending decision and ordinary payment attempt/finality, inapplicability reason, actor/source, timestamp, and correlation.

Database change assessment:

- Existing evidence reference metadata can store opaque object references once a protected storage/upload seam exists.
- Existing decision/application/payment tables do not by themselves provide opaque public continuation tokens or pending-review inapplicability after ordinary payment. These need additive canonical persistence or an approved existing command model not found in this analysis.

## 13. Primary Audit Question Answers

| # | Question | Answer |
| --- | --- | --- |
| 1 | Which service issues the opaque continuation token? | Payment Orchestrator should issue the browser-facing opaque token after Central PMS records the canonical decision and evidence reference. |
| 2 | Which service resolves the continuation token? | Payment Orchestrator should resolve it, then call Central PMS durable GET readback. |
| 3 | Which service owns token expiry and revocation? | Payment Orchestrator owns public-token expiry/revocation; Central PMS owns decision/payment state. |
| 4 | Which service owns evidence-upload authorization? | Payment Orchestrator owns browser upload authorization; Central PMS must authorize whether evidence is required/accepted for the decision. |
| 5 | Which service owns protected object-storage integration? | Target design should centralize protected statutory evidence storage under Central PMS authority, with Payment Orchestrator brokering the WebPay browser upload. |
| 6 | Which service stores only the opaque evidence reference? | Central PMS stores opaque evidence references in `discounts.discount_evidence_references` and service-channel review readback. |
| 7 | Which service closes or classifies the pending review after ordinary payment? | Central PMS. |
| 8 | Which service arbitrates approval-versus-payment races? | Central PMS. |
| 9 | Does an existing WebPay resume-token model already exist? | Not for statutory privilege review. Existing browser recovery is localStorage with internal identifiers. |
| 10 | Does payment return already use an opaque public token? | No. Current return/resume evidence includes `paymentAttemptId`, `correlationId`, and provider resume/handoff URLs. |
| 11 | Does the repository already contain an object-storage abstraction? | Not found in the inspected WebPay/Payment Orchestrator/Central PMS statutory evidence path. |
| 12 | Does any upload flow support short-lived upload or download URLs? | Not found. |
| 13 | Does Operator Console already support secure evidence retrieval? | It supports metadata-only list/capture for draft evidence, not secure image retrieval. |
| 14 | Is malware/MIME/image validation seam present? | Not found for WebPay statutory ID evidence. |
| 15 | Is approved service-to-service auth mechanism present? | Central PMS supports service identity and permission headers; Payment Orchestrator statutory client does not visibly send them. |
| 16 | Which named RBAC policy protects service-channel statutory routes? | `CentralPmsStatutoryDiscountDecisionSubmit` and `CentralPmsStatutoryDiscountDecisionRead`; WebPay submit permission is `statutory-discounts.decision.submit.webpay`. |
| 17 | Why did local WebPay receive the internal Central PMS identity error? | Payment Orchestrator likely called Central PMS without accepted service identity/permissions; Central PMS returned `CENTRAL_PMS_AUTHENTICATED_ACTOR_REQUIRED`, and the browser-facing error mapping did not replace it with customer-safe wording. |
| 18 | What evidence metadata is required? | Evidence type, capture method, file name, content type, size, opaque storage reference, optional masked reference, verification/scan status, hash for duplicate detection, retention/redaction/access classification, correlation and actor/service audit fields. |
| 19 | Which database records need evidence references? | `discounts.discount_evidence_references`, service-channel review `evidence_references`, and the canonical decision/readback via evidence recorded flags and validation linkage. |
| 20 | What continuation-token persistence is required? | Token hash, scope, parking session, decision command id, optional application/payment attempt ids, expiry, revocation, status, created/used timestamps, correlation/audit fields. |
| 21 | What payment-without-privilege record or command is required? | A Central PMS command/classification linking the pending decision to the ordinary payment attempt/finality and marking the privilege request inapplicable to that payment. |
| 22 | How is pending request closed after ordinary payment? | By Central PMS transaction/command after ordinary payment is created/finalized, not by browser-local state. |
| 23 | What audit linkage is required? | Decision, evidence reference, continuation token hash/id, application command, payment attempt, final payment confirmation, fiscal issuance reference, and Sales Invoice presentation reference. |
| 24 | What happens when approval and ordinary payment occur concurrently? | Central PMS must serialize or compare-and-set the state. The first authoritative transition wins; the loser receives deterministic inapplicable/conflict/retry readback. |
| 25 | What transaction or command wins? | Either approved application-before-payment or ordinary payment-before-application, based on Central PMS committed state ordering. |
| 26 | How prevent late approval mutating finalized payment? | Central PMS must block payable-basis application when a finalized ordinary payment attempt exists for that session/payment scope. |
| 27 | How recover ambiguous Pay regular amount? | Reuse the payment-intent idempotency key and read durable payment attempt/provider session state through Payment Orchestrator/Central PMS. |
| 28 | How prevent duplicate evidence upload? | Upload-session idempotency, object hash duplicate detection, one accepted evidence reference per decision/evidence type, and fail-closed scan states. |
| 29 | Can continuation token be used after completed payment? | It may resolve to read-only terminal status until token expiry, but must not allow mutation or new payment/application. |
| 30 | Can token be reused across another parking session? | No. It must be scoped to one parking session and one canonical decision/payment context. |
| 31 | What file types are allowed? | Unresolved. Candidate set is JPEG/PNG/HEIC only after security/product approval. |
| 32 | What maximum size is allowed? | Unresolved policy/implementation decision. |
| 33 | What image dimensions are required? | Unresolved policy/implementation decision. |
| 34 | Is camera capture preferred but upload allowed? | Yes, target decision: camera preferred, upload allowed. |
| 35 | How handle EXIF? | Strip EXIF/unnecessary metadata before protected storage or reviewer readback. Not implemented. |
| 36 | How handle malware scanning? | Must fail closed for statutory reviewability; not implemented. |
| 37 | How detect duplicates? | Store a content hash for duplicate detection only; hash is not entitlement authority. |
| 38 | How long retain evidence? | Unresolved legal/privacy policy. Do not invent a period. |
| 39 | Who can retrieve evidence? | Only authorized Operator Console reviewers/service paths with scoped review permission. |
| 40 | How audit access events? | Add audit on upload, scan, reviewer read-token issue, read-token use, redaction, purge, and failed access. |
| 41 | How prevent long-lived/reusable signed URLs? | Short TTL, single-use or bounded-use tokens where supported, token hash persistence, scoped object keys, and access audit. |
| 42 | What if storage/scanning unavailable? | Do not create an approvable statutory request; show safe retry/ordinary-payment path. |

## 14. Required Architecture Trace

Target privilege-applied path:

1. Parking-session resolution through WebPay -> Payment Orchestrator -> Central PMS.
2. Payment Orchestrator requests ordinance availability from Central PMS.
3. WebPay shows only covered entitlement types from authoritative availability.
4. WebPay collects controlled document type for the selected entitlement.
5. WebPay captures/uploads ID image through Payment Orchestrator evidence-upload API.
6. Protected storage records bytes outside PostgreSQL.
7. Central PMS records opaque evidence reference and scan/metadata state.
8. Payment Orchestrator submits canonical statutory request only after required evidence is durably stored and safe.
9. Payment Orchestrator issues opaque continuation token and redirects to review page.
10. Review page resolves token through Payment Orchestrator and reads Central PMS state via GET.
11. Operator Console reviews canonical decision and authorized evidence access.
12. If approved, WebPay submits/recover application intent through Payment Orchestrator.
13. Central PMS creates/reuses application-v1 and applied tariff snapshot.
14. WebPay displays authoritative applied payable basis.
15. WebPay explicitly submits payment using applied snapshot, final amount, currency, and decision/application ids.
16. Payment Orchestrator validates through Central PMS and creates/reuses payment/provider handoff.
17. Payment finality is recorded by Central PMS.
18. Fiscal issuance proceeds through POS Server.
19. WebPay displays POS Server-owned authoritative Sales Invoice presentation.

Target pending-review regular-payment path:

1. Pending review state remains open and payment stays disabled by default.
2. Customer explicitly selects Pay regular amount.
3. WebPay confirms the privilege will not apply to this payment.
4. Payment Orchestrator submits ordinary payment intent with ordinary payable basis and a stable idempotency key.
5. Central PMS creates/reuses one ordinary payment attempt.
6. Central PMS records or later classifies the pending statutory request as inapplicable to that completed payment.
7. Later approval cannot mutate payment, fiscal issuance, or Sales Invoice.
8. Fiscal issuance reflects the amount actually paid.

## 15. Verdict Matrix

| Area | Verdict | Evidence |
| --- | --- | --- |
| Ordinance-availability WebPay consumer | PARTIALLY_SUPPORTED | Central PMS route exists; Payment Orchestrator/WebPay consumer missing. |
| Document-type control | CONTRADICTED_BY_CURRENT_BEHAVIOR | WebPay renders a free-text `idDocumentType` input with placeholder `OSCA, PWD ID, or equivalent`. |
| Evidence upload | NOT_SUPPORTED | WebPay submits `evidenceCaptureRequested: false`; no upload route found. |
| Evidence storage | PARTIALLY_SUPPORTED | DB supports evidence references; protected object-storage integration absent. |
| Reviewer evidence access | PARTIALLY_SUPPORTED | Operator Console metadata list/capture exists; no secure image retrieval found. |
| Service authentication | CONTRADICTED_BY_CURRENT_BEHAVIOR | Central PMS requires service identity/permission; Payment Orchestrator statutory client does not visibly send it. |
| Continuation token | NOT_SUPPORTED | No server-issued opaque continuation token route/model found. |
| Continuation page | NOT_SUPPORTED | Current recovery is app localStorage, not opaque review URL. |
| Browser restart recovery | PARTIALLY_SUPPORTED | Versioned localStorage recovery exists but uses internal ids and is not continuation-token based. |
| Pay-regular-amount action | NOT_SUPPORTED | No explicit pending-review regular-payment action/confirmation found. |
| Approval/payment race protection | NOT_SUPPORTED | No durable inapplicability/race command found. |
| Late-approval handling | NOT_SUPPORTED | No guard found that prevents application after ordinary payment finality. |
| Payment/fiscal linkage | PARTIALLY_SUPPORTED | Payment/fiscal/Sales Invoice path works; pending privilege inapplicability linkage missing. |
| Privacy and retention | PARTIALLY_SUPPORTED | Metadata minimization exists; file validation, scanning, retention, signed access unresolved. |
| Automated test coverage | PARTIALLY_SUPPORTED | Current tests cover statutory proxy/readback/payment/recovery; missing availability/evidence/continuation/pay-regular/race coverage. |
| Manual test readiness | NOT_SUPPORTED | Gaps block controlled UAT. |

Overall verdict: PARTIALLY_SUPPORTED.

## 16. Gap Matrix

| ID | Gap | Severity | Layer | Nature | Owner | Prerequisite | Blocks | Recommended bounded remediation |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| G-01 | Payment Orchestrator statutory Central PMS calls lack accepted service identity/permission and can expose internal identity error text. | CRITICAL | Payment Orchestrator/Central PMS | Contract/security | Codex G | Existing Central PMS RBAC policy | implementation, merge, UAT, production | Add service-auth headers/token per existing Central PMS RBAC convention and map downstream auth failures to customer-safe errors. |
| G-02 | WebPay does not consume ordinance availability before showing Senior/PWD request. | CRITICAL | WebPay/Payment Orchestrator/Central PMS | Authority gating | Codex G | G-01 | implementation, UAT, production | Add WebPay-facing availability proxy and hide request when Central PMS returns unavailable/ambiguous/unsupported. |
| G-03 | WebPay free-text document type permits wrong entitlement/document pairing. | HIGH | WebPay UI/API | Validation/privacy | Codex G | G-02 preferred | implementation, UAT, production | Replace free text with entitlement-specific controlled enum and safe equivalent-document description. |
| G-04 | Secure ID photo capture/upload is absent. | CRITICAL | WebPay/Payment Orchestrator/Central PMS/storage | Evidence/security | Codex G plus storage owner | G-02, G-03 | implementation, UAT, production | Add evidence-upload contract, protected storage seam, scan state, and opaque reference before decision submission. |
| G-05 | Operator Console has no authorized raw image retrieval path. | HIGH | Central PMS/Operator Console/storage | Reviewer access/security | Codex G or Operator Console owner | G-04 | UAT, production | Add short-lived reviewer evidence access with audit and no direct object-key exposure. |
| G-06 | No opaque continuation token or review page. | HIGH | Payment Orchestrator/WebPay | Recovery/privacy | Codex G | G-01, G-04 | implementation, UAT, production | Add token issue/resolve model and WebPay review page using GET readback only. |
| G-07 | Current browser recovery persists internal IDs. | MEDIUM | WebPay | Privacy/recovery | Codex G | G-06 | UAT, production | Keep as internal fallback only; public continuation must be opaque token based. |
| G-08 | Pay regular amount while pending is not implemented. | HIGH | WebPay/Payment Orchestrator/Central PMS | Payment/finality | Codex G | G-06 | implementation, UAT, production | Add explicit UI confirmation and backend command/linkage for ordinary payment while review pending. |
| G-09 | Approval/payment race rule is missing. | CRITICAL | Central PMS/Payment Orchestrator | Concurrency/finality | Codex G with Central PMS owner | G-08 | implementation, UAT, production | Central PMS transactionally arbitrates ordinary payment vs payable-basis application; loser receives deterministic inapplicable/conflict readback. |
| G-10 | Late approval after completed ordinary payment is not classified. | HIGH | Central PMS/Operator Console | Finality/audit | Central PMS owner | G-09 | UAT, production | Add no-longer-applicable classification and readback after ordinary payment finality. |
| G-11 | Evidence file validation, scan, EXIF stripping, max size/dimensions unresolved. | HIGH | Security/storage | Policy/security | Security/product plus Codex G | G-04 | UAT, production | Freeze file policy and implement fail-closed validation/scan metadata. |
| G-12 | Evidence retention period unresolved. | MEDIUM | Privacy/database/storage | Policy | Privacy/compliance | G-04 | production | Define retention/redaction/purge policy; do not block engineering stubs that preserve configurable retention. |
| G-13 | Payment/fiscal audit linkage for paid-without-privilege is missing. | MEDIUM | Central PMS/POS fiscal handoff | Audit/fiscal | Central PMS owner | G-08, G-09 | UAT, production | Link decision/evidence/token/payment/fiscal refs and preserve actual paid amount for Sales Invoice. |
| G-14 | Automated tests do not cover availability/evidence/continuation/pay-regular/race scenarios. | MEDIUM | Tests | Proof | Codex G | Feature slices | merge, UAT | Add focused unit/integration/browser tests per slice. |
| G-15 | Controlled UAT is not ready. | HIGH | Environment/manual | Operational | Darwin/engineering | G-01 through G-14 | controlled UAT, production | Run integrated walkthrough only after implementation slices pass. |

## 17. Minimum Target Architecture

| Concern | Frozen target |
| --- | --- |
| Browser direction | Browser calls Payment Orchestrator only. |
| Ordinance availability | Payment Orchestrator proxies Central PMS availability and returns browser-safe availability. |
| Request visibility | WebPay shows request only for active applicable ordinance and covered entitlement types. |
| Document type | WebPay/Payment Orchestrator enforce entitlement-specific controlled values. |
| Evidence upload | Browser uploads/captures image through Payment Orchestrator; Central PMS-authoritative evidence storage records opaque reference and scan state. |
| Evidence storage | Protected object storage, not PostgreSQL blobs or Base64 DTOs. |
| Reviewer access | Operator Console requests short-lived authorized access through Central PMS; access is audited. |
| Service auth | Payment Orchestrator authenticates to Central PMS as WebPay service identity with `statutory-discounts.decision.submit.webpay` and read permissions. |
| Continuation | Payment Orchestrator issues opaque resume token and resolves it server-side. |
| Review page | WebPay review page uses opaque token and GET readback, not decision POST. |
| Pending payment | Pay regular amount requires explicit confirmation and uses ordinary basis. |
| Race authority | Central PMS arbitrates approval/application vs ordinary payment. |
| Finality | Later approval cannot mutate a completed ordinary payment or Sales Invoice. |
| Fiscal | POS Server Sales Invoice reflects amount actually paid and remains authoritative. |

## 18. Bounded Implementation Sequence

1. Payment Orchestrator to Central PMS service-authentication correction.
2. WebPay ordinance-availability consumer and request visibility gate.
3. Controlled entitlement-specific document-type dropdown.
4. Evidence upload contract and protected storage seam.
5. Operator Console authorized evidence retrieval.
6. Canonical evidence-reference linkage from WebPay evidence to Central PMS decision.
7. Continuation-token issue and resolve API.
8. WebPay continuation page and durable recovery.
9. Pay regular amount command and confirmation.
10. Approval-versus-payment race enforcement.
11. Late-approval inapplicability classification.
12. Payment and fiscal audit linkage.
13. Full integrated walkthrough.
14. Controlled UAT.

Parallelization guidance:

- Tasks 2 and 3 can proceed after Task 1 when DTO fields are frozen.
- Tasks 4 and 5 can be designed in parallel but must converge on the same storage and authorization model before implementation is complete.
- Tasks 7 and 8 can proceed after Tasks 1 and 4 because the continuation token must not point at missing evidence.
- Tasks 9, 10, and 11 must be sequenced together because payment finality and late approval are one race contract.
- Task 12 should follow the race contract.
- Integrated walkthrough and controlled UAT must wait for all critical/high gaps.

## 19. Recommended First Implementation Task

Persona: Codex G

Repository: `D:\SourceCodes\ExitPass`

Base branch: `dev`

Proposed branch: `feature/webpay-statutory-service-auth-safe-errors`

Precise bounded scope:

- Fix Payment Orchestrator statutory Central PMS client authentication for submit/read/application/availability readiness using the existing Central PMS RBAC convention.
- Add or confirm WebPay service identity/permission configuration for `statutory-discounts.decision.submit.webpay` and statutory read.
- Map Central PMS RBAC/auth/service-channel failures to customer-safe WebPay errors.
- Prove the browser does not receive `CENTRAL_PMS_AUTHENTICATED_ACTOR_REQUIRED`, internal route details, service identity ids, permission headers, or raw downstream body.

Prerequisites:

- Existing Central PMS RBAC policies remain unchanged.
- No direct browser-to-Central PMS authentication.

Explicit non-goals:

- No WebPay ordinance UI.
- No document type UI.
- No evidence upload.
- No continuation token.
- No pay-regular command.
- No Central PMS schema change.
- No APT/POS Server changes.

Tests:

- Payment Orchestrator `CentralPmsWebPayClientTests` for service identity/permission forwarding.
- Payment Orchestrator endpoint tests for customer-safe errors.
- Central PMS `StatutoryDiscountDecisionApiAccessPolicyIntegrationTests` as read-only regression.
- Security scan over changed files for raw authorization/header/error leakage.

Manual-testing requirement:

- Significant manual testing for this first implementation task: No, if automated service-auth and safe-error tests pass.
- Significant manual testing after the complete feature implementation: Yes.

## 20. Automated Test Requirements

Future implementation must add or extend:

- Payment Orchestrator service-auth tests.
- Payment Orchestrator availability proxy tests.
- WebPay availability and request-visibility tests.
- WebPay document-type selection tests.
- Evidence upload validation, scan, storage, duplicate, and failure tests.
- Operator Console authorized evidence read tests.
- Continuation-token issue/resolve/expiry/revocation tests.
- Browser refresh/restart/multiple-tab continuation tests.
- Pay-regular confirmation and ordinary payment idempotency tests.
- Approval/payment race tests.
- Late-approval inapplicability tests.
- Fiscal linkage tests proving Sales Invoice reflects the amount actually paid.

## 21. Manual Test Requirements

Significant manual testing required for analysis merge: No.

Significant manual testing required after implementation: Yes.

Required later scenarios:

1. Active ordinance with required evidence.
2. No ordinance, request absent.
3. Document-type options by entitlement.
4. Camera capture.
5. File upload.
6. Invalid file type.
7. Oversized file.
8. Scanning failure.
9. Evidence-storage failure.
10. Request submission and continuation redirect.
11. Browser refresh.
12. Browser restart.
13. Multiple tabs.
14. Approval before payment.
15. Rejection.
16. Expired request.
17. Pay regular amount while pending.
18. Approval racing with ordinary payment.
19. Late approval after completed ordinary payment.
20. No retroactive adjustment.
21. Authoritative Sales Invoice for the amount actually paid.
22. Customer-safe authentication and downstream errors.

## 22. Authorization Status

- WebPay integration implementation: authorized.
- APT integration implementation: authorized.
- APT cash acceptance: not authorized.
- WebPay controlled UAT: not authorized.
- APT controlled UAT: not authorized.
- Production rollout: not authorized.

## 23. Appendices: Evidence Inventory

### Files Inspected

- `src/Services/WebPayUi/src/App.tsx`
- `src/Services/WebPayUi/src/webpay.ts`
- `src/Services/WebPayUi/src/types.ts`
- `src/Services/WebPayUi/src/statutoryRecovery.ts`
- `src/Services/WebPayUi/src/App.test.tsx`
- `src/Services/WebPayUi/src/webpay.test.ts`
- `src/Services/WebPayUi/src/statutoryRecovery.test.ts`
- `src/Services/WebPayUi/e2e/webpay-authoritative-sales-invoice.spec.ts`
- `src/Services/WebPayUi/e2e/fixtures/webpay-browser-smoke-server.mjs`
- `src/Services/PaymentOrchestrator/src/ExitPass.PaymentOrchestrator.Api/Endpoints/WebPayPaymentIntentEndpoints.cs`
- `src/Services/PaymentOrchestrator/src/ExitPass.PaymentOrchestrator.Contracts/WebPay/WebPayStatutoryDiscountDtos.cs`
- `src/Services/PaymentOrchestrator/src/ExitPass.PaymentOrchestrator.Contracts/WebPay/WebPayPaymentIntentRequest.cs`
- `src/Services/PaymentOrchestrator/src/ExitPass.PaymentOrchestrator.Application/Abstractions/Integrations/ICentralPmsWebPayClient.cs`
- `src/Services/PaymentOrchestrator/src/ExitPass.PaymentOrchestrator.Infrastructure/Integrations/CentralPmsWebPayClient.cs`
- `src/Services/CentralPms/src/ExitPass.CentralPms.Api/Endpoints/StatutoryDiscountDecisionEndpoints.cs`
- `src/Services/CentralPms/src/ExitPass.CentralPms.Api/Endpoints/OperatorConsoleStatutoryDiscountDraftEndpoints.cs`
- `src/Services/CentralPms/src/ExitPass.CentralPms.Api/Endpoints/WebPayReceiptPresentationEndpoints.cs`
- `src/Services/CentralPms/src/ExitPass.CentralPms.Contracts/StatutoryDiscounts/StatutoryDiscountDecisionDtos.cs`
- `src/Services/CentralPms/src/ExitPass.CentralPms.Contracts/OperatorConsole/OperatorConsoleStatutoryDiscountEvidenceDtos.cs`
- `src/Services/CentralPms/src/ExitPass.CentralPms.Application/Security/CentralPmsRbacPolicyCatalog.cs`
- `src/Services/CentralPms/src/ExitPass.CentralPms.Application/OperatorConsole/OperatorConsoleStatutoryDiscountEvidenceModels.cs`
- `src/Services/CentralPms/src/ExitPass.CentralPms.Application/OperatorConsole/OperatorConsoleStatutoryDiscountEvidenceService.cs`
- `src/Services/CentralPms/src/ExitPass.CentralPms.Infrastructure/OperatorConsole/OperatorConsoleStatutoryDiscountEvidenceRepository.cs`
- `src/Services/CentralPms/tests/ExitPass.CentralPms.IntegrationTests/Api/StatutoryDiscountDecisionApiAccessPolicyIntegrationTests.cs`
- `src/Services/PaymentOrchestrator/tests/ExitPass.PaymentOrchestrator.IntegrationTests/WebPay/WebPayPaymentIntentEndpointIntegrationTests.cs`
- `src/Services/PaymentOrchestrator/tests/ExitPass.PaymentOrchestrator.UnitTests/Application/UseCases/WebPayPaymentIntent/WebPayPaymentIntentHandlerTests.cs`
- `src/Services/OperatorConsoleUi/e2e/operator-console-ordinance-review.spec.ts`
- `D:\SourceCodes\exitpassdb_v1.2\build\generated\exitpass-full-object.generated.sql`
- `D:\SourceCodes\exitpassdb_v1.2\objects\schemas\discounts\tables\discounts.statutory_discount_decision_commands.sql`
- `D:\SourceCodes\exitpassdb_v1.2\objects\schemas\discounts\tables\discounts.statutory_discount_payable_basis_application_commands.sql`
- `D:\SourceCodes\exitpassdb_v1.2\objects\schemas\operator_console\tables\operator_console.statutory_discount_service_channel_reviews.sql`
- `D:\SourceCodes\exitpassdb_v1.2\objects\schemas\discounts\tables\discounts.discount_evidence_references.sql`
- `D:\SourceCodes\exitpassdb_v1.2\objects\schemas\core\tables\core.payment_attempts.sql`
- `D:\SourceCodes\exitpassdb_v1.2\objects\schemas\payments\tables\payments.provider_sessions.sql`

### Symbols and Routes Inspected

- `StatutoryDiscountDecisionEndpoints.MapStatutoryDiscountDecisionEndpoints`
- `WebPayPaymentIntentEndpoints.MapWebPayPaymentIntentEndpoints`
- `CentralPmsWebPayClient.SendStatutoryDiscountDecisionAsync`
- `ICentralPmsWebPayClient`
- `CentralPmsRbacPolicyCatalog`
- `OperatorConsoleStatutoryDiscountEvidenceRepository`
- `createStatutoryRecoveryRecord`
- `loadStatutoryRecoveryRecord`
- `saveStatutoryRecoveryRecord`
- `WebPayStatutoryDiscountDecisionRequest`
- `WebPayStatutoryDiscountDecisionResponse`
- `StatutoryDiscountParkingAvailabilityResponse`

### Canonical Tables and Columns Verified

- `discounts.statutory_discount_decision_commands`: `statutory_discount_decision_command_id`, `request_reference`, `parking_session_id`, `source_channel`, `entitlement_type`, `business_identity`, `idempotency_scope`, `idempotency_key`, `semantic_request_hash`, `semantic_hash_source_version`, `statutory_discount_validation_id`, `payable_basis_application_id`, `original_tariff_snapshot_id`, `applied_tariff_snapshot_id`, `decision_status`, `command_status`, `decision_result_status`, `result_classification`, `retryable`, `recovery_classification`, amount/currency fields, evidence flags, correlation/timestamps.
- `discounts.statutory_discount_payable_basis_application_commands`: `statutory_discount_payable_basis_application_command_id`, `statutory_discount_decision_command_id`, `business_identity`, `idempotency_key`, `command_status`, `result_classification`, `recovery_classification`, application/validation/tariff/policy ids, approved amount/currency fields, source channel.
- `operator_console.statutory_discount_service_channel_reviews`: `statutory_discount_decision_command_id`, submitted safe facts, `evidence_references`, reviewer attribution, review status, validation/policy authority ids.
- `discounts.discount_evidence_references`: evidence storage/reference/hash/status/redaction/retention/audit fields.
- `core.payment_attempts`: payment attempt identity, parking session, tariff snapshot, idempotency key, rail, amount, currency, status, timestamps.
- `payments.provider_sessions`: provider session identity, payment attempt, rail, provider refs, idempotency key, status, amount/currency, checkout/QR data, expiry, raw metadata reference, audit fields.

## 24. Final Statement

WEBPAY_STATUTORY_PRIVILEGE_CONTINUATION_EVIDENCE_IMPACT_ANALYSIS_COMPLETE.

