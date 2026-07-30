# ExitPass Statutory Evidence State, Retention, Deletion, and Failure Matrix v1.0

## Purpose

This document freezes evidence lifecycle dimensions, allowed transitions, retention posture, deletion posture, safe errors, and audit events for later statutory evidence runtime implementation.

## Lifecycle Dimensions

Do not encode the full evidence lifecycle as one overloaded status. Use separate controlled dimensions.

| Dimension | Values |
|---|---|
| Upload status | `NOT_AUTHORIZED`, `UPLOAD_AUTHORIZED`, `UPLOAD_IN_PROGRESS`, `UPLOADED`, `UPLOAD_EXPIRED`, `UPLOAD_CANCELLED`, `UPLOAD_FAILED` |
| Validation status | `NOT_VALIDATED`, `VALIDATING`, `VALIDATION_PASSED`, `VALIDATION_FAILED` |
| Scan status | `NOT_SCANNED`, `SCANNING`, `SCAN_PASSED`, `SCAN_FAILED`, `SCAN_TIMEOUT`, `SCAN_UNAVAILABLE` |
| Binding status | `UNBOUND`, `BOUND_TO_REQUEST`, `REVIEW_STARTED`, `TERMINAL_BOUND`, `BINDING_REJECTED` |
| Review access status | `NOT_REVIEWABLE`, `REVIEWABLE`, `REVIEW_ACCESSIBLE`, `ACCESS_REVOKED` |
| Retention status | `RETENTION_NOT_CALCULATED`, `RETENTION_ACTIVE`, `RETENTION_EXPIRED`, `HOLD_ACTIVE` |
| Deletion status | `NOT_ELIGIBLE`, `DELETION_PENDING`, `OBJECT_DELETED`, `METADATA_TOMBSTONED`, `DELETION_FAILED`, `DELETED` |
| Block status | `NOT_BLOCKED`, `BLOCKED` |

`REVIEWABLE` requires upload success, validation pass, scan pass, binding eligibility, retention policy readiness, and no deletion or block state.

## Transition Matrix

