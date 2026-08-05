# ExitPass Statutory Evidence Channel-Safe Bootstrap, Upload, and Readiness Implementation Note v1.0

## Scope

I-016 adds Central PMS-owned WebPay and Assisted Payment Terminal statutory-evidence channel facades on top of the merged I-012 through I-015 foundations.

Implemented capabilities:

- WebPay-safe bootstrap, status, opaque upload-session, streaming upload, and finalization routes.
- APT-safe bootstrap, status, revalidation, opaque upload-session, streaming upload, and finalization routes.
- Server-derived evidence governance facts for document profile, profile version, retention class, retention-policy version, environment scope, document type, item role, and media profile.
- Rediscovery of existing canonical evidence metadata by statutory decision command.
- Opaque upload-session references backed by existing I-013 upload authorization records.
- Same-origin/service-mediated streaming upload to protected object storage without returning provider upload URLs to WebPay or APT consumers.
- APT pre-cash readiness integration through a new server-derived `statutoryEvidenceReadiness` dimension.

Out of scope and still not implemented:

- WebPay evidence UI.
- APT evidence UI.
- Evidence preview or download.
- Operator Console evidence review UI.
- OCR or biometric processing.
- Retention/deletion workers.
- Controlled UAT or production rollout.

## Route Map

WebPay:

- `POST /v1/webpay/statutory-discounts/evidence/bootstrap`
- `GET /v1/webpay/statutory-discounts/evidence/status`
- `POST /v1/webpay/statutory-discounts/evidence/upload-sessions`
- `PUT /v1/webpay/statutory-discounts/evidence/upload-sessions/{opaqueUploadSessionReference}`
- `POST /v1/webpay/statutory-discounts/evidence/upload-sessions/{opaqueUploadSessionReference}/finalize`

APT:

- `POST /v1/apt/statutory-discounts/evidence/bootstrap`
- `GET /v1/apt/statutory-discounts/evidence/status`
- `POST /v1/apt/statutory-discounts/evidence/revalidate`
- `POST /v1/apt/statutory-discounts/evidence/upload-sessions`
- `PUT /v1/apt/statutory-discounts/evidence/upload-sessions/{opaqueUploadSessionReference}`
- `POST /v1/apt/statutory-discounts/evidence/upload-sessions/{opaqueUploadSessionReference}/finalize`

The public channel routes are intentionally separate from the internal I-012/I-013 routes under `/v1/internal/statutory-discounts/evidence`.

## Authorization

Dedicated channel policies were added:

- `WebPayStatutoryEvidenceCapture` -> `statutory-discounts.evidence.capture.webpay`
- `AptStatutoryEvidenceCapture` -> `statutory-discounts.evidence.capture.assisted-payment-terminal`

Each route requires an authenticated service identity header and rejects human user headers. The React browser and APT desktop surfaces are not expected to supply privileged headers directly.

The application service also verifies durable evidence scope through the I-012 principal scope grant model:

- source channel must match the durable statutory request binding;
- the caller must have CAPTURE authority for the durable Site and Site Group;
- opaque evidence references and opaque upload-session references are not authorization.

## Server-Derived Bootstrap

Bootstrap resolves the statutory decision command server-side and derives:

- parking session;
- Site;
- Site Group;
- entitlement type;
- source channel;
- required document profile;
- required document profile version;
- retention class;
- retention policy version;
- environment scope;
- document type;
- item role.

Clients do not supply governance profile, retention policy, environment scope, Site authority, Site Group authority, validation result, malware result, reviewability, approval, payable-basis readiness, object key, checksum authority, or storage location.

Bootstrap is idempotent by statutory decision command and client operation key. When a canonical evidence set already exists, bootstrap returns the existing opaque set and item references instead of creating duplicates.

## Opaque Upload Session

The upload-session response exposes only:

- opaque upload-session reference;
- HTTP method;
- expiry;
- accepted content type;
- maximum content length;
- safe correlation reference.

It does not expose:

- S3 or MinIO endpoint;
- bucket or container name;
- object key or storage path;
- provider upload URL;
- signed query parameters;
- signing credential scope;
- provider headers;
- checksum value;
- credentials.

The implementation reuses I-013 upload authorization state as the durable upload-session ledger, but suppresses provider authorization details from channel DTOs.

## Streaming Upload

The channel `PUT` route streams the request body through Central PMS to the protected storage adapter. Central PMS computes a bounded SHA-256 hash while streaming and rejects mismatches against the previously declared checksum. The body is not persisted in PostgreSQL, DTOs, logs, audit events, temp files, browser storage, APT SQLite, or POS Server payloads.

Upload request checks include:

- content type must match the authorized session;
- content length must match the authorized session;
- maximum content length must be configured and respected;
- authorization must be issued, unexpired, and unused;
- source channel and durable scope must match;
- provider failures are mapped to safe retryable classifications.

## Finalization

Finalization calls the existing I-013 finalization service. Protected storage metadata verification remains server-side and authoritative. Finalization transitions upload state to `UPLOADED` and queues validation/scan work, but it does not mark validation as passed, scan as clean, evidence as reviewable, review as approved, or payable basis as applied.

## Lifecycle Rediscovery

Status/readback maps the canonical evidence set, item, statutory decision, and application states into channel-safe lifecycle classifications, including:

- `NOT_REQUIRED`
- `REQUIRED_NOT_STARTED`
- `ITEM_CREATED`
- `UPLOAD_SESSION_AVAILABLE`
- `UPLOAD_IN_PROGRESS`
- `UPLOADED`
- `VALIDATION_PENDING`
- `VALIDATION_FAILED`
- `SCAN_PENDING`
- `SCAN_RETRYABLE`
- `SCAN_FAILED`
- `MALWARE_DETECTED`
- `NOT_REVIEWABLE`
- `REVIEWABLE`
- `REVIEW_PENDING`
- `APPROVED`
- `REJECTED`
- `APPLIED`
- `UNKNOWN_FAIL_CLOSED`

Validation, malware scanning, reviewability, review, approval, and payable-basis application remain separate dimensions.

## Replacement

Responses include an explicit replacement posture:

- `REPLACEMENT_ALLOWED`
- `REPLACEMENT_NOT_ALLOWED`

Replacement is denied for locked, held, approved, rejected, applied, deleted, or otherwise authoritative objects. Clients must not infer replacement permission from raw lifecycle fields.

## APT Readiness

APT payable-basis readiness now includes a server-derived `statutoryEvidenceReadiness` dimension. `CASH_RECEIVED` remains blocked unless every existing APT readiness dimension passes and the evidence dimension is ready.

Evidence upload, validation, clean malware scan, or reviewability alone does not authorize cash acceptance. The statutory decision/application dimension still controls approval and payable-basis application readiness.

## Privacy Exclusions

Channel DTOs do not contain:

- evidence bytes or Base64;
- evidence-set or evidence-item internals beyond opaque references;
- object key or storage locator;
- bucket, endpoint, or storage provider metadata;
- signed upload URL or signed preview URL;
- checksum value;
- storage or scanner credentials;
- scanner endpoint;
- provider response bodies;
- customer identity, statutory ID, reviewer identity, review notes, parking-session reference, payment reference, or payable-basis amounts.

## Downstream Handoff

G-006 WebPay can use:

1. bootstrap to derive the required evidence item;
2. upload-session creation to obtain an opaque session;
3. `PUT` to upload bytes through Central PMS;
4. finalization to commit protected object metadata;
5. status to rediscover lifecycle after restart.

J-006 APT can use the same bounded sequence plus `POST /v1/apt/statutory-discounts/evidence/revalidate` and the existing payable-basis revalidation response, which now includes the evidence readiness dimension.

Controlled UAT and production rollout remain unauthorized.

## I-016B Disposable Runtime Proof Update

The I-016B closure run used disposable PostgreSQL 16, private MinIO, ClamAV, and the actual Central PMS API host. The canonical full generated DDL applied cleanly to the disposable database, the MinIO bucket was configured private, and anonymous bucket list/object GET returned forbidden.

