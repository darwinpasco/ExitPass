# ExitPass Central PMS Statutory Service-Channel Deferred Application Authorization Correction

## Purpose

This correction separates the caller authorization for two distinct statutory-benefit operations:

- a human Operator Console reviewer decides eligibility; and
- an authenticated WebPay or Assisted Payment Terminal service identity later requests application after the payable basis exists.

It corrects the blocking result from `OPCON-MVP-ACCEPT-20260825T011914Z-merged-rerun`. That acceptance remains failed and its external evidence is unchanged. Whole-console runtime and visual acceptance remains pending.

## Root Cause

The shared statutory-decision facade correctly resolved the approved eligibility and reviewer, but passed the service request's null Operator Console device and shift into the existing payable-basis application service. That service always evaluated Operator Console human readiness. The production evaluator therefore denied a valid service-channel application, while an integration-test replacement that always allowed Operator Console access masked the defect.

## Corrected Authority Boundary

Human review continues to require the existing H-006 session, CSRF protection, trusted Operator Console device, active operator shift, permission, Site or Site Group scope, session validity, credential version, and authorization epoch. This correction does not make device or shift optional for a human caller and does not add a browser-controlled authority field.

For deferred application, the endpoint creates a server-only caller context from the authenticated service identity and the single unambiguous submit permission. The production service-channel authorization policy then requires:

- an active canonical service identity;
- the WebPay or Assisted Payment Terminal application audience implied by the identity's canonical owning service;
- the exact channel permission;
- an active `SERVICE_PRINCIPAL` assignment for the request Site;
- a source channel matching the stored canonical decision;
- a Site matching the canonical approved validation/detail;
- an approved terminal decision for the matching parking session and entitlement;
- the canonical payable basis; and
- the existing application idempotency and concurrency protections.

Wrong-Site access and cross-channel access are concealed as `STATUTORY_DISCOUNT_DECISION_NOT_FOUND`. Other invalid service identity, audience, or lifecycle states fail closed as `ACCESS_DENIED`. The browser request cannot provide the server-only service caller context.

## Application and Attribution

Both caller policies converge on the existing payable-basis writer, statutory calculation, tariff-snapshot creation, staged command, advisory lock, and replay behavior. No duplicate calculation or application path is introduced.

The approved validation continues to retain `validated_by_user_id` as the human reviewer. The later application is persisted as `SYSTEM` and uses the existing service-identity attribution columns on the payable-basis application, validation update, and applied tariff snapshot. The service caller is not represented as the reviewer, and the reviewer is not represented as the later application caller. Correlation identifiers remain present on the canonical command and application records and on structured authorization audit events.

## Persistence Posture

No schema, migration, or locked v1.2 DDL change is required. The canonical schema already contains the necessary service-identity attribution columns, service-principal Site assignments, application channel, staged command, application, and tariff-snapshot structures.

## Regression Coverage

Focused production-path coverage uses actual dependency-injection registration, the production service-channel authorization service and PostgreSQL repository, isolated PostgreSQL 16, canonical service identities, canonical Site assignments, approved review records, payable-basis creation, application persistence, and replay.

The coverage proves WebPay and Assisted Payment Terminal success without Operator Console device or shift facts; one application and one applied tariff snapshot; PHP results; reviewer/service attribution separation; wrong audience, inactive identity, wrong Site, and cross-channel denial; pending and rejected non-application; human H-006 device/shift enforcement; RBAC and audience isolation; and the removed Operator Console application route.

This targeted correction is self-reviewed. Independent review was not performed. It does not mark the Operator Console MVP accepted.

