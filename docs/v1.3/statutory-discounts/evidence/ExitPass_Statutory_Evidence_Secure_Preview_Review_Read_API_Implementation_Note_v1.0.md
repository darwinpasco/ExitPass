# ExitPass Statutory Evidence Secure Preview and Review Read API Implementation Note v1.0

## Scope

I-017 adds the Central PMS backend required for an Operator Console reviewer to read safe statutory-evidence lifecycle metadata and preview a current reviewable JPEG or PNG inline. It does not add an Operator Console UI, evidence download, PDF preview, OCR, biometric processing, retention or deletion workers, Controlled UAT, or production rollout.

The implementation reuses the merged I-012 metadata/lifecycle authority, I-013 protected object adapter, I-015 validation and malware results, and I-016 current-item/replacement posture. No canonical database change is required.

## Pre-change capability audit

- Operator Console already owned statutory review approval/rejection through durable user, device, shift, Site, and Site Group access evaluation.
- Central PMS already retained opaque evidence references, current storage binding, provider metadata, validation, malware, reviewability, hold, retention, deletion, and replacement state.
- The protected S3-compatible adapter already authenticated server-side object reads for the scanner, but that scanner method intentionally buffered content. I-017 adds a response-owned streaming method without changing scanner behavior.
- Existing `ACCESS_ALLOWED` and `ACCESS_DENIED` statutory-evidence event types can record privacy-safe review access without a new table.
- No safe browser preview route, dedicated permission, review-safe DTO, current-version recheck, or inline response policy previously existed.

## Routes

Both routes are GET-only and remain under the existing Operator Console route boundary:

- `GET /v1/ops/operator-console/statutory-discounts/reviews/{statutoryDiscountDecisionCommandId}/evidence`
- `GET /v1/ops/operator-console/statutory-discounts/reviews/{statutoryDiscountDecisionCommandId}/evidence/{evidenceItemReference}/preview`

Internal statutory-evidence and object-storage routes are not exposed to the Operator Console browser.

## Authentication and authorization

The named policy is `OperatorConsoleStatutoryEvidenceReviewView` and its only permission is `statutory-discounts.evidence.review.view`. Capture, generic evidence view, governance view, scanner execution, statutory decision review/approval, WebPay, APT, and reconciliation permissions do not imply it.

After policy authorization, the application runs the existing Operator Console access evaluator with controlled action `REVIEW_EVIDENCE` and intent `REVIEW_PREVIEW`. The evaluator resolves durable reviewer, device, shift, Site, and Site Group context. The evidence repository then requires exact equality with the decision-bound evidence Site and Site Group. Unknown and cross-Site records are anti-enumerated. A contradictory Site Group context is denied before evidence lookup.

Preview never grants approval, rejection, upload, replacement, payable-basis application, storage administration, or download authority. Existing approval and rejection routes are unchanged.

## Review-safe metadata

The JSON contract returns only the decision reference, opaque evidence set/item references, controlled document type/role, declared and verified media type, safe content length, upload/validation/scan/reviewability/binding/retention/deletion/hold classifications, safe lifecycle timestamps, replacement posture, preview permission, a controlled denial reason, and correlation reference.

It excludes customer identity, statutory ID values, reviewer notes/identity, parking/payment facts, provider endpoint, bucket, object key, locator, checksum, provider version, signed URL/query, credential scope, provider headers, scanner endpoint/result/signature, connection string, SQL detail, and stack trace.

## Preview eligibility

Preview is allowed only when the decision, current evidence set, item, consumed upload authorization, protected object, and durable scope all match and:

- upload is finalized as `UPLOADED`;
- structural validation is `PASSED`;
- malware scan is `CLEAN` or the canonical equivalent `PASSED`;
- reviewability is `REVIEWABLE`;
- binding is neither `REJECTED` nor `SUPERSEDED`;
- retention is `ACTIVE` or `HELD`;
- deletion is `NOT_REQUESTED`;
- the set is not tombstoned;
- media is exactly `image/jpeg` or `image/png`, and declared/verified media agree;
- storage locator, checksum, object metadata, object version, and row versions still match.

Pending validation, failed validation, pending/retryable/unavailable scan, malicious/suspicious/unknown scan, non-reviewable, replaced, stale, deleted, pending-deletion, retention-inaccessible, missing, unsupported, or malformed state fails closed before bytes are returned.

I-012 hold semantics block deletion but do not broaden or automatically deny review. Therefore held evidence remains previewable only when every other review condition passes, while replacement remains denied. Any ambiguous retention/deletion state fails closed.

## Delivery design

I-017 uses direct authenticated Central PMS streaming and does not issue a bearer preview session or provider URL. Every GET reauthorizes the reviewer and revalidates the current evidence/object version. Consequently there is no reusable preview authority to expire, transfer, persist, or replay after a lifecycle change. API restart requires no session recovery.

The protected adapter uses `HttpCompletionOption.ResponseHeadersRead` and returns a response-owned non-seekable stream. Central PMS copies with a bounded 81,920-byte buffer and request cancellation. It creates no evidence temporary file, cache entry, Base64 representation, or PostgreSQL payload.