The hosted API proved:

- WebPay required-evidence bootstrap returned server-derived governance, retention, media, and replacement posture.
- WebPay not-required bootstrap returned a channel-safe no-evidence-required response.
- WebPay status rediscovered an existing canonical evidence set after host restart.
- WebPay opaque upload sessions exposed only method, expiry, accepted content type, maximum content length, correlation, and opaque upload-session reference.
- WebPay PNG upload streamed through Central PMS, finalized through the I-013 finalization service, and read back `REVIEWABLE` after I-015 validation and scan completion.
- APT JPEG and PNG uploads streamed through Central PMS, finalized through I-013, and read back `REVIEWABLE`.
- PDF upload-session creation and oversized upload-session creation were rejected before storage authority.
- Cross-channel bootstrap using an APT route for a WebPay decision was rejected.
- Channel upload-session DTOs did not expose provider URL, endpoint, bucket, object key, checksum, signed query material, credential identifier, or provider headers.

The closure run found and fixed one authorization gap: channel status lookup by `evidenceSetReference` previously reused the internal metadata read path and therefore required evidence-view authority. Channel status now resolves the opaque reference through the repository and authorizes it using the same durable source-channel, Site, Site Group, and capture-scope guard as decision-based rediscovery. WebPay and APT capture principals can rediscover their own evidence status without gaining general evidence-view authority.

Remaining I-016B blockers:

- Malware lifecycle readback was not proven through the channel surface. Runtime malware-test material was blocked by Windows host protection before it could be copied to the disposable scanner for direct scanner probing, and the JPEG-comment variant uploaded through the channel scanned clean. No malware-test content was retained.
- The focused integration-test command that included I-014 and I-015 database-backed tests did not complete green in the closure environment. The I-014 governance test correctly refused the non-`i014` disposable database name, and the canonical fixture tests failed during their own setup while applying SQL to a fixture database that did not yet exist. These are validation blockers for PR readiness in this closure run.
- Full APT payable-basis/CASH_RECEIVED runtime proof was not completed through the hosted APT payable-basis API in this run. Existing focused readiness tests remain the current evidence for the new `statutoryEvidenceReadiness` dimension.

Because those gates remain open, I-016B is not PR-ready and downstream G-006/J-006 must remain blocked pending a clean closure run that proves malware lifecycle readback, APT immediate pre-cash readiness, and focused database-backed regressions under repository-approved fixture configuration.

## I-016C Closure Update

The I-016C closure run used a fresh disposable PostgreSQL 16 container, private MinIO bucket, ClamAV-compatible scanner, and the actual Central PMS API host running with `ASPNETCORE_ENVIRONMENT=Production` and no launch profile. Canonical full generated DDL applied successfully. The MinIO bucket remained private; anonymous list returned forbidden. No protected local database or shared bucket was used.

Additional proofs:

- A clean APT JPEG control flowed through actual bootstrap, opaque upload-session issuance, channel streaming upload, I-013 finalization, I-015 worker processing, and channel status readback. The final status was `REVIEWABLE`, `readyForReview=true`, and `readyForAptPreCash=false`.
- Runtime-only malware-test bytes were generated entirely inside disposable Linux containers and were never written to the Windows host filesystem. ClamAV detected the exact raw marker, while generated JPEG variants containing the marker were scanned as clean by the disposable scanner.
- The raw marker was transmitted from a disposable Linux container directly to the actual APT channel streaming endpoint while declared as `image/jpeg`. The channel accepted the stream and finalization, then the I-015 worker failed closed at structural validation. Authoritative status readback returned `VALIDATION_FAILED`, `readyForReview=false`, and `readyForAptPreCash=false`.
- Production malformed JSON, missing body, and invalid opaque-reference requests returned safe HTTP 400 responses. Production logs for those expected client errors did not contain stack traces, JSON exception details, SQL diagnostics, provider authorization material, storage credentials, scanner endpoint, raw malware signature, raw scanner response, or evidence bytes.
- The I-014 guarded governance database test passed after using a compliant `exitpass_i014_*` disposable database name.
- The statutory evidence metadata and scan repository integration tests passed after configuring the canonical fixture to create and apply SQL inside the same disposable PostgreSQL container through `EXITPASS_STATUTORY_DB_FIXTURE_POSTGRES_CONTAINER` and `EXITPASS_STATUTORY_DB_FIXTURE_POSTGRES_USER`.
- PostgreSQL statutory evidence tables contained no `bytea` or Base64 evidence columns. Internal I-013 storage locator/checksum fields remain internal-only.

