# ExitPass WebPay Statutory Evidence Capture, Upload, and Recovery v1.0

## Scope

G-006 consumes the merged I-016 WebPay channel-safe statutory-evidence contract. WebPay captures one JPEG or PNG image, requests an opaque upload session, streams bytes through the same-origin WebPay boundary, finalizes the upload, and presents the authoritative lifecycle returned by Central PMS.

This slice does not add evidence preview, download, OCR, biometric processing, evidence review, approval, retention workers, Controlled UAT, or production rollout.

## Authority

- Central PMS derives Site, Site Group, evidence governance, media rules, replacement permission, validation, malware, reviewability, approval, and payable-basis authority.
- Payment Orchestrator authenticates to Central PMS with the existing non-human WebPay service identity and `statutory-discounts.evidence.capture.webpay` permission.
- WebPay uses only relative `/v1/webpay/...` routes. It does not add service identity, permission, Site-authority, Site Group-authority, or internal authorization headers.
- The browser performs MIME and size checks only for early feedback. Central PMS remains authoritative.
- Upload completion, validation, malware processing, reviewability, approval, and application remain distinct states.

## Route Map

| Browser route | Method | Purpose |
| --- | --- | --- |
| `/v1/webpay/statutory-discounts/evidence/bootstrap` | POST | Derive or rediscover the evidence requirement from a canonical decision command. |
| `/v1/webpay/statutory-discounts/evidence/status` | GET | Read authoritative evidence lifecycle by exactly one decision command or evidence-set reference. |
| `/v1/webpay/statutory-discounts/evidence/upload-sessions` | POST | Request one opaque, bounded upload session. |
| `/v1/webpay/statutory-discounts/evidence/upload-sessions/{opaqueReference}` | PUT | Stream the selected image through the same-origin channel-safe relay. |
| `/v1/webpay/statutory-discounts/evidence/upload-sessions/{opaqueReference}/finalize` | POST | Finalize through I-013 provider metadata verification and return authoritative evidence state. |

The browser never calls Central PMS, MinIO, S3, a scanner, or an evidence-review endpoint directly.

## Browser State Model

WebPay presents separate customer-safe states for:

- not required;
- required and ready for file selection;
- local validation failure;
- authorization preparation;
- upload in progress or cancelled;
- upload interruption, expiration, or provider failure;
- finalization in progress or failed;
- validation pending or failed;
- scan pending, retryable, failed, or unsafe file detected;
- not reviewable, reviewable, or review pending;
- approved, rejected, or applied;
- replacement allowed or not allowed;
- unknown fail-closed status.

`REVIEWABLE` explicitly does not mean approved. `APPROVED` explicitly does not mean applied. A successful browser PUT does not mean finalization or validation succeeded.

Only `VALIDATION_PENDING` and `SCAN_PENDING` use bounded polling. Terminal, reviewable, approval, rejection, applied, unknown, and retry-required states stop automatic polling.

## Capture and Upload

- The file input is labelled, keyboard accessible, constrained to `image/jpeg,image/png`, and includes `capture="environment"` for supported mobile browsers.
- Exactly one non-empty file is accepted. PDF, unsupported MIME types, and files over the server-returned limit are rejected before authorization.
- The selected `File` remains in component memory only for the active upload operation.
- I-016 requires a declared SHA-256 value when requesting an upload session. WebPay computes it transiently, sends it only in that request, and does not display, persist, log, or place it in an operation key. Central PMS verifies and owns checksum authority.
- The server returns only an opaque upload-session reference, method, expiry, accepted content type, and maximum length. No storage URL, bucket, object key, provider header, or credential is returned.
- The PUT body is streamed through Payment Orchestrator and Central PMS. The backend does not buffer it into an application DTO or persist it in PostgreSQL.
- Cancellation aborts only the browser transfer. WebPay then retrieves authoritative status before another attempt.

## Restart Recovery

G-005 restores the canonical statutory decision through decision readback or pending-lifecycle rediscovery. Once the decision is restored, the evidence component calls I-016 bootstrap again with the canonical decision command ID. It does not infer upload success from the prior browser process.

After refresh or restart:

1. resolve or rediscover the statutory lifecycle;
2. bootstrap evidence from the canonical decision;
3. discard any prior in-memory file and opaque upload session;
4. show the authoritative evidence lifecycle;
5. ask the customer to reselect a file only when capture or replacement is allowed.

No evidence reference, upload session, checksum, provider information, lifecycle authority, or bytes are added to localStorage, sessionStorage, IndexedDB, Cache Storage, cookies, or URL parameters. Existing G-005 recovery metadata remains a non-authoritative hint.

