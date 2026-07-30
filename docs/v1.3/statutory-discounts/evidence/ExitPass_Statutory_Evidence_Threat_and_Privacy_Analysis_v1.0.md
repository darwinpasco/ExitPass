# ExitPass Statutory Evidence Threat and Privacy Analysis v1.0

## Purpose

This document records the threat model and privacy analysis for statutory Senior Citizen and PWD evidence images. It is provider-neutral and contract-only.

## Threat Model

| Threat | Mitigation | Remaining risk |
|---|---|---|
| Evidence-reference guessing | UUID v4 opaque references and mandatory server authorization. | Online guessing still needs rate limits and monitoring. |
| Signed upload/read authorization leakage | Short expiry, single-object scope, no logging, no browser recovery storage. | User screenshots or compromised browser may expose a live authorization. |
| Internal object locator leakage | Locator internal-only; not in DTOs, logs, browser storage, payment, or fiscal payloads. | Debug tooling must be reviewed. |
| Cross-Site evidence access | Scope check uses server-side Site/Site Group resource facts. | Full enforcement depends on future scoped grant runtime. |
| Cross-request evidence reuse | Evidence item bound to exactly one request; binding rejects unrelated reuse. | Same customer repeating uploads creates duplicate bytes unless future dedupe is approved. |
| Malicious file upload | Allowlist, byte sniffing, quarantine, malware scan before preview. | Scanner coverage is not perfect. |
| Polyglot file | Detected type and decoder validation; reject mismatch. | Parser bypass risk remains and requires updates. |
| Decompression bomb | Size, dimensions, decoded-pixel caps, decode safeguards. | New image-parser vulnerabilities may appear. |
| EXIF location leakage | Strip metadata before reviewable storage or preview. | Original quarantined object may retain metadata until transformed or deleted. |
| Browser cache leakage | `Cache-Control: no-store`, no durable browser image storage, no service worker cache. | Browser/OS implementation bugs and screenshots remain residual risks. |
| Local Storage leakage | Evidence bytes and signed URLs prohibited; only opaque references allowed. | Existing WebPay recovery stores internal command refs until replaced by continuation token. |
| APT SQLite leakage | No evidence bytes in SQLite; temp files deleted best-effort. | SSD recovery cannot be guaranteed. |
| Log leakage | Structured logging allowlist; no object locators, signed URLs, raw responses, bytes, full IDs. | Developer-added debug logs remain a process risk. |
| Support-bundle leakage | Support bundles exclude evidence bytes and protected locators. | Manual screenshots may bypass tooling. |
| Credential theft | Least privilege, rotation, no credentials in clients/logs. | Compromised runtime identity can still act within granted scope. |
| Stale upload authorization | Short-lived, single-purpose, invalid after expiry/terminal request. | Object residue after late upload requires reconciliation. |
| Replay | Idempotency keys and semantic conflict checks. | Poor client retry behavior can still create user friction. |
| Concurrent upload completion | Row version and object fact comparison. | Worker ordering must be transactionally tested. |
| Byte replacement after scan | Immutable object or version check; checksum verified before preview. | Provider misconfiguration can permit overwrite. |
| Time-of-check/time-of-use race | Preview authorization verifies latest lifecycle and object facts. | Race tests require object-storage emulator. |
| Scanner bypass | Preview blocked until scan passed and scan policy current. | Scanner unavailable delays review and may push user to ordinary payment. |
| Object deletion race | Deletion state blocks preview before object delete. | Provider delete eventual consistency must be reconciled. |
| Hold bypass | Deletion worker checks hold immediately before delete. | Missing hold persistence would block production. |
| Retention misconfiguration | Production collection blocked without retention policy. | Wrong approved policy remains governance risk. |
| Excessive retention | Retention class/version visible to audit; deletion backlog metrics. | Legal policy may still require long retention. |
| Premature deletion | Hold and retention checks; deletion audit/tombstone. | Incorrect policy assignment may delete too early. |
| Public container configuration | Public access disabled, list denied, configuration checks. | Cloud console misconfiguration requires monitoring. |
| Environment mixing | Separate storage posture and internal locator namespace per environment. | Misconfigured credentials can cross environments. |
| Insider evidence browsing | Evidence preview requires permission, scope, audit, and short-lived grants. | Authorized reviewers can still view necessary evidence. |
| Reviewer screenshot or external-camera capture | Policy, training, watermarking consideration, audit. | Cannot be fully technically prevented. |
| Object-store outage | Evidence unavailable; ordinary payment remains available. | Statutory review may be delayed. |
| Metadata database outage | Evidence lifecycle unavailable; fail closed. | Ordinary payment remains available. |
| Reconciliation failure | Alerts and backlog metrics. | Orphans may persist until reconciliation recovers. |

