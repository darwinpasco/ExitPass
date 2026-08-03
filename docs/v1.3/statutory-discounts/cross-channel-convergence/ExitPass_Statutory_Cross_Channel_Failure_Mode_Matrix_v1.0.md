# ExitPass Statutory Cross-Channel Failure-Mode Matrix v1.0

| Failure mode | WebPay | APT | Operator Console | Central PMS | POS Server | Ordinary-payment effect | Retryability | Status |
|---|---|---|---|---|---|---|---|---|
| Policy not configured | Safe statutory unavailable | Deny statutory option | No review should be created | `NO_APPLICABLE_POLICY` | Not involved | Preserved | Retry after config | CONVERGED |
| Policy inactive | Safe unavailable | Safe unavailable | Block approval | Inactive coverage | Not involved | Preserved | Retry after activation | CONVERGED |
| Policy not yet effective | Safe future/unavailable | Safe unavailable | Block approval | `FUTURE_EFFECTIVE` | Not involved | Preserved | Retry when effective | CONVERGED |
| Policy expired | Safe unavailable | Safe unavailable | Block approval | `EXPIRED` | Not involved | Preserved | No unless renewed | CONVERGED |
| Ambiguous jurisdiction | Deny statutory path | Deny statutory path | Block approval | Fail closed | Not involved | Preserved where ordinary scope is valid | Support repair | CONVERGED |
| Site mismatch | Scope denied | Scope denied | Scope denied | Server scope fails | Fiscal facts mismatch fails | Correct-Site ordinary path may proceed | Correct request | CONVERGED |
| Site Group mismatch | Scope denied | Scope denied | Scope denied | Server scope fails | Fiscal facts mismatch should fail | Correct-scope ordinary path may proceed | Correct request | CONVERGED |
| Session not found | `NOT_FOUND` | No readiness | No review detail | Safe not found | Not involved | Depends on ordinary session lookup | Re-lookup | CONVERGED |
| Ambiguous session | `AMBIGUOUS_SESSION` | Re-resolution block | No review created | Safe ambiguous | Not involved | Preserved if exact ordinary session can be selected | Provide exact session | CONVERGED |
| Source unavailable | Retryable safe error | Availability/readiness unavailable | Review refresh/block | Dependency failure | Not involved | Preserved for statutory-only failure | Retryable | CONVERGED |
| Malformed authoritative state | Safe malformed state | Block statutory cash | Plain blocked message | Fail closed | Reject incomplete applied facts | Preserved where non-statutory path valid | Support repair | CONVERGED |
| Access denied | `ACCESS_DENIED` | Auth/scope deny | Route deny | Policy handler deny | POS route auth | Preserved if ordinary payment auth exists | No until auth fixed | CONVERGED |
| Decision conflict | Existing state readback | Existing state readback | Read-only terminal state | Idempotency/facade conflict | Not involved | Preserved | Same idempotency or support | CONVERGED |
| Review conflict | Existing state | Existing state | Read-only after terminal decision | State immutable by rules | Not involved | Preserved | Refresh | CONVERGED |
| Application conflict | Existing app readback | Existing app readback | Should not apply | Application-v1 conflict | Duplicate fiscal facts unique | Preserved if ordinary allowed | Same idempotency or support | PARTIALLY_CONVERGED |
| Payable basis changed | Reapply/revalidate | Pre-cash revalidate | Not applicable | Latest tariff/session resolved | Requires final facts | Ordinary payment can use current basis | Retry application | CONVERGED |
| Payment already completed | No retroactive statutory adjustment | No retroactive adjustment | Read-only/no automatic refund | Final ordinary payment stands | Fiscal already final | Ordinary payment complete | Not automatically retryable | CONVERGED |
| Central PMS unavailable | Statutory path unavailable | Statutory path unavailable | Review unavailable | Service unavailable | Not involved | Ordinary may continue if payment path independent | Retryable | CONVERGED |
| Payment Orchestrator unavailable | Online payment fails | Not applicable | Not applicable | Not owner | Not involved | WebPay payment may fail independently | Retryable | INTENTIONALLY_DIFFERENT |
| Terminal cash unavailable | Not applicable | Cash path unavailable | Not applicable | Readiness may fail | Not involved | Alternate payment may remain | Retryable | INTENTIONALLY_DIFFERENT |
| POS Server unavailable | Fiscal issuance unavailable | Fiscal issuance unavailable | Not applicable | Not owner | Service unavailable | All fiscal finality may fail | Retryable | CONVERGED |
| Fiscal issuance failed | Payment channel handles safe failure | APT blocks/reconciles | Not applicable | Not owner | Safe error/idempotency | Independent fiscal failure can block all payment finality | Depends | PARTIALLY_CONVERGED |
| Receipt presentation unavailable | Safe readback failure | Safe readback failure | Not applicable | Not owner | Render/readback safe error | Payment may already be final | Retryable | CONVERGED |
| Restart during pending review | Rediscover lifecycle | Re-resolve state | Queue still pending | Decision unchanged | Not involved | Preserved | Retry/refresh | CONVERGED |
| Restart after cash entry before custody | Not applicable | Local cash/custody recovery | Not applicable | Revalidate before irreversible boundary | Not involved | Cash-specific recovery | Controlled | PARTIALLY_CONVERGED |
| Restart after `CASH_RECEIVED` | Not applicable | Irreversible cash recovery/fiscal continuation | Not applicable | No new decision/application | Requires final facts | No ordinary rollback | Support/reconcile | PARTIALLY_CONVERGED |
| Duplicate callback | Payment idempotency | Cash/fiscal idempotency | Not applicable | Application idempotency | POS semantic hash | No duplicate fiscal/payment | Idempotent | PARTIALLY_CONVERGED |
| Duplicate retry | Same-decision rediscovery | Local + Central PMS idempotency | Read-only terminal states | Replay/conflict | POS replay/conflict | No duplicate | Idempotent | CONVERGED |
| Evidence service unavailable | Future statutory review unavailable | Future statutory review unavailable | Cannot approve without reviewable evidence | Future evidence owner unavailable | POS evidence-free | Ordinary payment preserved | Retry upload/review | NOT_IMPLEMENTED |
