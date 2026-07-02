# ExitPass Central PMS POS Server Controlled UAT Harness Planning v1.0

## 1. Document Control

| Field | Value |
| --- | --- |
| Document | Central PMS POS Server Controlled UAT Harness Planning |
| Version | v1.0 |
| Date | 2026-07-02 |
| Status | Documentation/planning only |
| Repository | `D:\SourceCodes\ExitPass` |
| Branch | `feature/central-pms-pos-server-controlled-uat-harness-planning` |
| Runtime reference | `D:\SourceCodes\ExitPass-PoSServer` on `dev` |

This plan does not implement a harness, CLI, endpoint, source-code change, SQL change, migration, generated artifact, DOCX artifact, POS Server runtime change, or live POS Server call.

## 2. Purpose and Scope

This plan compares safe invocation strategies for the Central PMS to POS Server fiscal issuance diagnostic seam.

The current implementation has an application-level diagnostic seam and the documentation baseline now includes:

- controlled POS Server UAT call operator runbook;
- controlled POS Server UAT evidence template.

The remaining gap is an approved, safe way for a UAT actor to invoke the seam. This plan recommends the next implementation approach before any tool or endpoint is built.

## 3. Current Implementation Baseline

Current Central PMS implementation has:

- `EnablePosServerFiscalIssuanceLiveCall = false` by default;
- `EnableControlledUatDiagnosticPath = false` by default;
- `RunPosServerFiscalIssuanceDiagnosticAsync(...)`;
- application-level seam only;
- no endpoint;
- no CLI/tooling;
- no payment confirmation wiring;
- no ExitAuthorization wiring;
- no fiscal gating enforcement;
- no retry scheduler;
- no GET readback worker;
- controlled UAT operator runbook;
- controlled UAT evidence template.

The seam can map a Central PMS fiscal context, call the POS Server fiscal document client only when explicitly enabled and guarded, apply success/replay/failure handlers, and return diagnostic fields including readiness, mapped-request flag, client-called flag, fiscal result classification, fiscal state, error code, `errorPosture`, and no-impact confirmations.

## 4. Authority Boundaries

The invocation strategy must preserve:

- Central PMS owns payment finality.
- Central PMS owns fiscal reference recording.
- Central PMS owns normal ExitAuthorization.
- POS Server owns fiscal issuance and numbering only.
- POS Server response is fiscal issuance evidence only.
- POS Server does not approve payment, exit, gate opening, entitlement, manual release, or continuity.
- Manual release is not normal ExitAuthorization.

No invocation mechanism may convert fiscal issuance evidence into payment finality, ExitAuthorization, gate instruction, entitlement approval, or manual release approval.

## 5. Non-Goals

This planning task does not:

- implement any harness/tool/endpoint;
- execute live calls;
- enable production payment/exit flow;
- issue ExitAuthorization;
- enforce fiscal gating;
- implement retry;
- implement a GET readback worker;
- implement an Operator Console queue;
- implement a Dashboard projection;
- modify source code;
- modify SQL;
- modify POS Server runtime.

## 6. Invocation Options Considered

Options considered:

- Option A: application-level test harness.
- Option B: internal CLI tool.
- Option C: internal diagnostic API endpoint.
- Option D: Operator Console action, future only.
- Option E: scheduled/job-based diagnostic, not recommended now.

All options must preserve explicit invocation, default-disabled configuration, run-id/evidence capture, sensitive-data exclusion, no payment/exit wiring, and no fiscal gating enforcement.

## 7. Option A: Application-Level Test Harness

Description:

- invoked by automated/integration test or a controlled internal harness;
- uses the existing `RunPosServerFiscalIssuanceDiagnosticAsync(...)` seam;
- no public endpoint;
- no operator UI;
- lower exposure;
- easiest to keep disabled by default;
- best fit for first controlled UAT;
- requires engineering participation.

Strengths:

- avoids new network exposure inside Central PMS;
- can run only in controlled UAT environments;
- can require explicit test fixture configuration;
- can capture the evidence template directly;
- keeps operational payment and exit paths untouched;
- limits execution to engineers/UAT leads who understand the fiscal-number allocation consequence.

