# ExitPass Continuity System Design Input Pack 03: Reconciliation, Manual Release, and Fiscal Exception

Status: Specialist input pack only

Branch: `docs/v1.3-continuity-system-design`

Assigned scope: Fiscal issuance failure/timeout, pending-exit handling, manual release governance, vendor acknowledgment failure, reconciliation tagging, post-restoration review, audit evidence, and reporting handoff.

This input pack is not the final Continuity System Design. It does not define final database tables, exact status values, database enum values, event payloads, endpoint paths, DTOs, POS Server recovery internals, fiscal counter mechanics, runbook procedures, or UAT scripts.

## 1. Purpose

This pack provides companion technical-design input for continuity reconciliation, fiscal issuance exceptions, payment-finality-but-fiscal-pending handling, manual release governance, vendor acknowledgment failure, gate/exit issue posture, and post-restoration review.

The pack preserves the approved ExitPass v1.3 authority model:

- Central PMS remains authority for payment-linked state, platform payment finality, fiscal reference recording, degraded resolve decisions under approved Continuity policy, and ExitAuthorization.
- Resolved Site POS Server remains fiscal issuance authority for Sales Invoice and fiscal records.
- Vendor PMS/HCP remains raw parking session lifecycle and normal tariff authority in normal mode.
- Vendor PMS Connector and HikCentral Connector report vendor facts, health, freshness, projection context, and acknowledgment outcomes; they do not approve payment finality, fiscal issuance, discount policy, ExitAuthorization, or gate release.
- Operator Console may govern and review exceptions but does not collect payment, issue Sales Invoice, declare payment finality, issue ExitAuthorization, or directly open gates.
- Management Dashboard and Reporting provides visibility only and does not become payment, fiscal, exit, discount, continuity activation, or reconciliation closure authority unless a later approved policy explicitly assigns workflow actions.

## 2. Source Documents Reviewed

- `docs/v1.3/continuity/system-design/ExitPass_Continuity_System_Design_Orchestration_Plan.md`
- `docs/v1.3/continuity/ExitPass_Continuity_BRD_v1.0.md`
- `docs/v1.3/ExitPass_System_Design_v1.3.md`
- `docs/v1.3/ExitPass_BRD_v1.3.md`
- `docs/v1.3/assisted-payment-terminal/ExitPass_Assisted_Payment_Terminal_System_Design_v1.0.md`
- `docs/v1.3/operator-console/ExitPass_Operator_Console_BRD_v1.1.md`
- `docs/v1.3/management-dashboard-reporting/ExitPass_Management_Dashboard_and_Reporting_BRD_v1.0.md`
- `docs/v1.3/pos-invoicing/ExitPass_POS_Invoicing_BRD_v1.0.md`
- `docs/v1.3/vendor-pms-connector/ExitPass_Vendor_PMS_Connector_System_Design_v1.0.md`
- `docs/v1.3/hikcentral-connector/ExitPass_HikCentral_Connector_Profile_v1.0.md`
- `docs/v1.3/ExitPass_v1.3_Open_Questions.md`

No approved-source contradiction was found for this pack's scope. The sources consistently preserve Central PMS payment and ExitAuthorization authority, Site POS Server fiscal authority, Operator Console non-payment governance, Management Dashboard visibility-only boundaries, projection non-authority, and mandatory audit/reconciliation treatment for continuity-origin activity.

## 3. Reconciliation Scope

Continuity reconciliation should compare facts from payment, provider, POS fiscal, Central PMS fiscal reference, vendor acknowledgment, gate outcome, manual release, continuity activation, projection/degraded basis, statutory discount, and settlement records.

The reconciliation posture should cover:

