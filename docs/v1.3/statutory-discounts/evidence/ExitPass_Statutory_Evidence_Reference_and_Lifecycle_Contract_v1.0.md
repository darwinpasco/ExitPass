# ExitPass Statutory Evidence Reference and Lifecycle Contract v1.0

## Purpose

This document freezes the secure statutory evidence contract for Senior Citizen and PWD parking privileges. It is a contract-only slice. It does not implement upload, retrieval, preview, storage, deletion, database migration, WebPay UI, APT UI, Operator Console preview, or object-storage runtime.

The contract preserves the merged statutory lifecycle:

1. A statutory parking-privilege request is created only after Central PMS confirms an active local ordinance for the parking Site.
2. Required evidence is collected before Operator Console review.
3. Operator Console approves or rejects beneficiary eligibility only.
4. WebPay or APT requests payable-basis application at payment time.
5. Central PMS calculates the authoritative final payable basis.
6. POS Server fiscalizes only the final amount actually paid.

Ordinary payment remains available when evidence services are unavailable.

## Baseline Reviewed

Primary worktree: `D:\SourceCodes\ExitPass-I-StatutoryEvidenceContract`

Branch: `docs/statutory-evidence-reference-retention-contract`

Application baseline: `18fba66e04a84c40b9b6be1c90d37d882c31f4be`

Canonical database generated baseline inspected read-only: `D:\SourceCodes\exitpassdb_v1.2\build\generated\exitpass-full-object.generated.sql`

Canonical DB branch and commit inspected read-only: `develop`, `7a785fd93d592b019fbb6ac6bbdf4fc82d8485dc`

Retired database exclusion: `D:\SourceCodes\ExitPass_DBv1.2` was not used.

Merged files and areas reviewed include:

- `docs/v1.3/statutory-discounts/security/ExitPass_Statutory_Privilege_Permission_Catalog_and_Enforcement_Contract_v1.0.md`
- `docs/v1.3/statutory-discounts/security/ExitPass_Statutory_Privilege_RBAC_Persistence_and_Management_Platform_Handoff_v1.0.md`
- `docs/v1.3/webpay/reviews/ExitPass_WebPay_Statutory_Privilege_Continuation_Evidence_and_Payment_Impact_Analysis_v1.0.md`
- `docs/v1.3/central-pms/implementation-slices/ExitPass_Central_PMS_Statutory_Discount_Local_Ordinance_Eligibility_Gate_Implementation_Note_v1.0.md`
- `src/Services/CentralPms/src/ExitPass.CentralPms.Contracts/StatutoryDiscounts/StatutoryDiscountDecisionDtos.cs`
- `src/Services/CentralPms/src/ExitPass.CentralPms.Contracts/OperatorConsole/OperatorConsoleStatutoryDiscountEvidenceDtos.cs`
- `src/Services/CentralPms/src/ExitPass.CentralPms.Application/OperatorConsole/OperatorConsoleStatutoryDiscountEvidenceService.cs`
- `src/Services/CentralPms/src/ExitPass.CentralPms.Infrastructure/OperatorConsole/OperatorConsoleStatutoryDiscountEvidenceRepository.cs`
- `src/Services/CentralPms/src/ExitPass.CentralPms.Api/Endpoints/OperatorConsoleStatutoryDiscountDraftEndpoints.cs`
- `src/Services/CentralPms/src/ExitPass.CentralPms.Application/Security/CentralPmsRbacPolicyCatalog.cs`
- `src/Services/PaymentOrchestrator/src/ExitPass.PaymentOrchestrator.Contracts/WebPay/WebPayStatutoryDiscountDtos.cs`
- `src/Services/WebPayUi/src/types.ts`
- `src/Services/WebPayUi/src/statutoryRecovery.ts`
- `src/Services/OperatorConsoleUi/src/types.ts`
- `src/Services/CentralPms/tests/ExitPass.CentralPms.UnitTests/Application/OperatorConsoleStatutoryDiscountEvidenceServiceTests.cs`
- canonical `discounts.discount_evidence_references`
- canonical `operator_console.statutory_discount_service_channel_reviews`

## Current State Inventory

Current implemented evidence fields:

