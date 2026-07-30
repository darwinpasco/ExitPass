# ExitPass Statutory Evidence Authorization and Access Matrix v1.0

## Purpose

This document freezes the statutory evidence authorization contract. It uses the I-002 statutory privilege permission catalog and does not introduce new production runtime, persistence, role-management APIs, or UI behavior.

## Current Permission Inventory

Existing relevant permission identifiers:

| Permission | Current use | Evidence contract posture |
|---|---|---|
| `statutory-discounts.evidence.capture` | Operator Console metadata capture policy | Required for assisted upload authorization and metadata submission. |
| `statutory-discounts.evidence.view` | Operator Console metadata list policy | Required for reviewer preview authorization; queue/detail read does not imply it. |
| `statutory-discounts.review.queue.read` | Service-channel review queue | Does not grant evidence preview. |
| `statutory-discounts.review.detail.read` | Service-channel review detail | Does not grant evidence preview. |
| `statutory-discounts.decision.approve` | Human approval | Does not grant evidence preview by itself. |
| `statutory-discounts.decision.reject` | Human rejection | Does not grant evidence preview by itself. |
| `statutory-discounts.audit.read` | Audit/report read | Metadata audit only unless paired with evidence-view or future audit-evidence permission. |
| `statutory-discounts.decision.submit.webpay` | WebPay service request submit | May submit opaque evidence references; does not grant evidence read. |
| `statutory-discounts.decision.submit.assisted-payment-terminal` | APT service request submit | May submit opaque evidence references; does not grant evidence read. |
| `statutory-discounts.payable-basis.apply` | Payment-time service application | Must not receive evidence bytes or preview authorization. |

Future permission identifiers required before runtime:

| Permission | Purpose | Blocker |
|---|---|---|
| `statutory-discounts.evidence.preview.issue` | Issue short-lived reviewer preview authorization | Not in catalog or canonical reference data. |
| `statutory-discounts.evidence.hold.manage` | Place/release legal or operational hold | Not in catalog or canonical reference data. |
| `statutory-discounts.evidence.delete.request` | Request governed deletion | Not in catalog or canonical reference data. |
| `statutory-discounts.evidence.lifecycle.read` | Read detailed lifecycle status | Not in catalog or canonical reference data. |

Do not implement these future identifiers as hard-coded runtime bypasses before RBAC catalog and persistence promotion.

## Actor Access Matrix

| Actor class | Upload authorization | Evidence metadata read | Preview evidence | Place hold | Delete | Notes |
|---|---|---|---|---|---|---|
| WebPay customer via Payment Orchestrator | Allowed through WebPay service and active request context | Own opaque status only | No | No | No | Browser never receives object credentials or read access. |
| APT channel | Allowed through APT service and active request context | Own opaque status only | No | No | No | No evidence bytes in SQLite, print, or logs. |
| Operator Console request initiator | Allowed with evidence capture permission and scope | Own request metadata | No unless separately evidence-view authorized | No | No | Self-review restrictions still apply. |
| Operator Console reviewer | No upload by review action | Review metadata by detail permission | Allowed only with evidence view and scope | No by default | No | Approval/rejection still requires separate decision permission. |
| Evidence viewer | No unless also capture actor | Scoped metadata | Scoped preview | No | No | Evidence viewing is separated from queue/detail. |
| Auditor | Metadata/audit read | Audit metadata | Preview only with explicit evidence/audit evidence permission | Possible with hold permission | No | Audit access is read-only unless hold/delete permission exists. |
| Privacy/compliance hold actor | No | Scoped lifecycle | Optional by policy | Yes | No | Hold does not broaden read access. |
| Retention deletion worker | No | Lifecycle metadata | No | No | Yes through service identity | Deletes only eligible objects and tombstones metadata. |
| WebPay service principal | Submit references, request upload authorization | Own channel status | No | No | No | Cannot approve or reject. |
| APT service principal | Submit references, request upload authorization | Own channel status | No | No | No | Cannot approve or reject. |
| POS Server | No | No | No | No | No | Fiscal payloads remain evidence-free. |
| Payment provider | No | No | No | No | No | Provider payloads remain evidence-free. |