- Continuity activation and deactivation records for affected Site, Site Group, dependency, incident/BCP reference, approval context, active scope, and closure posture.
- Degraded use of projection, including projection freshness at time of use, projection ambiguity, missing data, stale indicators, and approved degraded tariff/payable-basis context.
- Payment attempt, provider outcome, and Central PMS payment finality, including payment uncertainty that remains pending until verified by approved payment workflow and accepted by Central PMS.
- POS Server fiscal issuance success, failure, timeout, pending issuance, and Central PMS fiscal reference recording.
- Payment finality but fiscal pending/failed cases, without reversing payment finality automatically and without authorizing normal exit automatically.
- Vendor payment acknowledgment request and outcome where enabled, including failure, timeout, unavailable, unknown, duplicate, conflict, or backlog indicators.
- Gate/exit outcome and gate/physical release issue context.
- Manual release request, approval, rejection, execution context where applicable, reason, attribution, incident tag, audit tag, reconciliation tag, and post-review.
- Continuity-mode statutory discount validation/evidence context, including whether entitlement, policy basis, evidence, projection freshness, and payable-basis recalculation were safely validated.
- Continuity Terminal activity, including trusted device, cashier, shift, Site/Site Group, terminal mode, backend result display, and governance handoff context.
- Operator Console governance actions and Management Dashboard visibility/export context.
- Settlement and revenue assurance context where applicable.

Projection may support investigation and operational visibility, but projection alone must not close financial, fiscal, settlement, vendor acknowledgment, discount, or exit reconciliation.

## 4. Fiscal Issuance Exception Handling

Fiscal issuance exceptions occur when payment finality exists or is being evaluated but resolved Site POS Server fiscal issuance is failed, timed out, unknown, pending, missing its Central PMS fiscal reference, or otherwise under controlled review.

Required posture:

- Payment finality is not automatically reversed by fiscal issuance failure or timeout.
- Fiscal issuance failure after payment finality does not automatically authorize exit.
- Normal ExitAuthorization remains blocked until fiscal prerequisites succeed unless a separately approved exception/manual-release policy applies.
- The case enters a controlled fiscal exception, retry, escalation, or review workflow.
- Customer/operator messaging must clearly distinguish payment received, fiscal issuance pending/failed/unknown, and exit authorization pending or blocked.
- POS Server remains fiscal authority. Central PMS records fiscal references but does not become fiscal issuer.
- POS Server recovery implementation, fiscal counters, Sales Invoice sequence handling, electronic journal behavior, POSLog details, and BIR/accreditation recovery mechanics remain deferred to POS/Invoicing and POS Server design.

Exception evidence should include payment finality context, resolved Site, POS Server target, fiscal request/result context, failure or timeout context, Central PMS fiscal reference presence or absence, customer/operator messaging state, governance review actions, and reconciliation tags.

## 5. Payment Finality but Fiscal Pending / Failed Workflow

Conceptual workflow input:

1. Payment Orchestrator or an approved payment channel reports a verified provider outcome to Central PMS.
2. Central PMS applies platform controls and records payment finality only when accepted.
3. Central PMS routes fiscal issuance to the resolved Site POS Server.
4. If POS Server issuance succeeds, Central PMS records the fiscal reference and evaluates normal ExitAuthorization eligibility.
5. If fiscal issuance fails, times out, remains unknown, or lacks required fiscal reference, Central PMS blocks normal ExitAuthorization and places the case in controlled fiscal exception review.
6. Assisted Payment Terminal or Continuity Terminal may display backend status but must not infer exit eligibility from provider success, fiscal display, vendor state, or cashier judgment.
7. Operator Console or an approved operations workflow may support review/escalation, reason capture, evidence review, and supervisor approval where policy allows, but does not issue Sales Invoice or ExitAuthorization.
8. Manual release, if applicable, remains separate from normal ExitAuthorization and must be governed as a last-resort exception.

This workflow should preserve two independent facts: Central PMS payment finality may be true while fiscal issuance remains pending/failed, and fiscal failure does not undo payment finality unless a separately approved refund/reversal workflow later occurs.

## 6. Manual Release Governance Workflow

Manual release is a last-resort governed exception, not normal payment finality, not normal fiscal success, and not normal ExitAuthorization.

Manual release should require:

- Approved policy basis before availability.
- Supervisor approval where policy requires.
- Clear request, approval, rejection, and execution context where applicable.
- Reason code or reason category at a conceptual level, without defining final enum values.
- Human attribution for requesting, approving, rejecting, and executing actors.
- Device, terminal, shift, Site, Site Group, session, payment, fiscal, continuity, and incident context where applicable.
- Incident tag, audit tag, reconciliation tag, and post-review requirement.
- Preservation of unresolved payment, fiscal, vendor acknowledgment, gate, and settlement items after release.
- Review for overuse, revenue risk, fraud risk, fiscal exposure, and customer impact.

Manual release must not silently convert stale projection, missing fiscal issuance, unresolved vendor acknowledgment, payment uncertainty, or gate issue into normal authority records. It also must not close reconciliation by itself.