- `StatutoryDiscountDecisionRequest.EvidenceCaptureRequested`
- `StatutoryDiscountDecisionRequest.EvidenceReferences`
- `StatutoryDiscountEvidenceReferenceRequest.EvidenceType`
- `StatutoryDiscountEvidenceReferenceRequest.CaptureMethod`
- `StatutoryDiscountEvidenceReferenceRequest.FileName`
- `StatutoryDiscountEvidenceReferenceRequest.ContentType`
- `StatutoryDiscountEvidenceReferenceRequest.SizeBytes`
- `StatutoryDiscountEvidenceReferenceRequest.StorageReference`
- `StatutoryDiscountEvidenceReferenceRequest.ReferenceNumberMasked`
- `StatutoryDiscountEvidenceReferenceRequest.VerificationStatus`
- `OperatorConsoleStatutoryDiscountEvidenceCaptureRequest` metadata fields
- `OperatorConsoleStatutoryDiscountEvidenceListResponse` metadata-only fields

Current target-only or incomplete fields:

- WebPay statutory DTOs support metadata-only evidence references but no upload authorization, completion, scan state, or opaque continuation token.
- Operator Console UI types expose evidence metadata and governing-policy evidence requirements but no secure preview authorization.
- `discounts.discount_evidence_references` stores metadata and coarse lifecycle status only.
- `operator_console.statutory_discount_service_channel_reviews.evidence_references` is JSON reference-only metadata.

Current persistence:

- `discounts.discount_evidence_references` has `discount_evidence_reference_id`, validation link, evidence type, storage type, storage reference, evidence hash, capture status, access classification, redaction status, retention policy code, retention expiry, capture actor, purge actor, audit fields, and row version.
- It does not model upload operation identity, evidence set identity, quarantine, validation status, malware scan status, preview authorization, hold status, deletion job status, object verification facts, or governed object-store access grants.

Current channel behavior:

- Operator Console legacy draft evidence capture stores metadata only and can mark evidence captured.
- WebPay currently has no secure ID-document image upload route and no object-storage adapter.
- APT secure evidence handling is not implemented in this repository.
- POS Server receives no statutory evidence image contract.

Current privacy gaps:

- Existing `StorageReference` fields are too broad for public evidence references and must not carry provider object locators at channel boundaries.
- Current WebPay recovery can store internal statutory command references; the future public continuation contract must use an opaque server-issued continuation reference.
- There is no runtime enforcement for image bytes being excluded from browser storage, APT SQLite, logs, payment payloads, or fiscal payloads.
- Retention duration and retention-class selection are not approved.
- Malware scanning, content validation, metadata stripping, and short-lived reviewer access are absent.

Contradictions and obsolete terms:

- Current Operator Console metadata capture uses `UPLOAD` without owning secure byte upload.
- Canonical `evidence_storage_ref` comments permit object keys or URI references; this contract forbids those as public references and treats any storage locator as internal-only.
- Current `CAPTURED` evidence status is insufficient for secure image review because upload completion, validation, and scanning are separate gates.

## Ownership Boundary

Central PMS is the evidence-control owner for statutory parking evidence.

Central PMS owns:

- issuing upload authorization;
- creating opaque evidence references;
- creating evidence set and item metadata;
- binding evidence to one statutory request;
- enforcing Site and Site Group access scope;
- enforcing actor and permission rules;
- recording lifecycle state;
- issuing short-lived reviewer preview authorization;
- enforcing retention, hold, deletion, and reconciliation;
- recording audit events.

Protected object storage owns:

- durable storage of evidence bytes;
- provider-level object durability;
- provider-level encryption, access logging, object lock/versioning posture where configured;
- provider delete or lifecycle operation execution.

Protected object storage does not own:

- statutory request binding;
- entitlement type;
- document-type authority;
- review status;
- reviewer authorization;
- retention policy;
- public evidence-reference meaning.

Channels are clients:

- WebPay, APT, and Operator Console request-initiation surfaces submit evidence.
- Operator Console Review consumes evidence after server authorization.
- Payment Orchestrator may broker WebPay browser traffic but does not become evidence authority.
- POS Server, HikCentral, payment providers, gate services, and fiscal payloads do not receive evidence bytes or protected evidence references by default.

## Opaque Reference Contract

Public statutory evidence references are server-issued UUID v4 values.

They are:

- non-sequential;
- unguessable for practical purposes;
- stable for the governed evidence item lifecycle;
- meaningless without a fresh server-side authorization check;
- not a signed token;
- not a storage locator;
- not a checksum.

