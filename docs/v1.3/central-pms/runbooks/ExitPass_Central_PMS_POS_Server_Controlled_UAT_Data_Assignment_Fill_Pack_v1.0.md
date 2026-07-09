# ExitPass Central PMS POS Server Controlled UAT Data Assignment Fill Pack v1.0

## 1. Document Control

| Field | Value |
| --- | --- |
| Document | ExitPass Central PMS POS Server Controlled UAT Data Assignment Fill Pack |
| Version | v1.0 |
| Date | 2026-07-09 |
| Branch | `docs/controlled-uat-data-assignment-fill-pack` |
| Scope | Documentation-only field collection pack for controlled UAT data assignment |
| Source closure plan | `docs/v1.3/central-pms/runbooks/ExitPass_Central_PMS_POS_Server_Controlled_UAT_Data_Assignment_Blocker_Closure_Plan_v1.0.md` |

This fill pack is documentation-only. It does not modify source code, schema, tests, configuration, runtime state, Central PMS state, POS Server state, HikCentral state, payment provider state, fiscal state, ExitAuthorization state, gate state, refund/reversal state, rendering behavior, evidence files, or UAT runbooks.

No UAT scenarios were run while preparing this fill pack. No Central PMS, POS Server, HikCentral, or payment provider runtime endpoints were called.

## 2. Purpose

This fill pack helps a human collect and enter the real project values required to close blockers in the Controlled UAT Data Assignment Record.

Use it as a worksheet before editing the assignment record. It asks the concrete questions that must be answered, identifies who should answer them, gives acceptable value formats and examples, and states what evidence should be attached.

This pack does not approve UAT execution. It supports the next documentation step: filling the assignment record with real values and then creating a refreshed data assignment review.

## 3. How To Use This Fill Pack

1. Assign a fill coordinator, usually the UAT lead.
2. Work through each checklist group in order.
3. Replace every placeholder with a real project value, an approval reference, or `not_applicable_with_reason`.
4. Attach evidence references as document paths, ticket/change ids, approved screenshots, controlled local paths, or test output references.
5. Do not paste secrets, credentials, raw payment provider payloads, raw POS Server request/response bodies, PAN, CVV, unmanaged customer PII, or raw statutory evidence into this pack or the assignment record.
6. After collecting values, update the Controlled UAT Data Assignment Record.
7. Run the no-TBD review checklist in this pack.
8. Create a refreshed data assignment review before any execution planning continues.

Acceptable status values while filling:

- `open`
- `assigned_pending_evidence`
- `evidence_ready`
- `approved`
- `not_applicable_with_reason`
- `rejected`

No required field should remain `open`, `TBD`, blank, `not_started`, `incomplete`, or unapproved when submitted for data assignment review.

## 4. Field Collection Checklist

### 4.1 Owners And Approvals

