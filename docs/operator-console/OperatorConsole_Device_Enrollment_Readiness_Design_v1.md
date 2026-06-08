# Operator Console Device Enrollment Readiness Design v1.0

## 1. Title And Purpose

This document is the production device enrollment and readiness design for the ExitPass Operator Console.

It follows the #236 pilot-readiness sign-off, #237 production readiness gap review, and #238 Operator Console statutory discount UX sequence hardening. The current Operator Console UX and statutory discount backend/API flow are guarded for pilot validation, but production device trust remains a rollout blocker.

The goal is to define the device enrollment, trust, readiness, and revocation model before implementation. This is a docs-only design slice.

## 2. Scope

In scope:

- Operator Console device enrollment.
- Device identity and device binding.
- Binding devices to site, site group, operator model, and shift context.
- Device trust state and readiness.
- Activation, suspension, revocation, lost-device handling, and retirement.
- Operator access evaluation integration.
- Browser/PWA device binding considerations.
- mTLS and client certificate options.
- Local/dev fallback boundary.
- Audit trail and evidence requirements.
- Operational support and recovery.

Out of scope:

- Payment provider routing.
- AUB selection, configuration, routing, or invocation.
- WebPay.
- Coupon validation.
- Reconciliation.
- HikCentral or gate implementation.
- Raw evidence storage.
- OCR.
- Automated ID validation.
- Final implementation code.

## 3. Source Artifacts Reviewed

Found in this repository:

- `docs/operator-console/OperatorConsole_Production_Readiness_Gap_Review_v1.md`
- `docs/operator-console/OperatorConsole_Statutory_Discount_Pilot_Readiness_Signoff_v1.md`
- `docs/operator-console/OperatorConsole_Statutory_Discount_Validation_Runbook_v1.md`
- `docs/operator-console/OperatorConsole_Statutory_Discount_Pilot_Triage_Guide.md`
- `docs/operator-console/operator-console-schema-extension-design.md`
- `docs/operator-console/operator-console-db-patch-validation.md`
- `docs/operator-console/statutory-validation-and-access-contract.md`
- `infra/db/patches/ExitPass_OperatorConsoleSchema_v1.2.sql`
- `infra/db/fixtures/operator-console-access-evaluation/Seed-OperatorConsoleAccessEvaluationManualFixtures.sql`
- Operator Console UI source under `src/Services/OperatorConsoleUi/src`
- Operator Console Central PMS API, application, infrastructure, contracts, and tests under `src/Services/CentralPms`

Not found as standalone documents in this repository:

- ExitPass Operator Console BRD v1.0.
- ExitPass BRD v1.2.
- ExitPass API Contract Pack v1.2.
- ExitPass Engineering Pack v1.2.

This design uses the repo-available artifacts above and does not invent missing BRD requirements.

## 4. Current State Summary

Current implementation and validation state:

- The Operator Console UI uses local/dev fallback operator context values through Vite environment variables and deterministic default GUIDs.
- The deterministic sandbox fixture values include operator user, operator device binding, operator shift, site, and site group IDs for validation.
- Production-grade operator device enrollment is not yet operationalized.
- #235A found that `operator_console.*` tables were not present in the local baseline used for that validation, so the sandbox fixture used available identity, site, core, and discounts structures with conditional operator-console support.
- The repo now contains `infra/db/patches/ExitPass_OperatorConsoleSchema_v1.2.sql`, locally validated by `operator-console-db-patch-validation.md`, with `operator_console.operator_device_bindings`, assignment history, and access evaluation tables.
- The current `OperatorConsoleAccessEvaluationReadRepository` reads `identity.users` and `sites.sites`, then synthesizes locked-schema device binding, device assignment, and active shift context when request IDs are provided.
- The backend/API flow can evaluate operator context and deny controlled actions, but production identity/device/shift binding needs a formal enrollment and readiness workflow.

## 5. Device Trust Model