They contain no:

- container name;
- internal object locator;
- Site identifier;
- Site Group identifier;
- parking-session identifier;
- entitlement type;
- document type;
- customer identifier;
- filename;
- provider identifier;
- expiry timestamp.

Separate identifiers:

| Identifier | Format | Scope | Public posture |
|---|---|---|---|
| `evidenceUploadOperationId` | UUID v4 | One upload attempt for one evidence item | May be returned to the submitting channel; not sufficient for read access. |
| `evidenceSetId` | UUID v4 | One statutory request evidence set | May be returned as an opaque request-bound reference. |
| `evidenceItemId` | UUID v4 | One document side or item | Public opaque evidence reference. |
| `statutoryRequestReference` | Existing request/decision reference | Statutory request or decision binding | Existing channel contract; not evidence read authority. |
| `evidencePreviewAuthorizationId` | UUID v4 | Short-lived review preview authorization | Internal or reviewer-channel only; never stored in PostgreSQL as a signed access URL. |

Do not overload one identifier for upload, item, set, request binding, and preview authorization.

## Evidence Set Model

One statutory request has exactly one evidence set. The evidence set contains one or more evidence items required by the frozen policy authority.

The set is bound to:

- one statutory request reference;
- one statutory decision command when created;
- one parking session;
- one Site;
- one Site Group when available;
- one entitlement type;
- one policy authority when available;
- one controlled document-requirement version.

Evidence reuse across unrelated parking sessions or statutory requests is prohibited. Idempotent replay of the same request may reuse the same completed evidence references when the request semantics match.

Evidence set completion requires:

- all required evidence items are present;
- each required item is uploaded;
- each uploaded item passed content validation;
- each uploaded item passed malware scanning;
- no required item is expired, deleted, cancelled, or failed;
- required equivalent-document descriptions are present and controlled;
- no item is bound to another statutory request.

The evidence set is immutable after review begins. Replacement after review begins requires cancelling the review and starting a new governed review cycle.

## Evidence Item Model

Controlled document types:

| Entitlement | Controlled document type | Description requirement |
|---|---|---|
| `SENIOR_CITIZEN` | `OSCA_ID` | None beyond controlled type. |
| `SENIOR_CITIZEN` | `EQUIVALENT_DOCUMENT` | Bounded safe description required from an approved option set. |
| `PWD` | `PWD_ID` | None beyond controlled type. |
| `PWD` | `EQUIVALENT_DOCUMENT` | Bounded safe description required from an approved option set. |

Item roles:

- `FRONT_IMAGE`: required for every ID-document image item.
- `BACK_IMAGE`: required only when the frozen policy, document type, or validation rule explicitly requires it.
- `SUPPLEMENTAL_DOCUMENT`: allowed only when the frozen policy requires or permits it.

Maximum item count:

- Default set maximum: 4 items.
- Default per document type: front and back image plus up to 2 supplemental items when policy permits.
- Policy may lower the limit.
- Raising the limit requires product, privacy, and security approval.

Duplicate behavior:

- Duplicate upload completion for the same `evidenceUploadOperationId` is idempotent if object facts match.
- Duplicate content checksum in the same evidence set is rejected as `DUPLICATE_EVIDENCE_ITEM` unless it is the same item replay.
- Duplicate content across unrelated requests is not reused or linked automatically.

## Required Metadata Matrix