Gate or physical release execution remains outside normal Assisted Payment Terminal and Operator Console payment authority unless a future approved emergency boundary explicitly assigns and controls that responsibility. If such a boundary is later approved, it still must remain incident-tagged, audit-tagged, reconciliation-tagged, attributable, and post-reviewed.

## 7. Gate / Exit Issue Workflow

Gate or exit device issues do not alter payment truth, fiscal truth, vendor truth, or reconciliation requirements.

Conceptual posture:

- Gate/exit infrastructure consumes Central PMS authorization under normal flow.
- Gate failure, failed consumption, device unavailability, local barrier issue, or uncertain gate outcome should be visible, auditable, and reconciled.
- If Central PMS ExitAuthorization exists but gate execution fails, the issue is operational/gate-side and should not mutate payment finality or fiscal issuance truth.
- If Central PMS ExitAuthorization does not exist, the gate issue path must not be used to bypass payment or fiscal prerequisites except under formally approved manual emergency policy.
- Any physical release outside normal authorization consumption must be treated as a manual release or emergency release exception, with supervisor approval where required and full incident/audit/reconciliation tagging.

Gate outcome should be compared against ExitAuthorization state, payment finality, fiscal reference, manual release context, continuity activation context, and post-restoration review findings.

## 8. Vendor Payment Acknowledgment Failure Workflow

Vendor payment acknowledgment is downstream of Central PMS payment finality and required fiscal handling. It is not the source of ExitPass payment finality, fiscal success, or ExitAuthorization.

Conceptual workflow input:

1. Central PMS records platform payment finality after verified provider outcome.
2. Central PMS routes fiscal issuance and records the fiscal reference where required.
3. Central PMS determines, under later Site/vendor policy, whether vendor acknowledgment should be attempted, queued, retried, held, or escalated.
4. Vendor PMS Connector or HikCentral Connector attempts acknowledgment only where the capability is confirmed, enabled, permissioned, and explicitly approved.
5. Connector reports acknowledgment outcome context to Central PMS and reconciliation consumers.
6. Failed, timed out, unavailable, unknown, duplicate, already-paid, or conflicting acknowledgment outcomes remain auditable and reconciliation-tagged.

For HikCentral specifically, `parkingfee/confirm` is mutating and remains disabled unless deployment requirement, safety behavior, idempotency posture, retry policy, reconciliation handling, and ExitPass design approval are complete. Unknown acknowledgment outcome must not be retried blindly without later-approved idempotency and confirmation posture.

Open design items include whether acknowledgment is synchronous, asynchronous, queued/retried, exit-blocking, or Site/vendor-profile dependent.

## 9. Continuity-Origin Record Tagging

Continuity-origin activity must remain identifiable from activation through review and closure. Tagging should support reconstruction of why a case used continuity controls, which authority approved it, which dependency was affected, what facts were available at the time, and which reconciliation items remain unresolved.

Continuity-origin records should carry, at conceptual level:

- Continuity activation/deactivation context.
- Affected Site and Site Group.
- Affected dependency and incident/BCP reference.
- Continuity operating posture, such as degraded-watch, degraded-active, Continuity Terminal active, restoration-in-progress, post-restoration review, and closed/reconciled concepts.
- Projection freshness and degraded basis where projection or degraded tariff/payable basis informed the workflow.
- Payment, fiscal, vendor acknowledgment, gate, manual release, statutory discount, terminal activity, Operator Console governance, dashboard/export, and settlement/revenue assurance correlation where applicable.

These are design-level record-tagging concepts only. Exact database fields, event names, status names, enum values, table relationships, and payload schemas remain deferred.

## 10. Post-Restoration Review Workflow

After restoration or deactivation, continuity-origin activity should move into post-restoration review. Continuity-origin activity remains open until reviewed, reconciled, or formally closed by approved authority.

Post-restoration review should:

- Confirm activation/deactivation scope, authority, incident/BCP reference, affected dependency, and affected Site/Site Group.
- Review all continuity-origin payment attempts, provider outcomes, payment finality records, payment uncertainty cases, and settlement context where applicable.
- Review fiscal issuance successes, failures, timeouts, pending cases, fiscal references, and POS Server reconciliation context.
- Review vendor acknowledgment backlog, failures, unknown outcomes, and retry/escalation context.
- Review projection freshness, degraded tariff/payable-basis use, mapping ambiguity, stale/insufficient projection, and statutory discount handling under continuity.
- Review gate outcomes, failed consumption, physical release, manual release, and emergency release context.
- Review Operator Console governance actions and Management Dashboard access/export context.
- Identify unmatched, unresolved, escalated, or formally accepted exception items.
- Preserve unresolved questions for downstream design rather than inventing final statuses, SLAs, closure labels, or runbook actions.

Post-restoration closure should not be possible solely because operations returned to normal. Closure requires reconciliation disposition and review of continuity-origin financial, fiscal, operational, governance, and audit evidence.

## 11. Reconciliation Data Categories

At minimum, the Continuity System Design should preserve the following reconciliation categories as conceptual inputs:

| Category | Reconciliation purpose |
| --- | --- |
| Continuity activation/deactivation record | Establish approved degraded/BCP scope, timing, approver, and review boundary. |
| Affected Site/Site Group | Attribute operation, fiscal routing, reporting, and reconciliation scope. |
| Affected dependency | Identify Vendor PMS/HCP, connector, network, WebPay/APM, POS Server, payment provider, gate, or other impacted component. |
| Incident/BCP reference | Tie activity to approved business continuity context. |
| Projection freshness at degraded use | Show whether projection was fresh, stale, ambiguous, insufficient, or unavailable at decision time. |
| Degraded tariff/payable-basis basis | Explain approved basis used for payment or investigation under degraded controls. |
| Payment attempt | Correlate channel, Site/Site Group, resolved Site, amount, timing, and workflow context. |
| Provider outcome | Compare external payment evidence against Central PMS finality. |
| Central PMS payment finality | Identify platform payment truth accepted by Central PMS. |
| POS Server fiscal issuance | Compare fiscal success/failure/pending outcome from fiscal authority. |
| Central PMS fiscal reference | Link POS Server-issued fiscal document identity/status to platform control records. |
| Fiscal issuance failure/timeout | Preserve exception context, retry/review posture, and customer/operator messaging. |
| Vendor payment acknowledgment | Track downstream vendor notification outcome and backlog. |
| Gate outcome | Compare Central PMS authorization, gate consumption, failure, or physical release context. |
| Manual release request/approval/rejection/execution context | Preserve governance and non-normal release evidence. |
| Statutory discount validation/evidence context | Confirm approved validation, evidence, payable-basis effect, and continuity restrictions. |
| Continuity terminal activity | Correlate terminal mode, trusted device, cashier, shift, Site/Site Group, and backend result display. |
| Operator Console governance actions | Track review, approval, rejection, escalation, evidence access, and audit actions. |
| Management Dashboard visibility/export context | Track source/freshness labels, report/export access, and visibility-only handling. |
| Settlement/revenue assurance context | Compare payment, fiscal, vendor, manual release, and settlement facts where applicable. |

## 12. Reconciliation Status Concepts

The final design may need conceptual reconciliation states, but this pack does not define exact status values or database enum values.

Concepts to preserve:

- Open continuity-origin item pending review.
- Matched records across payment, provider, fiscal, vendor acknowledgment, gate, and settlement sources.
- Unmatched or inconsistent records requiring investigation.
- Fiscal exception under review.
- Vendor acknowledgment unresolved or backlogged.
- Manual release pending approval, rejected, approved, executed, or post-reviewed at a conceptual level only.
- Gate outcome unresolved or exception-linked.
- Payment uncertainty pending provider/payment workflow verification and Central PMS acceptance.
- Reviewed and escalated items.
- Closed/reconciled only after approved reconciliation and post-restoration review.

Design guidance:

- Projection can help explain an item but cannot move the item to financially or fiscally reconciled by itself.
- Manual release can explain physical exit but cannot close payment, fiscal, vendor acknowledgment, settlement, or reconciliation gaps by itself.
- Fiscal issuance success and Central PMS fiscal reference recording remain required for normal ExitAuthorization.
- Continuity-origin records must remain identifiable through closure.

## 13. Audit and Non-Repudiation Requirements

Audit posture should support reconstruction across authority boundaries without transferring authority to logs, dashboards, or events.

Audit evidence should correlate:

- Actor identity, role, supervisor approval where required, device, terminal, shift/session, Site, Site Group, and time context.
- Continuity activation/deactivation, incident/BCP reference, affected dependency, and active scope.
- Session lookup source, vendor reference, projection freshness, degraded basis, mapping ambiguity, and restriction warnings.
- Payment attempt, provider outcome, Central PMS payment finality, payment uncertainty, and settlement context where applicable.
- POS Server fiscal issuance request/result, failure/timeout, Sales Invoice reference where available, Central PMS fiscal reference, and fiscal exception review.
- Vendor acknowledgment request/outcome/backlog where enabled.
- ExitAuthorization state, gate outcome, manual release request/approval/rejection/execution context, and physical release exception context.
- Statutory discount validation/evidence actions and continuity-mode restrictions.
- Operator Console governance actions, evidence access, supervisor review, and fiscal/manual-release review.
- Management Dashboard report access/export activity, source labels, filters, generation time, and freshness labels.

High-risk actions such as fiscal exception escalation, supervisor override, manual release approval, continuity activation/deactivation review, evidence access, and report export should be attributable, permissioned, tamper-evident at the audit level, and privacy-aware.

## 14. Operator Console Governance Boundary

Operator Console may support:

- Read-only payment, fiscal issuance, ExitAuthorization, connector health, projection freshness, continuity state, and exception context display.
- Continuity activation approval where policy requires and deactivation/post-restoration review.
- Incident/BCP reference entry or review.
- Fiscal issuance exception review and escalation.
- Manual release governance where policy allows.
- Supervisor approval or rejection capture where policy requires.
- Evidence review, reason capture, audit review, and reconciliation support.

Operator Console must not:

- Collect payment.
- Declare payment finality.
- Issue Sales Invoice or mutate fiscal records.
- Issue or consume ExitAuthorization.
- Directly open gates.
- Treat projection as payment, tariff, fiscal, discount, or exit approval.
- Convert manual release into normal payment finality or normal ExitAuthorization.

Gate or physical release execution remains outside Operator Console unless a later approved System Design explicitly changes the boundary. Even if changed later, execution must remain governed, attributable, incident-tagged, audit-tagged, reconciliation-tagged, and post-reviewed.

## 15. Management Dashboard Visibility Boundary

Management Dashboard and Reporting may show continuity and exception visibility, including:

- Continuity state, degraded-watch/degraded-active indicators, Continuity Terminal activation, and incident backlog.
- Connector health, Vendor PMS/HCP availability, projection freshness, stale warnings, poll latency, and vendor acknowledgment backlog where authorized.
- Fiscal exception backlog, fiscal issuance pending/failed/succeeded summaries, and fiscal reference visibility where authorized.
- Manual release counts, reason categories, review state concepts, and reconciliation tags.
- Payment uncertainty, payment attempts, provider outcomes, payment finality, settlement comparison, reconciliation run/item concepts, and revenue assurance context.
- Post-restoration review status and continuity-origin reconciliation backlog.
- Report/export access and source/freshness labels.

Management Dashboard must not:

- Activate continuity.
- Approve manual release.
- Close reconciliation unless later approved policy explicitly assigns workflow actions.
- Declare payment finality.
- Issue ExitAuthorization.
- Open gates.
- Issue Sales Invoice or mutate fiscal records.
- Approve statutory discounts, apply coupons, or alter payable basis.
- Treat projection as financial truth, fiscal truth, payment finality, or exit authority.

Financial and revenue dashboards must use canonical payment, provider, fiscal, fiscal reference, settlement, and reconciliation records. Projection may appear only as separately labeled operational context.

## 16. Failure Modes and Fail-Closed Rules

The Continuity System Design should carry forward these fail-closed rules:

- Unknown payment outcome: no Central PMS payment finality until verified by approved payment workflow and accepted by Central PMS.
- Fiscal issuance failure, timeout, unknown result, or missing fiscal reference: block normal ExitAuthorization and route to controlled exception workflow.
- Stale, ambiguous, unavailable, or insufficient projection: fail closed or route to approved supervisor/manual review; do not use projection as financial, fiscal, discount, or exit truth.
- Missing or ambiguous Site/Site Group/vendor mapping: fail closed or route to approved review because mapping affects POS Server routing, reporting attribution, and reconciliation.
- Vendor acknowledgment failure, timeout, unknown result, or backlog: keep auditable and reconciliation-tagged; do not treat vendor paid state as ExitPass payment finality or fiscal success.
- Gate/device failure or uncertain gate outcome: preserve payment/fiscal truth and route to governed operational or manual-release path where policy allows.
- Manual release governance not approved: do not physically release under manual-release posture.
- Continuity activation missing, expired, out of scope, or not approved: continuity-only workflows remain disabled.
- Continuity-mode statutory discount cannot safely validate entitlement, policy basis, evidence requirement, projection freshness, or payable-basis recalculation: fail closed or route to supervisor/manual review.
- Terminal, Operator Console, dashboard, connector, or report trust is insufficient: do not infer payment finality, fiscal issuance, discount approval, ExitAuthorization, reconciliation closure, or gate release.

