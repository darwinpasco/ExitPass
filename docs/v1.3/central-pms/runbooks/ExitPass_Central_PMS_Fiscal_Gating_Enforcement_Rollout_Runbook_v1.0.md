# ExitPass Central PMS Fiscal Gating Enforcement Rollout Runbook v1.0

## 1. Document Control

| Field | Value |
| --- | --- |
| Document | Central PMS Fiscal Gating Enforcement Rollout Runbook |
| Version | v1.0 |
| Scope | Operational rollout and go/no-go controls for future fiscal-before-ExitAuthorization enforcement |
| Status | Documentation/runbook only |
| Repository | `D:\SourceCodes\ExitPass` |
| Runtime reference | `D:\SourceCodes\ExitPass-PoSServer` on `dev` |

This runbook is an operational control document. It does not implement fiscal gating enforcement, modify source code, write SQL, create migrations, call POS Server, or change production ExitAuthorization behavior.

## 2. Purpose and Scope

This runbook defines the readiness, rollout, monitoring, rollback, and manual exception procedure required before any future production branch enables fiscal-before-ExitAuthorization blocking.

The runbook covers:

- current Central PMS implementation baseline
- authority boundaries
- feature flag posture
- phased rollout
- Site and Site POS Server readiness
- POS Server readiness
- Central PMS fiscal reference state readiness
- shadow and future decision evidence review
- UAT and preflight evidence
- operational go/no-go criteria
- rollback and incident handling
- manual exception release controls
- communications and approval gates

This runbook does not enable any rollout phase by itself.

## 3. Authority Boundaries

The rollout must preserve these authority boundaries:

- Central PMS owns payment finality.
- Central PMS owns fiscal reference recording.
- Central PMS owns normal ExitAuthorization.
- POS Server owns fiscal issuance and numbering only.
- POS Server response is fiscal issuance evidence only.
- POS Server does not approve payment, exit, gate opening, entitlement, manual release, or continuity.
- Operator Console remains governance/review only.
- Management Dashboard remains visibility/reporting only.
- Manual release is not normal ExitAuthorization.

Fiscal success is not a gate command. ExitAuthorization remains a Central PMS decision after all configured Central PMS controls pass.

## 4. Current Implementation Baseline

Current Central PMS implementation has:

- fiscal reference persistence state
- fiscal reference DB harness/repository tests
- fiscal issuance orchestration shell
- POS Server client abstraction and request mapper
- success/replay handling
- failure/`errorPosture` handling
- unknown/readback planning hooks
- fiscal gating dry-run evaluator
- shadow observability
- fiscal reference context lookup for shadow evaluation
- structured shadow audit/event evidence
- feature-flag/readiness scaffolding
- future enforcement decision contract
- pre-enforcement UAT/preflight coverage

Current Central PMS implementation still does not have:

- production fiscal gating enforcement
- live POS Server calls from payment or exit flows
- retry scheduler
- GET readback worker
- Operator Console fiscal exception queues
- Management Dashboard fiscal visibility projections

## 5. Non-Goals

This runbook does not:

- implement production fiscal gating enforcement
- add a production ExitAuthorization blocking branch
- enable a feature flag
- call POS Server
- implement retry scheduling
- implement a GET readback worker
- implement Operator Console queues
- implement Management Dashboard projections
- implement Digital SI, printable SI, QR presentation, X-read, Z-read, BIR reports, EJ, POSLog, reprints, adjustments, counters, recovery, or gate behavior
- modify source code, SQL, migrations, generated artifacts, or DOCX files

## 6. Rollout Principles

- Roll out enforcement only after shadow evidence is stable and accepted.
- Roll out by environment first, then by Site / Site POS Server.
- Keep rollback simple, fast, and non-destructive.
- Do not lose payment finality records during rollback.
- Do not lose fiscal reference records during rollback.
- Do not reuse fiscal numbers.
- Do not treat manual release as normal ExitAuthorization.
- Preserve audit and correlation from payment finality through fiscal reference and ExitAuthorization decision.
- Fail closed only after the approved production blocking branch and go/no-go approvals exist.

## 7. Feature Flag Posture

Expected posture before enforcement:

- enforcement default OFF
- shadow evaluation ON
- readiness mode remains `readiness_only`
- `EnforcementWiredForBlocking = false`
- production blocking branch must not be introduced until this go/no-go checklist passes
- future enforcement must be Site/environment controlled
- rollback must be possible without losing payment finality or fiscal references

Future enforcement must not be enabled globally without pilot-site evidence and explicit approval.

