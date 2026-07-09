# ExitPass Central PMS POS Server Controlled UAT Conflict Result v1.0

## 1. Execution Timestamp

| Field | Value |
| --- | --- |
| Execution started | `2026-07-09T18:12:03.3413553+08:00` |
| Execution completed | `2026-07-09T18:12:13.3372321+08:00` |
| DB verification observed at | `2026-07-09T18:16:58.0980165+08:00` |
| Scenario | `conflict` |
| Final result | `passed` |

## 2. Commands / Procedure Used

Procedure summary:

1. Verified branch `feature/controlled-uat-conflict-scenario`.
2. Implemented narrow Central PMS controlled UAT conflict handling for the approved July 9 non-production fixture.
3. Ran focused Central PMS unit tests for controlled UAT invocation, fixture store, and fiscal issuance orchestration behavior.
4. Created the conflict request evidence file under:
   - `D:\ExitPass-UAT-Evidence\CPS-POS-UAT-20260709-DEV-ATC-001`
5. Built Central PMS:
   - `dotnet build src\Services\CentralPms\src\ExitPass.CentralPms.Api\ExitPass.CentralPms.Api.csproj`
6. Built POS Server:
   - `dotnet build D:\SourceCodes\ExitPass-PoSServer\src\ExitPass.PosServer.Api\ExitPass.PosServer.Api.csproj`
7. Started POS Server locally on:
   - `http://localhost:5000`
8. Started Central PMS locally on:
   - `http://localhost:56065`
9. Submitted the approved controlled UAT conflict request once to:
   - `POST http://localhost:56065/internal/controlled-uat/fiscal-issuance/run`
10. Stopped the local Central PMS and POS Server processes.
11. Ran read-only DB verification for fiscal reference, POS fiscal document, fiscal sequence, and side-effect counts.
12. Generated SHA-256 checksums for saved evidence files.

No replay batch, second newly-created run, payment provider call, HikCentral write, ExitAuthorization, gate, refund/reversal, or rendering scenario was executed.

## 3. Request Facts

| Field | Value |
| --- | --- |
| Run id | `CPS-POS-UAT-20260709-DEV-ATC-001` |
| Scenario | `conflict` |
| Expected run type | `conflict` |
| Conflict included | `true` |
| Central PMS URL | `http://localhost:56065` |
| POS Server URL | `http://localhost:5000` |
| Site | `DEV-SITE-ATC-001` |
| Site POS Server | `DEV-POS-SERVER-ATC-001` |
| Fiscal document type | `sales_invoice` |
| Original approved amount | PHP 100.00 / `10000` minor units |
| Conflict amount | PHP 100.01 / `10001` minor units |
| Upstream finality ref | `CPS-POS-UAT:CPS-POS-UAT-20260709-DEV-ATC-001:newly_created:001` |
| Correlation id | `b7b4cbea-0c8c-4d06-9f6f-728a0a3fc2df` |
| Business day date | `2026-07-09` |
| Parking session ref | `DEV-PARKING-SESSION-ATC-001` |
| Payment attempt ref | `DEV-PAYMENT-ATTEMPT-ATC-001` |
| Payment finality / confirmation ref | `DEV-PAYMENT-FINALITY-ATC-001` |
| Payable basis ref | `DEV-PAYABLE-BASIS-ATC-001` |
| Evidence reference | `DEV-UAT-CPS-POS-001-CONFLICT` |

The request used the same upstream finality reference as the already-passed `newly_created` run and changed exactly one semantic fiscal fact: amount from `10000` to `10001` minor units.

## 4. Conflict Result

| Field | Value |
| --- | --- |
| HTTP status | `409` |
| Response status | `conflict_failure_mapped` |
| Accepted | `false` |
| Validation passed | `false` |
| Diagnostic invoked | `false` |
| POS Server fiscal document creation attempted | `false` |
| Diagnostic status | `conflict_failure_mapped` |
| Error code | `controlled_semantic_conflict_detected` |
| Error posture | `DoNotRetryWithoutRequestChange` |
| Response errors | `controlled_semantic_conflict_detected`, `amount_minor_units_conflict` |
| Central PMS fiscal state | `FiscalIssuanceRecorded` |
| Sensitive data excluded | `true` |

The conflict failed closed before diagnostic invocation and before any POS Server fiscal document creation attempt.

## 5. Fiscal Issuance Reference Id

`14479d9a-844f-4dba-9578-e863ece93fbf`

Read-only DB verification showed exactly one active Central PMS fiscal issuance reference for upstream finality ref `CPS-POS-UAT:CPS-POS-UAT-20260709-DEV-ATC-001:newly_created:001`.

## 6. POS Server Fiscal Document Id / Number

| Field | Value |
| --- | --- |
| POS Server fiscal document id | `9bdf2948-dadd-450b-8776-be688b579395` |
| Fiscal document number | `SI-00000002-UAT` |
| Fiscal sequence value | `2` |
| Fiscal series | `central-pms-uat-si-sequence-policy` |
| Fiscal number prefix/suffix | `SI-` / `-UAT` |

Read-only POS Server DB verification showed exactly one fiscal document for the approved payment finality ref. The conflict did not allocate a new fiscal document number.

## 7. Fiscal Sequence Before / After