## Scope Rules

Evidence authorization follows the I-002 Site and Site Group contract:

- authorization scope comes from server-side grants or access evaluations;
- request `siteId` and `siteGroupId` are resource facts, not permission claims;
- Site authority permits evidence access only for that Site;
- Site Group authority permits access only to member Sites in that Site Group;
- global authority is exceptional and explicit;
- unknown Site-to-Site Group membership fails closed;
- stale cached grants must not silently broaden access after membership changes.

Future runtime must check scope on:

- upload authorization creation;
- upload completion;
- evidence set binding;
- metadata read;
- preview authorization issuance;
- access event recording;
- hold placement/release;
- deletion request;
- lifecycle read.

## Human-Versus-Service Boundary

Human endpoints:

- may initiate or review evidence only when the human actor has matching permission and scope;
- cannot present service identity headers as proof of authority;
- cannot invoke payment-time application because of evidence authority.

Service endpoints:

- may submit request evidence references only for their channel;
- cannot approve or reject eligibility;
- cannot receive reviewer preview access;
- cannot read evidence unless explicitly built as an internal evidence-control service with least privilege.

`WEBPAY` and `ASSISTED_PAYMENT_TERMINAL` source-channel values are business attribution only and do not authenticate access.

## Separation Of Duties

Self-review:

- A human actor who initiated evidence collection must not approve or reject the same request when durable initiator identity is available.
- The comparison must use server-side actor identity, not browser display names.
- Current service-channel self-review facts are incomplete; future evidence runtime must not claim full SoD certification until durable initiator linkage is present.

Evidence separation:

- queue read does not grant evidence preview;
- detail read does not grant evidence preview;
- approve/reject does not grant evidence preview;
- policy administration does not grant evidence preview;
- evidence preview does not grant approve/reject.

Hold and deletion separation:

- ordinary reviewers cannot place holds;
- hold actors cannot preview evidence unless separately authorized;
- deletion workers cannot preview evidence.

## Endpoint Policy Handoff

Future endpoint metadata must distinguish:

| Operation | Named policy target | Required permission |
|---|---|---|
| Create upload authorization | `CentralPmsStatutoryEvidenceUploadAuthorize` | `statutory-discounts.evidence.capture` plus channel submit permission where service-channel originated |
| Complete upload | `CentralPmsStatutoryEvidenceUploadComplete` | `statutory-discounts.evidence.capture` plus item ownership/scope |
| Cancel upload | `CentralPmsStatutoryEvidenceUploadCancel` | `statutory-discounts.evidence.capture` plus item ownership/scope |
| Bind evidence set | `CentralPmsStatutoryEvidenceBind` | request submit permission and evidence capture context |
| Read metadata | `CentralPmsStatutoryEvidenceMetadataRead` | `statutory-discounts.evidence.view` or lifecycle read by actor class |
| Issue preview authorization | `CentralPmsStatutoryEvidencePreviewIssue` | `statutory-discounts.evidence.view` plus review/detail scope |
| Place/release hold | `CentralPmsStatutoryEvidenceHoldManage` | future hold permission |
| Request deletion | `CentralPmsStatutoryEvidenceDeleteRequest` | future deletion permission |
| Lifecycle read | `CentralPmsStatutoryEvidenceLifecycleRead` | future lifecycle read or audit permission |

These names are proposed contract names only. Runtime implementation must use the established catalog naming convention and update canonical reference data before production use.

## Access Failure Rules

Fail closed when:

- permission is missing;
- actor class is wrong;
- Site or Site Group scope is missing or ambiguous;
- evidence reference is unknown;
- evidence belongs to another request, Site, Site Group, or entitlement;
- evidence is not reviewable;
- evidence is deleted, expired, or deletion pending;
- hold blocks deletion;
- retention policy is missing for production collection;
- scan or validation is incomplete.

Customer messages must not expose internal permission identifiers. Operator/admin diagnostics may include safe classifications only.

