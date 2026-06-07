# Operator Console Statutory Discount Pilot Feedback Log Template

Use this template for every failed validation step, operator confusion, privacy concern, control exception, or pilot observation from the Operator Console statutory discount workflow.

Do not record production credentials, raw ID numbers, raw evidence images, unredacted screenshots, or personal data. Use masked values, sandbox values, or correlation IDs only.

## Feedback Entry

| Field | Entry |
| --- | --- |
| Feedback/defect ID | `OC-SD-PILOT-YYYYMMDD-###` |
| Date/time | `YYYY-MM-DD HH:mm timezone` |
| Site | Site name or sandbox site code only |
| Operator/role | Anonymized operator label and role only, for example `Operator A - cashier` |
| Device/shift context | Non-sensitive device binding label, lane, shift window, or sandbox fixture only |
| Correlation ID | API/request correlation ID |
| Ticket/session reference | Masked or test-only reference only, for example `TKT-****-1234` |
| Workflow step | Select one value from the workflow step list below |
| Issue type | Select one value from the issue type list below |
| Severity | Select one value from the severity list below |
| Reproducibility | Always, intermittent, once, not yet reproduced, or needs investigation |
| Expected result | What should have happened according to the runbook or approved policy |
| Actual result | What happened, including exact redacted error text when useful |
| Screenshots/log references | Redacted screenshot name, log excerpt pointer, trace ID, or `none` |
| Steps to reproduce | Numbered reproduction steps using sandbox or masked references only |
| Immediate workaround | Operational workaround, `none`, or `stop pilot` |
| Owner | Person, role, or team responsible for triage |
| Target resolution | Target date, release, batch, or `backlog` |
| Status | New, triaging, accepted, deferred, rejected, fixed, verified, or closed |
| Resolution notes | Decision, fix summary, or reason for deferral/rejection |
| Regression test added or updated | Test file/name, docs-only, not required, or pending |
| Related PR/branch | PR number, branch name, or `none` |
| Sign-off | Supervisor/compliance/product sign-off name or role and date |

## Workflow Step Values

- Session lookup
- Policy resolution
- Draft creation
- Evidence capture
- Evidence list/read model
- Approval decision
- Apply-payable-basis
- Final verification
- Access/RBAC
- Privacy/evidence handling
- Operator usability

## Issue Type Values

- Defect
- Operator confusion
- Missing training
- Policy ambiguity
- Access/RBAC issue
- Evidence/privacy issue
- Performance issue
- Data/setup issue
- Documentation issue
- Enhancement request

## Severity Values

- S0 pilot blocker
- S1 control/compliance risk
- S2 workflow defect
- S3 usability/documentation issue
- S4 enhancement

## Reproduction Notes

1. Use the runbook step number and request name when possible.
2. Record only masked ticket/session references or test-only fixtures.
3. Include the correlation ID for each API request involved in the finding.
4. Preserve exact error codes and messages when they do not include sensitive data.
5. Redact operator names, customer names, ID numbers, vehicle identifiers, and images.

## Example Entry Skeleton

| Field | Entry |
| --- | --- |
| Feedback/defect ID | `OC-SD-PILOT-20260607-001` |
| Date/time | `2026-06-07 14:30 Asia/Manila` |
| Site | `Sandbox Site A` |
| Operator/role | `Operator A - cashier` |
| Device/shift context | `Sandbox lane 1, afternoon shift` |
| Correlation ID | `00000000-0000-0000-0000-000000000000` |
| Ticket/session reference | `TEST-TKT-****-0001` |
| Workflow step | `Evidence capture` |
| Issue type | `Defect` |
| Severity | `S2 workflow defect` |
| Reproducibility | `Always` |
| Expected result | `Expected behavior from runbook step` |
| Actual result | `Observed redacted behavior` |
| Screenshots/log references | `redacted-screenshot-001.png` |
| Steps to reproduce | `1. Start from sandbox session...` |
| Immediate workaround | `Use documented manual workaround or none` |
| Owner | `Backend owner` |
| Target resolution | `#235 batch 1` |
| Status | `New` |
| Resolution notes | `Pending triage` |
| Regression test added or updated | `Pending` |
| Related PR/branch | `none` |
| Sign-off | `Pending` |