## 8. Rollout Phases

| Phase | Name | Description | Exit criteria |
| --- | --- | --- | --- |
| Phase 0 | Documentation and preflight complete | Runbook, preflight checklist, decision contract, and shadow evidence tests are complete. | Required tests pass; runbook approved for planning. |
| Phase 1 | Shadow-only observation in lower environment | Run shadow evaluation without blocking in development/test environment. | Shadow payloads emitted; no startup/config regressions. |
| Phase 2 | Shadow-only observation in UAT | Run representative UAT payment-to-exit scenarios with fiscal reference states. | UAT evidence accepted; missing context and evaluation failures explained. |
| Phase 3 | Shadow-only observation in production | Observe production readiness without blocking. | Observation window stable; no unexpected failure rates. |
| Phase 4 | Limited site-level enforcement dry run / non-blocking compare | Compare future blocking decision against actual ExitAuthorization outcome for selected Site. | Business, support, compliance, and engineering owners accept observed impact. |
| Phase 5 | Controlled enforcement for one pilot Site, if approved | Enable future blocking only for one approved pilot Site after separate implementation branch. | Pilot metrics stable; rollback procedure verified. |
| Phase 6 | Wider rollout by Site / Site POS Server | Expand by Site and Site POS Server after pilot acceptance. | Per-site readiness and support signoff complete. |
| Phase 7 | Steady-state monitoring and reconciliation | Ongoing monitoring, exception handling, and reconciliation. | Regular review cadence established. |

This runbook does not enable any phase.

## 9. Pre-Production Readiness Checklist

- Focused `FiscalIssuance` tests pass.
- `ExitAuthorization` tests pass.
- `PaymentToExitOperationalEvidenceTests` pass.
- `FiscalIssuance` integration tests pass.
- Central PMS API build passes.
- Shadow decision evidence is emitted.
- Preflight checklist passes.
- POS Server fiscal issuance smoke is validated in the relevant environment.
- Central PMS fiscal reference persistence is validated.
- No known unrelated failures affect the payment-to-exit path.
- No production blocking branch is present.
- No live POS Server call is introduced from payment or exit flows before the planned enforcement implementation.

## 10. Site / Site POS Server Readiness Checklist

- Site is configured and active.
- Site POS Server is configured.
- Site-to-Site POS Server mapping is known.
- Payment channels under the Site are identified.
- WebPay, APM, Cashier-Assisted Terminal, and Continuity Terminal channels are mapped where applicable.
- Site Group is not used as fiscal authority.
- Site fiscal rollout owner is identified.
- Rollback contact is identified.
- Site operating hours and support coverage are known.
- Pilot Site selection is approved by operations and compliance owners.

## 11. POS Server Readiness Checklist

- POS Server is reachable from the Central PMS environment when live calls are later enabled.
- POS Server fiscal identity is configured.
- Fiscal sequence policy is configured.
- Fiscal sequence state is configured.
- POS Server API Contract version is aligned.
- `POST /v1/fiscal-documents/` is validated.
- `GET /v1/fiscal-documents/{fiscalDocumentId}` is validated.
- Idempotency behavior is validated.
- Replay behavior is validated.
- Conflict behavior is validated.
- Sequence allocation is validated.
- Response/status fields are validated.
- No X/Z/BIR/Digital SI assumptions are made unless separately implemented.

Required POS Server evidence fields for future gating readiness:

- `resultClassification`
- `fiscalIssuanceEvidenceStatus`
- `fiscalNumberAssignmentState`
- `fiscalDocumentStatusCodeId`
- `fiscalDocumentId`
- fiscal identity id
- fiscal sequence policy id
- fiscal sequence value
- fiscal document number

## 12. Central PMS Fiscal Reference State Readiness Checklist

- Fiscal reference persistence patch is applied.
- Validation SQL passed in the relevant environment.
- Repository tests passed.
- Fiscal reference state values are available.
- Exception reason taxonomy is available.
- Fiscal reference lookup by payment attempt works.
- Shadow evaluator can consume fiscal reference context.
- Audit/event evidence is emitted.
- No raw sensitive payloads are stored.
- Fiscal reference state can represent recorded, replayed, conflict, failed request, failed configuration, failed service, unknown, manual review, exception released, reconciled, and not required states.
- Future enforcement decision contract maps states to allow, block, not required by policy, exception release only, manual review required, or not evaluable.

## 13. Shadow Evaluation Evidence Review Checklist

Review evidence for:

- `evaluated_ready`
- `evaluated_blocked`
- `not_evaluated_missing_fiscal_context`
- `evaluation_failed_non_blocking`
- future decision `allow`
- future decision `block`
- future decision `not_required_by_policy`
- future decision `exception_release_only`
- future decision `manual_review_required`
- future decision `not_evaluable`

Each sample should include, where available:

- payment attempt id/ref
- payment confirmation id/ref
- parking session id/ref
- fiscal issuance reference id
- POS Server fiscal document id
- fiscal document number
- fiscal issuance state
- fiscal issuance evidence status
- fiscal number assignment state
- blocked reason
- exception reason
- `errorPosture`
- Site / Site POS Server reference
- correlation id / request id

Evidence must exclude secrets, credentials, PAN/CVV, tokens, raw provider callbacks, raw statutory entitlement evidence, unmanaged sensitive evidence images, and uncontrolled customer personal data.

## 14. Future Enforcement Decision Evidence Review Checklist

Confirm decision evidence for:

- recorded/assigned evidence -> `allow`
- replayed/assigned evidence -> `allow`
- pending/requested -> `block`
- conflict -> `block`
- failed request/configuration/service -> `block`
- unknown -> `block`
- not required with approved policy -> `not_required_by_policy`
- exception release -> `exception_release_only`
- manual review -> `manual_review_required`
- missing context -> `not_evaluable`

Confirm every event still reports:

- enforcement enabled flag
- enforcement wired for blocking flag
- future decision
- would allow normal ExitAuthorization
- would block normal ExitAuthorization
- manual review indicator
- exception release indicator
- not required indicator

Before a production blocking branch, `EnforcementWiredForBlocking` must remain `false`.

## 15. Test / UAT Evidence Checklist

Minimum evidence before go decision:

- Successful newly-created fiscal issuance.
- Successful idempotent replay.
- Replay after timeout.
- `409 fiscal_document_idempotency_conflict`.
- 400 request correction failure.
- 400 configuration correction failure.
- 503 service recovery failure.
- `fiscal_number_assignment_incomplete`.
- GET readback after unknown POST.
- Fiscal reference recording failure after POS Server success.
- Normal ExitAuthorization would be blocked until fiscal reference recorded.
- Manual release exception path after fiscal failure.
- No duplicate fiscal reference after replay.
- No duplicate ExitAuthorization after replay.
- Shadow evidence emitted for every future decision class.
- Sensitive payload exclusion verified.
- Existing payment-to-exit behavior verified unchanged before enforcement.

## 16. Operational Go/No-Go Checklist

Go criteria:

- All preflight tests pass.
- Shadow evidence is stable for the defined observation window.
- No unexpected missing fiscal context above accepted threshold.
- No unexplained `evaluation_failed_non_blocking`.
- POS Server readiness confirmed.
- Central PMS persistence readiness confirmed.
- Manual exception procedure approved.
- Rollback procedure approved.
- Operations/support trained.
- Business/BIR/accounting owner accepts enforcement posture.
- Pilot Site selected.
- Engineering on-call coverage confirmed.
- Support escalation path confirmed.
- Pilot Site rollback owner confirmed.

No-go criteria:

- POS Server is not ready.
- Site POS Server mapping is incomplete.
- Fiscal identity/policy/sequence configuration is incomplete.
- Shadow errors are unresolved.
- Missing fiscal context is unexplained.
- Manual exception procedure is not approved.
- Rollback is not tested.
- Open critical payment-to-exit defects exist.
- Audit evidence cannot be captured safely.
- Operations/support cannot staff the pilot window.

## 17. Rollback Checklist

If future enforcement is enabled and rollback is needed:

- Disable the future enforcement flag.
- Keep shadow evaluation active where safe.
- Preserve payment finality records.
- Preserve fiscal reference records.
- Stop blocking normal ExitAuthorization.
- Tag affected fiscal exceptions.
- Reconcile any manual releases.
- Notify operations/support.
- Capture incident report.
- Do not reuse fiscal numbers.
- Coordinate with POS Server/BIR/accounting where needed.
- Keep audit evidence for the rollback window.
- Record rollback approver, time, Site, Site POS Server, and reason.

Rollback must not delete fiscal evidence or mutate POS Server fiscal documents.

## 18. Manual Exception / Release Procedure

Manual release is not normal ExitAuthorization.

Manual exception release requires:

- approved reason code
- supervisor/operator approval according to policy
- incident tag
- reconciliation tag
- payment finality status
- fiscal issuance state and failure reason
- Site and Site POS Server context
- customer/session impact note where policy allows
- follow-up owner
- closure criteria

