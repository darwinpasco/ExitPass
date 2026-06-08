# Operator Console Production Readiness Gap Review v1.0

## 1. Title And Purpose

This document is a production UX/ops readiness gap review for the ExitPass Operator Console.

It follows the completed Operator Console statutory discount pilot-readiness sign-off in `docs/operator-console/OperatorConsole_Statutory_Discount_Pilot_Readiness_Signoff_v1.md`.

The goal is to identify remaining product, UX, operational, compliance, deployment, and support gaps before broader production rollout. The statutory discount backend/API flow is pilot-ready; this review does not conclude that the full Operator Console product is production-ready.

## 2. Scope Of Review

In scope:

- Operator Console product readiness.
- Statutory discount validation workflow.
- Operator UX.
- Supervisor UX.
- Device trust and enrollment.
- Shift and site validation.
- Audit and reporting.
- Operational runbooks.
- Deployment and support readiness.
- Privacy and compliance controls.
- Production data and policy readiness.

Out of scope:

- WebPay payment UI.
- Payment provider routing.
- AUB selection, configuration, routing, or invocation.
- HikCentral implementation work.
- Gate control implementation.
- Coupon validation.
- Reconciliation implementation.
- Raw evidence storage.
- OCR.
- Automated ID validation.

## 3. Source Artifacts Reviewed

Found in this repository:

- `docs/operator-console/OperatorConsole_Statutory_Discount_Pilot_Readiness_Signoff_v1.md`
- `docs/operator-console/OperatorConsole_Statutory_Discount_Validation_Runbook_v1.md`
- `docs/operator-console/OperatorConsole_Statutory_Discount_Pilot_Feedback_Log_Template.md`
- `docs/operator-console/OperatorConsole_Statutory_Discount_Pilot_Triage_Guide.md`
- `scripts/operator-console/README.md`
- `scripts/operator-console/Seed-StatutoryDiscountPilotFixture.sql`
- `scripts/operator-console/Verify-StatutoryDiscountPilotFixture.sql`
- `docs/operator-console/operator-console-schema-extension-design.md`
- `docs/operator-console/statutory-validation-and-access-contract.md`
- `docs/operator-console/statutory-discount-payable-basis-application-design.md`
- `docs/operator-console/statutory-discount-jurisdiction-policy-resolution-design.md`
- `docs/operator-console/statutory-discount-applied-tariff-snapshot-lifecycle-design.md`
- `src/Services/OperatorConsoleUi/src/App.tsx`
- `src/Services/OperatorConsoleUi/src/apiClient.ts`
- `src/Services/OperatorConsoleUi/src/types.ts`
- Operator Console API, service, repository, and test files under `src/Services/CentralPms`.
- Bruno collections under `bruno/operator-console-*`.

Not found as standalone documents in this repository:

- ExitPass Operator Console BRD v1.0.
- ExitPass BRD v1.2.
- ExitPass API Contract Pack v1.2.
- ExitPass Engineering Pack v1.2.

This review therefore uses the implementation artifacts and repository docs listed above. It does not invent missing BRD requirements.

## 4. Current Completed Capabilities

The completed statutory discount track proves the following backend/API capabilities:

- Session lookup backend/API path.
- Policy resolution for statutory discount validation.
- Statutory discount draft creation.
- Evidence-required gating.
- Metadata-only evidence capture.
- Approval decision.
- Apply-payable-basis after approval.
- Final read model verification.
- Negative controls:
  - Approval before evidence blocked with `EVIDENCE_REQUIRED_NOT_CAPTURED`.
  - Wrong evidence type rejected with `INVALID_OPERATOR_CONSOLE_STATUTORY_DISCOUNT_EVIDENCE_REQUEST`.
  - Apply before approval blocked with `STATUTORY_DISCOUNT_NOT_APPROVED`.
- Boundary mutation checks confirming no payment, provider, gate, coupon, or reconciliation mutation.
- Sandbox fixture support through `scripts/operator-console`.
- Manual validation runbook.
- Pilot feedback log template.
- Pilot triage guide.
- Pilot readiness sign-off package.

The current Operator Console UI includes a module shell, statutory discount queue, draft detail view, policy context display, evidence metadata list/capture form, and approval/rejection controls. It is not yet a full production operator workflow.

## 5. Production Readiness Assessment Matrix

