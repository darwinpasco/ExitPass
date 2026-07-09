# ExitPass Central PMS POS Server Controlled UAT Data Assignment Record Filled Draft v1.0

## 1. Document Control

| Field | Value |
| --- | --- |
| Document | ExitPass Central PMS POS Server Controlled UAT Data Assignment Record Filled Draft |
| Version | v1.0 |
| Date | 2026-07-09 |
| Scope | Filled data assignment package for refreshed Controlled UAT Data Assignment Review |
| Source fill pack | `ExitPass_Central_PMS_POS_Server_Controlled_UAT_Data_Assignment_Fill_Pack_v1.0.md` |
| Readiness posture | `ready_for_data_assignment_review` |
| Execution posture | `execution_not_authorized` |

This file is a filled assignment package for review. It does not authorize UAT execution. It does not run UAT scenarios, call runtime endpoints, create fiscal issuance, verify payment, mutate POS Server, write to HikCentral, issue ExitAuthorization, trigger gate behavior, create refund/reversal, or generate PDF/HTML/QR artifacts.

## 2. Owners And Approvals

| Field | Final value |
| --- | --- |
| UAT lead | Darwin Pasco |
| Engineering lead | Darwin Pasco / Central PMS Engineering |
| POS Server owner | Calvin Garcia |
| Central PMS owner | Darwin Pasco / Central PMS Engineering |
| Site owner | Calvin Garcia |
| Operations lead | Calvin Garcia |
| Evidence owner | Darwin Pasco |
| Privacy/compliance reviewer | Darwin Pasco |
| Rollback/support owner | Darwin Pasco, email |
| Run approval reference | `DEV-UAT-CPS-POS-001` |
| Evidence save approval reference | `EVID-CPS-POS-UAT-001` |
| Fiscal number allocation approval | `NONPROD-FISCAL-ALLOC-001` |

## 3. Environment Assignment

| Field | Final value |
| --- | --- |
| Environment name | `DEV-CONTROLLED-UAT-LOCAL` |
| Central PMS environment | `CentralPMS-DEV-LOCAL` |
| Central PMS base URL | `http://localhost:56065` |
| Central PMS HTTPS URL | `https://localhost:56064` |
| POS Server environment | `PoSServer-DEV-LOCAL` |
| POS Server base URL | `http://localhost:5000` |
| Database/environment reference | `Central PMS: exitpass_v12_dev; POS Server: posserver_api_smoke_validation_local; non-production/disposable local validation databases` |
| Production/non-production decision | `Non-production` |
| Diagnostic window start | `July 9, 2026 1:00 PM PHT` |
| Diagnostic window end | `July 9, 2026 3:00 PM PHT` |
| Evidence save mode | `Mode B temporary controlled location` |

## 4. Site / Site POS Server Assignment

| Field | Final value |
| --- | --- |
| Site id/ref | `DEV-SITE-ATC-001` |
| Site name | `DEV Site - Alabang Town Center` |
| Site group applicability | `not_applicable_with_reason: Site group is reporting and scope context only; fiscal authority is Site/Site POS Server scoped for this controlled assignment.` |
| Site POS Server id/ref | `DEV-POS-SERVER-ATC-001` |
| Site POS Server environment | `PoSServer-DEV-LOCAL` |
| Site POS Server base URL | `http://localhost:5000` |
| Expected fiscal identity | `DEV-FISCAL-IDENTITY-ATC-001` |
| Expected fiscal sequence policy | `DEV-SI-SEQUENCE-POLICY-ATC-001` |
| Expected fiscal sequence state | `DEV-SI-SEQUENCE-STATE-ATC-001` |
| Site owner approval | `SITE-APPROVAL-001` |
| POS Server owner approval | `POS-APPROVAL-001` |

## 5. POS Server Fiscal Configuration

| Field | Final value |
| --- | --- |
| Fiscal identity id/ref | `DEV-FISCAL-IDENTITY-ATC-001` |
| Fiscal identity active/effective check | `Yes - non-production fiscal identity assigned for this controlled assignment.` |
| Fiscal sequence policy id/ref | `DEV-SI-SEQUENCE-POLICY-ATC-001` |
| Fiscal sequence policy active/effective check | `Yes - non-production fiscal sequence policy assigned for this controlled assignment.` |
| Fiscal sequence state id/ref | `DEV-SI-SEQUENCE-STATE-ATC-001` |
| Fiscal sequence state configured check | `Yes - non-production sequence state assigned for this controlled assignment.` |
| Fiscal document type | `sales_invoice` |
| Numbering consequence accepted | `Yes - non-production allocation accepted under NONPROD-FISCAL-ALLOC-001.` |
| Idempotency behavior understood | `Yes` |
| Replay behavior understood | `Deferred for first execution unless explicitly included in a later approved scenario.` |
| Conflict behavior understood | `Deferred for first execution unless explicitly included in a later approved scenario.` |
| GET readback availability | `Deferred; no automatic readback worker is part of this assignment.` |
| Test/non-production sequence used | `Yes - non-production sequence only.` |
| POS Server final signoff | `POS-FISCAL-SIGNOFF-001` |

