# ExitPass Central PMS POS Server Controlled UAT Replay Result v1.0

## 1. Execution Timestamp

| Field | Value |
| --- | --- |
| Execution started | `2026-07-09T17:57:59.7184241+08:00` |
| Execution completed | `2026-07-09T17:58:11.4730220+08:00` |
| DB verification observed at | `2026-07-09T17:59:08.1106531+08:00` |
| Scenario | `replay` |
| Final result | `passed` |

## 2. Commands / Procedure Used

Procedure summary:

1. Verified branch `feature/controlled-uat-replay-scenario`.
2. Implemented narrow Central PMS controlled UAT replay handling for the approved July 9 non-production request.
3. Ran focused Central PMS unit tests for controlled UAT invocation, fixture store, and fiscal issuance orchestration behavior.
4. Created the replay request evidence file under:
   - `D:\ExitPass-UAT-Evidence\CPS-POS-UAT-20260709-DEV-ATC-001`
5. Built Central PMS:
   - `dotnet build src\Services\CentralPms\src\ExitPass.CentralPms.Api\ExitPass.CentralPms.Api.csproj`
6. Built POS Server:
   - `dotnet build D:\SourceCodes\ExitPass-PoSServer\src\ExitPass.PosServer.Api\ExitPass.PosServer.Api.csproj`
7. Started POS Server locally on:
   - `http://localhost:5000`
8. Started Central PMS locally on:
   - `http://localhost:56065`
9. Submitted the approved controlled UAT replay request once to:
   - `POST http://localhost:56065/internal/controlled-uat/fiscal-issuance/run`
10. Stopped the local Central PMS and POS Server processes.
11. Ran read-only DB verification for fiscal reference, POS fiscal document, fiscal sequence, and side-effect counts.
12. Generated SHA-256 checksums for saved evidence files.

No replay/conflict batch was run. Only the approved replay/idempotency request was submitted.

## 3. Request Facts

| Field | Value |
| --- | --- |
| Run id | `CPS-POS-UAT-20260709-DEV-ATC-001` |
| Scenario | `replay` |
| Expected run type | `replay` |
| Replay included | `true` |
| Central PMS URL | `http://localhost:56065` |
| POS Server URL | `http://localhost:5000` |
| Site | `DEV-SITE-ATC-001` |
| Site POS Server | `DEV-POS-SERVER-ATC-001` |
| Fiscal document type | `sales_invoice` |
| Amount | PHP 100.00 / `10000` minor units |
| Upstream finality ref | `CPS-POS-UAT:CPS-POS-UAT-20260709-DEV-ATC-001:newly_created:001` |
| Correlation id | `b7b4cbea-0c8c-4d06-9f6f-728a0a3fc2df` |
| Business day date | `2026-07-09` |
| Parking session ref | `DEV-PARKING-SESSION-ATC-001` |
| Payment attempt ref | `DEV-PAYMENT-ATTEMPT-ATC-001` |
| Payment finality / confirmation ref | `DEV-PAYMENT-FINALITY-ATC-001` |
| Payable basis ref | `DEV-PAYABLE-BASIS-ATC-001` |
| Evidence reference | `DEV-UAT-CPS-POS-001-REPLAY` |

The request used the same semantic fiscal facts and upstream finality reference as the already-passed `newly_created` run.

## 4. Replay Result

| Field | Value |
| --- | --- |
| HTTP status | `200` |
| Response status | `replay_recorded` |
| Accepted | `true` |
| Validation passed | `true` |
| Diagnostic invoked | `false` |
| POS Server fiscal document creation attempted | `false` |
| Diagnostic status | `replay_recorded` |
| Result classification | `IdempotentReplay` |
| Central PMS fiscal state | `FiscalIssuanceRecorded` |
| Sensitive data excluded | `true` |

The replay resolved to the existing recorded fiscal issuance and did not invoke POS Server fiscal document creation.

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

