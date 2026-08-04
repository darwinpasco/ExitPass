# ExitPass Statutory Evidence Governance Read API Implementation Note v1.0

## Scope

This slice adds a browser-safe, read-only Central PMS API for Management Platform statutory evidence governance inspection. It exposes configuration and readiness posture only. It does not expose customer evidence metadata, evidence bytes, upload authorizations, signed URLs, object keys, checksums, provider credentials, statutory request or decision references, reviewer data, payment data, parking-session references, or workflow mutation authority.

Implemented capabilities:

- Management Platform evidence-governance collection read
- Site detail read
- Site Group detail read
- dedicated permission and named RBAC policy
- server-owned Site and Site Group scope filtering
- browser-safe governance and readiness classifications
- warning and blocker catalogs
- freshness classification and evaluation timestamp
- SELECT-only repository posture
- H-004 frontend handoff contract

Not implemented in this slice:

- H-004 frontend
- evidence upload UI
- evidence preview or download
- signed preview URLs
- malware scanning execution
- OCR or biometric processing
- retention worker
- deletion worker
- object reconciliation worker
- evidence review UI
- hold management UI
- deletion-request UI
- provider configuration mutation
- Controlled UAT
- production rollout

## Route Contract

Collection:

`GET /v1/ops/management-platform/statutory-discounts/evidence-governance`

Site detail:

`GET /v1/ops/management-platform/statutory-discounts/evidence-governance/sites/{siteReference}`

Site Group detail:

`GET /v1/ops/management-platform/statutory-discounts/evidence-governance/site-groups/{siteGroupReference}`

The collection endpoint supports safe filters:

- `siteReference`
- `siteGroupReference`
- `entitlementType`
- `governanceStatus`
- `readinessStatus`
- `captureEnabled`
- `includeStale`

`siteReference` and `siteGroupReference` are filters only. They are not authority. Supplying both is rejected.

## Permission and Policy

Dedicated permission:

`statutory-discounts.evidence-governance.view`

Named policy:

`StatutoryEvidenceGovernanceView`

The governance-read policy is separate from:

- `statutory-discounts.evidence.capture`
- `statutory-discounts.evidence.view`
- `statutory-discounts.evidence.review-lock`
- `statutory-discounts.evidence.hold`
- `statutory-discounts.evidence.delete-request`
- statutory eligibility review
- statutory decision approval
- payable-basis application
- Management Platform statutory policy coverage read
- Operator Console permissions
- WebPay permissions
- APT permissions
- POS Server permissions

## Scope Behavior

The API derives authorized scope on the server. A caller can see only Sites resolved from durable Central PMS scope data.

Scope sources:

- active Operator Console user Site or Site Group shifts, where the repository already treats the user as authorized for the Site scope
- active statutory evidence principal scope grants for service identities

Request-supplied Site or Site Group references narrow the authorized scope. They do not grant access. Unknown and unauthorized references are handled through the same safe denial posture so hidden Site or Site Group existence is not confirmed.

An empty authorized scope returns a safe empty result with `EMPTY_AUTHORIZED_SCOPE`.

## Authoritative Sources

The response is built from Central PMS authoritative configuration:

- `sites.sites`
- `sites.site_groups`
- `operator_console.operator_shifts`
- `discounts.statutory_evidence_principal_scope_grants`
- `discounts.statutory_evidence_sets`
- `discounts.statutory_evidence_items`
- `discounts.statutory_evidence_retention_policies`
- `discounts.statutory_evidence_upload_authorizations`
- I-013 upload configuration under `CentralPms:StatutoryEvidence:Upload`

The API does not list object storage, read customer evidence objects, or call object storage per UI row.

## Response Contract

Contract version:

`management-platform-statutory-evidence-governance:v1`

Each Site row may include:

- Site reference and display name
- Site Group reference and display name
- entitlement types supported
- governance status
- readiness status
- capture configured and enabled posture
- required document profile and approved retention-policy posture
- allowed media types
- maximum upload size
- upload authorization TTL
- upload authorization readiness
- upload finalization readiness
- protected storage provider classification
- private access, encryption, checksum, and provider metadata verification posture
- lifecycle posture for upload, validation, scan metadata, reviewability, binding, hold, and deletion request
- operational readiness for malware scanning execution, secure preview, retention worker, deletion worker, and object reconciliation
- last evaluated timestamp
- configuration updated timestamp where available
- freshness status and stale flag
- retry recommendation
- support reference
- warning and blocker codes