Manual exception release must not:

- modify POS Server fiscal documents
- allocate fiscal numbers
- mark fiscal issuance as successful
- bypass audit
- silently convert into normal ExitAuthorization
- hide the case from future Operator Console or Management Dashboard visibility

## 19. Monitoring and Alerting Checklist

Monitor and alert on:

- fiscal gating shadow `evaluated_ready` rate
- `evaluated_blocked` rate
- missing fiscal context rate
- `evaluation_failed_non_blocking` count
- future decision allow/block ratio
- future decision `not_evaluable` count
- payment-finality-to-fiscal-reference time
- fiscal reference persistence failures
- POS Server fiscal issuance failures
- idempotency conflicts
- unknown outcomes
- manual release count tied to fiscal exception
- pilot Site blocked-exit count after future enforcement
- rollback flag changes

Minimum dashboard labels should identify:

- source of truth
- freshness
- Site
- Site POS Server
- observation window
- enforcement mode

Management Dashboard remains visibility/reporting only.

## 20. Incident and Reconciliation Checklist

For each incident:

- record incident id
- record Site and Site POS Server
- record payment attempt / payment confirmation / parking session references
- record fiscal issuance reference id if available
- record POS Server fiscal document id if available
- record fiscal document number if available
- record fiscal state and exception reason
- record future enforcement decision
- record whether normal ExitAuthorization was blocked or would have been blocked
- record manual release approval if applicable
- record reconciliation action and closure owner
- confirm no fiscal number reuse
- coordinate with POS Server/BIR/accounting where required

## 21. Communications Checklist

Notify:

- operations team
- support/helpdesk
- parking site manager
- BIR/accounting/compliance owner
- engineering on-call
- POS Server owner
- payment provider / vendor PMS contact if needed
- release manager
- pilot Site owner

Communication artifacts:

- rollout start notice
- rollout observation window notice
- pilot Site enablement notice
- rollback notice template
- incident escalation template
- post-enablement summary

## 22. Production Enablement Approval Checklist

Before any future production blocking enablement:

- Product owner approval recorded.
- Operations approval recorded.
- Support/helpdesk approval recorded.
- BIR/accounting/compliance approval recorded.
- Engineering lead approval recorded.
- POS Server owner approval recorded.
- Site owner approval recorded.
- Rollback approver identified.
- Observation window and thresholds approved.
- Manual exception procedure approved.
- Support staffing confirmed.
- Deployment window approved.

Do not enable enforcement if any required approval is missing.

## 23. Post-Enablement Review Checklist

After future pilot enablement:

- Review blocked ExitAuthorization cases.
- Review manual releases.
- Review fiscal reference persistence failures.
- Review missing fiscal context.
- Review unknown outcomes.
- Review POS Server failure categories.
- Review idempotency conflicts.
- Review customer/support impact.
- Review rollback readiness.
- Confirm audit and correlation completeness.
- Decide continue, pause, rollback, or expand.

## 24. Risks and Open Questions

- Final production enforcement branch is not yet implemented.
- Final site-level feature flag mechanism remains to be confirmed.
- Final acceptable threshold for missing fiscal context remains business-owned.
- Final observation window length remains operations-owned.
- POS Server availability and configured fiscal sequence state must be validated per environment.
- Manual exception approval policy requires operational and compliance signoff.
- Operator Console and Management Dashboard projections are not yet implemented.
- GET readback worker and retry scheduler are not yet implemented.
- UAT with live POS Server must avoid unintended production fiscal numbering in non-production environments.

## 25. Requirements Traceability Summary

| Requirement area | Runbook coverage |
| --- | --- |
| Payment finality authority | Authority boundaries, go/no-go, manual release procedure |
| Fiscal reference recording | Central PMS fiscal reference readiness, rollback, incident reconciliation |
| POS Server fiscal issuance evidence | POS Server readiness, shadow evidence, test/UAT evidence |
| Idempotency and replay | POS Server readiness, UAT evidence, incident reconciliation |
| ExitAuthorization gating | Rollout principles, feature flag posture, future decision review |
| Manual exception release | Manual exception / release procedure, rollback, incident checklist |
| Audit/correlation | Shadow evidence review, monitoring, incident and reconciliation |
| Sensitive data exclusion | Shadow evidence review, test/UAT evidence |
| Rollback | Rollback checklist and communications checklist |
| Operator Console boundary | Authority boundaries, manual release visibility, non-goals |
| Management Dashboard boundary | Authority boundaries, monitoring labels, non-goals |
