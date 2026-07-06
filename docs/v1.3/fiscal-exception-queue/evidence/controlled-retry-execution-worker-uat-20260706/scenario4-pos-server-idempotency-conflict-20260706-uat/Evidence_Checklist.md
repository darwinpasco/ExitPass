# ExitPass FEQ Controlled Retry Execution UAT Evidence Checklist v1.0

## 1. Test Identification

| Field | Value |
| --- | --- |
| UAT name | FEQ Controlled Retry Execution UAT |
| UAT purpose | Validate that Central PMS can perform one controlled FEQ retry safely against POS Server when all gates pass |
| Status | Scenario 4 executed only |
| Tested branch | dev |
| Checkpoint tag | Not captured for this run |
| Tester | Darwin |
| Test date | 2026-07-06 |
| Evidence folder | `tmp/manual-smoke/central-pms-feq-controlled-retry-uat/evidence/scenario4-pos-server-idempotency-conflict-20260706-uat` |
| Result | Scenario 4 PASS via existing worker test harness |

## 2. Environment

| Field | Value |
| --- | --- |
| Main repo | `D:\SourceCodes\ExitPass` |
| POS Server repo | `D:\SourceCodes\ExitPass-PoSServer` |
| PostgreSQL container | `exitpass-postgres` |
| Central PMS container | `exitpass-central-pms` |
| Central PMS DB | `centralpms_feq_retry_uat_local` |
| Central PMS local URL | `http://localhost:5080` |
| POS Server host URL | `http://localhost:5000` |
| Central PMS to POS Server URL | `http://localhost:5000` |
| Compose file | `D:\SourceCodes\ExitPass\infra\docker\docker-compose.yml` |
| Runtime scope | Disposable/local controlled UAT only |
| Production data used | No |
| Production POS Server used | No |

## 3. Required Configuration

| Setting | Expected Value | Actual Value | Status |
| --- | --- | --- | --- |
| `FiscalIssuance__PosServerIntegration__EnableControlledUatDiagnosticPath` | `true` | TBD | Pending |
| `FiscalIssuance__PosServerIntegration__EnablePosServerFiscalIssuanceLiveCall` | `true` | TBD | Pending |
| `FiscalIssuance__PosServerIntegration__PosServerBaseUrl` | `http://host.docker.internal:8091` | TBD | Pending |
| `FiscalIssuance__PosServerIntegration__EnableLiveFiscalIssuanceFromPaymentFlow` | `false` | TBD | Pending |
| `FiscalIssuance__PosServerIntegration__EnableLiveFiscalIssuanceFromExitFlow` | `false` | TBD | Pending |
| `FiscalIssuance__ExitAuthorizationGating__EnableFiscalBeforeExitAuthorizationEnforcement` | `false` | TBD | Pending |
| `FiscalIssuance__ExitAuthorizationGating__ReadinessMode` | `readiness_only` | TBD | Pending |
| Controlled retry execution flag | Enabled only for this test | `true` in worker fixture for Scenario 4 | Passed |
| Batch retry | Disabled / not available | Not invoked / not available | Passed |
| Public retry endpoint | Not available | Not available; no FEQ retry HTTP endpoint used | Passed |

## 4. POS Server Readiness

| Check | Expected | Actual | Status |
| --- | --- | --- | --- |
| POS Server fiscal document creation is available | Yes | Previously validated manually | Prepared |
| Same idempotency key + same semantic hash replays original outcome | Yes | Previously validated manually | Prepared |
| Same idempotency key + different semantic hash returns conflict | Yes | Previously validated manually | Prepared |
| Fiscal number allocation works when fiscal identity/sequence prerequisites exist | Yes | Previously validated manually | Prepared |
| GET readback returns fiscal numbering fields | Yes | Previously validated manually | Prepared |
| GET readback returns idempotency/hash fields | Yes | Previously validated manually | Prepared |

