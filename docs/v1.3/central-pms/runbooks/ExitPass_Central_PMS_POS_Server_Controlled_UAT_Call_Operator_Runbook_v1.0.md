# ExitPass Central PMS POS Server Controlled UAT Call Operator Runbook v1.0

## 1. Document Control

| Field | Value |
| --- | --- |
| Document | Central PMS POS Server Controlled UAT Call Operator Runbook |
| Version | v1.0 |
| Date | 2026-07-02 |
| Status | Documentation/runbook only |
| Repository | `D:\SourceCodes\ExitPass` |
| Branch | `feature/central-pms-pos-server-controlled-uat-call-operator-runbook` |
| Runtime reference | `D:\SourceCodes\ExitPass-PoSServer` on `dev` |

This document does not add an endpoint, tool, live call, source code, SQL, migration, generated artifact, or DOCX artifact.

## 2. Purpose and Scope

This runbook defines the safe operator and UAT procedure for a future controlled Central PMS to POS Server fiscal issuance diagnostic call.

The current implementation exposes only an application-level diagnostic seam:

- `EnablePosServerFiscalIssuanceLiveCall = false` by default.
- `EnableControlledUatDiagnosticPath = false` by default.
- `RunPosServerFiscalIssuanceDiagnosticAsync(...)`.
- No API endpoint.
- No CLI or operator tooling.
- No payment confirmation production-flow wiring.
- No ExitAuthorization production-flow wiring.

This runbook defines who may invoke the seam later, what approvals and configuration are required, what test data is acceptable, what evidence must be captured, and when the activity must be aborted.

## 3. Current Implementation Baseline

Current Central PMS implementation has:

- disabled-by-default POS Server fiscal issuance live-call seam;
- configuration hardening and readiness diagnostics;
- controlled UAT diagnostic method;
- request mapper for POS Server fiscal document creation;
- response parsing and orchestration handlers for success, replay, conflict, request failure, configuration failure, service failure, and unknown/fail-closed outcomes;
- fiscal reference persistence and state recording.

Current implementation does not have:

- API endpoint for this diagnostic path;
- CLI or operator tool for this diagnostic path;
- payment confirmation wiring;
- ExitAuthorization wiring;
- fiscal gating enforcement;
- retry scheduler;
- GET readback worker;
- Operator Console fiscal exception queue;
- Management Dashboard fiscal visibility projection.

## 4. Authority Boundaries

The following authority boundaries must remain intact:

- Central PMS owns payment finality.
- Central PMS owns fiscal reference recording.
- Central PMS owns normal ExitAuthorization.
- POS Server owns fiscal issuance and numbering only.
- POS Server response is fiscal issuance evidence only.
- POS Server does not approve payment, exit, gate opening, entitlement, manual release, or continuity.
- Manual release is not normal ExitAuthorization.

A successful diagnostic fiscal issuance does not authorize exit, open a gate, prove entitlement, approve a manual release, or complete any BIR report or Digital SI workflow.

## 5. Non-Goals

This runbook does not:

- expose an endpoint;
- expose operator tooling;
- execute a live call;
- enable a production flow;
- issue ExitAuthorization;
- enforce fiscal gating;
- implement retry behavior;
- implement a GET readback worker;
- implement an Operator Console queue;
- implement a Dashboard projection;
- implement BIR reports, X/Z reporting, Digital SI, QR presentation, EJ, POSLog, reprints, adjustments, counters, recovery, or gate behavior;
- modify POS Server fiscal documents manually.

## 6. Who May Run This UAT Path

Only approved UAT/engineering roles may invoke this path after a future controlled invocation mechanism exists:

- engineering lead;
- Central PMS developer/operator with explicit approval;
- POS Server owner or fiscal system owner;
- UAT lead;
- site rollout owner;
- compliance/accounting observer where fiscal numbers may be allocated.

Ordinary parking operators, cashier-assisted terminal users, APM users, WebPay users, continuity terminal users, and support staff without explicit approval must not invoke this path.

## 7. Required Approvals Before Invocation

Before invocation, approval must be recorded from:

- engineering lead;
- product/business owner;
- POS Server owner;
- site owner for the test Site;
- compliance/accounting owner if fiscal numbers may be allocated;
- operations lead.

Approval must identify:

- environment;
- Site and Site POS Server;
- expected transaction/test case;
- whether fiscal numbers may be allocated;
- rollback owner;
- evidence owner;
- observation window.

## 8. Environment Prerequisites