Successful responses set exact `Content-Type` and `Content-Length` plus:

- `Content-Disposition: inline`
- `Cache-Control: no-store, private, max-age=0`
- `Pragma: no-cache`
- `X-Content-Type-Options: nosniff`
- `Referrer-Policy: no-referrer`
- `X-Frame-Options: SAMEORIGIN`
- restrictive `Content-Security-Policy`

Range/download behavior and cross-origin provider access are not implemented.

## Audit and safe errors

Central PMS records controlled access events for metadata read, preview start, completion, cancellation, denial, stale target, and storage failure. Events contain controlled result/reason, opaque internal relationships, durable scope, actor reference, timestamp, and correlation only. They contain no bytes, object key, checksum, provider/scanner data, customer ID value, SQL, or exception detail.

Public errors use safe not-found/anti-enumeration, forbidden, lifecycle conflict, unsupported-media, and retryable storage-unavailable classifications. I-017 Production logs record only controlled exception type and correlation for route failures; exception messages and stacks are not passed to the logger by these endpoints.

## Replacement, restart, and failures

The object metadata and the database row versions are rechecked after the protected object stream is opened and before the response is returned. Replacement, row-version advancement, deletion, or object-version change invalidates the request as stale. Preview does not change review, replacement, payable basis, or evidence lifecycle.

A forced API restart preserved metadata and preview behavior because all authority is durable. A protected-storage outage returned a retryable safe `503`; the same current item streamed after storage recovery. A throttled client disconnect produced a privacy-safe cancellation event and did not mutate upload, validation, scan, or reviewability state.

## Disposable proof

Validation used isolated PostgreSQL 16 and private versioned MinIO containers on unique loopback ports and a unique Docker network, with no persistent volumes. The current canonical full DDL applied cleanly. Repository-approved synthetic Operator Console identity/scope data and synthetic evidence rows were used. No real identity document was used.

Observed hosted results in `Production`:

- metadata read: `200`, safe DTO only;
- clean JPEG preview: `200`, exact image bytes and privacy headers;
- clean PNG preview: `200`, exact image bytes and privacy headers;
- validation pending: `409 STATUTORY_EVIDENCE_VALIDATION_PENDING`;
- malware: `409 STATUTORY_EVIDENCE_MALWARE_DETECTED`;
- superseded: `409 STATUTORY_EVIDENCE_STALE`;
- cross-item: anti-enumerated `404`;
- cross-Site: anti-enumerated `404`;
- cross-Site Group: `403` before evidence lookup;
- MinIO unavailable: retryable `503` with no provider detail;
- MinIO recovered: `200`;
- forced API restart: metadata and preview remained `200`;
- client disconnect: partial response aborted, cancellation event persisted, item lifecycle unchanged;
- anonymous MinIO list and GET: `403`.

Automated validation passed 34 focused I-017 unit/storage tests, 16 focused endpoint tests, one canonical DB projection/current-version/audit test, 260 affected unit regressions, and 210 affected integration regressions. Release build and `git diff --check` passed.

PostgreSQL schema/value inspection found no evidence byte or Base64 column/value. Public DTO/source, response, log, and event scans found no provider URL, endpoint, bucket, object key, checksum value, signed query, credential scope, scanner detail, connection string, SQL diagnostic, or public stack trace. Proof resources and synthetic files were removed after validation.

## Manual API verification

Use only synthetic images and an authorized Operator Console identity whose durable device/shift scope matches the review request.

1. Call the metadata GET with the decision reference. Expect `200`, safe lifecycle metadata, and `previewPermitted=true` only for current clean reviewable items.
2. Call the preview GET for a permitted JPEG. Expect `200`, `image/jpeg`, inline/no-store/nosniff headers, and no provider authority.
3. Repeat for PNG. Expect `image/png` with the same privacy posture.
4. Set or select pending-validation evidence. Expect controlled `409` and no body bytes.
5. Set or select malicious evidence. Expect controlled `409`, non-reviewable posture, and no scanner detail.
6. Use an out-of-scope Site/Site Group or mismatched decision/item. Expect forbidden or anti-enumerated not-found according to the access boundary.
7. Replace or advance the item/object version. The old request must return stale/denied.
8. Stop disposable storage and call preview. Expect retryable `503` without endpoint/bucket/key detail; restart storage and retry.
9. Inspect response headers and privacy-safe access events. Events must contain only controlled classifications, relationships, scope, actor, time, and correlation.

## H-005 handoff

H-005 may consume the two routes with the dedicated permission/policy. The UI must render metadata without deriving authority, request preview only when `previewPermitted` is true, display controlled denial reasons, use the inline response only for the active review, avoid browser durable storage, and preserve existing approve/reject authority. It must not call internal evidence/storage routes, request provider URLs, add download/export, infer reviewability, or apply payable basis.

Operator Console preview UI and review workspace remain H-005 scope. Evidence download, PDF preview, OCR, biometrics, retention/deletion workers, Controlled UAT, and production rollout remain out of scope.