## Replacement and Retry

Capture controls are available only for capture-ready states or an explicit `REPLACEMENT_ALLOWED` response. `REPLACEMENT_NOT_ALLOWED` removes the file control and displays safe read-only guidance. WebPay does not override review locks or stale versions.

Retries are customer initiated except for bounded validation and scan polling. Authentication, authorization, provider, timeout, conflict, malformed, and unknown failures use the existing safe public error envelope. Raw Central PMS or provider text is not reflected.

## Payment Boundary

Evidence capture, upload, finalization, validation, malware processing, and reviewability never authorize a discount. G-005's warned regular-payment action remains available while the statutory request is pending when ordinary payment authority remains valid. WebPay continues to use Central PMS payable-basis authority and Payment Orchestrator replay protection.

## Accessibility and Responsive Behavior

- labelled file input and clear JPEG/PNG guidance;
- visible keyboard focus;
- accessible progress element and bounded live status updates;
- labelled retry, refresh, and cancel actions;
- focus movement to terminal upload errors;
- non-color status labels;
- single-column evidence rules and actions on narrow viewports;
- long messages and selected file names wrap safely.

## Automated Validation

Focused coverage verifies client routes and headers, exact I-016 field names, JPEG/PNG acceptance, PDF/empty/oversize rejection, opaque authorization, progress, cancellation plumbing, finalization, safe errors, replacement lock, lifecycle separation, and browser-storage exclusion.

The deterministic Chromium fixture is loopback-only and validation-only. It records counts and metadata, never evidence bytes. It covers not-required, JPEG, PNG, PDF/oversize rejection, provider interruption, refresh rediscovery, reviewable, malware, replacement lock, narrow layout, keyboard focus, same-origin Network headers, and browser storage.

## Manual Walkthrough

Use synthetic images only. The persistent loopback harness leaves WebPay available for Edge and DevTools inspection. From `src/Services/WebPayUi`:

The first manual-harness revision built WebPay without `VITE_WEBPAY_DEFAULT_VENDOR_SYSTEM_ID`, so the browser stopped at a local configuration error before resolving `WEBPAY-EVIDENCE-G006`. The corrected harness owns the deterministic browser-smoke context: Site Group `40000000-0000-4000-8000-000000000001`, Site `50000000-0000-4000-8000-000000000001`, and test-only vendor system `60000000-0000-4000-8000-000000000001`. It injects these values only into the harness build process; Darwin does not set environment variables manually.

`SelfTest` now rejects missing or mismatched vendor configuration, validates the loopback WebPay and Payment Orchestrator fixture URLs, validates fixture files and runtime state, calls the actual `/v1/webpay/parking-session` route for the synthetic ticket, and runs a focused Chromium probe through statutory submission until the evidence panel is visible. `Start` repeats the ticket probe before leaving a clean persistent fixture for the walkthrough.

```powershell
npm.cmd ci
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Invoke-WebPayStatutoryEvidenceManualValidation.ps1 -Action SelfTest
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Invoke-WebPayStatutoryEvidenceManualValidation.ps1 -Action Start -Scenario validation-pending
Start-Process msedge.exe -ArgumentList '--inprivate','http://127.0.0.1:5196/'
```

Enter ticket `WEBPAY-EVIDENCE-G006`. Switch a running fixture without restarting it by using `-Action SetScenario -Scenario <name>`, then resolve the ticket and submit a new synthetic statutory request. Read privacy-safe counters with `-Action State`. At the end, run `-Action Stop` followed by `-Action Cleanup`; these operations use only the recorded harness PID and `.local\g006-manual`.

Confirm each deterministic scenario in the headed browser:

1. evidence not required;
2. required JPEG and PNG uploads;
3. PDF and oversize rejection before authorization;
4. progress, cancellation, interruption, retry, expiration, and finalization;
5. refresh and browser restart with server-authoritative rediscovery;
6. validation pending, scan pending, reviewable, malware, review pending, approved, rejected, applied, and unknown fail-closed presentation;
7. replacement allowed and review-locked replacement denied;
8. regular-payment continuation without privilege application;
9. desktop, narrow, keyboard, visible-focus, and status-announcement behavior;
10. Network inspection for same-origin routes and absent privileged headers;
11. Storage inspection for absent evidence bytes, Base64, upload session, checksum, and lifecycle authority.

The deterministic automated proof does not substitute for Darwin's required significant browser walkthrough.

## Readiness

Automated build, unit, integration, Chromium, security, privacy, and cleanup evidence must pass before PR preparation. Controlled UAT and production rollout remain unauthorized.