- Use a non-production environment where possible.
- If production is used, restrict execution to an approved pilot Site and controlled test transaction only.
- Identify the Central PMS environment.
- Identify the POS Server environment.
- Confirm network connectivity from Central PMS to POS Server.
- Confirm logs and evidence capture are enabled.
- Confirm rollback owner is online.
- Confirm no customer-impacting payment or exit flow wiring exists.
- Confirm no production fiscal gating enforcement is enabled.

## 9. Configuration Prerequisites

Required configuration for any future invocation:

- `EnablePosServerFiscalIssuanceLiveCall = true`.
- `EnableControlledUatDiagnosticPath = true`.
- `PosServerBaseUrl` is present and valid.
- Timeout is present and positive.
- Payment-flow live-call guard remains false.
- Exit-flow live-call guard remains false.
- Fiscal gating enforcement remains disabled.
- No secrets are stored in source-controlled files.

Expected readiness posture before invocation:

- readiness status is `enabled_ready`;
- diagnostic guard is explicitly enabled only for the UAT window;
- no payment confirmation path calls the POS Server client;
- no ExitAuthorization path calls the POS Server client.

## 10. POS Server Prerequisites

- POS Server API is reachable from the selected Central PMS environment.
- Test Site is mapped to the correct Site POS Server.
- POS Server fiscal identity is configured.
- Fiscal sequence policy is configured.
- Fiscal sequence state is configured.
- `POST /v1/fiscal-documents/` is validated for the environment.
- `GET /v1/fiscal-documents/{fiscalDocumentId}` is available for approved manual verification if needed.
- Idempotency behavior is understood.
- Replay behavior is understood.
- Conflict behavior is understood.
- Fiscal number allocation consequence is understood.
- Non-production fiscal sequence or approved test policy is preferred.

If a production fiscal sequence is used, the run may allocate a real fiscal number. That consequence must be explicitly approved before invocation.

## 11. Central PMS Prerequisites

- Fiscal reference persistence patch is applied.
- Fiscal reference repository tests have passed in the target build.
- Fiscal issuance orchestration tests have passed in the target build.
- POS Server live-call seam tests have passed in the target build.
- Configuration readiness reports `enabled_ready`.
- No payment/exit flow wiring exists for live POS Server calls.
- No fiscal gating enforcement is enabled.
- Shadow/audit evidence is available for comparison.
- The fiscal issuance reference row or context required by the diagnostic path exists and is safe to use.

## 12. Test Data Prerequisites

Prepare approved test data before invocation:

- test Site;
- Site POS Server id/ref;
- payment attempt ref;
- payment confirmation ref;
- parking session ref;
- payable basis ref;
- stable upstream finality reference;
- business day date;
- currency;
- amount in minor units;
- document lines;
- tender facts;
- tax facts;
- total facts;
- discount references only when approved test data exists.

The upstream finality reference must be stable and must identify the same semantic fiscal issuance request across repeat/replay attempts.

## 13. Fiscal Document Request Data Checklist

Confirm the request data contains:

- `sitePosServerId` or `sitePosServerRef`;
- fiscal document type id/key;
- fiscal document status id where applicable;
- `businessDayDate`;
- Central PMS parking session reference;
- Central PMS payment attempt reference;
- Central PMS payment confirmation reference;
- payable basis reference;
- `payableBasis.upstreamFinalityRef`;
- currency code;
- payable amount minor units;
- at least one document line where required;
- at least one tender where required;
- tax detail where required by the test case;
- totals;
- safe reference context;
- correlation id.

Do not proceed if the request data is not traceable to an approved test transaction.

## 14. Sensitive Data Exclusion Checklist

The diagnostic request must not include:

- PAN or CVV;
- tokens;
- credentials;
- secrets;
- raw provider callback payloads;
- raw entitlement evidence;
- uncontrolled images or files;
- unmanaged customer personal data;
- free-form blobs that have not been classified and approved.

Use references only. Do not embed raw provider, card, entitlement, image, or customer evidence payloads.

## 15. Pre-Run Safety Checklist

Before invocation, confirm:

- approvals are recorded;
- environment is correct;
- test Site and Site POS Server are correct;
- fiscal number allocation consequence is accepted;
- readiness status is `enabled_ready`;
- `EnableControlledUatDiagnosticPath` is enabled only for the approved window;
- payment-flow and exit-flow live-call guards remain false;
- fiscal gating enforcement remains off;
- no retry scheduler exists or is started;
- no GET readback worker exists or is started;
- evidence capture owner is ready;
- rollback owner is online;
- support/operations are aware of the test window.

Abort if any item is not true.

## 16. Invocation Model, Current State

Current state is application-level only.