| Concept | Design definition |
| --- | --- |
| Device record | A logical Operator Console browser, PWA install, kiosk, or managed workstation allowed to initiate controlled Operator Console workflows. |
| Device binding | The trust relationship between a device record and its trust material, site/site group assignment, lifecycle state, and audit history. |
| Device status | Lifecycle state used by access evaluation: `PENDING_ENROLLMENT`, `ACTIVE`, `SUSPENDED`, `REVOKED`, `LOST`, or `RETIRED`. The existing schema patch uses `PENDING`, `ACTIVE`, `SUSPENDED`, `REVOKED`, `LOST`, `EXPIRED`, and `RETIRED`; implementation should map `PENDING_ENROLLMENT` to `PENDING` unless a later controlled enum change is approved. |
| Trust level | Strength of device proof: `BROWSER_KEY_ONLY`, `MTLS_ONLY`, `BROWSER_KEY_AND_MTLS`, or `UNVERIFIED`. |
| Site/site group assignment | The site and site group where the device is allowed to perform Operator Console workflows. Assignment changes must be reconstructable. |
| Operator assignment | Either a device is operator-bound to one or more approved users, or it is a trusted site-shared device usable by active operators assigned to the same site. |
| Shift linkage | Access is allowed only when the operator has an active shift for the requested site and the device assignment matches that site. |
| Enrollment actor | Site supervisor, operations admin, or authorized IT/admin actor who initiates enrollment. |
| Activation actor | Supervisor, operations admin, or security/admin actor authorized to approve the device after challenge verification. |
| Revocation actor | Supervisor, operations admin, security/admin actor, or automated security process with audit trail. |
| Readiness signal | Last-seen timestamp, trust material validity, site assignment health, shift compatibility, and access evaluation status. |

Device trust is an access precondition. It is not payment authority and does not bypass statutory discount evidence, approval, payable-basis, privacy, or audit controls.

## 6. Proposed Lifecycle

Lifecycle states:

- `PENDING_ENROLLMENT`: Device record or request exists, but it cannot perform controlled actions.
- `ACTIVE`: Device trust material is accepted, site assignment is active, and access evaluation may allow controlled actions.
- `SUSPENDED`: Device is temporarily blocked but may be reactivated after review.
- `REVOKED`: Device is permanently blocked unless a new enrollment is created.
- `LOST`: Device is reported missing or outside operational control; it must deny access and require investigation.
- `RETIRED`: Device was intentionally removed from service.

Allowed transitions:

| Transition | From | To | Actor |
| --- | --- | --- | --- |
| Enroll | none | `PENDING_ENROLLMENT` | Supervisor, operations admin, or IT/admin |
| Approve/activate | `PENDING_ENROLLMENT` | `ACTIVE` | Supervisor, operations admin, or security/admin |
| Suspend | `ACTIVE` | `SUSPENDED` | Supervisor, operations admin, security/admin, or automated policy |
| Reactivate | `SUSPENDED` | `ACTIVE` | Supervisor, operations admin, or security/admin after review |
| Revoke | `PENDING_ENROLLMENT`, `ACTIVE`, `SUSPENDED`, `LOST` | `REVOKED` | Operations admin or security/admin |
| Mark lost | `ACTIVE`, `SUSPENDED` | `LOST` | Supervisor, operations admin, security/admin, or support |
| Retire | `ACTIVE`, `SUSPENDED` | `RETIRED` | Operations admin or IT/admin |

`REVOKED`, `LOST`, and `RETIRED` should not reactivate in place. A new enrollment should be required if the device returns to service.

## 7. Enrollment Workflow

Recommended production enrollment flow:

1. A supervisor, operations admin, or IT/admin initiates a device enrollment request for a target site and site group.
2. The system creates an enrollment request with a short-lived registration code, QR challenge, or browser challenge.
3. The device opens the Operator Console enrollment screen and presents the code/challenge.
4. The browser or managed device generates or presents trust material:
   - browser key-pair public key and thumbprint,
   - mTLS client certificate thumbprint,
   - managed kiosk/device certificate reference,
   - or a service identity reference for managed deployments.
5. The system binds the device to the site and site group.
6. The system either binds the device to specific operators or marks it as a trusted site-shared device, based on site policy.
7. A supervisor/admin reviews device name, site assignment, trust material summary, requested actor, and expiration.
8. The supervisor/admin approves activation.
9. The device becomes eligible for access evaluation only after activation.
10. Every step writes audit evidence with actor, site, device reference, state transition, reason code, and correlation ID.

