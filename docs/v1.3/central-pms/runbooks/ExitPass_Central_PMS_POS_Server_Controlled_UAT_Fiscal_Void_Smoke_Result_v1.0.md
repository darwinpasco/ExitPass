# ExitPass Central PMS POS Server Controlled UAT Fiscal Void Smoke Result v1.0

## 1. Execution Timestamp

| Field | Value |
| --- | --- |
| Execution completed | `2026-07-09T22:23:18.0988239+08:00` |
| Scenario | `fiscal_void_smoke` |
| Final result | `passed` |

## 2. Approved Target Document

| Field | Value |
| --- | --- |
| Profile id | `CPS-POS-UAT-20260709-DEV-ATC-001` |
| Fiscal issuance reference id | `14479d9a-844f-4dba-9578-e863ece93fbf` |
| POS Server fiscal document id | `9bdf2948-dadd-450b-8776-be688b579395` |
| Fiscal document number | `SI-00000002-UAT` |
| Expected fiscal sequence value | `2` |
| Reason code | `CONTROLLED_UAT_VOID_SMOKE` |
| Correlation id | `b7b4cbea-0c8c-4d06-9f6f-728a0a3fc2df` |
| Evidence path | `D:\ExitPass-UAT-Evidence\CPS-POS-UAT-20260709-DEV-ATC-001` |

## 3. Command / Procedure Used

Procedure summary:

1. Inspected the existing POS Server fiscal document API/runtime shape.
2. Verified there was no existing POS Server arbitrary fiscal void/cancel runtime endpoint to use safely.
3. Added a narrow Central PMS internal controlled UAT fiscal void smoke path for the approved non-production document only.
4. Ran focused Central PMS tests for controlled UAT invocation, fixture store, fiscal orchestration, and fiscal void smoke behavior.
5. Built Central PMS:
   - `dotnet build src\Services\CentralPms\src\ExitPass.CentralPms.Api\ExitPass.CentralPms.Api.csproj --no-restore`
6. Started Central PMS locally on:
   - `http://localhost:56065`
7. Submitted the approved controlled fiscal void smoke request once to:
   - `POST http://localhost:56065/internal/controlled-uat/fiscal-issuance/void-smoke`
8. Recorded non-sensitive request, response, summary, build log, runtime logs, and SHA-256 hashes under the approved evidence path.
9. Stopped the local Central PMS process.

The smoke path wrote a metadata-only void/cancellation posture to the approved POS Server fiscal document header context and status history. It did not call POS Server fiscal document creation and did not allocate a new fiscal Sales Invoice number.

## 4. Result

| Field | Value |
| --- | --- |
| HTTP status | `200` |
| Response status | `controlled_uat_void_smoke_recorded` |
| Accepted | `true` |
| Fiscal document status/posture after void/cancel | `CONTROLLED_UAT_VOID_SMOKE_RECORDED` |
| Fiscal sequence value | `2` |
| Status history recorded | `true` |
| Idempotent replay | `false` |
| New fiscal number allocated | `false` |

## 5. New Fiscal Number Allocation

No new fiscal Sales Invoice number was allocated.

| Check | Value |
| --- | --- |
| Original fiscal document number | `SI-00000002-UAT` |
| Post-smoke fiscal document number | `SI-00000002-UAT` |
| Fiscal sequence value reported by smoke response | `2` |
| New fiscal number allocated | No |

## 6. Side-Effect Checks

| Side effect | Result |
| --- | --- |
| Payment finality changed | `false` |
| ExitAuthorization issued | `false` |
| Gate behavior triggered | `false` |
| Refund/reversal created | `false` |
| HikCentral called | `false` |
| Payment provider called | `false` |
| PDF/HTML/QR rendering generated | `false` |

## 7. Evidence Files And Checksums

Evidence folder:

```text
D:\ExitPass-UAT-Evidence\CPS-POS-UAT-20260709-DEV-ATC-001
```

| Evidence file | Purpose |
| --- | --- |
| `controlled-posserver-fiscal-uat-CPS-POS-UAT-20260709-DEV-ATC-001-void-smoke-request.json` | Approved void smoke request body |
| `controlled-posserver-fiscal-uat-CPS-POS-UAT-20260709-DEV-ATC-001-void-smoke-response.json` | Central PMS void smoke response |
| `controlled-posserver-fiscal-uat-CPS-POS-UAT-20260709-DEV-ATC-001-void-smoke-summary.json` | Void smoke execution summary |
| `controlled-posserver-fiscal-uat-CPS-POS-UAT-20260709-DEV-ATC-001-void-smoke-hash.txt` | SHA-256 checksums |
| `central-pms-void-smoke-build.log` | Central PMS build output |
| `central-pms-void-smoke-stdout.log` | Central PMS runtime stdout capture |
| `central-pms-void-smoke-stderr.log` | Central PMS runtime stderr capture |

```text
40C5F807231F8F3F1A31AED63F1C9198DDDD8F8EE3BAFA06EDA3EBB81E255476  D:\ExitPass-UAT-Evidence\CPS-POS-UAT-20260709-DEV-ATC-001\controlled-posserver-fiscal-uat-CPS-POS-UAT-20260709-DEV-ATC-001-void-smoke-request.json
37F9159BF6B392D0A153BD2AC74B24471FB6631F923A6CA8D1C9433F785046AE  D:\ExitPass-UAT-Evidence\CPS-POS-UAT-20260709-DEV-ATC-001\controlled-posserver-fiscal-uat-CPS-POS-UAT-20260709-DEV-ATC-001-void-smoke-response.json
09EF0CDC5FCB4B521DFC5BECE89BA6EE25C7C74955C65ECF09081BAE39C9FC1A  D:\ExitPass-UAT-Evidence\CPS-POS-UAT-20260709-DEV-ATC-001\controlled-posserver-fiscal-uat-CPS-POS-UAT-20260709-DEV-ATC-001-void-smoke-summary.json
AD4413C8B5E9F3529F644426701FFFA15224D943FCA06A2EC683580AF9074E06  D:\ExitPass-UAT-Evidence\CPS-POS-UAT-20260709-DEV-ATC-001\central-pms-void-smoke-build.log
9C9AEC901B0280C2571E0157F5F7032C97E60708EF2FE16486302B46E752D7F0  D:\ExitPass-UAT-Evidence\CPS-POS-UAT-20260709-DEV-ATC-001\central-pms-void-smoke-stdout.log
E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855  D:\ExitPass-UAT-Evidence\CPS-POS-UAT-20260709-DEV-ATC-001\central-pms-void-smoke-stderr.log
```

## 8. Boundary Statement

During this fiscal void smoke:

- only the approved non-production fiscal document was targeted;
- no new fiscal Sales Invoice number was allocated;
- no payment finality was mutated;
- no ExitAuthorization was issued;
- no gate behavior was triggered;
- no refund/reversal was created;
- no HikCentral endpoint was called or written;
- no payment provider was called;
- no PDF, HTML, or QR artifact was generated;
- no final BIR statutory wording was introduced;
- no production fiscal sequence was used.

## 9. Validation Result

Validation completed:

- Focused Central PMS unit tests for controlled UAT invocation, fixture store, fiscal issuance orchestration, and fiscal void smoke behavior: passed, `123` tests.
- Approved fiscal void smoke runtime call: passed with `controlled_uat_void_smoke_recorded`.
- Fiscal document posture after smoke: `CONTROLLED_UAT_VOID_SMOKE_RECORDED`.
- Fiscal sequence value remained `2`.
- `git diff --check`: passed.