Fail-closed behavior should prevent silent fallback, unmanaged offline fiscal issuance, unmanaged degraded tariff basis, unmanaged manual release, and closure of continuity-origin items without review.

## 17. Open Reconciliation / Manual Release Questions

The following open questions should be carried forward for Lead synthesis and downstream packs:

- What exact degraded projection freshness threshold applies before projection can support degraded resolve?
- What exact health states, freshness labels, stale warning rules, and alert thresholds apply to connectors and projection?
- Who has exact BCP/Continuity Terminal activation authority, and what approval workflow applies by Site/Site Group?
- What exact fiscal issuance exception release policy applies, if any?
- What exact manual release policy and emergency override boundary apply?
- Which manual release cases require supervisor approval, and are any approval levels Site-specific?
- What exact vendor payment acknowledgment behavior applies by Site/vendor profile: synchronous, asynchronous, queued, retried, exit-blocking, or informational?
- How should unknown vendor acknowledgment outcomes be confirmed safely without duplicate vendor-side payment effects?
- For HikCentral, is `parkingfee/confirm` required before exit, and does it mark paid, allow exit, both, or another vendor state?
- For HikCentral, what exact `cardNum` meaning and ticket/card/plate identifier policy applies?
- What exact post-restoration reconciliation SLA, closure authority, and closure labels apply?
- What exact reconciliation item concepts belong in the final Continuity System Design versus Database/API/Engineering Pack?
- What exact dashboard/reporting source tables, aggregation rules, export formats, and refresh intervals apply?
- What exact POS Server offline fiscal issuance policy, if any, is approved by BIR/accounting/POS Server design?
- What exact fiscal recovery, tamper-evident anchoring, and fiscal continuity mechanism applies without silent rollback?
- What exact gate/manual emergency release execution boundary is approved, if any, outside normal Central PMS authorization consumption?

## 18. Summary for Lead

Lead synthesis should preserve these points in the Continuity System Design:

- Fiscal issuance failure after payment finality does not automatically authorize exit.
- Payment finality is not automatically reversed by fiscal failure.
- Normal ExitAuthorization remains blocked until fiscal prerequisites succeed unless approved exception/manual-release policy applies.
- Manual release is last-resort governed exception, not normal ExitAuthorization, and must be supervisor-approved where required, incident-tagged, audit-tagged, reconciliation-tagged, reason-coded, attributable, and post-reviewed.
- Operator Console governs and reviews but does not collect payment, issue Sales Invoice, issue ExitAuthorization, or directly open gates.
- Assisted Payment Terminal and Continuity Terminal display backend status and hand off governance; they do not declare finality, issue fiscal documents independently, authorize exit, or execute gate release.
- Gate/physical release execution remains outside normal APT/Operator Console payment authority unless a future approved emergency boundary exists.
- Vendor acknowledgment failure is downstream of Central PMS payment finality and fiscal prerequisites; it must remain auditable and reconciliation-tagged.
- Payment uncertainty remains pending until verified by approved payment workflow and accepted by Central PMS.
- Projection supports operational visibility and investigation but cannot close financial, fiscal, settlement, vendor acknowledgment, or exit reconciliation by itself.
- Reconciliation must compare payment, provider, POS fiscal, Central PMS fiscal reference, vendor acknowledgment, gate outcome, manual release, continuity activation, projection/degraded basis, statutory discount, and settlement records.
- Continuity-origin activity remains open until reviewed, reconciled, or formally closed under approved authority.
- Exact statuses, API contracts, DTOs, database tables, event payloads, POS Server internals, fiscal recovery mechanics, runbook steps, UAT scripts, retry policies, thresholds, and closure labels remain deferred.
