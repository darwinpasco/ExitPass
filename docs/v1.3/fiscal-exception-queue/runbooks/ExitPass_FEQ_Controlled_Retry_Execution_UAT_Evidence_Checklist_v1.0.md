# ExitPass FEQ Controlled Retry Execution UAT Evidence Checklist v1.0

Use one copy of this checklist per UAT run. Store completed copies and supporting evidence outside source control.

## Test Identity

| Field | Value |
| --- | --- |
| Test run id |  |
| Tester |  |
| Date/time started |  |
| Date/time completed |  |
| Result | Pass / Fail / Blocked |
| Branch tested |  |
| Commit SHA |  |
| Checkpoint tag |  |
| Central PMS build/version |  |
| POS Server build/version |  |
| Runbook version | ExitPass_FEQ_Controlled_Retry_Execution_UAT_Runbook_v1.0 |

## Environment

| Field | Value |
| --- | --- |
| Environment name |  |
| Machine/host |  |
| Disposable Central PMS database name |  |
| Disposable POS Server database/tenant/site context |  |
| Central PMS base URL or service host |  |
| POS Server base URL |  |
| Evidence folder path |  |
| Logs path |  |
| Screenshots path |  |
| Query exports path |  |

## Feature Flags And Local Overrides

| Setting | Value | Evidence location |
| --- | --- | --- |
| `FiscalExceptionControlledRetryExecution:EnableControlledRetryExecution` |  |  |
| `FiscalExceptionRetrySchedulingPreparation:EnableSchedulePreparation` |  |  |
| `FiscalExceptionRetrySchedulingPreparation:RetrySchedulePolicyConfigured` |  |  |
| `FiscalExceptionRetrySchedulingPreparation:RetryBackoffPolicyConfigured` |  |  |
| `FiscalExceptionRetryExecutionPreparation:EnableExecutionPreparation` |  |  |
| `FiscalExceptionRetryExecutionPreparation:ServiceIdentityAllowed` |  |  |
| POS Server local/controlled create endpoint enabled |  |  |
| Scheduler/background execution disabled |  |  |
| Public endpoint/UI path not used |  |  |

## FEQ Case Evidence

| Field | Value | Evidence location |
| --- | --- | --- |
| Fiscal issuance reference id |  |  |
| Payment confirmation id |  |  |
| Payment attempt id |  |  |
| Parking session id |  |  |
| Site id |  |  |
| Site POS Server id/ref |  |  |
| Fiscal document type code id/ref |  |  |
| Upstream finality reference |  |  |
| POS Server idempotency scope |  |  |
| POS Server idempotency key |  |  |
| Idempotency key source | payableBasis.upstreamFinalityRef |  |

## Semantic Hash Evidence

| Field | Value | Evidence location |
| --- | --- | --- |
| Semantic hash value |  |  |
| Semantic hash algorithm | SHA-256 / sha256 |  |
| Semantic hash source version | sha256:v1 |  |
| Semantic hash status | available / confirmed |  |
| Source fact count / safe summary |  |  |
| Central PMS hash recalculation/check result |  |  |
| POS Server readback hash value |  |  |
| POS Server readback hash version | sha256:v1 |  |
| Hash values match | Yes / No / N/A |

## Gate Evidence

| Gate | Expected | Actual | Evidence location |
| --- | --- | --- | --- |
| Latest readback classification | not_found |  |  |
| Readback attempt id | present |  |  |
| Retry eligibility result | eligible |  |  |
| Command preparation status | PreparedNonExecutable |  |  |
| Command preparation audit id | present |  |  |
| Scheduler preparation status | ScheduledPrepared |  |  |
| Scheduler preparation audit id | present |  |  |
| Execution preparation posture | ReadyForExecutionWhenEnabled |  |  |
| POS Server readiness gates | confirmed |  |  |
| Same upstream finality reference retained | yes |  |  |
| Service identity | present |  |  |
| Approval reference | present |  |  |
| Dual-control reference | present when required |  |  |
| Unsafe FEQ state absent | yes |  |  |
| Audit persistence available | yes |  |  |