| Field name | Question to ask | Who should answer | Acceptable format | Example value | Evidence to attach |
| --- | --- | --- | --- | --- | --- |
| UAT lead | Who owns the controlled UAT assignment package end to end? | Project sponsor or delivery lead | Person or group name | `Darwin Pasco` | Approval email, ticket, or signoff reference |
| Engineering lead | Who owns Central PMS code/config readiness and test evidence? | Engineering manager or tech lead | Person or group name | `Central PMS Engineering - Darwin Pasco` | Engineering signoff reference |
| POS Server owner | Who owns POS Server fiscal configuration and availability? | POS Server owner | Person or group name | `POS Server Owner - <name>` | POS Server owner signoff |
| Central PMS owner | Who owns Central PMS runtime environment readiness? | Central PMS owner | Person or group name | `Central PMS Owner - <name>` | Central PMS owner signoff |
| Site owner | Who approves the selected Site and Site POS Server mapping? | Site/business owner | Person or group name | `Site Owner - <name>` | Site approval reference |
| Operations lead | Who owns run window, support coverage, and operational stop criteria? | Operations lead | Person or group name | `Operations Lead - <name>` | Operations approval |
| Evidence owner | Who owns evidence folder, checksums, and reviewer handoff? | Evidence owner or QA lead | Person or group name | `Evidence Owner - <name>` | Evidence save approval |
| Privacy/compliance reviewer | Who confirms sensitive-data exclusions and fiscal-numbering risk posture? | Compliance/privacy reviewer | Person or group name | `Compliance Reviewer - <name>` | Sensitive-data checklist signoff |
| Rollback/support owner | Who is online and empowered to stop or roll back the run? | Operations/support lead | Person or group name plus contact path | `Support Owner - <name>, Teams bridge <ref>` | Support coverage reference |
| Run approval reference | What approval authorizes moving to readiness review? | UAT lead | Ticket/change id or approval id | `DEV-UAT-CPS-POS-001` | Approval record |
| Evidence save approval reference | What approval authorizes the evidence location and handling? | Evidence owner | Ticket/change id or approval id | `EVID-CPS-POS-UAT-001` | Evidence approval |
| Fiscal number allocation approval | If fiscal number allocation may occur, who accepts the risk and sequence impact? | POS Server owner and compliance reviewer | Approval id or `not_applicable_with_reason` | `NONPROD-FISCAL-ALLOC-001` | Fiscal numbering approval or non-production sequence evidence |

### 4.2 Environment

| Field name | Question to ask | Who should answer | Acceptable format | Example value | Evidence to attach |
| --- | --- | --- | --- | --- | --- |
| Environment name | What named environment will be used for this controlled UAT assignment? | UAT lead | Stable environment label | `DEV-CONTROLLED-UAT-LOCAL` | Environment inventory reference |
| Central PMS environment | Which Central PMS runtime will be used? | Central PMS owner | Environment label | `CentralPMS-DEV-DOCKER` | Runtime startup record or config reference |
| Central PMS base URL | What Central PMS base URL is expected for controlled local validation? | Central PMS owner | `http://host:port` or `https://host:port` | `http://localhost:5080` | Local runtime smoke or config reference |
| POS Server environment | Which POS Server runtime will be used? | POS Server owner | Environment label | `PoSServer-DEV-LOCAL` | POS Server startup/config reference |
| POS Server base URL | What POS Server URL will Central PMS use? | POS Server owner | URL reference without credentials | `http://host.docker.internal:8091` | Config reference with secrets redacted |
| Database/environment reference | Which DBs or environment aliases are approved for the run? | Engineering lead and POS Server owner | Database names or aliases | `centralpms_feq_retry_uat_local`, `posserver_api_smoke_validation_local` | DB safety confirmation |
| Production/non-production decision | Is this non-production? If not, where is production approval? | UAT lead and compliance reviewer | `Non-production` or approval reference | `Non-production` | Environment risk approval |
| Diagnostic window start | When may controlled diagnostic flags be enabled? | Operations lead | `YYYY-MM-DD HH:mm TZ` | `2026-07-10 14:00 PHT` | Approved run window |
| Diagnostic window end | When must controlled diagnostic flags be disabled? | Operations lead | `YYYY-MM-DD HH:mm TZ` | `2026-07-10 16:00 PHT` | Approved run window |
| Evidence save mode | Which evidence save mode is approved? | Evidence owner | `Mode A` or `Mode B` plus reason | `Mode B temporary controlled location` | Evidence handling approval |

### 4.3 Site / Site POS Server Assignment

