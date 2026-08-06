# ExitPass Operator Console Statutory Evidence Review Workspace v1.0

## Purpose

H-005 adds review-safe statutory-evidence metadata and temporary JPEG/PNG preview to the existing Operator Console statutory-discount review detail. Evidence supports the reviewer; preview success does not approve, reject, verify, mutate payable basis, or change evidence lifecycle.

## Existing architecture audit

The Operator Console is a separate React 19, TypeScript, and Vite application under `src/Services/OperatorConsoleUi`. It uses an in-application history router, a shared same-origin `/v1` client, a statutory queue/detail flow, Vitest with jsdom, and a Playwright Chromium fixture server. The existing detail already owns reviewer attestation, rejection reason, approve/reject submission, stale-decision behavior, and the boundary that applies an approved privilege later through WebPay or Cashier-Assisted Terminal.

H-005 reuses that detail route:

`/operator-console/statutory-discounts/{draftId}`

No parallel frontend or route alias was added. The decision command identifier returned by the existing detail API remains internal and is used only as I-017 route authority.

## I-017 integration

The browser uses only these same-origin, read-only routes:

- `GET /v1/ops/operator-console/statutory-discounts/reviews/{decisionId}/evidence`
- `GET /v1/ops/operator-console/statutory-discounts/reviews/{decisionId}/evidence/{evidenceItemReference}/preview`

The dedicated permission is `statutory-discounts.evidence.review.view`, enforced by Central PMS policy `OperatorConsoleStatutoryEvidenceReviewView`. Frontend capability rendering is advisory; backend authorization, Site scope, Site Group scope, current lifecycle, object version, and preview eligibility remain authoritative.

Both requests use `cache: "no-store"`. Preview accepts only `image/jpeg` and `image/png`. No provider URL, signed URL, object key, bucket, checksum, service credential, or storage diagnostic is accepted as preview authority.

## Workspace layout

The existing review detail now presents:

- entitlement, request channel, review status, evidence-set posture, replacement posture, hold, retention, and deletion summaries;
- one review-safe item card per I-017 item;
- upload, structural validation, malware scan, reviewability, binding, retention, deletion, hold, safe file size, and finalized time;
- backend-provided preview eligibility and a controlled denial explanation;
- an explicit `Preview` action for currently eligible JPEG/PNG evidence only;
- the existing document-verification and approve/reject sections as separate workflows.

Canonical decision, evidence-set, evidence-item, Site, Site Group, parking-session, payment, and correlation identifiers are not rendered. Item references remain internal React keys and request parameters.

## Preview design and lifecycle

Preview uses authenticated fetch followed by an in-memory `Blob` object URL. The image remains hidden behind an announced decoding state until its browser `load` event proves successful decoding. Decode failure revokes the failed URL, removes the broken image, and presents a controlled retry state. The dialog preserves aspect ratio, contains the image within the viewport, supports zoom in, zoom out, and fit-to-view, closes with `Escape`, traps focus while open, and returns focus to the invoking control.

The frontend aborts active preview requests, clears preview state, and revokes an object URL when:

- the dialog closes;
- the evidence item, decision, Site, or Site Group changes;
- authorization-driven component state changes;
- the component unmounts.

No download action, iframe, external image proxy, provider URL, Base64 persistence, browser history entry, or durable preview cache is created.

## Controlled state mapping

I-017 denial codes map to fixed operator messages for validation pending/failed, scan pending/unavailable/failed, malware detected, not reviewable, invalid binding, stale/replaced evidence, deletion in progress, inaccessible retention, unfinished upload, missing evidence, and unsupported media. Unknown codes fail closed as `This evidence is not currently eligible for preview.`

Metadata `403` becomes a safe access-denied state. Metadata `404` uses an anti-enumerated missing-or-out-of-scope state. Preview `404` is non-retryable. Storage unavailability is retryable and reissues the Central PMS preview route, forcing current authority revalidation. Raw `ProblemDetails.detail`, response bodies, URLs, provider diagnostics, SQL, exceptions, and stack traces are never rendered.

## Review-flow separation

Preview does not call the existing evidence-capture mutation, review-decision mutation, or any payable-basis route. It does not mark evidence reviewed and does not require every item to be previewed. Existing approval attestation, rejection reasons, confirmations, concurrency handling, and payment-time application boundaries remain unchanged.

## Accessibility and responsive behavior

The workspace uses semantic headings and description lists, text-backed status labels, announced loading/errors, labelled controls, a modal dialog with focus management, accurate image descriptions without statutory IDs, and no hover-only state. Layout proof covers desktop, 390 x 844, and 200% browser zoom. A pre-existing rejection selector overflow at narrow width was corrected with bounded form-control sizing.

## Security and privacy controls