Final blocker:

- The required `MALWARE_DETECTED` channel lifecycle readback for a supported JPEG or PNG evidence object remains unproven. The scanner detected only the exact raw malware-test marker; the channel correctly rejected that object at structural validation before malware-scan success could be authoritative. No real malicious JPEG/PNG fixture was introduced, retained, logged, documented, or committed. This preserves the security posture but leaves the I-016 malware lifecycle readback gate blocked.
- Full hosted APT payable-basis/CASH_RECEIVED scenario coverage remains incomplete in this closure run. The clean and raw-marker evidence states both returned `readyForAptPreCash=false`; the exhaustive approved/applied payable-basis matrix still requires a dedicated APT readiness fixture or controlled hosted scenario.

I-016 therefore remains not PR-ready. G-006 and J-006 remain blocked because the final malware lifecycle readback and full APT immediate pre-cash readiness proof are incomplete.

## I-016D Closure Update

The I-016D closure run used the preserved implementation with a fresh disposable PostgreSQL 16 database, private MinIO bucket, ClamAV-compatible scanner, and the actual Central PMS API host running in Production. The run did not use `exitpass_v12_dev`, a protected local database, shared buckets, production credentials, or retained malware-test fixtures.

Supported-image malware lifecycle was proven with a harmless runtime-generated marker embedded in a structurally valid synthetic JPEG and a custom ClamAV signature loaded only inside the disposable scanner container. The marker, custom signature, and generated image were not committed and are excluded from this note. Through the actual APT channel bootstrap, opaque upload-session, streaming upload, I-013 finalization, and I-015 worker path:

- the clean image control persisted `PASSED:CLEAN:CLEAN:REVIEWABLE`;
- the marked supported image persisted `PASSED:MALICIOUS:MALICIOUS:NOT_REVIEWABLE`;
- channel readback returned a malware-detected/not-reviewable posture for the marked image;
- the marked image did not become reviewable, approved, applied, or APT pre-cash ready;
- evidence events contained only controlled classifications, including `MALWARE_DETECTED`, and no raw scanner response or signature text.

Privacy checks in the disposable run found no evidence `bytea` or Base64 columns in PostgreSQL, no signed URL/object-key/checksum/provider/scanner endpoint matches in Production API logs, and no sensitive storage or scanner values in evidence event classifications. Internal I-013 storage locator/checksum columns remain internal implementation state and are not public DTO fields.

Remaining I-016D blockers:

- The full hosted APT payable-basis and `CASH_RECEIVED` readiness matrix was not completed. The hosted route depends on the production vendor parking, terminal-cash, statutory decision/application, and POS Server sales-invoice readiness dimensions. Existing service-level and focused integration tests prove the `statutoryEvidenceReadiness` contribution, but the exhaustive hosted matrix still needs a bounded disposable fixture or POS-compatible readiness stub that exercises every dimension without weakening the production route.
- The restart, outage, disconnect, stale-session, and replay/conflict matrix was not completed in this closure run beyond the previously accepted restart rediscovery, streaming, finalization, and privacy proofs.
- Because those mandatory closure gates remain open, I-016 is still not PR-ready. G-006 and J-006 remain blocked pending hosted WebPay/APT replay/outage proof and the full APT immediate pre-cash readiness matrix.

## I-016E Closure Update