| Field name | Question to ask | Who should answer | Acceptable format | Example value | Evidence to attach |
| --- | --- | --- | --- | --- | --- |
| Site id/ref | Which Site is approved for the controlled UAT run? | Site owner | GUID or stable symbolic ref | `DEV-SITE-ATC-001` | Site mapping evidence |
| Site name | What is the human-readable Site name? | Site owner | Site display name | `DEV Site - Alabang Town Center` | Site approval |
| Site group applicability | Is Site Group relevant for fiscal authority or reporting only? | Site owner | `not_applicable_with_reason` or group ref | `Not applicable for fiscal authority` | Site group decision |
| Site POS Server id/ref | Which Site POS Server mapping is approved? | POS Server owner | GUID or stable symbolic ref | `DEV-POS-SERVER-ATC-001` | POS Server mapping evidence |
| Site POS Server environment | Which POS Server environment serves this Site? | POS Server owner | Environment label | `PoSServer-DEV-LOCAL` | Environment reference |
| Site POS Server base URL | What URL reference maps this Site to POS Server? | POS Server owner | URL reference without credentials | `http://host.docker.internal:8091` | Config reference |
| Expected fiscal identity | Which fiscal identity should POS Server use for this Site? | POS Server owner | GUID or stable symbolic ref | `DEV-FISCAL-IDENTITY-ATC-001` | Fiscal identity evidence |
| Expected fiscal sequence policy | Which fiscal sequence policy applies? | POS Server owner | GUID or stable symbolic ref | `DEV-SI-SEQUENCE-POLICY-ATC-001` | Fiscal policy evidence |
| Expected fiscal sequence state | Which fiscal sequence state is expected to allocate numbers? | POS Server owner | GUID or stable symbolic ref | `DEV-SI-SEQUENCE-STATE-ATC-001` | Fiscal sequence state evidence |
| Site owner approval | Has the Site owner approved this mapping? | Site owner | Approval reference | `SITE-APPROVAL-001` | Approval record |
| POS Server owner approval | Has the POS Server owner approved this mapping? | POS Server owner | Approval reference | `POS-APPROVAL-001` | Approval record |

### 4.4 POS Server Fiscal Configuration

| Field name | Question to ask | Who should answer | Acceptable format | Example value | Evidence to attach |
| --- | --- | --- | --- | --- | --- |
| Fiscal identity id/ref | What fiscal identity will be used? | POS Server owner | GUID or stable symbolic ref | `DEV-FISCAL-IDENTITY-ATC-001` | Row/config existence evidence |
| Fiscal identity active/effective confirmation | Is the fiscal identity active and effective during the window? | POS Server owner | `Yes` plus evidence reference | `Yes - config snapshot POS-FISCAL-001` | Active/effective proof |
| Fiscal sequence policy id/ref | What sequence policy will be used? | POS Server owner | GUID or stable symbolic ref | `DEV-SI-SEQUENCE-POLICY-ATC-001` | Row/config existence evidence |
| Fiscal sequence policy active/effective confirmation | Is the sequence policy active and effective? | POS Server owner | `Yes` plus evidence reference | `Yes - config snapshot POS-FISCAL-002` | Active/effective proof |
| Fiscal sequence state id/ref | What sequence state will allocate the fiscal number? | POS Server owner | GUID or stable symbolic ref | `DEV-SI-SEQUENCE-STATE-ATC-001` | Row/config existence evidence |
| Fiscal sequence state configured confirmation | Is sequence state configured for safe non-production use? | POS Server owner | `Yes` plus evidence reference | `Yes - non-production sequence state` | Sequence state proof |
| Fiscal document type | What document type is being requested? | POS Server owner and engineering lead | Approved type key | `sales_invoice` | Document type mapping |
| Numbering consequence accepted | Has the team accepted fiscal number allocation consequences? | POS Server owner and compliance reviewer | `Yes` or `not_applicable_with_reason` | `Yes - non-production allocation accepted` | Approval reference |
| Idempotency behavior understood | Does the owner understand same-key same-hash behavior? | POS Server owner | `Yes` | `Yes` | Owner signoff |
| Replay behavior understood | Does the owner understand replay expectations? | POS Server owner | `Yes`, `Deferred`, or `not_applicable_with_reason` | `Deferred for first run` | Scenario decision |
| Conflict behavior understood | Does the owner understand conflict expectations? | POS Server owner | `Yes`, `Deferred`, or `not_applicable_with_reason` | `Deferred for first run` | Scenario decision |
| GET readback availability | Is manual readback available if later needed? | POS Server owner | `Available`, `Not available`, or `Deferred` | `Deferred - no automatic readback worker` | Readback posture note |
| Test/non-production sequence used | Is a non-production fiscal sequence used? | POS Server owner | `Yes` or approval reference | `Yes` | Non-production sequence evidence |
| POS Server final signoff | Has POS Server owner approved fiscal config? | POS Server owner | Approval reference | `POS-FISCAL-SIGNOFF-001` | Signoff record |