| Area | BRD / operational expectation | Current implementation/readiness | Gap | Risk level | Recommended next slice | Production blocker? |
| --- | --- | --- | --- | --- | --- | --- |
| Operator login/authentication | Operators authenticate with production identity, not local fixture IDs. | UI uses local fallback operator context from Vite env/default GUIDs; production login flow is not evident in UI. | Production authentication handoff and session context are not hardened. | High | #238 Operator Console UX production flow review and screen-state hardening | Yes |
| RBAC/operator identity | Controlled actions require active user, role, device, site, and shift checks. | Backend access evaluation is implemented and tested; production enrollment path remains open. | Production role assignment and identity mapping process is not operationalized. | High | #240 Operator Console shift/site validation production workflow | Conditional |
| Device enrollment/trust | Registered browser/device trust with revocation and audit. | Design exists; fixture support is conditional; UI has no enrollment/admin surface. | No production-ready device enrollment, revocation, or lost-device workflow. | High | #239 Operator Console device enrollment/readiness design | Yes |
| Shift validation | HR/timekeeping shift mapping authorizes controlled actions. | Access model and proposal exist; local fixture can emulate context. | Production HR mapping/import, revocation, takeover, and exception process are not complete. | High | #240 Operator Console shift/site validation production workflow | Yes |
| Site assignment | Operator/device/shift must match the site and site group. | Backend checks exist in API tests; UI sends context but does not expose assignment health. | Operators need visible site/shift readiness and mismatch handling. | Medium | #240 Operator Console shift/site validation production workflow | Conditional |
| Session lookup UX | Operator can resolve active sessions safely by supported identifiers. | Backend/API path is proven; UI queue/detail shell does not expose a production session lookup/start workflow. | Missing guided session lookup UX and disambiguation states. | High | #238 Operator Console UX production flow review and screen-state hardening | Yes |
| Statutory discount UX | Operator can move through lookup, policy, draft, evidence, decision, apply, and final verification safely. | UI covers queue/detail, evidence, and decision; runbook covers full manual API path. | Missing end-to-end guided screen flow and apply-payable-basis action/status handling. | High | #238 Operator Console UX production flow review and screen-state hardening | Yes |
| Metadata-only evidence capture UX | UI must prefer `OPERATOR_CONFIRMED` and avoid raw evidence by default. | UI supports `OPERATOR_CONFIRMED`, `MANUAL_REFERENCE`, and `UPLOAD` metadata options. | Production UX needs policy-driven method availability and clearer no-raw-evidence guardrails. | High | #238 Operator Console UX production flow review and screen-state hardening | Conditional |
| Manual reference masking UX | Manual reference must be masked and operator-safe. | Backend validation passed; UI permits manual reference entry but masking preview/confirmation is not prominent. | Need explicit masking confirmation, warnings, and response display rules. | Medium | #244 Operator Console privacy/compliance evidence-handling review | Conditional |
| Approval/rejection workflow | Approval blocked until evidence; rejection requires reason. | Backend negative controls passed; UI blocks approval when loaded detail shows evidence unsatisfied. | UI must harden stale-state, refresh, idempotency, conflict, and terminal-state handling. | Medium | #238 Operator Console UX production flow review and screen-state hardening | Conditional |
| Supervisor review/override | Supervisor review/override when policy requires escalation. | Design states supervisor review is later scope. | No supervisor review, override, escalation queue, or approval audit UX. | High | #241 Operator Console supervisor review and override workflow | Conditional |
| Audit logs/read model | Controlled actions and evidence access are auditable. | Backend access/evidence/read models exist; UI includes an audit activity placeholder. | Production audit timeline and evidence access audit screens are incomplete. | Medium | #242 Operator Console audit/reporting read model and screens | Conditional |
| Reporting screens | Operations, QA, and compliance can review outcomes. | No production reporting screens evident in Operator Console UI. | Missing pilot/production dashboards, exports, and controlled report access. | Medium | #242 Operator Console audit/reporting read model and screens | Conditional |
| Pilot feedback loop | Feedback is logged, triaged, and closed. | Feedback template and triage guide exist; sign-off found no accepted defects. | Need operational owner, cadence, storage location, and closeout procedure for live pilot. | Medium | #245 Operator Console deployment/observability readiness | Conditional |
| Sandbox fixture/tooling | Sandbox data can be seeded and verified repeatably. | #235A scripts and README are present. | Tooling is sandbox-only and must not become production seed or operational dependency. | Low | #245 Operator Console deployment/observability readiness | No |
| Production policy registry readiness | Official policies configured and reviewed before use. | Policy registry/schema design and validation docs exist; sandbox policy was deterministic. | Production policies require official ordinance review, configuration, and sign-off. | High | #243 Operator Console production policy registry readiness | Yes |
| Privacy/compliance review | No raw ID, no raw evidence bytes, metadata-only default, audited access. | Sandbox validation passed metadata-only controls. | Production site policy, retention, masking, evidence access, and compliance sign-off remain required. | High | #244 Operator Console privacy/compliance evidence-handling review | Yes |
| Deployment/environment readiness | Production Operator Console deployment is hardened and supportable. | Local/sandbox validation used `http://localhost:5080`; no production deployment evidence found in reviewed docs. | Need env config, auth, secrets, monitoring, runbook, rollback, and support ownership. | High | #245 Operator Console deployment/observability readiness | Yes |
| Observability/logging/monitoring | Operators and support can trace failures by correlation ID. | Correlation IDs are used in API flow and runbooks. | Need production dashboards/alerts for access denial, evidence capture, apply failures, and boundary mutation alerts. | Medium | #245 Operator Console deployment/observability readiness | Conditional |
| Operational support/runbook | Production support has SOPs for normal and exception paths. | Validation runbook exists for controlled sandbox execution. | Need production SOPs for operator support, policy questions, access denial, device loss, shift takeover, and incident escalation. | High | #245 Operator Console deployment/observability readiness | Conditional |
| User training/SOP | Operators and supervisors can execute safely without backend observers. | Runbook and feedback artifacts exist; no committed training package found. | Need role-specific training, quick reference, and dry-run evidence. | Medium | #245 Operator Console deployment/observability readiness | Conditional |
| Regression/E2E test coverage | Backend and UI critical flows have regression coverage. | Backend unit/integration/E2E tests are present; UI tests exist for current shell. | Need production UX flow tests for sequencing, stale state, negative controls, and evidence method policy. | Medium | #238 Operator Console UX production flow review and screen-state hardening | Conditional |
| Security hardening | No local fallback identity in production; controlled headers/claims; device trust enforced. | UI has local fallback IDs; backend supports access evaluation. | Production claims, header trust boundary, device trust, CSRF/CORS, and secret handling need final review. | High | #245 Operator Console deployment/observability readiness | Yes |

