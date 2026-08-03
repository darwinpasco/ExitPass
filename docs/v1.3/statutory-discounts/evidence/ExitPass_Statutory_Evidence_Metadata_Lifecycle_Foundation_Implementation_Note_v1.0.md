# ExitPass Statutory Evidence Metadata Lifecycle Foundation Implementation Note v1.0

## Scope

I-012 implements the first runtime and persistence foundation for the secure statutory evidence contract. Central PMS owns evidence metadata, lifecycle state, opaque references, idempotency records, and privacy-safe events. PostgreSQL stores governed metadata only.

This slice does not implement evidence byte upload, object storage, signed upload authorization, signed preview authorization, malware scanning execution, image or PDF processing, OCR, biometric processing, browser UI, APT UI, Operator Console preview, retention workers, deletion workers, object deletion, controlled UAT, or production rollout.

## Canonical Database Objects

The canonical database repository adds these evidence metadata objects under the `discounts` schema:

- `statutory_evidence_retention_policies`
- `statutory_evidence_principal_scope_grants`
- `statutory_evidence_sets`
- `statutory_evidence_items`
- `statutory_evidence_operations`
- `statutory_evidence_events`

The generated canonical DDL is regenerated from source objects. The model is additive and does not replace the existing legacy `discount_evidence_references` compatibility table.

## Evidence Set

An evidence set has a server-issued opaque `evidence_set_reference` and is bound to exactly one statutory decision command, parking session, Site, Site Group, entitlement type, source channel, required document profile, and retention policy version. A unique active-set constraint prevents duplicate active evidence sets for the same statutory decision command.

Create-set authority is derived from `discounts.statutory_discount_decision_commands` joined to `core.parking_sessions`. The request body Site, Site Group, parking session, entitlement, validation ID, and source channel are consistency checks only. If a caller supplies values that do not match the durable statutory request binding, Central PMS rejects the operation and records a privacy-safe denial event without creating evidence metadata or an idempotency success row.

Production-style creation fails closed unless the requested retention class and policy version resolve to an approved enabled retention policy for the requested environment. No default retention duration is invented by this task.

## Evidence Item

An evidence item has a server-issued opaque `evidence_item_reference` under one evidence set. Items store controlled metadata only:

- document type
- item role
- media/profile posture
- upload status
- validation status
- scan status
- reviewability status
- binding status
- retention status
- deletion status
- hold posture

The runtime accepts `SENIOR_CITIZEN_ID`, `PWD_ID`, `AUTHORIZATION_LETTER`, and `SUPPORTING_DOCUMENT`. The database enum reserves `OTHER` for a later governed profile, but this slice rejects it because no governance rule exists yet.

## Lifecycle Dimensions

Lifecycle is intentionally separated:

- upload: `NOT_AUTHORIZED`, `AUTHORIZED`, `UPLOADING`, `UPLOADED`, `FAILED`, `EXPIRED`, `CANCELLED`
- validation: `NOT_STARTED`, `PENDING`, `PASSED`, `FAILED`, `UNSUPPORTED`
- scan: `NOT_STARTED`, `PENDING`, `PASSED`, `FAILED`, `UNAVAILABLE`, `TIMEOUT`
- reviewability: `NOT_REVIEWABLE`, `REVIEWABLE`, `LOCKED_FOR_REVIEW`, `REVIEW_COMPLETED`
- binding: `UNBOUND`, `BOUND`, `REJECTED`, `SUPERSEDED`
- retention: `POLICY_REQUIRED`, `ACTIVE`, `ELIGIBLE_FOR_DELETION`, `HELD`, `EXPIRED`, `TOMBSTONED`
- deletion: `NOT_REQUESTED`, `REQUESTED`, `IN_PROGRESS`, `DELETED`, `FAILED`, `BLOCKED_BY_HOLD`, `OBJECT_MISSING`

Review lock prevents item addition or replacement through this metadata API. Tombstoned or deleted evidence is not returned to an active reviewable state.

## API Surface

Central PMS exposes only metadata operations:

- `POST /v1/internal/statutory-discounts/evidence/sets`
- `POST /v1/internal/statutory-discounts/evidence/sets/{evidenceSetReference}/items`
- `GET /v1/internal/statutory-discounts/evidence/sets/{evidenceSetReference}`
- `POST /v1/internal/statutory-discounts/evidence/sets/{evidenceSetReference}/lock-for-review`
- `POST /v1/internal/statutory-discounts/evidence/sets/{evidenceSetReference}/hold`
- `POST /v1/internal/statutory-discounts/evidence/sets/{evidenceSetReference}/hold/release`
- `POST /v1/internal/statutory-discounts/evidence/sets/{evidenceSetReference}/deletion-request`