| From | To | Responsible actor/service | Retryable | Customer message | Reviewer message | Audit event |
|---|---|---|---|---|---|---|
| `NOT_AUTHORIZED` | `UPLOAD_AUTHORIZED` | Central PMS evidence control | Yes | You may upload the required document. | Not visible. | `EVIDENCE_UPLOAD_AUTHORIZATION_CREATED` |
| `UPLOAD_AUTHORIZED` | `UPLOAD_IN_PROGRESS` | Channel/object storage | Yes | Upload is in progress. | Not visible. | `EVIDENCE_UPLOAD_STARTED` |
| `UPLOAD_IN_PROGRESS` | `UPLOADED` | Channel plus Central PMS verification | Yes | Upload received. | Not visible. | `EVIDENCE_UPLOAD_COMPLETED` |
| `UPLOAD_AUTHORIZED` | `UPLOAD_EXPIRED` | Central PMS scheduler | Yes with new authorization | Upload expired. Please try again. | Not visible. | `EVIDENCE_UPLOAD_AUTHORIZATION_EXPIRED` |
| `UPLOAD_AUTHORIZED` | `UPLOAD_CANCELLED` | Channel or Central PMS | Yes before terminal request | Upload cancelled. | Not visible. | `EVIDENCE_UPLOAD_CANCELLED` |
| `UPLOADED` | `VALIDATING` | Validation worker | Yes | We are checking the file. | Evidence is not ready yet. | `EVIDENCE_CONTENT_VALIDATION_STARTED` |
| `VALIDATING` | `VALIDATION_PASSED` | Validation worker | No | File accepted. | Validation passed. | `EVIDENCE_CONTENT_VALIDATION_PASSED` |
| `VALIDATING` | `VALIDATION_FAILED` | Validation worker | Yes with replacement | The file could not be accepted. | Evidence failed validation. | `EVIDENCE_CONTENT_VALIDATION_FAILED` |
| `VALIDATION_PASSED` | `SCANNING` | Scan worker | Yes | We are checking the file. | Evidence is not ready yet. | `EVIDENCE_SCAN_STARTED` |
| `SCANNING` | `SCAN_PASSED` | Scan worker | No | File accepted. | Scan passed. | `EVIDENCE_SCAN_PASSED` |
| `SCANNING` | `SCAN_FAILED` | Scan worker | No for same file | The file cannot be used. | Evidence failed security scan. | `EVIDENCE_SCAN_FAILED` |
| `SCANNING` | `SCAN_TIMEOUT` | Scan worker | Yes | File check is delayed. | Evidence is not ready. | `EVIDENCE_SCAN_TIMEOUT` |
| `SCANNING` | `SCAN_UNAVAILABLE` | Scan worker | Yes | File check is temporarily unavailable. | Evidence is not ready. | `EVIDENCE_SCAN_UNAVAILABLE` |
| `UNBOUND` | `BOUND_TO_REQUEST` | Central PMS | Yes when semantics match | Evidence attached to the request. | Evidence attached. | `EVIDENCE_BOUND_TO_REQUEST` |
| `BOUND_TO_REQUEST` | `REVIEW_STARTED` | Operator Console review service | No replacement | Review started. | Evidence locked for review. | `EVIDENCE_REVIEW_STARTED` |
| `SCAN_PASSED` plus `VALIDATION_PASSED` | `REVIEWABLE` | Central PMS | No | Evidence is ready for review. | Evidence is ready for review. | `EVIDENCE_BECAME_REVIEWABLE` |
| `REVIEWABLE` | `REVIEW_ACCESSIBLE` | Central PMS preview auth | Yes | Not customer-visible. | Preview available briefly. | `EVIDENCE_PREVIEW_AUTHORIZED` |
| `REVIEW_ACCESSIBLE` | `ACCESS_REVOKED` | Central PMS or expiry | Yes with fresh authorization | Not customer-visible. | Preview expired. | `EVIDENCE_PREVIEW_EXPIRED` |
| `RETENTION_NOT_CALCULATED` | `RETENTION_ACTIVE` | Central PMS | No | Evidence retained under policy. | Evidence retained under policy. | `EVIDENCE_RETENTION_CALCULATED` |
| `RETENTION_ACTIVE` | `RETENTION_EXPIRED` | Retention worker | No | Evidence retention period ended. | Evidence eligible for deletion. | `EVIDENCE_RETENTION_EXPIRED` |
| Any non-deleted | `HOLD_ACTIVE` | Authorized hold actor | No | Evidence is under review hold when customer-visible. | Hold active. | `EVIDENCE_HOLD_PLACED` |
| `HOLD_ACTIVE` | prior retention status | Authorized hold actor | No | Hold released when customer-visible. | Hold released. | `EVIDENCE_HOLD_RELEASED` |
| `RETENTION_EXPIRED` | `DELETION_PENDING` | Deletion scheduler | Yes | Evidence is queued for deletion. | Evidence queued for deletion. | `EVIDENCE_DELETION_REQUESTED` |
| `DELETION_PENDING` | `OBJECT_DELETED` | Deletion worker | Yes | Evidence object deleted. | Object deleted. | `EVIDENCE_OBJECT_DELETED` |
| `OBJECT_DELETED` | `METADATA_TOMBSTONED` | Central PMS | Yes | Evidence record closed. | Metadata tombstoned. | `EVIDENCE_METADATA_TOMBSTONED` |
| `METADATA_TOMBSTONED` | `DELETED` | Central PMS | No | Evidence deleted. | Evidence deleted. | `EVIDENCE_DELETED` |
| `DELETION_PENDING` | `DELETION_FAILED` | Deletion worker | Yes | Deletion is delayed. | Deletion failed safely. | `EVIDENCE_DELETION_FAILED` |

Forbidden operations:

- preview before `REVIEWABLE`;
- approve before required evidence is `REVIEWABLE`;
- mutate bytes after `SCAN_PASSED`;
- replace evidence after `REVIEW_STARTED`;
- bind evidence across Site, request, or entitlement;
- delete while `HOLD_ACTIVE`;
- use deleted evidence in review or payment.

## Content Validation Allowlist

Default accepted content types:

- `image/jpeg`
- `image/png`

PDF posture:

- PDF is not accepted by default for first secure statutory ID-image runtime.
- PDF may be enabled only when a specific policy or document type requires it and the runtime includes PDF validation, malicious-content detection, password/encryption rejection, and safe preview handling.

Explicitly prohibited:

- SVG;
- HTML;
- JavaScript;
- executables;
- archives;
- office macro formats;
- animated image formats;
- polyglot files;
- password-protected files;
- encrypted PDFs;
- files whose detected type differs from declared type.

Client-declared MIME type is not authoritative.

## File Size and Dimension Posture

Default first runtime limits:

| Limit | Value | Basis |
|---|---|---|
| Maximum individual file size | 8 MiB | Large enough for readable phone photos while limiting upload, scan, and storage exposure. |
| Maximum evidence set size | 16 MiB | Supports front/back and supplemental items without broad data hoarding. |
| Minimum useful image dimensions | 800 x 500 pixels | Below this, ID text is usually unreadable for review. |
| Maximum image dimensions | 6000 x 6000 pixels | Limits memory pressure and decompression risk. |
| Maximum decoded pixels | 36 megapixels | Prevents decompression and preview resource abuse. |
| Maximum items per set | 4 | Bounded operational review surface. |