Limitations:

- less convenient for non-engineering UAT users;
- requires a controlled harness build/run process;
- requires clear test-data injection and evidence output conventions.

Planning posture: recommended first.

## 8. Option B: Internal CLI Tool

Description:

- explicit command-line invocation;
- useful for controlled UAT once application-level harness evidence is stable;
- can require local config, run id, evidence path, and approval reference;
- more implementation effort than an application-level harness.

Strengths:

- easier to run repeatedly in UAT than an integration-test harness;
- can write evidence files directly;
- can enforce explicit run id and input file validation;
- can be restricted to engineering/UAT machines.

Risks:

- misuse risk if distributed broadly;
- local configuration drift;
- secrets or endpoint details may be mishandled if not carefully designed;
- may bypass normal service hosting diagnostics unless built around the same application services.

Planning posture: later candidate after Option A evidence is stable.

## 9. Option C: Internal Diagnostic API Endpoint

Description:

- easier to invoke remotely;
- could support controlled UAT without shell access;
- highest exposure among near-term options.

Required controls:

- disabled by default;
- explicit diagnostic guard;
- authenticated operator identity;
- authorization for a specific fiscal UAT permission;
- run id required;
- Site/Site POS Server scope required;
- evidence-template reference required;
- public-client access blocked;
- payment/exit production-flow wiring prohibited.

Risks:

- requires settled authentication/authorization conventions;
- could be accidentally exposed to unintended clients;
- can allocate fiscal numbers if misused;
- requires deeper audit, rate-limit, environment allow-list, and operational controls.

Planning posture: not recommended until role/auth/evidence controls are finalized.

## 10. Option D: Operator Console Action, Future Only

Description:

- future governed workflow surface;
- could present approvals, run status, evidence capture, and audit in a controlled UI.

Prerequisites:

- Operator Console fiscal exception queues exist;
- fiscal UAT/diagnostic roles are defined;
- approval workflow is implemented;
- evidence storage and audit event model are stable;
- no ordinary operator path can invoke fiscal issuance diagnostics.

Planning posture: future only, not suitable for the current slice.

## 11. Option E: Scheduled/Job-Based Diagnostic, Not Recommended Now

Description:

- a scheduled or background job could call the diagnostic seam.

Why not recommended:

- diagnostic fiscal issuance must be explicit and approved;
- scheduled calls risk fiscal number allocation without current operator intent;
- retry-like behavior could be confused with a scheduler;
- unknown outcomes require human-controlled reconciliation;
- evidence must be captured per run before, during, and after invocation.

Planning posture: reject for current UAT diagnostic use.

## 12. Comparison Matrix

| Option | Safety | Complexity | Authorization requirements | Evidence capture ease | Fiscal-number allocation risk | Disabled-by-default fit | UAT usability | Recommended timing |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| A: Application-level test harness | High | Low/medium | Engineering/UAT process controls | High | Low when run by engineering with explicit config | High | Medium | First |
| B: Internal CLI tool | Medium/high | Medium | Local operator approval and machine control | High | Medium if distributed too broadly | High | High | After Option A |
| C: Internal diagnostic API endpoint | Medium | Medium/high | Strong auth/RBAC, environment allow-list, run id, Site scope | Medium/high | Medium/high if exposed incorrectly | Medium/high | High | After auth/evidence controls settle |
| D: Operator Console action | Medium/high after prerequisites | High | Full Operator Console governance | High | Medium unless workflow is strict | Medium/high | High | Future only |
| E: Scheduled/job diagnostic | Low | Medium | Job governance plus approvals | Low/medium | High | Medium | Low | Not recommended |

## 13. Security and Authorization Considerations

Minimum security posture:

- no ordinary parking operators;
- no terminal users;
- engineering/UAT only for first implementation;
- explicit approvals required;
- run id required;
- environment allow-list required;
- Site and Site POS Server scope required;
- evidence-template path/reference required;
- sensitive payload rejection required;
- no secrets in source-controlled files;
- audit logs required;
- no public client or payment-channel access.