No route accepts evidence bytes, Base64, multipart content, object keys, signed URLs, preview requests, OCR output, scanner payloads, or provider credentials.

## Authorization

The bounded policy surface is:

- `CentralPmsStatutoryEvidenceCaptureMetadata` with `statutory-discounts.evidence.capture`
- `CentralPmsStatutoryEvidenceViewMetadata` with `statutory-discounts.evidence.view`
- `CentralPmsStatutoryEvidenceHoldManage` with `statutory-discounts.evidence.hold`
- `CentralPmsStatutoryEvidenceDeletionRequest` with `statutory-discounts.evidence.delete-request`

Possession of an opaque evidence reference is not authorization. Every route requires a named Central PMS policy and server-side Site/Site Group scope authorization.

The scope source is server-owned:

- Create-set resolves the statutory decision command and parking session server-side, derives Site, Site Group, entitlement, source channel, and validation binding, then checks the authenticated actor against `discounts.statutory_evidence_principal_scope_grants`.
- Evidence-set and evidence-item operations resolve the opaque reference to the durable `statutory_evidence_sets` binding, then authorize against the bound Site and Site Group. Route, header, query, and request body values do not become authority.
- Capture operations require `capture_allowed` for the actor and scope. Item capture also requires the actor source channel to match the evidence set source channel.
- Read operations require `view_allowed` for the actor and bound scope. Hold authority alone does not grant read authority.
- Lock-for-review requires `review_lock_allowed` for the reviewer actor and bound scope.
- Hold placement and release require `hold_allowed`; hold blocks deletion state changes but does not broaden read access.
- Deletion request requires `deletion_request_allowed`; deletion permission does not bypass Site/Site Group scope.

WebPay and APT service identities are bounded to metadata capture for their authorized statutory request and Site/Site Group scope. They do not receive reviewer, hold, deletion, or preview authority from this slice. Operator Console actors may view or lock metadata only when the evidence view/review policy and server-derived scope grant allow it; this does not create payable-basis application authority. Ordinary payment credentials, POS Server credentials, fiscal-document permissions, and Management Platform coverage-read permission alone receive no evidence metadata authority.

Malformed references, unknown references, and cross-scope attempts return safe denial behavior and append privacy-safe events. Denial events do not store evidence bytes, raw request bodies, object locators, checksums, credentials, full customer identity, or signed URLs.

## Idempotency And Concurrency

Metadata operations use an operation scope, idempotency key, and semantic request hash. The same key with the same semantics returns the governed original result. The same key with changed semantics records a privacy-safe conflict event and returns a safe conflict without mutating evidence metadata.

Correlation IDs are recorded for support and traceability but are not idempotency identities.

## Audit And Security Events

The event table records append-oriented privacy-safe classifications for creation, replay, semantic conflict, lifecycle transitions, hold, deletion request, access denied, malformed lookup, cross-scope attempt, and invalid transition cases.

Events must not contain evidence bytes, Base64, full ID numbers, names, birth dates, addresses, biometric data, signed URLs, object keys, checksum values, raw request bodies, credentials, authorization headers, or stack traces.

## Privacy Exclusions

Public DTOs expose only opaque references and safe lifecycle metadata. They do not expose internal storage locators, checksum values, signed URLs, scanner vendor payloads, internal database identifiers, actor personal details, raw statutory identity, reviewer notes, or object-provider errors.

POS Server remains evidence-free.

## Ordinary Payment Preservation

This slice does not alter ordinary WebPay payment, ordinary APT cash, payable-basis application, POS fiscal issuance, exit authorization, Operator Console approval/rejection authority, or Management Platform coverage reads.

Evidence metadata failure may leave a statutory path pending or rejected according to policy, but it does not remove ordinary non-statutory payment availability.

## Future Work

Separate future tasks are required for:

- evidence upload authorization and object-storage adapter
- validation and malware scanning worker
- WebPay/APT/Operator Console upload consumers
- Operator Console secure preview
- retention, deletion, and reconciliation workers
- controlled UAT evidence runbook

Controlled UAT and production rollout remain unauthorized.