## 6. Explicit Gap List

1. **OC-GAP-001: Production authentication and operator context are not complete**
   - Description: The UI currently uses environment/default operator context fallbacks and shows a placeholder operator status.
   - Why it matters: Production operators must authenticate through approved identity controls, and controlled actions must not depend on local fixture values.
   - Suggested owner: Security/Identity and Operator Console UI.
   - Suggested next slice: #238 Operator Console UX production flow review and screen-state hardening.
   - Severity/risk: High.
   - Blocker classification: Production blocker.

2. **OC-GAP-002: Device enrollment and revocation workflow is not production-ready**
   - Description: Device trust design exists, but no production enrollment, lost-device, revocation, or admin UX is evident.
   - Why it matters: Device trust is part of the access boundary for controlled actions.
   - Suggested owner: Backend/Architecture and Security.
   - Suggested next slice: #239 Operator Console device enrollment/readiness design.
   - Severity/risk: High.
   - Blocker classification: Production blocker.

3. **OC-GAP-003: Shift/site validation is not operationalized for production**
   - Description: HR/timekeeping mapping, imported shift state, shift revocation, takeover, and site assignment are still design/proposal or fixture-backed concerns.
   - Why it matters: Operators must be authorized for the specific site and active shift at action time.
   - Suggested owner: Backend/Architecture and Operations.
   - Suggested next slice: #240 Operator Console shift/site validation production workflow.
   - Severity/risk: High.
   - Blocker classification: Production blocker.

4. **OC-GAP-004: Session lookup and workflow start UX are missing from the production flow**
   - Description: Backend/API session lookup is proven, but the UI reviewed centers on queue/detail and does not provide a guided session lookup/start workflow.
   - Why it matters: Operators need a safe first screen for ticket/session resolution, ineligible sessions, and ambiguity.
   - Suggested owner: Operator Console UI.
   - Suggested next slice: #238 Operator Console UX production flow review and screen-state hardening.
   - Severity/risk: High.
   - Blocker classification: Production blocker.

