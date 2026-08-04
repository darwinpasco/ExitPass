# ExitPass Statutory Evidence Upload Authorization and Object Storage Implementation Note v1.0

## Scope

This slice implements the first upload runtime layer on top of the statutory evidence metadata foundation. Central PMS remains the evidence-control owner. Protected object storage provides byte durability only and is not statutory authority.

Implemented runtime capabilities:

- short-lived upload authorization for an existing evidence item
- server-generated internal object key assignment
- protected object-storage adapter abstraction
- S3-compatible signing adapter
- upload-authorization metadata persistence
- upload finalization through server-side object metadata verification
- content-type, content-length, checksum, expiry, lifecycle, idempotency, replay, and semantic-conflict enforcement
- server-derived Site, Site Group, request, session, entitlement, and source-channel scope enforcement reused from the metadata foundation
- privacy-safe upload audit and denial event posture

Not implemented in this slice:

- evidence byte proxying through Central PMS
- preview, download, or signed read authorization
- malware scanning execution
- OCR or biometric processing
- image recognition
- WebPay, APT, or Operator Console UI
- retention, deletion, or reconciliation workers
- controlled UAT
- production rollout

## API Contract

Upload authorization:

`POST /v1/internal/statutory-discounts/evidence/sets/{evidenceSetReference}/items/{evidenceItemReference}/upload-authorizations`

The request accepts controlled declaration facts only:

- declared content type
- declared content length
- media class
- checksum algorithm
- declared SHA-256 checksum
- client operation key

The request does not accept bucket names, object keys, provider credentials, evidence bytes, Base64, multipart bodies, ID numbers, beneficiary names, OCR text, or scanner output.

The response exposes only short-lived authorization material:

- opaque upload authorization reference
- upload method
- short-lived upload URL
- required request headers
- expiry timestamp
- maximum content length
- accepted content type
- correlation ID

The response does not expose permanent object URLs, read URLs, provider credentials, bucket credentials, broad session credentials, internal object keys as standalone fields, or checksums.

Upload finalization:

`POST /v1/internal/statutory-discounts/evidence/sets/{evidenceSetReference}/items/{evidenceItemReference}/upload-finalizations`

Finalization resolves the evidence set and item server-side, verifies scope, verifies that the upload authorization belongs to the same item, checks expiry and consumed state, queries object metadata from the provider, and updates evidence metadata only after provider-reported content type, length, and SHA-256 checksum match the authorization record.

Client claims that an upload succeeded are not trusted.

## Protected Storage Adapter

The runtime introduces `IStatutoryEvidenceProtectedObjectStorageAdapter` as the provider boundary. The current implementation is `S3CompatibleStatutoryEvidenceObjectStorageAdapter`.

Provider posture:

- private bucket or container only
- no public read
- no anonymous list
- no anonymous get
- short-lived PUT authorization only
- exact object-key restriction inside the signed authorization
- strict content-type and content-length binding
- checksum header requirement
- TLS-required production posture
- credentials remain server-side configuration only

MinIO or another S3-compatible service is the intended disposable validation provider. Production provider configuration is intentionally abstracted behind the adapter.

## Object Key Design

Internal object keys are generated server-side and are not public identifiers. The current shape uses a random partition plus the internal evidence item identifier:

`evidence/{random-partition}/{internal-evidence-item-id}/{random-upload-generation}`

The key does not include customer name, statutory ID number, plate number, Site name, parking ticket number, entitlement text, or the public opaque evidence reference. The key may appear inside the short-lived signed upload URL because S3-compatible direct upload requires it as authorization material; it is never stored in public DTOs or logs.

## Lifecycle Transitions

Upload authorization moves an eligible item from `NOT_AUTHORIZED` to `AUTHORIZED`.

Finalization moves the item to `UPLOADED` only after provider metadata verification passes.

Finalization does not mark validation as `PASSED`, scan as `PASSED`, or reviewability as `REVIEWABLE`. Those dimensions remain separate future runtime slices.

Rejected cases fail closed without advancing the upload state:

- unsupported content type
- missing upload profile or maximum size
- content length exceeded
- expired authorization
- consumed or unusable authorization
- object not found
- provider unavailable
- content-type mismatch
- content-length mismatch
- checksum mismatch
- lifecycle conflict
- scope denial
- semantic conflict

## Idempotency

Upload authorization and upload finalization use deterministic operation scope and key plus a semantic request hash. Exact replay returns the governed result without creating duplicate active upload authorization rows. Same key with changed semantics returns `IDEMPOTENCY_SEMANTIC_CONFLICT` and mutates nothing.

Correlation ID is not an idempotency key.

## Authorization

Upload authorization and finalization require the evidence capture policy and the durable evidence binding established by the metadata foundation. Scope is derived server-side from the evidence set and item:

- statutory request
- parking session
- Site
- Site Group
- entitlement type
- source channel

Possession of an opaque evidence reference or upload authorization reference is insufficient. WebPay and APT capture identities remain bounded to their authorized request and Site scope. Operator Console `VIEW` authority does not grant upload replacement authority. Ordinary payment credentials and POS Server credentials remain denied.

## Audit and Privacy

The repository records privacy-safe upload events for authorization issuance, replay, conflict, verification start, verification success, verification failure, provider unavailability, finalization, and denial.

Events and public DTOs must not contain evidence bytes, Base64, full ID numbers, beneficiary names, birth dates, addresses, biometrics, signed URL logs, object keys, checksums, provider credentials, authorization headers, raw provider errors, SQL errors, or stack traces.

## Validation Status

Completed in this environment:

- Central PMS Release build
- focused statutory evidence unit tests
- focused statutory evidence API integration tests
- canonical DB generated-DDL regeneration
- canonical DB object-source layout validation
- canonical DB v1.3 Central PMS object-source validation
- disposable PostgreSQL 16 generated-DDL apply using `docker exec` hosted `psql`
- disposable DB object-source coverage validation
- required upload authorization table, enum, index, constraint, and foreign-key inventory
- MinIO S3-compatible private-bucket validation
- anonymous bucket list denial
- anonymous object read denial
- real Central PMS API upload authorization
- direct signed `PUT` of synthetic JPEG and PNG bytes to protected object storage
- provider metadata verification during upload finalization
- SHA-256 checksum verification
- exact upload authorization replay
- exact finalization replay after Central PMS restart
- provider outage failure mapping
- no PostgreSQL evidence byte or Base64 column proof
- signed URL, object-key, checksum, and credential logging scan

The Windows host did not require host-installed `psql`; PostgreSQL client operations were executed inside the disposable PostgreSQL container. Production rollout and controlled UAT remain unauthorized.

S3-compatible presigned URLs contain temporary signed authorization query material, including credential scope, as part of the short-lived upload grant. This is acceptable only because the URL is restricted to one object, method, content type, and content length; expires quickly; is not persisted; and is not logged. Permanent access keys, secret keys, session credentials, bucket credentials, and connection strings are never returned in public DTOs.

## Future Handoff

Recommended follow-up slices:

1. Implement malware scanning and validation worker integration.
2. Add WebPay upload UI using only short-lived upload authorization.
3. Add APT upload UI without storing evidence bytes in SQLite.
4. Add Operator Console secure preview using separate short-lived presentation authorization.
5. Implement retention, deletion, and object reconciliation workers.