Changing these values requires product, security, and privacy approval. The runtime must apply decompression-bomb protection before full decode.

## Validation Rules

Validation must:

- detect actual content type from bytes;
- compare detected type to declared type;
- reject corrupt files;
- reject unsupported type;
- reject excessive size;
- reject unusably small images;
- reject excessive dimensions;
- reject decompression bombs;
- normalize safe filename metadata without preserving the original unrestricted filename;
- strip EXIF and location metadata before reviewable storage or preview;
- compute a cryptographic checksum internally;
- detect duplicate checksum within the same evidence set;
- preserve unknown document facts as unknown, not false.

## Malware and Security Scanning

Evidence remains quarantined until scanning succeeds.

Scanner contract:

- scan worker is owned by Central PMS evidence control or a directly governed security service;
- scanner receives internal object locator and evidence item reference;
- scan result is stored as controlled classification;
- raw scanner signatures are not exposed to customers or normal reviewers;
- scanner timeout returns retryable `SCAN_TIMEOUT`;
- scanner unavailable returns retryable `SCAN_UNAVAILABLE`;
- malicious or suspicious file returns non-retryable for the same file and requires replacement;
- unsupported file returns validation failure before scanner access where possible;
- scan engine/version reference is stored internally for audit;
- rescan is required after scanner signature/version policy demands it;
- reviewer access is blocked unless latest required scan passed.

## Safe Error Classifications

| Error | Customer-safe message | Operator-safe message | Retryable |
|---|---|---|---|
| `EVIDENCE_SERVICE_UNAVAILABLE` | Evidence upload is temporarily unavailable. You may continue with regular payment. | Evidence service unavailable. | Yes |
| `UPLOAD_AUTHORIZATION_UNAVAILABLE` | Upload cannot start right now. | Upload authorization unavailable. | Yes |
| `UPLOAD_AUTHORIZATION_EXPIRED` | Upload expired. Please try again. | Upload authorization expired. | Yes |
| `UPLOAD_INCOMPLETE` | Upload did not finish. | Upload incomplete. | Yes |
| `FILE_TOO_LARGE` | The file is too large. | File exceeds configured limit. | Yes with smaller file |
| `UNSUPPORTED_FILE_TYPE` | This file type is not accepted. | Unsupported detected content type. | Yes with supported file |
| `CORRUPT_FILE` | The file could not be read. | Decode failed. | Yes with replacement |
| `CONTENT_TYPE_MISMATCH` | The file type could not be confirmed. | Declared and detected type mismatch. | Yes with replacement |
| `MALWARE_DETECTED` | The file cannot be used. | Security scan failed. | No for same file |
| `SCAN_UNAVAILABLE` | File checking is temporarily unavailable. | Scanner unavailable. | Yes |
| `SCAN_TIMEOUT` | File checking is delayed. | Scanner timeout. | Yes |
| `VALIDATION_FAILED` | The file cannot be accepted. | Content validation failed. | Yes with replacement |
| `EVIDENCE_NOT_REVIEWABLE` | Evidence is not ready for review. | Validation or scan incomplete. | Yes |
| `EVIDENCE_REFERENCE_UNKNOWN` | Evidence reference was not found. | Unknown opaque reference. | No |
| `EVIDENCE_SCOPE_MISMATCH` | Evidence does not belong to this request. | Site/request/entitlement mismatch. | No |
| `EVIDENCE_ALREADY_BOUND` | Evidence is already attached to another request. | Cross-request reuse attempt. | No |
| `EVIDENCE_EXPIRED` | Evidence expired. Please submit again. | Evidence expired. | Yes |
| `EVIDENCE_DELETED` | Evidence is no longer available. | Evidence deleted. | No |
| `EVIDENCE_ACCESS_DENIED` | You are not authorized to view this evidence. | Access denied. | No |
| `HOLD_ACTIVE` | Evidence is under hold. | Hold prevents deletion. | No |
| `RETENTION_POLICY_MISSING` | Evidence collection is not available for this request. | Retention policy missing. | No for production |
| `DELETION_PENDING` | Evidence is being deleted. | Deletion pending. | No |
| `DELETION_FAILED` | Evidence cleanup is delayed. | Deletion failed. | Yes |
| `OBJECT_MISSING` | Evidence cannot be loaded. | Metadata object missing. | No |
| `METADATA_OBJECT_MISMATCH` | Evidence cannot be confirmed. | Metadata and object mismatch. | No |

Errors must not expose storage locators, scanner signatures, credentials, raw provider errors, stack traces, checksums, or raw database errors.

## Retention Matrix

No numeric retention period is set in this contract. Each row requires a server-side retention class with approved duration before production collection.