### 4.5 Central PMS Configuration

| Field name | Question to ask | Who should answer | Acceptable format | Example value | Evidence to attach |
| --- | --- | --- | --- | --- | --- |
| Fiscal reference persistence confirmed | Is Central PMS fiscal reference persistence available in the target environment? | Engineering lead | `Yes` plus evidence reference | `Yes - build/test evidence CPS-FISCAL-001` | Test output or implementation note |
| Repository/harness tests evidence | Which existing test output supports the harness path? | Engineering lead | Test command/result reference | `dotnet test ... Passed` | Test result record |
| Controlled UAT harness available | Is the application-level controlled UAT harness available? | Engineering lead | `Yes` plus reference | `Yes - harness planning v1.0` | Harness planning reference |
| Evidence exporter available | Is safe evidence export available? | Engineering lead | `Yes` plus reference | `Yes - evidence writer approval record` | Exporter evidence reference |
| Manual-save procedure available | Is manual-save procedure available? | Evidence owner | `Yes` plus reference | `Yes - manual save procedure v1.0` | Manual-save runbook |
| EnablePosServerFiscalIssuanceLiveCall intended value | What value is intended during the approved window? | Engineering lead | `true during approved window only` or `false` | `true during approved window only` | Config reference without secrets |
| EnableControlledUatDiagnosticPath intended value | What value is intended during the approved window? | Engineering lead | `true during approved window only` or `false` | `true during approved window only` | Config reference without secrets |
| Payment-flow guard false confirmation | Is payment-flow mutation disabled? | Engineering lead | `Yes - false` | `Yes - false` | Config evidence |
| Exit-flow guard false confirmation | Is ExitAuthorization/exit-flow mutation disabled? | Engineering lead | `Yes - false` | `Yes - false` | Config evidence |
| Fiscal gating enforcement false confirmation | Is fiscal gating enforcement disabled? | Engineering lead | `Yes - false` | `Yes - false` | Config evidence |
| No retry/readback worker confirmation | Are retry/readback workers out of scope and disabled? | Engineering lead | `Yes` | `Yes` | Config or service list evidence |
| No endpoint/CLI/tooling confirmation | Is there no public execution endpoint/tooling involved? | Engineering lead | `Yes` | `Yes` | Invocation surface reference |
| Engineering final signoff | Has engineering approved Central PMS assignment values? | Engineering lead | Approval reference | `CPS-ENG-SIGNOFF-001` | Signoff record |

### 4.6 HikCentral / Vendor PMS Session Source

| Field name | Question to ask | Who should answer | Acceptable format | Example value | Evidence to attach |
| --- | --- | --- | --- | --- | --- |
| Session source applicability | Is HikCentral/Vendor PMS involved in the assignment data source? | Operations lead | `Applicable` or `not_applicable_with_reason` | `Applicable - approved fixture only` | Source decision |
| Parking session source | Where does the parking session reference come from? | Operations lead and Site owner | Source label or fixture reference | `Approved static fixture` | Fixture approval |
| Approved test parking session ref | What parking session ref is approved? | Operations lead | Symbolic ref or GUID | `DEV-PARKING-SESSION-ATC-001` | Test data plan |
| HikCentral write posture | Will this run write to HikCentral? | Operations lead | Must be `No` | `No` | No-write acknowledgement |
| Vendor PMS owner approval | Has the source owner approved the fixture/reference? | Source owner | Approval reference or `not_applicable_with_reason` | `VENDOR-SOURCE-APPROVAL-001` | Approval record |