Read-only POS Server DB verification showed exactly one fiscal document for the approved payment finality ref. No new fiscal document number was allocated by the replay.

## 7. Fiscal Sequence Before / After

| Check | Value |
| --- | --- |
| Before replay, from passed run evidence | `2` |
| After replay, from read-only DB verification | `2` |
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
| `controlled-posserver-fiscal-uat-CPS-POS-UAT-20260709-DEV-ATC-001-replay-request.json` | Approved replay request body |
| `controlled-posserver-fiscal-uat-CPS-POS-UAT-20260709-DEV-ATC-001-replay-run-response.json` | Central PMS replay response |
| `controlled-posserver-fiscal-uat-CPS-POS-UAT-20260709-DEV-ATC-001-replay-execution-summary.json` | Replay execution summary |
| `controlled-posserver-fiscal-uat-CPS-POS-UAT-20260709-DEV-ATC-001-replay-db-verification.json` | Read-only DB verification |
| `controlled-posserver-fiscal-uat-CPS-POS-UAT-20260709-DEV-ATC-001-replay-hash.txt` | SHA-256 checksums |
| `central-pms-replay-build.log` | Central PMS build output |
| `pos-server-replay-build.log` | POS Server build output |
| `central-pms-replay-stdout.log` | Central PMS runtime stdout capture |
| `central-pms-replay-stderr.log` | Central PMS runtime stderr capture |
| `pos-server-replay-stdout.log` | POS Server runtime stdout capture |
| `pos-server-replay-stderr.log` | POS Server runtime stderr capture |

```text
D6FAE60487B986EE382CECA7B9FBCDC07E72335812B6A7A9B6F889A355B57AC6  controlled-posserver-fiscal-uat-CPS-POS-UAT-20260709-DEV-ATC-001-replay-request.json
7161DC4899EFCFBFF3082CC898A5D28745726E6F01906B99757FF0C4354FB5E4  controlled-posserver-fiscal-uat-CPS-POS-UAT-20260709-DEV-ATC-001-replay-run-response.json
8B77D5A38E24C2D6359593AD51C2F7E95514F07A30D7C9B1068BD2F721F46AE3  controlled-posserver-fiscal-uat-CPS-POS-UAT-20260709-DEV-ATC-001-replay-execution-summary.json
96F2A80DA2B0DC94C737F917B2DC18374799DF035446B78D73EF8379151E4150  controlled-posserver-fiscal-uat-CPS-POS-UAT-20260709-DEV-ATC-001-replay-db-verification.json
2316D9EEC55958C033A5AB9EEF594926B5CE5E728C8B1E7A5A3E4D4AA59064F7  central-pms-replay-build.log
4A303D1832E9D43176FDDDBD5F5E844DC60E80217B99A32D2EB13F9AD919C88D  pos-server-replay-build.log
F1945CD6C19E56B3C1C78943EF5EC18116907A4CA1EFC40A57D48AB1DB7ADFC5  central-pms-replay-stdout.log
F1945CD6C19E56B3C1C78943EF5EC18116907A4CA1EFC40A57D48AB1DB7ADFC5  central-pms-replay-stderr.log
F1945CD6C19E56B3C1C78943EF5EC18116907A4CA1EFC40A57D48AB1DB7ADFC5  pos-server-replay-stdout.log
F1945CD6C19E56B3C1C78943EF5EC18116907A4CA1EFC40A57D48AB1DB7ADFC5  pos-server-replay-stderr.log
```

## 10. Final Result

Final result: `passed`

The approved replay/idempotency scenario returned the existing fiscal issuance result without allocating a new fiscal document number.

## 11. Boundary Statement

During this replay:

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
- Approved replay runtime call: passed with `replay_recorded`.
- Read-only DB verification: one Central PMS fiscal reference, one POS fiscal document, fiscal sequence value unchanged at `2`, side-effect counts `0`.
- `git diff --check`: passed with Git line-ending notices only.