Any future endpoint or CLI must reject invocation when approvals, run id, Site scope, or evidence target are missing.

## 14. Configuration Guard Requirements

Required configuration guards:

- `EnablePosServerFiscalIssuanceLiveCall = true` only during approved controlled run;
- `EnableControlledUatDiagnosticPath = true` only during approved controlled run;
- valid `PosServerBaseUrl`;
- positive timeout;
- payment-flow live-call guard remains false;
- exit-flow live-call guard remains false;
- fiscal gating enforcement remains false;
- `EnforcementWiredForBlocking = false`;
- no production payment/exit flow wiring.

The harness must fail before mapping or client call when readiness is not `enabled_ready`.

## 15. UAT Evidence Integration Requirements

The invocation strategy must populate or help populate the controlled UAT evidence template:

- run summary;
- approval record;
- environment and Site/Site POS Server record;
- configuration readiness record;
- POS Server readiness record;
- Central PMS readiness record;
- test data and fiscal request facts;
- sensitive-data exclusion confirmation;
- invocation record;
- POS Server response record;
- Central PMS fiscal reference result;
- idempotency/replay/conflict/failure/unknown evidence;
- shadow/audit evidence where available;
- payment, ExitAuthorization, and gate impact confirmations;
- reconciliation and cleanup records.

The first harness should produce a structured evidence output file or a clearly mapped console/test artifact that can be copied into the template without reinterpretation.

## 16. Required Request/Input Model

The first harness input model should include:

- run id;
- environment;
- Site id/ref;
- Site POS Server id/ref;
- parking session ref;
- payment attempt ref;
- payment confirmation ref;
- payable basis ref;
- upstream finality ref;
- business day date;
- currency;
- amount minor units;
- document lines;
- tenders;
- tax/totals;
- correlation id;
- evidence owner;
- approval references.

Input model rules:

- no PAN/CVV;
- no tokens;
- no credentials;
- no secrets;
- no raw provider callback payloads;
- no raw entitlement evidence;
- no uncontrolled files/images;
- no unmanaged customer personal data;
- no free-form sensitive blobs.

## 17. Required Response/Output Model

The first harness output model should include:

- diagnostic status;
- readiness status;
- request mapped flag;
- client called flag;
- POS Server HTTP status;
- response code;
- result classification;
- fiscal document id;
- fiscal document number;
- fiscal issuance evidence status;
- fiscal number assignment state;
- Central PMS fiscal state result;
- error code;
- `errorPosture`;
- no payment finality changed flag;
- no ExitAuthorization issued flag;
- no gate behavior flag;
- evidence capture reference;
- correlation id;
- timestamp.

Output must be safe for evidence capture and must exclude raw sensitive payloads.

## 18. Sensitive Data Exclusion Requirements

The invocation strategy must reject or avoid:

- PAN;
- CVV;
- tokens;
- credentials;
- secrets;
- raw provider callback payloads;
- raw entitlement evidence;
- uncontrolled uploaded evidence files;
- unmanaged customer personal data;
- free-form sensitive blobs.

References are allowed when they are approved, stable, and non-secret.

## 19. Operational Safety Controls

Required operational controls:

- approvals completed before run;
- run id required;
- target environment explicitly selected;
- Site/Site POS Server verified;
- fiscal number allocation consequence accepted;
- readiness status verified as `enabled_ready`;
- diagnostic guard enabled only for the approved window;
- evidence capture initialized before invocation;
- rollback owner online;
- support/operations notified when required;
- no concurrent unapproved diagnostic run for the same Site/upstream finality reference.

The harness must not infer payment finality or create new payment facts.

## 20. Audit/Logging/Evidence Requirements

Required evidence:

- run id;
- invocation timestamp;
- operator/executor;
- approvals;
- environment;
- Site and Site POS Server;
- upstream finality reference;
- correlation id;
- configuration readiness;
- POS Server request facts, safe only;
- POS Server response summary;
- Central PMS fiscal state result;
- no-impact confirmations for payment finality, ExitAuthorization, and gate behavior;
- cleanup status;
- evidence output location.

Audit records must support reconstruction from approved test data to fiscal issuance result without exposing sensitive payloads.