The available seam is `RunPosServerFiscalIssuanceDiagnosticAsync(...)` on the Central PMS fiscal issuance live integration service. It is callable only from code or a future controlled harness. There is no supported operator endpoint, CLI, job, dashboard button, or payment/exit production-flow trigger.

Current invocation must not be attempted by ad hoc reflection, direct database mutation, unauthorized scripts, or manual HTTP calls into non-existent endpoints.

## 17. Future Invocation Model, Once Tooling or Endpoint Exists

If a future branch adds tooling or an endpoint, it must:

- require explicit diagnostic/UAT enablement;
- require authenticated and authorized operator identity;
- require Site and Site POS Server scope;
- reject missing approvals or missing run id;
- reject sensitive payload terms;
- expose only safe request/reference fields;
- return the diagnostic result without issuing ExitAuthorization;
- record structured evidence;
- remain unavailable to normal payment confirmation and ExitAuthorization flows;
- remain disabled by default.

If authorization conventions are unclear, prefer an application-level test harness over an endpoint.

## 18. Expected Diagnostic Statuses

Expected diagnostic statuses include:

| Status | Meaning | Operator response |
| --- | --- | --- |
| `disabled` | Live-call seam is disabled. | Do not proceed unless approved config change is expected. |
| `diagnostic_disabled` | Live call may be configured, but UAT diagnostic guard is off. | Enable only during approved UAT window. |
| `config_invalid` | Required live-call configuration is missing or invalid. | Fix config before any call; do not retry with invalid config. |
| `local_context_invalid` | Central PMS request context failed local validation. | Correct test data; do not call POS Server. |
| `request_mapped` | Request was mapped locally. | Capture mapped request evidence where safe. |
| `pos_server_call_attempted` | POS Server client call was attempted. | Capture correlation, timestamp, and logs. |
| `newly_created_recorded` | POS Server returned first-time success and Central PMS recorded evidence. | Capture full fiscal evidence and reconcile. |
| `replay_recorded` | POS Server returned idempotent replay and Central PMS recorded/reconciled evidence. | Confirm replay matches original fiscal document. |
| `conflict_failure_mapped` | POS Server returned idempotency conflict. | Stop and route for review. |
| `request_failure_mapped` | POS Server returned request/data failure. | Correct request facts before any retry. |
| `configuration_failure_mapped` | POS Server returned fiscal identity/policy/sequence/config failure. | Correct configuration before any retry. |
| `service_failure_mapped` | POS Server returned service/persistence failure. | Wait for service recovery and reconcile. |
| `unknown_fail_closed` | Outcome is not sufficient to record success. | Treat as unknown; do not assume fiscal issuance success. |

## 19. Evidence Capture Checklist

Capture the following:

- run id;
- date/time;
- environment;
- Site;
- Site POS Server;
- operator/approver;
- configuration readiness status;
- upstream finality reference;
- request correlation id;
- POS Server HTTP status;
- POS Server response code;
- result classification;
- fiscal document id;
- fiscal document number;
- fiscal identity id;
- fiscal sequence policy id;
- fiscal sequence value;
- fiscal evidence status;
- fiscal number assignment state;
- Central PMS fiscal state result;
- error code and `errorPosture` if failure;
- screenshots or log references where appropriate;
- confirmation that no payment finality was changed by the diagnostic path;
- confirmation that no ExitAuthorization was issued by the diagnostic path;
- confirmation that no gate behavior occurred.

Evidence must not include raw sensitive payloads.

## 20. Success Criteria

A controlled UAT run is successful only when:

- readiness status is `enabled_ready`;
- diagnostic path is explicitly invoked by an approved actor;
- POS Server call succeeds as `newly_created` or expected `idempotent_replay`;
- returned fiscal evidence is complete;
- Central PMS records or reconciles fiscal evidence;
- no payment finality mutation occurs from the diagnostic path;
- no ExitAuthorization is issued by the diagnostic path;
- no gate behavior occurs;
- evidence capture is complete;
- post-run reconciliation has no mismatch.

## 21. Failure/Abort Criteria

Abort immediately when:

- environment is wrong;
- production fiscal sequence risk is not approved;
- base URL or timeout configuration is invalid;
- Site or Site POS Server mapping is wrong;
- fiscal identity, policy, or sequence state is missing;
- POS Server response is unexpected;
- outcome is unknown and no readback/reconciliation plan is approved;
- sensitive data is detected;
- diagnostic path attempts to affect payment or exit flow;
- support or rollback owner is unavailable;
- any participant cannot explain whether a fiscal number may be allocated.

## 22. Idempotency and Replay Handling Rules