| Scenario | Default posture | Deletion eligibility |
|---|---|---|
| Incomplete upload | Short cleanup class required | After upload authorization expiry and grace window. |
| Failed validation | Short cleanup class required | After customer retry window or terminal request. |
| Malware rejected | Quarantine class required | After security policy permits deletion or quarantine disposal. |
| Cancelled request | Cancellation class required | After cancellation finality and no hold. |
| Rejected eligibility | Rejected-review class required | After approved retention period and no hold. |
| Approved eligibility | Approved-review class required | After approved retention period and no hold. |
| Ordinary payment completed while review pending | Inapplicable-review class required | After inapplicability finality and no hold. |
| Applied benefit | Applied-benefit class required | After fiscal/audit retention period and no hold. |
| Expired approval | Expired-approval class required | After expiry finality and no hold. |
| Abandoned request | Abandoned class required | After abandonment classification and no hold. |
| Audit/investigation hold | Hold class overlays base class | Not eligible until hold released. |

Production evidence collection must fail closed with `RETENTION_POLICY_MISSING` when no approved retention class can be selected.

## Deletion and Reconciliation

Deletion scheduler responsibilities:

- query deletion-eligible metadata;
- verify no active hold;
- verify retention expired;
- block review access before object delete;
- delete object through least-privilege service identity;
- confirm deletion or record retryable failure;
- tombstone metadata;
- emit audit events;
- preserve minimal non-sensitive audit facts.

Reconciliation detects:

- metadata with missing object;
- object without metadata;
- wrong checksum;
- wrong size;
- unexpected object state;
- expired upload residue;
- deletion pending too long;
- hold inconsistency;
- environment mismatch.

Safe remediation:

- quarantine;
- retry;
- deletion;
- operator/security review;
- block review access;
- audit event.

Reconciliation must never attach orphan objects to statutory requests automatically.

## Audit Event Catalog

| Event | Safe contents |
|---|---|
| `EVIDENCE_UPLOAD_AUTHORIZATION_CREATED` | upload operation id, evidence item id, actor class, scope classification |
| `EVIDENCE_UPLOAD_AUTHORIZATION_EXPIRED` | upload operation id, evidence item id |
| `EVIDENCE_UPLOAD_STARTED` | upload operation id |
| `EVIDENCE_UPLOAD_COMPLETED` | evidence item id, size class, detected type |
| `EVIDENCE_UPLOAD_CANCELLED` | evidence item id, reason classification |
| `EVIDENCE_CONTENT_VALIDATION_STARTED` | evidence item id |
| `EVIDENCE_CONTENT_VALIDATION_PASSED` | evidence item id, detected type |
| `EVIDENCE_CONTENT_VALIDATION_FAILED` | evidence item id, failure classification |
| `EVIDENCE_SCAN_STARTED` | evidence item id |
| `EVIDENCE_SCAN_PASSED` | evidence item id, scanner policy version ref |
| `EVIDENCE_SCAN_FAILED` | evidence item id, controlled classification |
| `EVIDENCE_BECAME_REVIEWABLE` | evidence item id, evidence set id |
| `EVIDENCE_BOUND_TO_REQUEST` | evidence set id, statutory request ref |
| `EVIDENCE_PREVIEW_AUTHORIZED` | evidence item id, reviewer actor ref, expiry class |
| `EVIDENCE_VIEWED` | evidence item id, reviewer actor ref, request ref |
| `EVIDENCE_ACCESS_DENIED` | evidence item id if known, actor ref, denial classification |
| `EVIDENCE_HOLD_PLACED` | evidence set/item id, hold reason classification |
| `EVIDENCE_HOLD_RELEASED` | hold id, release reason classification |
| `EVIDENCE_RETENTION_CALCULATED` | evidence set/item id, retention class, policy version |
| `EVIDENCE_DELETION_REQUESTED` | evidence set/item id, deletion reason |
| `EVIDENCE_OBJECT_DELETED` | evidence item id |
| `EVIDENCE_DELETION_FAILED` | evidence item id, failure classification |
| `EVIDENCE_METADATA_TOMBSTONED` | evidence item id |
| `EVIDENCE_RECONCILIATION_MISMATCH_DETECTED` | mismatch classification, environment classification |

Audit events must not include evidence bytes, signed URLs, object locators in broad logs, full filenames, full ID numbers, beneficiary identity, raw reviewer notes, scanner output, or storage credentials.

## Observability

Metrics:

- upload authorizations created;
- upload success rate;
- validation failures by controlled classification;
- scan latency;
- scan failures;
- reviewable evidence count;
- expired upload cleanup count;
- retention deletion backlog;
- orphan reconciliation count;
- preview authorization count;
- access-denial count.

Metrics must not use evidence reference, parking-session reference, or personal identifiers as high-cardinality labels.