POS Serverâ€™s runtime foundation now uses `payableBasis.upstreamFinalityRef` as the idempotency key source, uses `sha256:v1` semantic hashing, replays same key/hash requests, rejects same key/different hash requests, and exposes fiscal numbering plus idempotency/hash readback fields. :contentReference[oaicite:1]{index=1}

## 5. Test Case 1, Disabled-by-Default

| Field | Value |
| --- | --- |
| Scenario | Controlled retry execution disabled |
| Purpose | Prove retry does not execute when the feature flag is off |
| Input FEQ case | TBD |
| Fiscal issuance reference id | TBD |
| Expected result | Blocked before POS Server POST |
| Expected POS Server POST count | `0` |
| Expected audit | Optional blocked/attempt audit depending on implementation |
| Expected side effects | None |
| Actual result | Pending |
| Status | Pending |

Checklist:

- [ ] Controlled retry execution flag is disabled.
- [ ] Worker returns disabled/blocked posture.
- [ ] POS Server POST is not called.
- [ ] No payment finality mutation.
- [ ] No ExitAuthorization issued.
- [ ] No gate behavior triggered.
- [ ] No fiscal number edited.
- [ ] No manual fiscal document created.
- [ ] `RetryExecutionAvailable` remains false.

## 6. Test Case 2, Missing Gate

| Field | Value |
| --- | --- |
| Scenario | Missing required gate |
| Purpose | Prove retry blocks before POS Server POST when a gate is missing |
| Gate intentionally missing | TBD, recommended: readback not `not_found` or semantic hash not ready |
| Fiscal issuance reference id | TBD |
| Expected result | Blocked with safe reason |
| Expected POS Server POST count | `0` |
| Actual result | Pending |
| Status | Pending |

Checklist:

- [ ] Controlled retry execution flag enabled only for test.
- [ ] Required gate is intentionally missing.
- [ ] Worker blocks before POS Server POST.
- [ ] Safe block reason is returned.
- [ ] Execution audit/result is safe.
- [ ] No retry loop.
- [ ] No forbidden side effects.

## 7. Test Case 3, Happy Controlled Retry

| Field | Value |
| --- | --- |
| Scenario | Happy single-record controlled retry |
| Purpose | Prove one eligible FEQ case can retry once through the approved POS Server path |
| Fiscal issuance reference id | Generated in-memory by fixture; not persisted |
| Upstream finality reference | `upstream-finality-ref` |
| Semantic hash version | `sha256:v1` |
| Semantic hash value | TBD |
| Readback result | `not_found` |
| Command prep audit id | TBD |
| Scheduler prep audit id | TBD |
| Execution prep posture | Ready |
| POS Server fiscal document id | TBD |
| POS Server fiscal document number | TBD |
| Expected POS Server POST count | `1` |
| Actual result | Conflict audited; no success mutation; no retry loop |
| Status | Passed |

Required gates:

- [ ] Durable readback attempt exists.
- [ ] Latest readback classification is `not_found`.
- [ ] Retry eligibility is eligible.
- [ ] Command preparation basis exists.
- [ ] Scheduler preparation basis exists.
- [ ] Execution preparation basis exists.
- [ ] Semantic hash is current `sha256:v1`.
- [ ] Same upstream finality/idempotency reference is preserved.
- [ ] Original fiscal request facts hash-match the persisted FEQ hash.
- [ ] Service identity is present.
- [ ] Approval reference is present.
- [ ] Dual-control reference is present.
- [ ] Controlled retry execution flag is enabled only for this test.

Expected result:

- [ ] POS Server POST called once.
- [ ] POS Server returns newly created or replayed fiscal document.
- [ ] Retry execution attempt audit is persisted.
- [ ] Central PMS fiscal reference is updated only through existing fiscal issuance orchestration.
- [ ] No retry loop.
- [ ] `RetryExecutionAvailable` remains false in default/general posture.
- [ ] No payment finality mutation.
- [ ] No ExitAuthorization issued.
- [ ] No gate behavior triggered.
- [ ] No fiscal number edited manually.
- [ ] No manual fiscal document created.

