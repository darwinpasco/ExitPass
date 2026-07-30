# ExitPass Statutory Evidence Runtime Implementation Handoff v1.0

## Purpose

This handoff breaks the secure statutory evidence runtime into bounded future tasks. It deliberately avoids one mega-branch.

## Current Verdict

The evidence contract is frozen enough for future implementation tasks to begin. Runtime is not started.

Production evidence collection remains blocked because:

- canonical schema does not yet model evidence set/item/upload operation, validation, scan, preview authorization, hold, deletion, or reconciliation lifecycle;
- Central PMS does not issue upload authorization;
- no protected object-storage adapter exists for statutory ID images;
- no validation or malware scan worker exists;
- Operator Console cannot preview evidence images securely;
- WebPay and APT upload consumers do not exist;
- retention durations and retention classes require legal/privacy approval;
- controlled UAT is not authorized.

## Future Implementation Sequence

### 1. Evidence metadata schema

Repository: canonical database repository.

Scope:

- evidence sets;
- evidence items;
- upload operations;
- validation status;
- scan status;
- binding status;
- preview authorization audit;
- retention class/version;
- hold status;
- deletion status;
- reconciliation state;
- constraints and indexes.

Do not store bytes, Base64, signed URLs, credentials, raw IDs, OCR output, or biometrics.

Validation:

- clean rebuild;
- upgrade;
- rerun;
- constraints;
- idempotency;
- no object locators in public reference columns.

### 2. Central PMS evidence object-storage adapter and upload authorization

Repository: Central PMS runtime repository.

Scope:

- provider-neutral storage interface;
- single-object pre-signed POST authorization;
- object existence/size/checksum verification;
- upload operation idempotency;
- no buffering through JSON;
- no credential exposure.

Blocked by:

- metadata schema;
- approved provider/environment configuration;
- security review.

### 3. Evidence validation and scanning worker

Scope:

- JPEG/PNG allowlist;
- file size/dimension checks;
- content sniffing;
- corrupt file rejection;
- EXIF stripping posture;
- malware scan adapter;
- quarantine-to-reviewable transition;
- scan timeout/unavailable retry.

Validation:

- object-storage emulator;
- scanner emulator;
- malicious/suspicious/timeout paths;
- no reviewer access before scan pass.

### 4. Channel upload consumers

WebPay:

- upload authorization request;
- browser transient-byte handling;
- no durable browser image storage;
- opaque evidence references in recovery state only;
- duplicate-tab and refresh behavior.

APT:

- camera/file picker handling;
- no image in SQLite, logs, print history, or cash journal;
- temp-file best-effort cleanup;
- restart recovery using opaque references only.

Operator Console request initiation:

- staff-assisted upload path;
- Site/Site Group scope;
- self-review handoff.

### 5. Operator Console secure preview

Scope:

- metadata read;
- preview authorization request;
- short-lived inline preview;
- no download by default;
- no raw object locator;
- access audit;
- no preview after terminal state except authorized audit purpose.

Manual testing required:

- reviewer preview;
- access denied;
- expired preview;
- keyboard/accessibility;
- cache restrictions.

### 6. Retention, hold, deletion, and reconciliation worker

Scope:

- retention-class selection;
- hold placement/release;
- deletion scheduler;
- object deletion;
- metadata tombstone;
- orphan reconciliation;
- mismatch handling;
- backlog metrics.

Blocked by:

- approved retention classes and legal/privacy policy;
- canonical hold/deletion model.

### 7. Controlled UAT evidence runbook

Scope:

- synthetic and approved UAT evidence;
- environment separation;
- screenshot and log privacy;
- storage access proof;
- reviewer preview proof;
- deletion and retention proof;
- incident abort criteria.

Controlled UAT remains unauthorized until all critical implementation slices pass.

## Future API Contract Summary

Recommended Central PMS routes, subject to final route convention review:

| Route | Method | Purpose |
|---|---|---|
| `/v1/statutory-discounts/evidence/upload-authorizations` | POST | Create upload authorization. |
| `/v1/statutory-discounts/evidence/upload-authorizations/{uploadOperationId}/complete` | POST | Complete upload and start validation. |
| `/v1/statutory-discounts/evidence/upload-authorizations/{uploadOperationId}/cancel` | POST | Cancel upload. |
| `/v1/statutory-discounts/evidence/sets/{evidenceSetId}` | GET | Read safe metadata/lifecycle. |
| `/v1/statutory-discounts/evidence/sets/{evidenceSetId}/bind` | POST | Bind set to statutory request. |
| `/v1/ops/operator-console/statutory-discounts/reviews/{reviewId}/evidence/{evidenceItemId}/preview-authorizations` | POST | Issue reviewer preview authorization. |
| `/v1/statutory-discounts/evidence/{evidenceItemId}/access-events` | POST | Record evidence access result. |
| `/v1/ops/statutory-discounts/evidence/{evidenceItemId}/holds` | POST | Place hold. |
| `/v1/ops/statutory-discounts/evidence/holds/{holdId}/release` | POST | Release hold. |
| `/v1/ops/statutory-discounts/evidence/{evidenceItemId}/deletion-requests` | POST | Request deletion. |
| `/v1/ops/statutory-discounts/evidence/{evidenceItemId}/lifecycle` | GET | Read lifecycle status. |

Channel proxies may exist for WebPay and APT, but Central PMS remains the evidence-control owner.

## Future Automated Test Matrix