### 4.7 Payment / Payable / Reference Values

| Field name | Question to ask | Who should answer | Acceptable format | Example value | Evidence to attach |
| --- | --- | --- | --- | --- | --- |
| Run id | What unique run id identifies this assignment package? | UAT lead | `CPS-POS-UAT-YYYYMMDD-...` | `CPS-POS-UAT-20260710-DEV-ATC-001` | Run approval |
| Correlation id | What correlation id should be used for evidence traceability? | Engineering lead | GUID | `00000000-0000-4000-8000-000000000101` | Traceability note |
| Parking session ref | What parking session is tied to the test facts? | Operations lead | Symbolic ref or GUID | `DEV-PARKING-SESSION-ATC-001` | Approved fixture |
| Payment attempt ref | What payment attempt ref is approved? | UAT lead or payment owner | Symbolic ref or GUID | `DEV-PAYMENT-ATTEMPT-ATC-001` | Approved test data |
| Payment confirmation ref | What payment confirmation ref is approved? | Payment owner | Symbolic ref or GUID | `DEV-PAYMENT-CONFIRMATION-ATC-001` | Approved test data |
| Payable basis ref | What payable basis ref anchors totals? | Engineering lead | Symbolic ref or GUID | `DEV-PAYABLE-BASIS-ATC-001` | Payable basis fixture |
| Business day date | What business day should the fiscal request use? | UAT lead | `YYYY-MM-DD` | `2026-07-10` | Assignment approval |
| Currency code | What currency applies? | UAT lead/accounting reviewer | ISO currency code | `PHP` | Assignment approval |
| Amount minor units | What total amount in minor units applies? | Engineering lead/accounting reviewer | Integer | `10000` | Totals reconciliation |
| Expected run type | What scenario type is approved first? | UAT lead | `newly_created`, `replay`, `conflict`, or deferred | `newly_created` | Scenario approval |

### 4.8 Upstream Finality Reference

| Field name | Question to ask | Who should answer | Acceptable format | Example value | Evidence to attach |
| --- | --- | --- | --- | --- | --- |
| Upstream finality ref | What stable idempotency/finality reference will be used? | Engineering lead | `CPS-POS-UAT:<run-id>:<scenario>:<sequence>` | `CPS-POS-UAT:CPS-POS-UAT-20260710-DEV-ATC-001:newly_created:001` | Idempotency assignment |
| Pattern used | What pattern generated the finality reference? | Engineering lead | Pattern string | `CPS-POS-UAT:<run-id>:<scenario>:<sequence>` | Pattern approval |
| One semantic request confirmation | Is exactly one semantic request intended for the first run? | Engineering lead | `Yes` | `Yes` | Semantic request summary |
| Replay ref reuse confirmation | If replay is included, will the same finality ref and same semantic facts be reused? | Engineering lead | `Yes`, `Deferred`, or `not_applicable_with_reason` | `Not applicable - replay not in first run` | Scenario decision |
| Conflict bypass prohibition acknowledgement | Is bypassing conflict controls prohibited? | Engineering lead and POS Server owner | `Yes` | `Yes` | Owner acknowledgement |
| Assigned by | Who assigned this reference? | Engineering lead | Person name | `<name>` | Assignment note |
| Approved by | Who approved this reference? | UAT lead or engineering lead | Person name plus approval reference | `<name>, DEV-UAT-CPS-POS-001` | Approval record |

### 4.9 Fiscal Request Facts

