# Operator Console Statutory Discount Pilot Triage Guide

## Purpose

This guide defines how pilot feedback for the Operator Console statutory discount workflow is classified, escalated, converted into defects, and closed. It is limited to the Operator Console statutory discount validation pilot and does not authorize changes to payment routing, providers, AUB, coupons, reconciliation, HikCentral, gates, raw evidence storage, OCR, or document verification.

## Triage Cadence

- Review new pilot feedback entries daily during active pilot execution.
- Review S0 and S1 entries immediately when reported.
- Confirm status, owner, workaround, and target resolution for every open S0, S1, and S2 entry before the next pilot session.
- Summarize accepted, deferred, rejected, fixed, and verified entries at pilot closeout.

## Triage Participants

- Pilot supervisor or operations lead.
- Product or business owner for statutory discount policy interpretation.
- Backend owner for API, read model, evidence gating, access, and persistence behavior.
- Operator Console UI owner when a documented issue is specifically about the Operator Console UI.
- Compliance/privacy owner for evidence handling, masking, access, and audit concerns.
- QA/test owner for reproduction, regression test coverage, and verification.

## Severity Definitions

| Severity | Definition | Examples |
| --- | --- | --- |
| S0 pilot blocker | Prevents safe continuation of the pilot or creates immediate customer, compliance, or control exposure. | Approval succeeds before evidence; raw ID image stored unexpectedly; unauthorized operator can view evidence. |
| S1 control/compliance risk | Workflow can continue only after immediate review because controls, privacy, or statutory policy enforcement may be unreliable. | Wrong evidence type satisfies entitlement; raw ID number exposed in response; access denial missing for unauthorized operator. |
| S2 workflow defect | Operator Console statutory discount workflow behaves incorrectly, but a safe workaround exists and controls remain intact. | Wrong status shown in read model; deterministic error message is misleading; valid metadata-only evidence is rejected. |
| S3 usability/documentation issue | Operator confusion, training gap, or runbook mismatch that does not change control behavior. | Runbook step name mismatches endpoint response; operator unclear which reference to record. |
| S4 enhancement | Useful future improvement outside the current defect-fix scope. | Dashboard request; reporting request; automation request; additional workflow convenience. |

## Go/No-Go Impact By Severity

- S0: Stop the affected pilot path immediately. Resume only after owner fix, compliance/product approval when applicable, and verification.
- S1: Pause the affected step or site path until triage confirms scope, workaround, and escalation owner.
- S2: Continue only with a documented workaround and owner approval.
- S3: Continue pilot execution and capture training or documentation follow-up.
- S4: Do not block pilot execution. Convert to backlog only after product review.

## Classification Rules

- Classify as a defect only when behavior is reproducible or clearly observable and differs from the runbook, approved policy, API contract, access rule, evidence rule, or privacy expectation.
- Classify as operator confusion when the system behavior is correct but the operator cannot complete the step without clarification.
- Classify as missing training when the runbook is correct but pilot operators need additional preparation.
- Classify as policy ambiguity when the correct statutory discount handling is not clear from approved product or compliance policy.
- Classify as access/RBAC issue when operator identity, role, device, shift, site, or claim handling produces unexpected access behavior.
- Classify as evidence/privacy issue when evidence metadata, masking, visibility, retention, or access audit behavior is unexpected.
- Classify as data/setup issue when the finding depends on missing or incorrect sandbox fixtures, site policy setup, test ticket state, or operator/device setup.
- Classify as documentation issue when the runbook or endpoint description is inaccurate but implementation behavior is accepted.
- Classify as enhancement request when the request adds a new capability rather than correcting approved behavior.

## Escalation Rules

- Escalate S0 and privacy/control red flags immediately to the pilot supervisor, product owner, backend owner, and compliance/privacy owner.
- Escalate policy ambiguity to product and compliance before any code fix is accepted.
- Escalate access/RBAC issues to backend and security owners before expanding pilot scope.
- Escalate payment/provider/gate/coupon/reconciliation mutation concerns immediately and keep them out of Operator Console statutory discount defect batches unless separately approved.
- Escalate any report containing unredacted personal data so the artifact can be removed or replaced with a redacted version.

## Stop-Pilot Rules

Stop the affected pilot path immediately when any of the following occurs:

- Raw ID number is exposed.
- Raw image or document is stored unexpectedly.
- Evidence is visible to an unauthorized operator.
- Approval succeeds before required evidence is captured.
- Wrong evidence type satisfies entitlement.
- Operator Console statutory discount validation mutates payment/provider/gate/coupon/reconciliation behavior.
- AUB is selected, routed to, configured, or invoked.
- The team cannot determine whether a control failure exposed production personal data.

## Continue-With-Workaround Rules

The pilot may continue with a workaround only when all of the following are true:

- Severity is S2 or lower, or S1 has explicit compliance/product approval to continue.
- The workaround does not weaken evidence gating, access control, privacy, payable-basis approval, or auditability.
- The workaround does not require WebPay, payment provider, AUB, coupon, reconciliation, HikCentral, gate, database baseline, Docker, CI/CD, or seed data changes.
- The workaround is recorded in the feedback log entry.
- The owner and target resolution are assigned.

## Backlog Conversion Rules

- Convert S4 enhancement requests to backlog items only after product review.
- Convert S3 documentation or training findings to docs/training work when implementation behavior is accepted.
- Convert policy ambiguity to backlog only after product and compliance document the desired policy.
- Reject speculative issues that have no pilot feedback entry, reproduction, runbook mismatch, or observable defect.
- Defer broad schema redesign, dashboards, reports, coupons, reconciliation UI, payment routing/provider changes, AUB, OCR, document verification, raw evidence storage, HikCentral changes, and gate changes unless separately scoped.

## Regression Test Requirements

- Every accepted code defect must have a regression test unless the change is docs-only.
- Prefer the narrowest test that reproduces the pilot finding and verifies the corrected behavior.
- Include the feedback/defect ID in the test name, test comment, or PR description when practical.
- Access/RBAC defects require tests covering denied and allowed behavior.
- Evidence gating defects require tests covering the incorrect edge case and the accepted evidence path.
- Read model defects require tests verifying the response shape and status values.
- Documentation-only fixes must cite the feedback/defect ID in the change summary or resolution notes.

## Privacy/Control Red Flags Requiring Immediate Escalation

- Raw ID number exposed.
- Raw image/document stored unexpectedly.
- Evidence visible to unauthorized operator.
- Approval succeeds before evidence.
- Wrong evidence type satisfies entitlement.
- Payment/provider/gate/coupon/reconciliation mutation.

## Defect Closure Criteria

Close an accepted defect only when all applicable criteria are met:

- Feedback/defect ID is recorded.
- Reproduction or observable evidence is documented.
- Scope is confirmed as Operator Console statutory discount validation.
- Fix or documentation update is complete.
- Regression test is added or updated for code changes.
- Verification result is recorded.
- Privacy/control review is complete for S0, S1, and evidence/privacy issues.
- Status is updated to fixed, verified, or closed.
- Resolution notes identify the PR/branch and any remaining risk.