| Field | Classification | Notes |
|---|---|---|
| `evidenceItemId` | Required | Public opaque UUID evidence reference. |
| `evidenceSetId` | Required | Groups evidence for one statutory request. |
| `evidenceUploadOperationId` | Required for upload attempts | Separate from item and set identities. |
| `statutoryRequestReference` | Required | Request/decision binding. |
| `statutoryDiscountDecisionCommandId` | Optional until decision exists | Required before review completion. |
| `parkingSessionId` | Required internal binding | Resource fact, not public evidence identity. |
| `siteId` | Required internal binding | Used for scope checks. |
| `siteGroupId` | Optional internal binding | Required when known. |
| `entitlementType` | Required | `SENIOR_CITIZEN` or `PWD`. |
| `controlledDocumentType` | Required | Controlled enum, not free text. |
| `equivalentDocumentDescriptionCode` | Required when equivalent document | Controlled safe option. |
| `itemRole` | Required | Front, back, or supplemental role. |
| `declaredContentType` | Optional | Client-declared; not authority. |
| `detectedContentType` | Required after validation | Server-detected. |
| `contentLengthBytes` | Required after upload | Server-verified. |
| `contentChecksum` | Required internal only | Cryptographic checksum; not shown in public errors. |
| `internalStorageLocator` | Required internal only after authorization | Never public, never logged broadly. |
| `storageProviderClass` | Required internal only | Provider-neutral class, not account detail. |
| `uploadStatus` | Required | See state matrix. |
| `validationStatus` | Required | See state matrix. |
| `scanStatus` | Required | See state matrix. |
| `bindingStatus` | Required | See state matrix. |
| `retentionStatus` | Required | See state matrix. |
| `deletionStatus` | Required | See state matrix. |
| `createdAt` | Required | Server timestamp. |
| `uploadCompletedAt` | Optional | Set after verified completion. |
| `reviewableAt` | Optional | Set only after validation and scan success. |
| `expiresAt` | Required for incomplete upload | Server-calculated. |
| `retentionClassCode` | Required before production collection | Server-selected. |
| `retentionPolicyVersion` | Required before production collection | Server-selected. |
| `retentionUntil` | Required after retention calculation | Null blocks production evidence collection unless class explicitly allows deferred calculation. |
| `holdStatus` | Required | Separate from access. |
| `deletedAt` | Optional | Tombstone timestamp. |
| `createdByActorRef` | Required safe actor reference | Human or service; no display name authority. |
| `lastTransitionActorRef` | Required safe actor/service reference | Actor or worker. |
| `rowVersion` | Required | Concurrency token. |

## Prohibited Field Matrix

Do not store in PostgreSQL, browser storage, APT SQLite, payment payloads, fiscal payloads, logs, or audit events:

- image bytes;
- Base64 evidence;
- OCR output;
- extracted full ID number;
- beneficiary name extracted from evidence;
- date of birth extracted from evidence;
- address extracted from evidence;
- facial templates;
- biometric data;
- unrestricted reviewer notes containing personal data;
- signed access URLs;
- provider credentials;
- encryption keys;
- unrestricted original filenames;
- client-supplied storage paths;
- scanner raw signatures;
- provider response bodies.

## Upload Protocol

Authoritative pattern:

1. Channel requests upload authorization from Central PMS evidence control.
2. Central PMS validates parking session, Site, Site Group, entitlement, policy availability, document type, actor, permission, and scope.
3. Central PMS creates one upload operation, one evidence item, and the evidence set if needed.
4. Central PMS issues a short-lived single-object delegated upload authorization using pre-signed POST semantics.
5. Channel uploads bytes directly to protected object storage.
6. Object remains quarantined and unreadable by reviewers.
7. Channel reports upload completion.
8. Central PMS independently verifies object existence, size, checksum, and detected content type.
9. Validation and malware scanning run.
10. Evidence item becomes reviewable only after all checks pass.
11. Statutory request submission references the opaque evidence set or item references.

Pre-signed POST semantics are selected because they can carry a provider-enforced content-length range, exact target object constraint, and approved content-type conditions without routing image bytes through ordinary JSON requests. A provider-specific delegated upload token may be used only if it enforces the same constraints. Pre-signed PUT is not the default because it is easier to misconfigure without content-length and form-policy restrictions.

## Upload Authorization

Upload authorization must be:

- short lived, default 10 minutes;
- scoped to one `evidenceItemId`;
- scoped to one internal object locator;
- scoped to a server-approved content-length range;
- scoped to an approved content type;
- single purpose;
- non-renewable after successful completion;
- invalid after expiry;
- invalid after cancellation;
- invalid after terminal request state;
- unusable for read, delete, or list;
- unusable for any other Site, parking session, statutory request, or entitlement.

Safe retry:

- Expired unused authorization returns `UPLOAD_AUTHORIZATION_EXPIRED`.
- The channel may request a new authorization for the same evidence item only while the request is not terminal and review has not begun.
- If bytes were uploaded after expiry, Central PMS must not bind or review the object; reconciliation may delete or quarantine it.

Permanent object-storage credentials are prohibited in browser code, APT configuration, Operator Console, logs, environment output, API responses, and support evidence.

## Channel Handling

### WebPay