## Privacy Principles

Purpose limitation:

- Evidence is collected only to support statutory parking privilege eligibility review for one request.
- Evidence is not used for OCR, biometric matching, fraud analytics, marketing, payment scoring, gate decisions, or fiscal reporting without a separate approved privacy review.

Data minimization:

- Collect only required document images and controlled metadata.
- Store image bytes only in protected object storage.
- Store PostgreSQL metadata and opaque references only.
- Do not extract or persist full ID number, name, birth date, address, face template, or biometrics.

Access limitation:

- Evidence access requires separate permission and scope.
- Review queue/detail access does not imply image access.
- Payment, POS Server, HikCentral, and provider systems do not receive evidence bytes.

Storage limitation:

- Retention is server-side policy controlled.
- Production collection is blocked without approved retention class and policy version.
- Failed, abandoned, and malicious uploads have bounded cleanup classes.

Auditability:

- Creation, upload, validation, scan, binding, preview, access denial, hold, deletion, and reconciliation events are auditable.
- Audit events contain safe references and classifications only.

Deletion governance:

- Deletion checks retention and hold state.
- Metadata tombstone remains minimal.
- Deleted evidence cannot be restored through ordinary review APIs.

Incident response:

- Suspected evidence leakage requires blocking preview, revoking active access authorizations, placing hold where appropriate, rotating credentials if needed, and preserving audit facts.
- Raw evidence must not be attached to general incident tickets unless a restricted evidence channel is approved.

Support handling:

- Support may use safe metadata and lifecycle statuses.
- Support does not receive raw images by default.
- Support bundles exclude evidence bytes, signed URLs, object locators, credentials, and full ID values.

Non-production data:

- Local development and automated tests use synthetic files only.
- Controlled UAT may use approved test evidence only under a separate runbook.
- Production evidence must not be copied into development or test environments.

Prohibited expansion:

- OCR;
- facial recognition;
- biometric processing;
- automatic entitlement decisions;
- unrestricted document recognition;
- representative identity collection unless separately approved.

Future privacy-impact assessment triggers:

- enabling PDF support;
- changing retention durations;
- adding OCR or automatic extraction;
- adding biometric or face comparison;
- exporting evidence;
- allowing bulk download;
- sharing evidence with third parties;
- storing evidence outside the approved environment region;
- changing storage provider;
- using evidence for fraud analytics.

## Privacy Gaps In Current Runtime

| Gap | Current fact | Required next step |
|---|---|---|
| Secure image upload absent | Metadata-only DTOs and repository exist. | Add protected storage and upload authorization. |
| Retention duration not approved | Canonical table has retention code/expiry fields only. | Promote retention policy model and approved classes. |
| Scan and validation absent | No validation or scanner worker found. | Add validation/scanning worker before review preview. |
| Preview authorization absent | Evidence list returns metadata only. | Add short-lived preview authorization and audit. |
| Browser recovery stores internal refs | Existing WebPay localStorage recovery stores statutory command refs. | Replace public continuation with opaque server token. |
| `StorageReference` is too broad | Current metadata can contain upload metadata strings. | Replace public use with opaque evidence references and keep locators internal-only. |

## Security and Privacy Decision

The secure evidence contract is complete for implementation planning, but statutory evidence runtime remains blocked until metadata schema, object storage, validation, scan, preview authorization, retention, deletion, reconciliation, and channel consumers are implemented and tested.