5. **OC-GAP-005: Apply-payable-basis UX is not complete**
   - Description: Backend apply-payable-basis passed validation, but the UI does not expose the full apply action and post-apply verification path.
   - Why it matters: Operators need a clear, safe handoff from approved validation to applied payable basis and final read model state.
   - Suggested owner: Operator Console UI and Central PMS backend.
   - Suggested next slice: #238 Operator Console UX production flow review and screen-state hardening.
   - Severity/risk: High.
   - Blocker classification: Production blocker for full rollout.

6. **OC-GAP-006: Evidence capture UX allows modes that need policy gating**
   - Description: The UI exposes `OPERATOR_CONFIRMED`, `MANUAL_REFERENCE`, and `UPLOAD` metadata options.
   - Why it matters: Production pilots need policy-driven controls so operators cannot select unsupported evidence modes or enter raw sensitive data.
   - Suggested owner: Operator Console UI and Compliance/Privacy.
   - Suggested next slice: #244 Operator Console privacy/compliance evidence-handling review.
   - Severity/risk: High.
   - Blocker classification: Conditional production blocker.

7. **OC-GAP-007: Manual reference masking needs stronger UX controls**
   - Description: Backend masking behavior was validated, but the UI needs explicit operator guidance and masked response display.
   - Why it matters: Manual reference entry is sensitive-adjacent and can create privacy exposure if operators enter full IDs.
   - Suggested owner: Operator Console UI and Compliance/Privacy.
   - Suggested next slice: #244 Operator Console privacy/compliance evidence-handling review.
   - Severity/risk: Medium.
   - Blocker classification: Conditional production blocker.

8. **OC-GAP-008: Supervisor review and override are later scope**
   - Description: Existing design states supervisor review/override are later scope.
   - Why it matters: Some production policies and exception paths may require supervisor approval before applying discounts.
   - Suggested owner: Product, Operations, and Backend/Architecture.
   - Suggested next slice: #241 Operator Console supervisor review and override workflow.
   - Severity/risk: High.
   - Blocker classification: Conditional production blocker.

9. **OC-GAP-009: Audit activity UI is only a placeholder**
   - Description: The UI includes an audit activity placeholder, while production requires actionable audit/read-model visibility.
   - Why it matters: Support, QA, supervisors, and compliance need traceable action history and evidence access records.
   - Suggested owner: Backend/Architecture and Operator Console UI.
   - Suggested next slice: #242 Operator Console audit/reporting read model and screens.
   - Severity/risk: Medium.
   - Blocker classification: Conditional production blocker.

10. **OC-GAP-010: Reporting screens are absent**
    - Description: No production reporting, export, or dashboard screens are evident for the Operator Console.
    - Why it matters: Pilot operations need exception tracking, approval/rejection counts, evidence capture outcomes, and access denial trends.
    - Suggested owner: Product, Operations, and Operator Console UI.
    - Suggested next slice: #242 Operator Console audit/reporting read model and screens.
    - Severity/risk: Medium.
    - Blocker classification: Conditional production blocker.

11. **OC-GAP-011: Production policy registry readiness is not complete**
    - Description: Sandbox validation used deterministic policy data; production requires reviewed official ordinance configuration.
    - Why it matters: Statutory discounts must not be auto-applied from unreviewed or unofficial policy records.
    - Suggested owner: Product, Compliance, and Backend/Architecture.
    - Suggested next slice: #243 Operator Console production policy registry readiness.
    - Severity/risk: High.
    - Blocker classification: Production blocker.

12. **OC-GAP-012: Production privacy/compliance evidence review remains open**
    - Description: Metadata-only validation passed, but production retention, masking, evidence access, and operator instructions need formal approval.
    - Why it matters: Statutory ID workflows can expose sensitive personal data if controls drift from metadata-only behavior.
    - Suggested owner: Compliance/Privacy.
    - Suggested next slice: #244 Operator Console privacy/compliance evidence-handling review.
    - Severity/risk: High.
    - Blocker classification: Production blocker.

13. **OC-GAP-013: Deployment and environment readiness are not demonstrated**
    - Description: Validation used local/sandbox endpoints; production deployment evidence was not found in reviewed docs.
    - Why it matters: Production rollout needs hardened config, auth, rollback, support contacts, and observability.
    - Suggested owner: Platform/DevOps and Operations.
    - Suggested next slice: #245 Operator Console deployment/observability readiness.
    - Severity/risk: High.
    - Blocker classification: Production blocker.