- Reuse the same upstream finality reference only for the same semantic request.
- Do not change payload facts under the same upstream finality reference.
- Replay should return the original fiscal document id and original numbering fields.
- Replay must not advance the fiscal sequence.
- Do not create a new upstream finality reference to bypass a conflict.
- Do not issue duplicate ExitAuthorization based on replay.
- If replay mismatches Central PMS evidence, stop and route to review.

## 23. Conflict Handling Rules

If POS Server returns an idempotency conflict:

- stop the run;
- preserve the upstream finality reference;
- preserve the request facts and response;
- do not generate a new upstream finality reference to bypass the conflict;
- do not retry automatically;
- route the case to engineering/POS Server owner review;
- record the conflict in UAT evidence.

## 24. Unknown Outcome Handling Rules

If outcome is unknown or fail-closed:

- do not assume fiscal issuance succeeded;
- preserve the upstream finality reference;
- capture Central PMS and POS Server logs;
- use manual GET readback only if approved and fiscal document id is known;
- remember no automatic GET readback worker exists;
- do not issue ExitAuthorization based on unknown result;
- keep the case open until reconciled or explicitly closed by approved reviewers.

## 25. Post-Run Reconciliation Checklist

After the run:

- compare POS Server fiscal document evidence with Central PMS fiscal reference;
- verify fiscal document id;
- verify fiscal document number;
- verify fiscal identity id;
- verify fiscal sequence policy id;
- verify fiscal sequence value;
- verify evidence status and number assignment state;
- verify no duplicate fiscal reference exists;
- verify no ExitAuthorization side effect exists;
- verify no gate side effect exists;
- record the final result in UAT evidence;
- close or escalate the case.

## 26. Rollback/Cleanup Checklist

After the run or on abort:

- disable `EnableControlledUatDiagnosticPath`;
- disable `EnablePosServerFiscalIssuanceLiveCall` if no longer needed;
- preserve evidence and logs;
- do not delete fiscal records without approved process;
- do not reuse fiscal numbers;
- document any allocated numbers;
- notify stakeholders that the UAT window is closed;
- record unresolved exceptions for follow-up.

Rollback must not delete payment finality records, fiscal reference records, POS Server fiscal documents, or sequence evidence without an approved data governance process.

## 27. Communications Checklist

Notify the following before and after the run:

- engineering lead;
- Central PMS owner;
- POS Server owner;
- operations lead;
- site rollout owner;
- compliance/accounting owner when fiscal numbers may be allocated;
- support/helpdesk if production-like environment is used;
- rollback owner;
- UAT evidence owner.

Communication should include:

- run id;
- environment;
- Site;
- Site POS Server;
- start and end time;
- expected fiscal allocation risk;
- result status;
- rollback/cleanup completion.

## 28. Risks and Open Questions

Risks:

- production fiscal number allocation if the wrong sequence is used;
- idempotency conflict from changed payload under the same upstream finality reference;
- unknown outcome after network or service failure;
- incomplete evidence interpreted incorrectly as success;
- accidental exposure through an endpoint or tool without authorization controls;
- operator misunderstanding that fiscal issuance equals ExitAuthorization.

Open questions:

- final approved UAT invocation mechanism remains undefined;
- final role/permission model for any future endpoint/tool remains undefined;
- production pilot Site and Site POS Server are not selected by this runbook;
- manual GET readback approval workflow remains operationally defined outside this document;
- final evidence retention location remains to be selected.

## 29. Requirements Traceability Summary

| Requirement area | Runbook coverage |
| --- | --- |
| Current implementation baseline | Sections 2, 3, 16 |
| Authority boundaries | Section 4 |
| Non-goals | Section 5 |
| Authorized roles and approvals | Sections 6, 7 |
| Environment/config/POS Server/Central PMS prerequisites | Sections 8-11 |
| Test data and fiscal request facts | Sections 12, 13 |
| Sensitive data exclusion | Section 14 |
| Safety controls | Sections 15, 21 |
| Invocation model | Sections 16, 17 |
| Diagnostic statuses | Section 18 |
| Evidence capture | Section 19 |
| Success and failure handling | Sections 20, 21 |
| Idempotency, replay, conflict, unknown outcome | Sections 22-24 |
| Reconciliation and rollback | Sections 25, 26 |
| Communications | Section 27 |
| Risks and open questions | Section 28 |

Recommended next task:

`feature/central-pms-pos-server-controlled-uat-evidence-template`

Purpose: create a structured UAT evidence template for controlled POS Server fiscal issuance diagnostic runs before exposing tooling or endpoints.