| Field name | Question to ask | Who should answer | Acceptable format | Example value | Evidence to attach |
| --- | --- | --- | --- | --- | --- |
| Fiscal document type | What fiscal document type will be requested? | Engineering lead and POS Server owner | Approved type key | `sales_invoice` | POS Server mapping evidence |
| Business day date | What business day date appears in the request? | UAT lead | `YYYY-MM-DD` | `2026-07-10` | Assignment record |
| Site ref | Which Site ref appears in request facts? | Site owner | Same as approved Site ref | `DEV-SITE-ATC-001` | Site mapping |
| Site POS Server ref | Which Site POS Server ref appears in request facts? | POS Server owner | Same as approved POS Server ref | `DEV-POS-SERVER-ATC-001` | POS mapping |
| Parking session ref | Which parking session ref appears in request facts? | Operations lead | Approved symbolic ref or GUID | `DEV-PARKING-SESSION-ATC-001` | Test data plan |
| Payment refs | Which payment attempt and confirmation refs appear? | Payment owner | Approved symbolic refs or GUIDs | `DEV-PAYMENT-ATTEMPT-ATC-001`, `DEV-PAYMENT-CONFIRMATION-ATC-001` | Test data plan |
| Payable basis ref | Which payable basis ref appears? | Engineering lead | Approved symbolic ref or GUID | `DEV-PAYABLE-BASIS-ATC-001` | Payable fixture |
| Upstream finality ref | Which upstream finality ref appears? | Engineering lead | Approved finality ref | `CPS-POS-UAT:...:001` | Idempotency assignment |
| Currency | What currency appears? | Accounting reviewer | ISO currency code | `PHP` | Totals approval |
| Amount minor units | What amount appears? | Accounting reviewer | Integer minor units | `10000` | Totals reconciliation |
| Line count | How many fiscal lines are expected? | Engineering lead | Integer | `1` | Line summary |
| Tender count | How many tenders are expected? | Engineering lead | Integer | `1` | Tender summary |
| Tax detail presence | Are tax details present? | Accounting reviewer | `Yes` or `No` with reason | `Yes - zero tax detail` | Tax summary |
| Totals presence | Are request totals present and reconciled? | Accounting reviewer | `Yes` | `Yes` | Totals reconciliation |
| Correlation id | Which correlation id ties this request to evidence? | Engineering lead | GUID | `00000000-0000-4000-8000-000000000101` | Traceability note |

### 4.10 Line / Tender / Tax / Totals Facts

| Field name | Question to ask | Who should answer | Acceptable format | Example value | Evidence to attach |
| --- | --- | --- | --- | --- | --- |
| Line summary | What line item summary will be sent? | Engineering lead | Short structured summary | `1 parking fee line, PHP 100.00` | Line fact sheet |
| Line amount total | What is total line amount in minor units? | Accounting reviewer | Integer minor units | `10000` | Reconciliation sheet |
| Tender summary | What tender summary will be sent? | Engineering lead | Short structured summary | `1 cash/test tender, PHP 100.00` | Tender fact sheet |
| Tender amount total | What is total tender amount in minor units? | Accounting reviewer | Integer minor units | `10000` | Reconciliation sheet |
| Tax detail summary | What tax detail summary will be sent? | Accounting reviewer | Short structured summary | `Tax amount 0, PHP` | Tax fact sheet |
| Tax amount total | What is total tax amount in minor units? | Accounting reviewer | Integer minor units | `0` | Reconciliation sheet |
| Grand total | What is the final grand total in minor units? | Accounting reviewer | Integer minor units | `10000` | Reconciliation sheet |
| Totals match payable basis | Do line, tender, tax, and grand totals match payable basis? | Accounting reviewer | `Yes` | `Yes` | Reconciliation signoff |
| Sensitive data excluded | Do line/tender/tax facts exclude sensitive data? | Privacy/compliance reviewer | `Yes` | `Yes` | Sensitive-data checklist |
| Approval reference | Who approved these facts? | Accounting reviewer or UAT lead | Approval id | `TOTALS-APPROVAL-001` | Approval record |

### 4.11 Evidence Folder / Path