## Scenario Results

### Scenario 1: Disabled By Default

| Field | Value |
| --- | --- |
| Controlled retry enabled | false |
| Worker status |  |
| Block reason |  |
| POS Server POST count |  |
| Retry execution attempt id |  |
| Fiscal reference changed | Yes / No |
| Result | Pass / Fail / Blocked |
| Notes |  |

### Scenario 2: Missing Gate

| Field | Value |
| --- | --- |
| Missing/unsafe gate selected |  |
| Controlled retry enabled | true |
| Worker status |  |
| Block reason |  |
| POS Server POST count |  |
| Retry execution attempt id |  |
| Fiscal reference changed | Yes / No |
| Result | Pass / Fail / Blocked |
| Notes |  |

### Scenario 3: Happy Controlled Retry

| Field | Value |
| --- | --- |
| Controlled retry enabled | true |
| Worker status | Executed / ReplayMatched |
| POS Server POST count |  |
| Retry execution attempt id |  |
| POS Server result classification | NewlyCreated / IdempotentReplay |
| POS Server fiscal document id |  |
| Fiscal document number |  |
| Fiscal series / prefix / suffix |  |
| Fiscal number assigned at/by |  |
| Fiscal reference updated by orchestration path | Yes / No |
| Retry loop observed | Yes / No |
| Result | Pass / Fail / Blocked |
| Notes |  |

### Scenario 4: POS Server Idempotency Conflict

| Field | Value |
| --- | --- |
| Conflict method | Changed semantic facts / Simulated local conflict |
| Controlled retry enabled | true |
| Worker status | Conflict |
| Block reason |  |
| POS Server POST count |  |
| Retry execution attempt id |  |
| Second POST observed | Yes / No |
| Fiscal reference success recorded | Yes / No |
| Result | Pass / Fail / Blocked |
| Notes |  |

### Scenario 5: Unknown Or Unavailable

| Field | Value |
| --- | --- |
| Unknown/unavailable method | Timeout / POS unavailable / Local config block / Test double |
| Controlled retry enabled | true |
| Worker status | Unknown / Unavailable / Failed |
| Block reason |  |
| POS Server POST count |  |
| Retry execution attempt id |  |
| Future readback required | Yes / No |
| Second POST observed | Yes / No |
| Result | Pass / Fail / Blocked |
| Notes |  |

## Forbidden Side-Effect Confirmation

| Side effect | Expected | Actual | Evidence location |
| --- | --- | --- | --- |
| Payment finality mutation | No |  |  |
| ExitAuthorization issued | No |  |  |
| Gate behavior triggered | No |  |  |
| Fiscal number edited by Central PMS | No |  |  |
| Manual fiscal document created | No |  |  |
| Public endpoint used | No |  |  |
| Operator Console UI used | No |  |  |
| Management Dashboard workflow used | No |  |  |
| Batch retry path used | No |  |  |
| Scheduler/background job created | No |  |  |
| POS Server repository changed | No |  |  |
| Production data used | No |  |  |

## Evidence Attachments

| Evidence item | Location | Notes |
| --- | --- | --- |
| Central PMS config snapshot with secrets redacted |  |  |
| POS Server config/base URL proof with secrets redacted |  |  |
| FEQ detail before scenario |  |  |
| FEQ detail after scenario |  |  |
| Readback attempt query/export |  |  |
| Command prep audit query/export |  |  |
| Scheduler prep audit query/export |  |  |
| Execution prep posture capture |  |  |
| Retry execution audit query/export |  |  |
| Fiscal issuance reference before/after snapshot |  |  |
| POS Server response/readback capture |  |  |
| Application logs excerpt |  |  |
| Screenshots, if any |  |  |

## Sign-Off

| Role | Name | Date | Result | Notes |
| --- | --- | --- | --- | --- |
| Tester |  |  |  |  |
| Reviewer |  |  |  |  |
| Approver, if required |  |  |  |  |