The I-016E closure run reused the preserved implementation and started a disposable PostgreSQL 16 database, private MinIO bucket, POS Server process, and Central PMS Production host. The canonical full generated Central PMS DDL applied cleanly. POS Server schema was applied from the POS Server repository SQL apply order into a separate disposable database. No protected local database, shared bucket, production credential, or retained evidence fixture was used.

The hosted APT payable-basis route was exercised through `POST /v1/terminal-cash-payments/payable-basis/resolve` against the actual Central PMS Production host. A synthetic ordinary, non-statutory APT fixture was configured with active terminal cash and POS Sales Invoice readiness:

- `statutoryEvidenceReadiness` returned `READY` with the safe message that no statutory evidence is required.
- `terminalCashAvailability` returned `READY`.
- POS Server effective Sales Invoice readiness returned `READY` through the Central PMS Management Platform readiness client.
- `salesInvoiceConfigurationReadiness` returned `READY`.
- `fiscalReadiness` returned `READY`.
- `cashAcceptanceReadiness` returned `READY`.
- `readyForCashAcceptance` returned `true`.

The closure run found one Central PMS POS-readiness compatibility defect: the current POS Server effective-readiness API wraps the payload in `resource`, uses `salesInvoiceHeaderProfileId`, `lifecycleStatus`, `failureCodes`, `birAccreditationPosture`, `ptuPosture`, and `overlapPosture`, and may return an opaque string `profileVersion`. Central PMS previously mapped that response to default readiness values, leaving APT cash readiness blocked even when POS Server was ready. The POS Server administration client now maps the current `resource` envelope safely, preserving opaque string `profileVersion` by returning a null numeric profile version instead of treating it as a malformed or authoritative numeric value. Focused unit coverage was added for the current POS Server resource envelope.

Focused validation after the fix:

- Release Central PMS API build: passed with existing XML-documentation warnings only.
- `PosServerSalesInvoiceProfileAdministrationClientTests`: 34 passed, 0 failed, 0 skipped.

Observed adjacent-repository issue:

- POS Server profile creation through its admin API failed in the disposable proof with PostgreSQL error `42P08` while checking duplicate profile versions. I-016 did not modify POS Server. To continue the APT readiness proof without editing the adjacent repository, the disposable POS database was seeded directly with the minimum synthetic fiscal identity, Site POS Server, and approved Sales Invoice profile rows. Direct POS effective-readiness then returned `READY`.

Remaining I-016E blockers:

- The full 21-scenario hosted APT evidence/payable-basis/CASH_RECEIVED matrix was not completed. The positive ordinary/evidence-not-required hosted case now passes, but the evidence-required missing/upload-pending/validation-pending/scan-pending/retryable-failure/malware/review/rejected/approved-not-applied/applied-changed cases still need a deterministic hosted fixture.
- The restart, outage, disconnect, stale-session, and replay/conflict matrix was not completed in this closure run beyond previously accepted restart rediscovery, streaming, finalization, malware lifecycle, and privacy proofs.
- Because those mandatory closure gates remain open, I-016 is still not PR-ready. G-006 and J-006 remain blocked pending hosted WebPay/APT replay/outage proof and the full APT immediate pre-cash readiness matrix.

## Final Bounded Closure

The final closure used the revised acceptance model: comprehensive application-level state-machine coverage plus representative Production-hosted paths. It did not require a separate Docker orchestration for every intermediate lifecycle classification.

Automated coverage now drives `StatutoryEvidenceChannelService` from durable repository read models for required-not-started, item-created, authorization/upload pending, validation pending/failed, scan pending/retryable/terminal, malicious, non-reviewable, reviewable, review-pending, approved, rejected, applied, not-required, and unknown fail-closed states. APT payable-basis aggregation tests prove that each blocked classification produces a blocked `statutoryEvidenceReadiness` dimension. Only `NOT_REQUIRED` or `APPLIED` can satisfy the evidence dimension, and cash readiness still requires every other terminal-cash, Sales Invoice, fiscal, statutory, session, and tariff dimension.

Representative Production-hosted APT calls to `POST /v1/terminal-cash-payments/payable-basis/resolve` and the immediate revalidation route proved:

- evidence not required with all other dimensions ready returned `READY` and allowed cash readiness;
- required item/upload/validation pending states returned controlled evidence blocking reasons;
- malicious evidence returned `STATUTORY_EVIDENCE_MALWARE_DETECTED` and remained non-reviewable and cash-blocked;
- approved without payable-basis application returned `STATUTORY_EVIDENCE_APPROVED_NOT_APPLIED` and remained blocked;
- approved and applied with unchanged amount returned all readiness dimensions `READY`;
- immediate revalidation with a changed expected amount returned `AMOUNT_CHANGED` and blocked stale cash acceptance;
- terminal-cash, Sales Invoice, fiscal/POS, and ordinance failures remained independent fail-closed dimensions;
- ordinary non-statutory readiness remained unchanged.

One bounded restart path proved that canonical metadata and an unexpired opaque upload session survive API restart. The restarted Production host rediscovered `UPLOAD_SESSION_AVAILABLE`, streamed the object through the private adapter, left it non-authoritative before finalization, finalized through I-013, and rediscovered `VALIDATION_PENDING` after a second restart. Exact authorization replay returned the same opaque session, changed semantics conflicted, a second active session was denied, finalization replay was stable, and reuse after consumption was denied.

Expiry and scope proof used canonical-valid disposable state. Expired sessions transitioned to `EXPIRED`, replacement received a new opaque reference, and the old session remained unusable. Unknown references and out-of-scope channel/terminal principals returned the same safe denial posture. A request-supplied Site header is not authority; Site and Site Group enforcement remains based on the durable principal grant and evidence binding, with cross-Site principals covered by the I-012 repository integration tests.

The bounded provider interruption stopped only the disposable MinIO service before upload. The actual channel route returned retryable `PROVIDER_UNAVAILABLE`, canonical status remained `UPLOAD_SESSION_AVAILABLE`, and no object became authoritative. After MinIO recovery, the same valid opaque session uploaded and finalized successfully. The initial `NoSuchBucket` observation was a disposable fixture defect; recreating the private bucket restored the expected path, and anonymous list/get remained denied.

Final focused validation totals:

- Release Central PMS API build: succeeded with 0 warnings and 0 errors.
- all statutory-evidence unit tests: 104 passed.
- repository-backed channel readiness mapping: 37 passed.
- APT statutory facade/readiness aggregation: 36 passed.
- I-012 metadata/lifecycle unit tests: 24 passed.
- I-013 upload/finalization unit tests: 21 passed.
- I-014 governance readiness unit tests: 12 passed; guarded DB no-write test: 1 passed.
- I-015 scan-worker unit tests: 10 passed.
- I-012/I-015 DB-backed repository tests: 10 passed.
- RBAC and POS readiness-envelope mapper tests: 76 passed.
- WebPay/APT statutory, Operator Console authority, ordinary-payment, and statutory fiscal mapper tests: 140 passed.
- APT payable-basis/ordinance and Operator Console API regressions: 77 passed.
- `git diff --check`: passed.

The adjacent POS profile-create `42P08` result was not reproduced after the disposable POS schema and date/profile fixtures were prepared correctly. It is classified as a prior disposable setup/request-state issue, not an I-016 Central PMS client defect. The final hosted proof used the supported POS effective-readiness boundary and did not modify ExitPass-PoSServer.

Production logs and public DTO scans found no evidence bytes, Base64, provider URL, endpoint, bucket, object key, checksum value, signed query, credential scope, provider headers, scanner endpoint, raw scanner/storage response, credentials, connection strings, SQL diagnostics, or public stack traces. PostgreSQL statutory-evidence tables contain no `bytea` or Base64 evidence columns, and privacy-safe evidence events contain controlled codes only.

I-016 proves the authoritative Central PMS contracts. G-006 can resume its WebPay consumer implementation. J-006 can resume its desktop consumer implementation and remains responsible for enforcing the authoritative Central PMS result in the Windows application; I-016 did not perform a desktop UI or physical-cash walkthrough.

Controlled UAT and production rollout remain unauthorized.