Enrollment must not store private keys, raw secrets, passwords, or unencrypted certificate material.

## 8. Trust Mechanism Options

| Option | Strengths | Weaknesses | Recommended use |
| --- | --- | --- | --- |
| Browser key-pair binding | Works for browser/PWA; private key remains client-side; practical MVP for Operator Console. | Requires browser storage protection, rotation, and recovery; can be lost during browser reset. | Recommended MVP for browser/PWA Operator Console. |
| mTLS client certificate | Strong device identity at gateway/reverse proxy; centralized revocation possible. | Requires certificate issuance, renewal, installation, and gateway enforcement operations. | Recommended for managed production devices where certificate lifecycle is ready. |
| Managed kiosk/device certificate | Strong fit for IT-managed workstations and kiosks; supports inventory controls. | Requires device management process and operational support. | Recommended for fixed site devices and controlled pilot stations. |
| Local/dev header fallback | Fast for local validation and deterministic tests. | Spoofable if trusted in production; not a device trust mechanism. | Development and sandbox only. Must be disabled or rejected in production trust decisions. |

Recommendation: production should use browser key-pair binding or mTLS/certificate-backed device identity. Local/dev header fallback must remain development-only and must not be treated as production trust.

## 9. Access Evaluation Integration

Device readiness must affect Operator Console access before workflow start and before every controlled action:

- Missing device binding denies controlled actions with a reason such as `DEVICE_BINDING_NOT_FOUND` or future `DEVICE_NOT_REGISTERED`.
- `PENDING_ENROLLMENT`, inactive, suspended, revoked, lost, retired, or unverified devices deny controlled actions.
- A site mismatch between request, device binding, assignment history, and shift denies controlled actions.
- A site group mismatch denies controlled actions.
- A missing, inactive, revoked, ended, or mismatched shift denies controlled actions.
- Operator mismatch denies controlled actions when the site uses operator-bound devices.
- A trusted site-shared device may permit any active operator assigned to that site if policy allows.
- Access evaluation must produce operator-safe reason codes and persist denied/controlled-action evidence where configured.

The current access service already recognizes device trust levels `BROWSER_KEY_ONLY`, `MTLS_ONLY`, and `BROWSER_KEY_AND_MTLS` as trusted. Production implementation should replace locked-schema synthesized context with actual operator-console device, assignment, identity, and shift reads.

## 10. UX Implications

Required Operator Console UX states:

| UX state | Operator-facing behavior |
| --- | --- |
| Device not enrolled | Block statutory discount workflow and show enrollment/support instructions. |
| Device pending approval | Block controlled actions and show that supervisor/admin activation is pending. |
| Device active and ready | Show ready state with site and shift context. |
| Device suspended/revoked/lost | Block controlled actions and show escalation instructions. |
| Site mismatch | Block controlled actions and show expected site versus current site context. |
| Shift missing/inactive | Block controlled actions and show shift support/escalation instructions. |
| Operator not authorized on this device | Block controlled actions and show support path; do not expose unrelated operator details. |
| Degraded/local-dev mode | Show a clear non-production indicator and prevent production trust assumptions. |

Required copy and guardrails:

- Do not allow statutory discount workflow until device context is trusted in production.
- Show the access denial reason in operator-safe language.
- Show support/escalation instructions, such as contacting the site supervisor or operations support.
- Do not imply that device readiness authorizes payment collection, gate opening, coupon validation, OCR, or raw evidence upload.

## 11. Audit And Evidence Model

Log these device and access events:

- Enrollment request created.
- Device challenge issued and completed.
- Device approval/activation.
- Suspension, revocation, lost, and retired state changes.
- Reactivation after suspension.
- Operator access attempts and controlled-action evaluations.
- Denied access reasons.
- Device trust level changes.
- Site or site group reassignment.
- Key or certificate rotation.
- Certificate expiry and renewal.
- Break-glass or manual override if allowed in a later slice.

Required audit context:

- Correlation ID.
- Device binding ID or enrollment request ID.
- Device display name/code.
- Site and site group.
- Actor user or service identity.
- Previous and new state.
- Reason code and bounded note.
- Trust mechanism summary, using thumbprints or references only.
- Timestamp and source IP/device signal where approved by privacy policy.