14. **OC-GAP-014: Monitoring and alerting need production thresholds**
    - Description: Correlation IDs exist, but production dashboards and alerts for Operator Console controls are not documented.
    - Why it matters: Support must detect access denial spikes, evidence capture failures, apply failures, and boundary mutation anomalies.
    - Suggested owner: Platform/Observability and Backend/Architecture.
    - Suggested next slice: #245 Operator Console deployment/observability readiness.
    - Severity/risk: Medium.
    - Blocker classification: Conditional production blocker.

15. **OC-GAP-015: Operator and supervisor SOP/training package is incomplete**
    - Description: Runbook and feedback artifacts exist, but production role-based SOP/training materials were not found.
    - Why it matters: Operators must execute the workflow safely without backend engineers supervising every step.
    - Suggested owner: Operations and Product.
    - Suggested next slice: #245 Operator Console deployment/observability readiness.
    - Severity/risk: Medium.
    - Blocker classification: Conditional production blocker.

16. **OC-GAP-016: UX regression coverage needs production flow hardening**
    - Description: Backend E2E coverage is strong for the validated API path; UI production sequencing and stale-state coverage need expansion.
    - Why it matters: The UI should prevent sequencing mistakes and keep operators inside safe workflow states.
    - Suggested owner: Operator Console UI and QA.
    - Suggested next slice: #238 Operator Console UX production flow review and screen-state hardening.
    - Severity/risk: Medium.
    - Blocker classification: Conditional production blocker.

17. **OC-GAP-017: Security hardening review remains required**
    - Description: Production claims/header trust boundary, local fallback disabling, CORS, CSRF assumptions, and device trust enforcement need final review.
    - Why it matters: Operator Console controlled actions are sensitive and must not be authorized from spoofable local context.
    - Suggested owner: Security/Architecture.
    - Suggested next slice: #245 Operator Console deployment/observability readiness.
    - Severity/risk: High.
    - Blocker classification: Production blocker.

## 7. Suggested Implementation Roadmap

Recommended follow-up slices:

1. **#238 Operator Console UX production flow review and screen-state hardening**
   - Harden the operator-facing workflow from session lookup through final verification.
   - Add or specify screen states that prevent sequencing mistakes, stale decision state, unsupported evidence modes, and unclear payable-basis status.

2. **#239 Operator Console device enrollment/readiness design**
   - Finalize browser/device registration, revocation, lost-device handling, device assignment, and operational support workflows.

3. **#240 Operator Console shift/site validation production workflow**
   - Finalize HR identity mapping, shift import/readiness, site assignment checks, revocation, and takeover readiness.

4. **#241 Operator Console supervisor review and override workflow**
   - Add supervisor queue, override conditions, approvals, rejection reasons, escalation audit, and policy-driven enforcement.

5. **#242 Operator Console audit/reporting read model and screens**
   - Build audit timeline, evidence access audit, pilot outcome reporting, access denial reporting, and export controls.

6. **#243 Operator Console production policy registry readiness**
   - Review official ordinance inputs, policy registry configuration, verification status, fallback policy, and production sign-off.

7. **#244 Operator Console privacy/compliance evidence-handling review**
   - Harden metadata-only evidence UX, masking, retention, access audit, and operator instructions.

8. **#245 Operator Console deployment/observability readiness**
   - Finalize production config, auth, CORS, monitoring, alerts, rollback, support runbook, and training/SOP package.

## 8. Go/No-Go Position

- GO for backend/API statutory discount pilot-readiness, already validated through #236.
- CONDITIONAL GO for a controlled pilot using sandbox/pilot process, deterministic or approved pilot fixtures, trained operators, and explicit operational supervision.
- NO-GO for full production rollout until the production gaps listed in this review are resolved or formally accepted by Product, Backend/Architecture, Operations, Security, and Compliance/Privacy.

## 9. Recommended Immediate Next Slice

Recommended next slice: **#238 Operator Console UX production flow review and screen-state hardening**.

Reason: the backend/API path is validated, but operators need a production-grade UX that prevents sequencing mistakes, makes evidence status clear, blocks unsupported evidence and decision paths, exposes session lookup/start workflow safely, and guides the apply-payable-basis/final verification flow without backend observer intervention.

## 10. Boundary Confirmations

This review made no runtime or infrastructure changes:

- No backend code changes.
- No frontend code changes.
- No database, DDL, migration, or seed changes.
- No Docker or CI/CD changes.
- No WebPay changes.
- No payment/provider routing changes.
- No AUB changes.
- No coupon, reconciliation, HikCentral, or gate changes.
- No sensitive credentials, production IDs, raw evidence, or personal data added.
- No SQL was run for this review.