## 6. Central PMS Configuration

| Field | Final value |
| --- | --- |
| Fiscal reference persistence verifyed | `Yes - based on merged fiscal status visibility/read-model implementation and focused validation evidence.` |
| Repository/harness tests evidence | `Central PMS focused unit tests passed: 34 tests.` |
| Controlled UAT harness available | `Yes - controlled diagnostic path only.` |
| Evidence exporter available | `Manual evidence save only unless exporter is explicitly approved in a later slice.` |
| Manual-save procedure available | `Yes - use evidence folder/path and SHA-256 checksum procedure.` |
| EnablePosServerFiscalIssuanceLiveCall intended value | `true during approved diagnostic window only` |
| EnableControlledUatDiagnosticPath intended value | `true during approved diagnostic window only` |
| Payment-flow guard false check | `Yes - false` |
| Exit-flow guard false check | `Yes - false` |
| Fiscal gating enforcement false check | `Yes - false` |
| No retry/readback worker check | `Yes` |
| No endpoint/CLI/tooling check | `Yes - no public execution endpoint/tooling.` |
| Engineering final signoff | `CPS-ENG-SIGNOFF-001` |

## 7. HikCentral / Vendor PMS Session Source

| Field | Final value |
| --- | --- |
| Session source applicability | `Applicable only as approved static/reference fixture; no HikCentral write.` |
| Parking session source | `Approved static fixture.` |
| Approved test parking session ref | `DEV-PARKING-SESSION-ATC-001` |
| HikCentral write posture | `No` |
| Vendor PMS owner approval | `VENDOR-SOURCE-APPROVAL-001` |

## 8. Payment / Payable / Reference Values

| Field | Final value |
| --- | --- |
| Run id | `CPS-POS-UAT-20260709-DEV-ATC-001` |
| Correlation id | `b7b4cbea-0c8c-4d06-9f6f-728a0a3fc2df` |
| Parking session ref | `DEV-PARKING-SESSION-ATC-001` |
| Payment attempt ref | `DEV-PAYMENT-ATTEMPT-ATC-001` |
| Payment finality record ref | `DEV-PAYMENT-FINALITY-ATC-001` |
| Payable basis ref | `DEV-PAYABLE-BASIS-ATC-001` |
| Business day date | `2026-07-09` |
| Currency code | `PHP` |
| Amount minor units | `10000` |
| Expected run type | `newly_created` |

## 9. Upstream Finality Reference

| Field | Final value |
| --- | --- |
| Upstream finality ref | `CPS-POS-UAT:CPS-POS-UAT-20260709-DEV-ATC-001:newly_created:001` |
| Pattern used | `CPS-POS-UAT:<run-id>:<scenario>:<sequence>` |
| One semantic request check | `Yes` |
| Replay ref reuse check | `not_applicable_with_reason: Replay is not in the first execution scenario.` |
| Conflict bypass prohibition acknowledgement | `Yes` |
| Assigned by | `Darwin Pasco` |
| Approved by | `Darwin Pasco / Central PMS Engineering` |

## 10. Fiscal Request Facts

| Field | Final value |
| --- | --- |
| Fiscal document type | `sales_invoice` |
| Business day date | `2026-07-09` |
| Site ref | `DEV-SITE-ATC-001` |
| Site POS Server ref | `DEV-POS-SERVER-ATC-001` |
| Parking session ref | `DEV-PARKING-SESSION-ATC-001` |
| Payment refs | `DEV-PAYMENT-ATTEMPT-ATC-001`, `DEV-PAYMENT-FINALITY-ATC-001` |
| Payable basis ref | `DEV-PAYABLE-BASIS-ATC-001` |
| Upstream finality ref | `CPS-POS-UAT:CPS-POS-UAT-20260709-DEV-ATC-001:newly_created:001` |
| Currency | `PHP` |
| Amount minor units | `10000` |
| Line count | `1` |
| Tender count | `1` |
| Tax detail presence | `Yes - zero tax detail.` |
| Totals presence | `Yes` |
| Correlation id | `b7b4cbea-0c8c-4d06-9f6f-728a0a3fc2df` |

## 11. Line / Tender / Tax / Totals Facts