No raw secrets, private keys, full certificate private material, raw evidence, or unnecessary personal data should be stored.

## 12. Security And Privacy Requirements

Security requirements:

- Never store browser private keys server-side.
- Store only public keys, thumbprints, certificate references, or service identity references.
- Provide a key/certificate rotation path.
- Provide an immediate revocation path for lost or compromised devices.
- Treat local/dev header fallback as untrusted in production.
- Bind trust to site/site group and current lifecycle state.
- Re-evaluate access before every controlled action.
- Device trust does not bypass evidence-required gating, approval gating, payable-basis gating, privacy controls, or audit.

Privacy requirements:

- Device fingerprinting must avoid unnecessary personal data.
- Device display names should be operational labels, not personal device names unless approved.
- Last-seen and readiness telemetry should be limited to operational security needs.
- Operator mismatch messages must not reveal unrelated operator personal data.

## 13. Schema And API Design Considerations

Repository schema/design findings:

- `infra/db/patches/ExitPass_OperatorConsoleSchema_v1.2.sql` contains `operator_console.operator_device_bindings`, `operator_console.operator_device_assignment_history`, and access evaluation tables.
- `docs/operator-console/operator-console-db-patch-validation.md` states that the patch executed successfully in local non-production validation and created the expected operator-console schema objects.
- #235A sign-off notes that `operator_console.*` tables were not present in the local baseline during the sandbox fixture validation.
- `identity.users` currently supports operator user identity and is already read by the access evaluation repository.
- `identity.service_identities` can support managed device or mTLS-backed non-human identities through service identity references, but should not replace browser key binding for normal browser/PWA operators unless explicitly chosen.
- The current read repository synthesizes locked-schema device binding and shift context from request IDs and site/user reads; production should read actual operator-console tables.

Potential future tables/entities if not already present in the target production baseline:

- `operator_console.operator_devices`
- `operator_console.operator_device_bindings`
- `operator_console.operator_device_events`
- `operator_console.operator_device_enrollment_requests`
- `operator_console.operator_device_assignment_history`

Endpoint candidates for a later implementation slice:

- `POST /v1/ops/operator-console/devices/enrollment-requests`
- `POST /v1/ops/operator-console/devices/{deviceId}/activate`
- `POST /v1/ops/operator-console/devices/{deviceId}/suspend`
- `POST /v1/ops/operator-console/devices/{deviceId}/revoke`
- `POST /v1/ops/operator-console/devices/{deviceId}/mark-lost`
- `POST /v1/ops/operator-console/devices/{deviceId}/retire`
- `GET /v1/ops/operator-console/devices/readiness`

This document does not implement tables, migrations, API contracts, or runtime behavior.

## 14. Operational Readiness Checklist

Before production rollout, answer and operationalize:

- Who may enroll devices?
- Who may approve/activate devices?
- How are lost devices reported and revoked?
- How are devices reassigned between sites?
- How are operators instructed to escalate access denial?
- How are device audit events reviewed?
- How are pilot devices separated from production devices?
- How are browser keys or device certificates rotated?
- How is local/dev mode disabled or blocked in production?
- How are certificate expiry warnings surfaced?
- How does support recover a device after browser storage reset?
- How does operations decommission retired devices?

## 15. Gap List

1. **OC-DEVICE-GAP-001: Production enrollment request workflow is not implemented**
   - Description: There is no operational request/approval workflow for registering Operator Console devices.
   - Risk: Unauthorized or unmanaged devices could be treated as trusted if local fallback assumptions leak into production.
   - Recommended owner: Backend/Architecture and Security.
   - Recommended next slice: #241 Operator Console device readiness API contract and backend design.
   - Production blocker classification: Yes.

2. **OC-DEVICE-GAP-002: Current access repository synthesizes device trust**
   - Description: The read repository builds locked-schema trusted device context rather than reading production device binding rows.
   - Risk: Pilot validation can pass without proving real production device enrollment.
   - Recommended owner: Backend/Architecture.
   - Recommended next slice: #241 Operator Console device readiness API contract and backend design.
   - Production blocker classification: Yes.