| Check | Value |
| --- | --- |
| Before conflict, from passed run evidence | `2` |
| After conflict, from read-only DB verification | `2` |
| New fiscal number allocated | No |

## 8. Side-Effect Checks

Read-only Central PMS DB side-effect counts for correlation id `b7b4cbea-0c8c-4d06-9f6f-728a0a3fc2df`:

| Check | Count |
| --- | ---: |
| `core.exit_authorizations` | `0` |
| `gates.gate_authorization_consumptions` | `0` |
| `gates.gate_events` | `0` |
| `operations.manual_gate_logs` | `0` |

No payment provider call, HikCentral write, ExitAuthorization, gate behavior, refund/reversal, PDF/HTML/QR generation, or final BIR statutory wording was introduced.

## 9. Evidence Files And Checksums

Evidence folder:

```text
D:\ExitPass-UAT-Evidence\CPS-POS-UAT-20260709-DEV-ATC-001
```

| Evidence file | Purpose |
| --- | --- |
| `controlled-posserver-fiscal-uat-CPS-POS-UAT-20260709-DEV-ATC-001-conflict-request.json` | Approved conflict request body |
| `controlled-posserver-fiscal-uat-CPS-POS-UAT-20260709-DEV-ATC-001-conflict-run-response.json` | Central PMS conflict response |
| `controlled-posserver-fiscal-uat-CPS-POS-UAT-20260709-DEV-ATC-001-conflict-execution-summary.json` | Conflict execution summary |
| `controlled-posserver-fiscal-uat-CPS-POS-UAT-20260709-DEV-ATC-001-conflict-db-verification.json` | Read-only DB verification |
| `controlled-posserver-fiscal-uat-CPS-POS-UAT-20260709-DEV-ATC-001-conflict-hash.txt` | SHA-256 checksums |
| `central-pms-conflict-build.log` | Central PMS build output |
| `pos-server-conflict-build.log` | POS Server build output |
| `central-pms-conflict-stdout.log` | Central PMS runtime stdout capture |
| `central-pms-conflict-stderr.log` | Central PMS runtime stderr capture |
| `pos-server-conflict-stdout.log` | POS Server runtime stdout capture |
| `pos-server-conflict-stderr.log` | POS Server runtime stderr capture |

```text
0D4C9E6C2C7EBD799BFABF96C39A0C72750A0537790F860CF82DDDF72AA396BD  controlled-posserver-fiscal-uat-CPS-POS-UAT-20260709-DEV-ATC-001-conflict-request.json
1A1176304FDD91706A0C20BDA0D46E8380735129E7A602FAB96BFFF2AF6B9752  controlled-posserver-fiscal-uat-CPS-POS-UAT-20260709-DEV-ATC-001-conflict-run-response.json
D025D173BB5F8C7CDD8E7D03379487209064CEBD4CE5C560C272C82B47B26017  controlled-posserver-fiscal-uat-CPS-POS-UAT-20260709-DEV-ATC-001-conflict-execution-summary.json
37E8ABC60B231F924966FBADA6C27474C91FA923CD594B7426213587CBE0F5CD  controlled-posserver-fiscal-uat-CPS-POS-UAT-20260709-DEV-ATC-001-conflict-db-verification.json
0401A4CB8E259DED71C1E6A7F7A5554D84ADF4021A5A0199D64E57432C7CCC61  central-pms-conflict-build.log
261DF021684EF2C5ECFBB3B7B0442BBEEC6DEDE30757E8EB037DC667AE048A45  pos-server-conflict-build.log
F1945CD6C19E56B3C1C78943EF5EC18116907A4CA1EFC40A57D48AB1DB7ADFC5  central-pms-conflict-stdout.log
F1945CD6C19E56B3C1C78943EF5EC18116907A4CA1EFC40A57D48AB1DB7ADFC5  central-pms-conflict-stderr.log
F1945CD6C19E56B3C1C78943EF5EC18116907A4CA1EFC40A57D48AB1DB7ADFC5  pos-server-conflict-stdout.log
F1945CD6C19E56B3C1C78943EF5EC18116907A4CA1EFC40A57D48AB1DB7ADFC5  pos-server-conflict-stderr.log
```

## 10. Final Result

Final result: `passed`

The approved conflict scenario failed closed as expected. It reported the existing fiscal issuance evidence, did not create a second Central PMS fiscal issuance reference, did not call POS Server for fiscal document creation, and did not allocate a new fiscal number.

## 11. Boundary Statement

During this conflict scenario:

- no second fiscal issuance reference was created for the approved upstream finality ref;
- no second POS Server fiscal document was created for the approved payment finality ref;
- no new fiscal number was allocated;
- no ExitAuthorization was issued;
- no gate behavior was triggered;
- no refund/reversal was created;
- no payment provider was called;
- no HikCentral endpoint was called or written;
- no PDF, HTML, or QR artifact was generated;
- no final BIR statutory wording was introduced;
- no production fiscal sequence was used.

## 12. Validation Result

Validation completed:

- Focused Central PMS unit tests for controlled UAT invocation, fixture store, and orchestration behavior: passed.
- Approved conflict runtime call: passed with `409 conflict_failure_mapped`.
- Read-only DB verification: one Central PMS fiscal reference, one POS fiscal document, fiscal sequence value unchanged at `2`, side-effect counts `0`.
- `git diff --check`: passed with Git line-ending notices only.