## 21. Rollback/Abort Controls

Abort if:

- wrong environment;
- config is not ready;
- production fiscal number risk is not approved;
- Site/Site POS Server mismatch;
- sensitive data is detected;
- POS Server response is unexpected;
- unknown outcome occurs without readback/reconciliation plan;
- payment/exit flow mutation is detected;
- rollback owner is unavailable.

Rollback/cleanup must:

- disable `EnableControlledUatDiagnosticPath`;
- disable `EnablePosServerFiscalIssuanceLiveCall` if not needed;
- preserve evidence and logs;
- not delete fiscal records without approved process;
- not modify POS Server fiscal documents;
- not reuse fiscal numbers.

## 22. Recommended Option

Recommended first option: Option A, application-level internal test harness.

Reasons:

- safest before endpoint/tooling;
- uses the existing application seam;
- avoids public or remote exposure;
- allows controlled evidence capture;
- limits execution to engineering/UAT environment;
- preserves no payment/exit production wiring;
- avoids introducing auth/RBAC surface before requirements are settled;
- supports one controlled run at a time with explicit configuration and run id.

Later options:

- Option B, internal CLI, can be considered after Option A captures successful UAT evidence and the input/output schema stabilizes.
- Option C, internal endpoint, can be considered only after role/auth/evidence controls are finalized.
- Option D, Operator Console action, is future-only after fiscal exception queue/governance workflows exist.
- Option E should not be used for this diagnostic purpose.

## 23. Recommended Implementation Sequence

Recommended sequence:

1. Create an application-level UAT harness/test fixture invoking `RunPosServerFiscalIssuanceDiagnosticAsync(...)`.
2. Require explicit config:
   - `EnablePosServerFiscalIssuanceLiveCall = true`;
   - `EnableControlledUatDiagnosticPath = true`;
   - valid `PosServerBaseUrl`;
   - payment/exit flow guards false;
   - fiscal gating enforcement false.
3. Require run id and evidence template path/reference.
4. Require safe input model.
5. Validate Site/Site POS Server and upstream finality reference semantics.
6. Execute one controlled diagnostic run.
7. Capture evidence template fields.
8. Confirm no payment finality mutation.
9. Confirm no ExitAuthorization or gate behavior.
10. Disable diagnostic config after run.
11. Review evidence and reconcile fiscal reference outcome.
12. Only after successful evidence, consider CLI or endpoint strategy.

First implementation branch should not add endpoint/tooling and should not wire payment/exit flows.

## 24. Risks and Open Questions

Risks:

- accidental fiscal number allocation in the wrong environment;
- changed request payload under the same upstream finality reference;
- unknown outcome without approved reconciliation plan;
- sensitive data captured in evidence;
- future endpoint exposure before auth/RBAC controls are settled;
- UAT actor misunderstanding fiscal issuance as ExitAuthorization.

Open questions:

- final harness input file format;
- final evidence output location and retention policy;
- whether first harness should run as an integration test category or a dedicated internal test executable;
- final approval evidence format;
- final Site allow-list mechanism;
- manual GET readback workflow for unknown outcome.

## 25. Requirements Traceability Summary

| Requirement area | Plan coverage |
| --- | --- |
| Current baseline | Section 3 |
| Authority boundaries | Section 4 |
| Non-goals | Section 5 |
| Options considered | Sections 6-11 |
| Comparison matrix | Section 12 |
| Security and authorization | Section 13 |
| Configuration guards | Section 14 |
| Evidence integration | Section 15 |
| Input and output models | Sections 16, 17 |
| Sensitive data exclusion | Section 18 |
| Operational safety | Section 19 |
| Audit/logging/evidence | Section 20 |
| Rollback/abort | Section 21 |
| Recommendation | Section 22 |
| Implementation sequence | Section 23 |
| Risks/open questions | Section 24 |

Recommended next task:

`feature/central-pms-pos-server-controlled-uat-application-harness`

Purpose: implement the first safe application-level UAT harness for invoking the controlled diagnostic seam using approved config/test data, without adding endpoint/tooling and without wiring payment/exit flows.