| Field | Final value |
| --- | --- |
| Line summary | `1 parking fee line, PHP 100.00` |
| Line amount total | `10000` |
| Tender summary | `1 test tender, PHP 100.00` |
| Tender amount total | `10000` |
| Tax detail summary | `Tax amount 0, PHP` |
| Tax amount total | `0` |
| Grand total | `10000` |
| Totals match payable basis | `Yes` |
| Sensitive data excluded | `Yes` |
| Approval reference | `TOTALS-APPROVAL-001` |

## 12. Evidence Folder / Path

| Field | Final value |
| --- | --- |
| Save mode | `Mode B` |
| Target location reference | `D:\ExitPass-UAT-Evidence\CPS-POS-UAT-20260709-DEV-ATC-001` |
| Evidence owner | `Darwin Pasco` |
| Hash/checksum required | `Yes` |
| Hash/checksum command | `Get-FileHash -Algorithm SHA256 <file>` |
| Ticket/change linkage | `DEV-UAT-CPS-POS-001` |
| Reviewer signoff path | `docs/v1.3/central-pms/runbooks/ExitPass_Central_PMS_POS_Server_Controlled_UAT_Data_Assignment_Review_Refresh_v1.0.md` |
| Temporary local handling owner | `Darwin Pasco` |
| Evidence approval reference | `EVID-CPS-POS-UAT-001` |

## 13. Sensitive-Data / Privacy Checks

| Field | Final value |
| --- | --- |
| No PAN | `Yes` |
| No CVV | `Yes` |
| No tokens | `Yes` |
| No credentials/secrets | `Yes` |
| No raw provider callbacks | `Yes` |
| No raw entitlement evidence | `Yes` |
| No uncontrolled files/images | `Yes` |
| No unmanaged PII | `Yes` |
| No free-form sensitive blobs | `Yes` |
| Plate/ticket masking decision | `Synthetic` |

## 14. Rollback / Stop Criteria

| Field | Final value |
| --- | --- |
| Stop criteria | `Unexpected POS mutation; wrong DB; wrong site/POS Server mapping; wrong fiscal sequence; unexpected payment mutation; unexpected ExitAuthorization; unexpected gate event; evidence save failure; sensitive-data exposure.` |
| Rollback/support owner | `Darwin Pasco, email` |
| Owner availability window | `July 9, 2026 1:00 PM-3:00 PM PHT` |
| Forbidden side-effect checks | `exit_authorizations=0; gate_events=0; refund/reversal records=0; payment mutation outside approved fixture=0` |
| Local DB safety | `Central PMS: exitpass_v12_dev; POS Server: posserver_api_smoke_validation_local; non-production/disposable local validation databases` |
| Production sequence exclusion | `Yes - non-production sequence only.` |
| Abort procedure | `Stop services, preserve logs/evidence, capture checksums, do not retry without refreshed approval.` |
| Escalation contact | `Darwin Pasco, email` |

## 15. No-placeholder Review Result

| Check | Result |
| --- | --- |
| No placeholder values in required fields | `Pass` |
| No blank required fields | `Pass` |
| No unstarted required statuses | `Pass` |
| No unfinished required statuses | `Pass` |
| `not_applicable_with_reason` justified | `Pass` |
| Owners named | `Pass` |
| Approvals linked | `Pass` |
| Evidence linked | `Pass` |
| Sensitive data excluded | `Pass` |
| Runtime calls not performed by this record | `Pass` |

## 16. Ready For Data Assignment Review Checklist

| Gate | Result |
| --- | --- |
| Assignment record filled | `Ready for review` |
| Owners approved | `Ready for review` |
| Environment approved | `Ready for review` |
| Site/POS mapping approved | `Ready for review` |
| Central PMS config assigned | `Ready for review` |
| Session/payment/payable refs assigned | `Ready for review` |
| Upstream finality assigned | `Ready for review` |
| Fiscal facts reconciled | `Ready for review` |
| Evidence path ready | `Ready for review` |
| Privacy complete | `Ready for review` |
| Stop criteria ready | `Ready for review` |
| Review input complete | `Ready for review` |

## 17. Explicit Non-Goals

This filled record does not:

- execute UAT;
- call runtime endpoints;
- call Central PMS runtime endpoints;
- call POS Server runtime endpoints;
- call HikCentral runtime endpoints;
- call payment provider runtime endpoints;
- create fiscal issuance;
- verify payment;
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

## 18. Recommended Next Step

Create a refreshed Controlled UAT Data Assignment Review using this filled package as input. The next review should decide whether this package advances from `ready_for_data_assignment_review` to the next allowed readiness state.