| Field name | Question to ask | Who should answer | Acceptable format | Example value | Evidence to attach |
| --- | --- | --- | --- | --- | --- |
| Save mode | Which save mode will be used? | Evidence owner | `Mode A` or `Mode B` | `Mode B` | Evidence handling approval |
| Target location reference | Where will evidence be saved? | Evidence owner | Controlled path or repository path, no secrets | `D:\ExitPass-UAT-Evidence\CPS-POS-UAT-20260710-DEV-ATC-001` | Folder creation proof |
| Evidence owner | Who can write/read the evidence location? | Evidence owner | Person or group name | `<name>` | Access confirmation |
| Hash/checksum required | Is checksum required? | Evidence owner | `Yes` or `No with reason` | `Yes` | Evidence governance decision |
| Hash/checksum command | What command or method computes checksum? | Evidence owner | Command reference | `Get-FileHash -Algorithm SHA256 <file>` | Procedure reference |
| Ticket/change linkage | Which ticket/change links evidence to approval? | UAT lead | Ticket/change id | `DEV-UAT-CPS-POS-001` | Ticket link |
| Reviewer signoff path | Where will reviewer signoff be recorded? | Evidence owner | Document path or ticket | `docs/.../review.md` | Reviewer assignment |
| Temporary local handling owner | If temporary local handling is used, who owns cleanup? | Evidence owner | Person name or `not_applicable_with_reason` | `<name>` | Cleanup responsibility |
| Evidence approval reference | What approval authorizes the evidence path? | Evidence owner | Approval id | `EVID-CPS-POS-UAT-001` | Approval record |

### 4.12 Sensitive-Data / Privacy Checks

| Field name | Question to ask | Who should answer | Acceptable format | Example value | Evidence to attach |
| --- | --- | --- | --- | --- | --- |
| No PAN | Does the dataset exclude PAN/card numbers? | Privacy/compliance reviewer | `Yes` | `Yes` | Checklist signoff |
| No CVV | Does the dataset exclude CVV? | Privacy/compliance reviewer | `Yes` | `Yes` | Checklist signoff |
| No tokens | Does the dataset exclude sensitive tokens? | Privacy/compliance reviewer | `Yes` | `Yes` | Checklist signoff |
| No credentials/secrets | Does the dataset exclude credentials and secrets? | Privacy/compliance reviewer | `Yes` | `Yes` | Checklist signoff |
| No raw provider callbacks | Does evidence exclude raw payment provider callbacks? | Privacy/compliance reviewer | `Yes` | `Yes` | Checklist signoff |
| No raw entitlement evidence | Does evidence exclude raw entitlement/statutory evidence? | Privacy/compliance reviewer | `Yes` | `Yes` | Checklist signoff |
| No uncontrolled files/images | Are uncontrolled images/files excluded? | Evidence owner | `Yes` | `Yes` | Evidence review |
| No unmanaged PII | Is unmanaged customer PII excluded? | Privacy/compliance reviewer | `Yes` | `Yes` | Checklist signoff |
| No free-form sensitive blobs | Are free-form payload blobs excluded? | Engineering lead | `Yes` | `Yes` | Request/evidence review |
| Plate/ticket masking decision | Are plate/ticket values masked, synthetic, or explicitly approved? | Privacy/compliance reviewer | `Masked`, `Synthetic`, or approval ref | `Synthetic` | Data classification note |

### 4.13 Rollback / Stop Criteria

| Field name | Question to ask | Who should answer | Acceptable format | Example value | Evidence to attach |
| --- | --- | --- | --- | --- | --- |
| Stop criteria | What exact conditions stop the run? | Operations lead | Bulleted criteria | `Unexpected POS mutation, wrong DB, wrong sequence, forbidden side effect` | Stop checklist |
| Rollback/support owner | Who is online and empowered to stop the run? | Operations lead | Person/group plus contact | `<name>, Teams bridge <ref>` | Support coverage |
| Owner availability window | When is support coverage active? | Operations lead | `YYYY-MM-DD HH:mm-HH:mm TZ` | `2026-07-10 14:00-16:00 PHT` | Run window approval |
| Forbidden side-effect checks | What checks prove no forbidden side effects? | Engineering lead | Query/checklist reference | `exit_authorizations=0, gate_events=0, refunds=0` | Side-effect check plan |
| Local DB safety | How is DB safety/disposability proven? | Engineering lead | DB name plus proof reference | `centralpms_feq_retry_uat_local, disposable` | DB safety evidence |
| Production sequence exclusion | How is production fiscal sequence excluded? | POS Server owner | `Yes` or explicit approval reference | `Yes - non-production sequence` | Fiscal config evidence |
| Abort procedure | How does the operator abort if stop criteria trigger? | Operations lead | Procedure reference | `Stop services and preserve evidence package` | Abort checklist |
| Escalation contact | Who is contacted on stop/abort? | Operations lead | Contact path or bridge | `Teams bridge <ref>` | Escalation plan |