- Browser asks Payment Orchestrator for WebPay evidence upload authorization.
- Payment Orchestrator calls Central PMS as a service principal.
- Bytes remain only in transient browser memory until upload completes.
- No Local Storage, Session Storage, IndexedDB, service worker cache, or Base64 evidence.
- Browser recovery may store only opaque request, evidence set, and evidence item references.
- Signed upload authorization is not stored in recovery state.
- Refresh before completion shows a safe restart or retry path.
- Duplicate tabs share no byte state; completed evidence references replay idempotently.
- Cancellation marks pending upload operations cancelled and does not create reviewable evidence.

### APT

- APT may capture from camera or file picker only after Central PMS upload authorization.
- No evidence image in encrypted SQLite, application data directory, print history, cash journal, logs, or payment payloads.
- If OS APIs require a temporary file, APT stores it in a restricted temporary location, uploads immediately, and deletes it best-effort after upload or cancellation.
- Secure cleanup on SSD storage is best-effort and cannot be represented as guaranteed physical erasure.
- Application restart resumes from opaque references only, not bytes.

### Operator Console Submission

- Staff-assisted submission uses the same Central PMS evidence-control owner.
- Staff actor must have evidence capture permission and matching Site or Site Group scope.
- Self-review prohibition from the RBAC contract applies when the same durable human actor initiated the request.
- Browser storage remains metadata-only.

### Operator Console Review

- Reviewer access requires fresh server-side authorization and evidence-view permission.
- Possession of `evidenceItemId` is never sufficient.
- Preview authorization is short lived and item-scoped.
- No download by default.
- No bulk export.
- No copy of evidence into review notes.
- Evidence access after request completion requires an authorized audit purpose.

## Binding Rules

Central PMS must reject evidence binding when:

- reference belongs to another Site;
- reference belongs to another Site Group where scope is material;
- reference belongs to another request;
- reference belongs to another entitlement type;
- evidence is expired;
- evidence is deleted or deletion pending;
- validation failed;
- malware scan failed or is incomplete;
- evidence set is incomplete;
- reference is unknown;
- object facts mismatch metadata;
- item is already bound to an unrelated request;
- client supplies an object locator or provider identifier.

Once review begins, evidence bytes and metadata that are legally material to review are immutable.

## Preview Authorization

Review presentation uses a short-lived read authorization issued by Central PMS after authorization.

Rules:

- scoped to one evidence item;
- expires quickly, default 5 minutes;
- no list, write, delete, or cross-object permission;
- not stored in PostgreSQL;
- not logged;
- not returned to WebPay, APT payment, Payment Orchestrator payment-intent, or POS Server fiscal payloads;
- not reusable after request scope changes or terminal access revocation.

HTTP posture:

- `Cache-Control: no-store`
- `Pragma: no-cache`
- restrictive referrer policy
- inline controlled preview allowed for reviewer UI
- forced download disabled by default
- content disposition must not expose the original filename
- frame and content security headers must prevent clickjacking and script execution around previews

## Retention Contract

No fixed legal retention duration is frozen here.

Production evidence collection is blocked until an approved retention policy exists. The policy is selected server-side from Site, jurisdiction, document type, and purpose. Clients cannot supply retention duration.

Rules:

- retention class code is stored;
- retention policy version is stored;
- `retentionUntil` is calculated server-side;
- policy changes do not silently shorten existing retention;
- shortening retention requires governed approval;
- extending retention requires an authorized policy or hold;
- failed, cancelled, abandoned, and malicious evidence must not be retained indefinitely.

Required retention categories:

- incomplete upload;
- failed validation;
- malware rejected;
- cancelled request;
- rejected eligibility;
- approved eligibility;
- ordinary payment completed while review was pending;
- applied benefit;
- expired approval;
- abandoned request;
- audit or investigation hold.

## Hold Contract

A hold prevents deletion but does not broaden evidence access.

Hold metadata:

- hold reference;
- reason classification;
- start timestamp;
- expiry or review timestamp;
- placed-by authorized actor;
- release authorization;
- audit event;
- safe customer visibility classification.

Reviewers cannot place arbitrary holds from the ordinary review screen. Hold placement belongs to an auditor, privacy, compliance, or support role with explicit permission.

## Deletion Contract

Deletion has two parts:

1. protected object deletion;
2. metadata lifecycle transition to tombstone.

Deletion flow:

1. Scheduler finds eligible evidence.
2. Central PMS checks retention, hold, terminal state, and access rules.
3. Worker requests object delete through least-privilege service identity.
4. Worker records delete confirmation or retryable failure.
5. Metadata transitions to tombstone with minimal retained audit facts.
6. Reconciliation checks for orphan objects or orphan metadata.

Deletion is idempotent. A deleted evidence item cannot be restored through ordinary review APIs.

The contract does not claim guaranteed secure erasure on SSD or object-storage backup media; deletion guarantees depend on provider lifecycle behavior and backup posture.

## Environment Separation

Storage environments:

- local development;
- automated tests;
- controlled UAT;
- production.

Synthetic evidence only is allowed outside approved controlled UAT or production processes.

Internal object locators must include environment separation, but public evidence references must not expose environment, tenant, Site, or provider details.

## Storage Security

Minimum requirements:

- encryption in transit;
- encryption at rest;
- private container or equivalent;
- public access disabled;
- object listing denied to channel clients;
- least-privilege service identities;
- provider access logging;
- key rotation posture;
- credential rotation posture;
- environment separation;
- governed versioning posture;
- documented deletion and backup limitations;
- disaster-recovery posture;
- regional or residency configuration approved for the environment;
- no consumer cloud synchronization tools.

## Future API Operations

Future operations must follow current ExitPass versioning under Central PMS and channel proxies where appropriate.

| Operation | Actor | Permission | Request fields | Response fields | Idempotency | Safe errors |
|---|---|---|---|---|---|---|
| Create upload authorization | WebPay/APT/OC submitter via service or human actor | evidence capture plus scope | request ref, parking session, entitlement, document type, item role, idempotency key | upload operation id, evidence item id, evidence set id, expiry, constrained upload form | Same request semantics returns same active authorization or item state | unavailable, policy unavailable, scope denied, retention missing |
| Complete upload | Uploading channel | evidence capture plus item ownership | upload operation id, evidence item id, declared size/checksum, idempotency key | lifecycle status, retry guidance | Same object facts returns same result | expired, incomplete, mismatch |
| Cancel upload | Uploading channel | evidence capture plus item ownership | upload operation id, evidence item id, reason | cancelled status | Replay returns cancelled | already reviewable, terminal |
| Read evidence metadata | Reviewer/auditor/support | evidence view or audit read | evidence set or item id | metadata only | Read-only | access denied, unknown |
| Bind evidence set | Channel/service request submitter | request submit plus evidence capture | request ref, evidence set id, decision command id | bound status | Same semantics replays | scope mismatch, incomplete |
| Issue preview authorization | Reviewer | evidence view plus scope | evidence item id, review id | short-lived access indirection | Replay may issue a new short-lived access event | not reviewable, denied |
| Record access | Central PMS/storage callback | system only | preview auth id, outcome | audit accepted | Idempotent by event id | mismatch |
| Place hold | Auditor/privacy/compliance | audit/evidence hold permission | evidence set/item id, reason, expiry/review time | hold active | Same semantics replays | denied |
| Release hold | Auditor/privacy/compliance | audit/evidence hold permission | hold id, reason | hold released | Same semantics replays | denied |
| Request deletion | Retention worker/privacy admin | deletion permission | evidence set/item id or retention batch | deletion pending/deleted | Same semantics replays | hold active, not eligible |
| Read lifecycle status | Submitter/reviewer/auditor by scope | read/detail/evidence/audit as applicable | evidence set/item id | safe lifecycle status | Read-only | denied, unknown |

No operation exposes provider object locators.

## Idempotency

Rules:

- replay with identical semantics returns the same governed result;
- replay does not create duplicate evidence metadata;
- replay does not create endless object locators;
- changed semantics under the same idempotency identity returns conflict;
- conflict response does not expose stored hashes or internal field differences;
- expired upload authorization does not become valid through replay;
- completed evidence cannot be silently overwritten;
- preview authorization replay may issue a new short-lived authorization only after a fresh authorization check and audit event.

## Production Gate

Production evidence runtime must remain blocked until all are implemented and tested:

- metadata schema promotion for upload/set/item/lifecycle states;
- protected object-storage adapter;
- upload authorization and completion;
- content validation;
- malware scanning;
- reviewer preview authorization;
- retention/hold/deletion worker;
- reconciliation worker;
- channel upload consumers;
- security and privacy review;
- controlled UAT.