| Test | Needs browser | Needs Windows APT | Needs object-storage emulator | Needs scanner emulator | Needs PostgreSQL | Needs controlled UAT |
|---|---|---|---|---|---|---|
| valid JPEG upload | Optional | Optional | Yes | Yes | Yes | No |
| valid PNG upload | Optional | Optional | Yes | Yes | Yes | No |
| supported PDF when approved | Optional | Optional | Yes | Yes | Yes | Maybe |
| unsupported MIME type | Optional | Optional | Yes | No | Yes | No |
| extension/content mismatch | Optional | Optional | Yes | No | Yes | No |
| corrupt image | Optional | Optional | Yes | No | Yes | No |
| oversized file | Optional | Optional | Yes | No | Yes | No |
| undersized unusable image | Optional | Optional | Yes | No | Yes | No |
| decompression bomb | No | No | Yes | No | Yes | No |
| malformed PDF | No | No | Yes | Yes | Yes | Maybe |
| encrypted PDF | No | No | Yes | Yes | Yes | Maybe |
| malicious file | No | No | Yes | Yes | Yes | No |
| scanner unavailable | No | No | Yes | Yes | Yes | No |
| scanner timeout | No | No | Yes | Yes | Yes | No |
| expired upload authorization | Optional | Optional | Yes | No | Yes | No |
| upload to wrong object locator | No | No | Yes | No | Yes | No |
| duplicate upload completion | No | No | Yes | No | Yes | No |
| changed checksum | No | No | Yes | No | Yes | No |
| changed content length | No | No | Yes | No | Yes | No |
| object replaced after scan | No | No | Yes | Yes | Yes | No |
| incomplete evidence set | Optional | Optional | Yes | Yes | Yes | No |
| wrong document type | Optional | Optional | Yes | Yes | Yes | No |
| equivalent document without description | Optional | Optional | Yes | Yes | Yes | No |
| cross-Site reference | No | No | Yes | Yes | Yes | No |
| cross-request reference | No | No | Yes | Yes | Yes | No |
| cross-entitlement reference | No | No | Yes | Yes | Yes | No |
| reference reuse | No | No | Yes | Yes | Yes | No |
| binding replay | No | No | Yes | Yes | Yes | No |
| binding conflict | No | No | Yes | Yes | Yes | No |
| unauthorized reviewer | No | No | Yes | Yes | Yes | No |
| out-of-scope reviewer | No | No | Yes | Yes | Yes | No |
| prohibited self-review | No | No | Yes | Yes | Yes | No |
| preview authorization expiry | Browser useful | No | Yes | Yes | Yes | No |
| preview URL replay | Browser useful | No | Yes | Yes | Yes | No |
| browser cache restrictions | Yes | No | Yes | Yes | Yes | No |
| no browser durable image storage | Yes | No | Yes | Yes | Yes | No |
| no APT SQLite image storage | No | Yes | Yes | Yes | Yes | No |
| no Base64 request | Yes | Yes | Yes | Yes | Yes | No |
| no payment payload image | Yes | Yes | No | No | Yes | No |
| no fiscal payload evidence | Optional | Optional | No | No | Yes | No |
| hold placement | No | No | Yes | Yes | Yes | No |
| hold release | No | No | Yes | Yes | Yes | No |
| retention policy missing | No | No | Yes | Yes | Yes | No |
| deletion eligible | No | No | Yes | No | Yes | No |
| deletion blocked by hold | No | No | Yes | No | Yes | No |
| object deletion failure | No | No | Yes | No | Yes | No |
| repeated deletion | No | No | Yes | No | Yes | No |
| orphan object | No | No | Yes | No | Yes | No |
| missing object | No | No | Yes | No | Yes | No |
| checksum reconciliation mismatch | No | No | Yes | No | Yes | No |
| environment separation | Optional | Optional | Yes | No | Yes | Yes before rollout |
| log and support-bundle privacy scan | Yes | Yes | Optional | Optional | Optional | Yes before rollout |

## Browser Test Matrix

Future browser proof must cover:

- no Local Storage image bytes;
- no Session Storage image bytes;
- no IndexedDB image bytes;
- no service worker cache of evidence;
- upload authorization expiry;
- retry after expired authorization;
- duplicate tabs;
- page refresh during upload;
- cancellation;
- review pending with opaque references only;
- no signed access authorization in recovery state.

## Windows APT Test Matrix

Future APT proof must cover:

- no image in SQLite;
- no image in application data directory after upload/cancel/restart;
- no image in print history;
- no image in cash journal;
- no image in logs;
- temp-file best-effort cleanup;
- upload retry after restart;
- no Base64 request;
- no evidence in payment or fiscal payload.

## Object Storage and Scanner Test Matrix

Future object-storage/scanner proof must cover:

- private container equivalent;
- public list denied;
- upload authorization cannot read;
- preview authorization cannot write/delete/list;
- content-length enforcement;
- content-type enforcement;
- checksum verification;
- quarantine before scan;
- scan pass/fail/timeout/unavailable;
- object replacement detection;
- delete and tombstone;
- orphan reconciliation.

## Required Policy Decisions Before Runtime

- approved retention classes and durations;
- provider selection and environment configuration;
- regional/data-residency posture;
- whether PDF support is allowed;
- whether any audit role may download/export evidence;
- whether watermarking is required for reviewer previews;
- support-bundle redaction process;
- controlled UAT evidence runbook.

## Non-Goals For Future First Runtime Slice

The first runtime slice must still avoid:

- OCR;
- biometric extraction;
- automatic entitlement decisioning;
- POS Server evidence payloads;
- payment-provider evidence payloads;
- fiscal evidence embedding;
- broad export;
- production rollout.