- Preview metadata and bytes remain in memory only while needed.
- `localStorage`, `sessionStorage`, IndexedDB, and Cache Storage are not used.
- Preview response payloads do not enter telemetry, analytics, console logging, URLs, or screenshots committed to source.
- Runtime DOM checks reject application GUIDs in text, form values, accessible metadata, and every non-blob attribute. A browser-generated UUID is permitted only in an `img` `blob:` URL created from the fetched image `Blob`; it is never application authority and remains subject to object-URL revocation.
- Synthetic fixture identifiers remain only in non-user-visible test source.
- The fixture server returns no-store, no-cache, no-sniff, inline, and no-referrer headers for preview bytes.

## Automated validation

Run from `src/Services/OperatorConsoleUi`:

```powershell
npm.cmd ci
npx.cmd tsc -b --pretty false
npm.cmd test
npm.cmd run build
npm.cmd run test:browser-smoke
```

Focused tests cover the HTTP boundary, malformed payloads, media validation, every controlled lifecycle denial, empty/denied/unavailable metadata, preview retry, cancellation, object URL revocation, decision and scope changes, focus return, no browser storage writes, no mutation calls, and DOM privacy. Chromium tests require `img.complete`, positive natural dimensions, exact synthetic dimensions, matching JPEG/PNG signatures and response media types, visible zoom resizing, fit restoration, and decoded storage-outage recovery. The complete suite also preserves the pre-existing statutory ordinance review scenarios.

## Manual walkthrough

Build, then start the deterministic server:

```powershell
cd D:\wt\H005\src\Services\OperatorConsoleUi
npm.cmd ci
npm.cmd run build
npm.cmd run test:browser-smoke:server
```

The server prints all H-005 URLs. The JPEG fixture is a blue-and-orange landscape image; the PNG fixture is a purple-and-green portrait image. Validate that each image is visibly different, zoom changes its displayed size, and Fit to view restores the fitted presentation. Also validate validation pending, malware detected, replaced evidence, storage outage/retry, permission denial, cross-Site denial, cross-Site-Group denial, decision switching, keyboard-only operation, `Escape`, focus return, 390 x 844, and 200% zoom.

In DevTools, confirm review evidence uses only the two I-017 GET route shapes and contains no provider or storage authority. Inspect Local Storage, Session Storage, IndexedDB, and Cache Storage and confirm no preview bytes, Base64, evidence identifiers, storage locators, permission authority, or scope authority are persisted.

Run this DOM check in representative states. It deliberately permits browser-generated UUIDs only in `img[src^="blob:"]`:

```javascript
const guidPattern = /\b[0-9a-f]{8}(?:-[0-9a-f]{4}){3}-[0-9a-f]{12}\b/ig;

[...document.querySelectorAll("*")].flatMap((element) => {
  const findings = [];
  for (const attribute of element.attributes) {
    guidPattern.lastIndex = 0;
    if (
      guidPattern.test(attribute.value) &&
      !(element instanceof HTMLImageElement && attribute.name === "src" && attribute.value.startsWith("blob:"))
    ) {
      findings.push({ type: "attribute", tag: element.tagName, attribute: attribute.name, value: attribute.value });
    }
  }
  for (const node of element.childNodes) {
    guidPattern.lastIndex = 0;
    if (node.nodeType === Node.TEXT_NODE && node.textContent && guidPattern.test(node.textContent)) {
      findings.push({ type: "text", tag: element.tagName, value: node.textContent });
    }
  }
  if (element instanceof HTMLInputElement || element instanceof HTMLTextAreaElement || element instanceof HTMLSelectElement) {
    guidPattern.lastIndex = 0;
    if (guidPattern.test(element.value)) {
      findings.push({ type: "form-value", tag: element.tagName, value: element.value });
    }
  }
  return findings;
});
```

Expected: `[]`.

While an eligible preview is open, verify the permitted local rendering URL and successful decoding separately:

```javascript
const previewImage = document.querySelector(".statutoryEvidencePreviewDialog img");
({
  usesBlobUrl: previewImage?.src.startsWith("blob:") ?? false,
  complete: previewImage?.complete ?? false,
  naturalWidth: previewImage?.naturalWidth ?? 0,
  naturalHeight: previewImage?.naturalHeight ?? 0
});
```

Expected: `usesBlobUrl` and `complete` are `true`; both natural dimensions are greater than zero. The UUID portion of this browser-local URL is not an ExitPass identifier.

Press Enter in the server terminal to stop only the fixture process started by the script.

## Completion boundary and exclusions

H-005 implements review-safe metadata and temporary JPEG/PNG preview only. Evidence download, PDF preview, OCR, biometric processing, facial recognition, automatic entitlement decisions, Operator Console upload/replacement, hold mutation, deletion requests/workers, retention workers, backend expansion, database changes, Controlled UAT, and production rollout remain out of scope.

Manual headed-browser approval remains required before staging or PR authorization.