## 5. No-TBD Review Checklist

Before updating the assignment record, verify:

| Check | Pass Criteria |
| --- | --- |
| No `TBD` values | Search the filled package and assignment record for `TBD`; none remain in required fields. |
| No blank required fields | Every required owner, environment, Site, POS, Central PMS, payment/payable, finality, fiscal fact, evidence, privacy, and stop field has a value. |
| No `not_started` required statuses | Required fields are not left as `not_started`. |
| No `incomplete` required statuses | Required fields are not left as `incomplete`. |
| `not_applicable_with_reason` is justified | Every not-applicable item includes a short reason and owner. |
| Owners named | Every required owner role has a person or group name. |
| Approvals linked | Every required approval field has an approval reference. |
| Evidence linked | Every required evidence field has a document path, ticket id, screenshot reference, or controlled file path. |
| Sensitive data excluded | Privacy checklist is complete and exceptions are approved. |
| Runtime calls not performed | The fill process did not call Central PMS, POS Server, HikCentral, or payment provider runtime endpoints. |

## 6. Ready For Data Assignment Review Checklist

The package is ready for a refreshed data assignment review only when:

| Gate | Required Result |
| --- | --- |
| Assignment record updated | Real project values are entered into the Controlled UAT Data Assignment Record. |
| Owners approved | Required owner and approval rows are complete. |
| Environment approved | Non-production environment, DB references, base URLs, and window are assigned. |
| Site/POS mapping approved | Site, Site POS Server, fiscal identity, policy, and sequence state are assigned and evidenced. |
| Central PMS config assigned | Controlled flags and forbidden guards are documented. |
| Session/payment/payable refs assigned | Parking, payment, confirmation, payable basis, run id, and correlation id are filled. |
| Upstream finality assigned | Finality ref pattern and semantic request confirmation are complete. |
| Fiscal facts reconciled | Request facts, lines, tenders, taxes, totals, and payable basis reconcile. |
| Evidence path ready | Evidence location exists or is approved for creation before execution. |
| Privacy complete | Sensitive-data/privacy checks pass. |
| Stop criteria ready | Stop owner, criteria, side-effect checks, and escalation path are assigned. |
| Review input complete | The evidence package is attached or referenced for reviewers. |

Submitting the package for review does not authorize execution. It only supports a refreshed data assignment review.

## 7. Explicit Non-Goals

This fill pack does not:

- execute UAT;
- call runtime endpoints;
- call Central PMS runtime endpoints;
- call POS Server runtime endpoints;
- call HikCentral runtime endpoints;
- call payment provider runtime endpoints;
- create fiscal issuance;
- confirm payment;
- mutate POS Server;
- write to HikCentral;
- trigger gate behavior;
- create refund/reversal;
- generate PDF;
- generate HTML;
- generate QR;
- define final BIR statutory wording;
- create payment, fiscal, gate, refund/reversal, or rendering transactions;
- modify source code;
- modify schema;
- modify tests.

## 8. Recommended Next Step

Recommended next step:

1. Use this fill pack as the interview worksheet for UAT, engineering, POS Server, Site, operations, evidence, and privacy owners.
2. Update the Controlled UAT Data Assignment Record with the real project values and evidence references.
3. Run the no-TBD review checklist.
4. Create a refreshed Controlled UAT Data Assignment Review.
5. Continue only if the refreshed review accepts the assignment package and advances the readiness posture.