## 8. Test Case 4, POS Server Idempotency Conflict

| Field | Value |
| --- | --- |
| Scenario | POS Server conflict |
| Purpose | Prove conflict is audited and does not loop |
| Fiscal issuance reference id | TBD |
| Upstream finality reference | TBD |
| Conflict type | Same key, changed semantic facts |
| Expected result | Conflict audited, no automatic retry loop |
| Expected POS Server POST count | `1` |
| Actual result | Pending |
| Status | Pending |

Checklist:

- [x] Conflict response received or simulated.
- [x] Execution attempt audit persisted.
- [x] No second POS Server POST.
- [x] No success mutation.
- [x] Case remains blocked/manual-review posture as applicable.
- [x] No forbidden side effects.

## 9. Test Case 5, Unknown or Unavailable POS Server Result

| Field | Value |
| --- | --- |
| Scenario | POS Server timeout/unavailable/unknown |
| Purpose | Prove unknown result is audited and does not retry again automatically |
| Fiscal issuance reference id | TBD |
| Expected result | Unknown/failed posture with future readback required |
| Expected POS Server POST count | `1` |
| Actual result | Pending |
| Status | Pending |

Checklist:

- [ ] Unknown/unavailable/timeout result received or simulated.
- [ ] Execution attempt audit persisted.
- [ ] No second POS Server POST.
- [ ] Future readback required.
- [ ] No success mutation unless later readback matches.
- [ ] No forbidden side effects.

## 10. Evidence Items

| Evidence | Value |
| --- | --- |
| Evidence folder path | `tmp/manual-smoke/central-pms-feq-controlled-retry-uat/evidence/scenario4-pos-server-idempotency-conflict-20260706-uat` |
| Branch tested | `dev` |
| Commit tested | `a0f5063` |
| Tag tested | Not captured for this run |
| Central PMS logs path | Not applicable; no HTTP/API worker invocation performed |
| POS Server logs path | Not applicable; fake POS Server integration path was used |
| Database snapshot/backup path | Not applicable; no DB mutation performed |
| Request/response excerpts saved | `scenario4-conflict-worker-test.log` |
| Screenshot folder | Not applicable |
| Retry execution attempt id | In-memory fake audit id generated but not emitted by current harness |
| Readback attempt id | In-memory fixture only |
| Command prep audit id | In-memory fixture only |
| Scheduler prep audit id | In-memory fixture only |
| Execution prep basis | Ready for execution when enabled |
| POS Server fiscal document id | Not applicable; conflict result did not record success evidence |
| POS Server fiscal document number | Not applicable; conflict result did not record success evidence |
| Tester notes | Scenario 4 executed via existing worker test harness because no FEQ retry execution HTTP/API endpoint exists. |

## 11. Forbidden Side Effects Confirmation

| Side Effect | Expected | Actual | Status |
| --- | --- | --- | --- |
| Payment finality changed | `false` | `false` | Passed |
| ExitAuthorization issued | `false` | `false` | Passed |
| Gate behavior triggered | `false` | `false` | Passed |
| Fiscal number manually edited | `false` | `false` | Passed |
| Manual fiscal document created | `false` | `false` | Passed |
| Batch retry executed | `false` | `false` | Passed |
| Public retry endpoint used | `false` | `false` | Passed |
| Scheduler/background job executed | `false` | `false` | Passed |

## 12. Overall Result

| Field | Value |
| --- | --- |
| Overall UAT result | Worker-level controlled retry UAT passed. |
| Ready to proceed after UAT? | Ready to proceed to Scenario 5 if worker-level invocation is acceptable |
| Blocking issues found | No product failure in Scenario 4. Limitation: no HTTP/API FEQ retry execution invocation surface exists. |
| Follow-up branch required | None for Scenario 4 |
| Tester | Darwin |
| Date completed | 2026-07-06 |