3. **OC-DEVICE-GAP-003: Device readiness UX states are absent**
   - Description: The UI has guarded statutory discount sequencing, but not device enrollment, pending approval, suspended, revoked, lost, or site mismatch readiness screens.
   - Risk: Operators may not understand why access is blocked or how to escalate.
   - Recommended owner: Operator Console UI and Operations.
   - Recommended next slice: #242 Operator Console device readiness UX states.
   - Production blocker classification: Conditional.

4. **OC-DEVICE-GAP-004: Trust mechanism selection is not operationalized**
   - Description: Browser key binding and mTLS are documented, but issuance, storage, rotation, and revocation operations are not finalized.
   - Risk: Device identity could be weak, unrecoverable, or hard to revoke.
   - Recommended owner: Security, Platform, and Backend/Architecture.
   - Recommended next slice: #241 Operator Console device readiness API contract and backend design.
   - Production blocker classification: Yes.

5. **OC-DEVICE-GAP-005: Device audit and support processes need ownership**
   - Description: Required device state-change and readiness audit events are not tied to an operations process.
   - Risk: Lost-device and access-denial incidents may not be handled consistently.
   - Recommended owner: Operations and Compliance/Privacy.
   - Recommended next slice: #244 Operator Console audit/reporting read model and screens.
   - Production blocker classification: Conditional.

6. **OC-DEVICE-GAP-006: Local/dev fallback boundary needs production hardening**
   - Description: UI and API validation still support local/dev fallback context for sandbox use.
   - Risk: Spoofable context could be mistaken for production trust.
   - Recommended owner: Security and Platform.
   - Recommended next slice: #245 Operator Console deployment/observability readiness.
   - Production blocker classification: Yes.

7. **OC-DEVICE-GAP-007: Operator-bound versus site-shared policy is undecided**
   - Description: The design allows either operator-bound or site-shared devices, but production site policy must choose allowed models.
   - Risk: Access evaluation rules and UX may diverge by site without clear policy.
   - Recommended owner: Product, Operations, and Security.
   - Recommended next slice: #240 Operator Console shift/site validation production workflow.
   - Production blocker classification: Conditional.

## 16. Recommended Implementation Slices

Recommended follow-up slices:

1. **#240 Operator Console shift/site validation production workflow**
   - Finalize how operator shifts, site assignment, takeover, and site mismatch states are operationalized.

2. **#241 Operator Console device readiness API contract and backend design**
   - Define device enrollment/readiness DTOs, access repository changes, trust material validation, and read/write boundaries.

3. **#242 Operator Console device readiness UX states**
   - Add UI states for not enrolled, pending approval, active, suspended, revoked, lost, site mismatch, shift missing, and local/dev mode.

4. **#243 Operator Console supervisor review and override workflow**
   - Define supervisor controls for device activation, access exceptions, and workflow override where policy allows.

5. **#244 Operator Console audit/reporting read model and screens**
   - Provide device event, access denial, readiness, and controlled-action reporting.

6. **#245 Operator Console deployment/observability readiness**
   - Harden production config, local fallback disablement, monitoring, alerts, and support runbooks.

Recommended immediate next slice: **#240 Operator Console shift/site validation production workflow**.

Reason: device readiness and shift/site readiness are tightly coupled in access evaluation. The current device design can move to API contract work, but production rollout still needs the operator/shift/site policy finalized so device enrollment rules can choose between operator-bound and site-shared models.

If backend owners prefer to remove the locked-schema synthesis first, #241 can move ahead of #240. That order is defensible because real device readiness APIs would provide the concrete contract that shift/site UX can consume.

## 17. Go/No-Go Position

- GO for continued controlled sandbox and pilot validation.
- CONDITIONAL GO for a limited operational pilot only if devices and operators are manually controlled, supervised, and explicitly approved for the pilot window.
- NO-GO for full production rollout until device enrollment/trust and shift/site validation are implemented or formally accepted as operational controls.

## 18. Boundary Confirmations

This design made no runtime or infrastructure changes:

- No backend code changes.
- No frontend code changes.
- No database, DDL, migration, or seed changes.
- No Docker or CI/CD changes.
- No WebPay changes.
- No payment/provider routing changes.
- No AUB changes.
- No coupon, reconciliation, HikCentral, or gate changes.
- No sensitive credentials, production IDs, private keys, raw evidence, or personal data added.
- No SQL was run for this design.