## Classifications

Governance status:

- `CONFIGURED_READY`
- `CONFIGURED_PARTIALLY_READY`
- `CONFIGURATION_INCOMPLETE`
- `CAPTURE_DISABLED`
- `CONFIGURATION_UNAVAILABLE`
- `UNKNOWN`

Capability readiness:

- `READY`
- `PARTIALLY_READY`
- `NOT_CONFIGURED`
- `DISABLED`
- `NOT_IMPLEMENTED`
- `UNAVAILABLE`
- `STALE`
- `UNKNOWN`

Freshness:

- `FRESH`
- `STALE`

Warnings and blockers:

- `CAPTURE_DISABLED`
- `UPLOAD_PROFILE_INCOMPLETE`
- `MAXIMUM_SIZE_NOT_CONFIGURED`
- `ALLOWED_MEDIA_NOT_CONFIGURED`
- `UPLOAD_TTL_INVALID`
- `PROTECTED_STORAGE_NOT_CONFIGURED`
- `STORAGE_PRIVACY_UNVERIFIED`
- `ENCRYPTION_POSTURE_UNKNOWN`
- `CHECKSUM_CONFIGURATION_INCOMPLETE`
- `RETENTION_POLICY_UNAVAILABLE`
- `MALWARE_SCANNING_NOT_IMPLEMENTED`
- `SECURE_PREVIEW_NOT_IMPLEMENTED`
- `RETENTION_WORKER_NOT_IMPLEMENTED`
- `DELETION_WORKER_NOT_IMPLEMENTED`
- `OBJECT_RECONCILIATION_NOT_IMPLEMENTED`
- `CONFIGURATION_STALE`

Malware scan lifecycle metadata can be reported as supported when the I-012 metadata tables exist. Malware scanning execution remains `NOT_IMPLEMENTED` until a real scanning worker exists.

## Browser-Safe Exclusions

DTOs must not contain:

- evidence-set reference
- evidence-item reference
- statutory request reference
- statutory decision reference
- customer name
- customer identity
- statutory ID number
- ID issuer
- ID image metadata
- evidence object key
- storage locator
- bucket or container name
- storage endpoint
- signed upload URL
- signed preview URL
- checksum value
- provider ETag
- provider version ID
- provider credential scope
- permanent credentials
- connection strings
- raw provider response
- reviewer identity
- review notes
- beneficiary data
- plate number
- ticket number
- parking-session reference
- payment reference
- payable-basis data

## Read-Only and No-Write Posture

The repository uses SELECT-only queries. The endpoints do not create evidence metadata, upload authorizations, finalizations, evidence locks, holds, deletion requests, statutory decisions, applications, payable-basis records, policy mutations, feature flag mutations, or provider configuration mutations.

No business-state mutation is required for the read API. If a future repository convention requires access auditing, that audit must remain privacy-safe and must be documented separately from business state.

## H-004 Resume Handoff

H-004 should consume:

- route family: `/v1/ops/management-platform/statutory-discounts/evidence-governance`
- permission: `statutory-discounts.evidence-governance.view`
- policy: `StatutoryEvidenceGovernanceView`
- contract version: `management-platform-statutory-evidence-governance:v1`
- DTOs in `ExitPass.CentralPms.Contracts.ManagementPlatform`

Frontend constraints:

- do not infer readiness from timestamps alone
- do not treat `NOT_IMPLEMENTED` worker capabilities as enabled
- do not expose evidence capture, preview, hold, deletion, or upload actions from this read API
- do not ask for customer identity, statutory ID, evidence references, plate, ticket, parking session, object key, checksum, or signed URL fields
- do not use browser-computed Site or Site Group authority
- treat empty authorized scope as an empty governed view
- treat scope denied and unknown references with the same safe user-facing posture

## Validation Posture

Required validation includes:

- route registration
- dedicated policy and permission mapping
- denial for CAPTURE, VIEW, REVIEW_LOCK, HOLD, DELETE_REQUEST, policy coverage, Operator Console, WebPay, APT, and POS permissions
- Site and Site Group scope behavior
- anti-enumeration behavior
- readiness classification mapping
- DTO privacy exclusions
- SELECT-only repository scan
- no-write row-count proof against disposable PostgreSQL
- security, privacy, secret, raw-error, object-key, checksum, signed-URL, provider-secret, and connection-string scans
- `git diff --check`

Controlled UAT is not authorized. Production rollout is not authorized.
