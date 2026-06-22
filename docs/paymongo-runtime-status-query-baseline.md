# PayMongo Runtime Status Query Baseline

Date: 2026-06-23

## Purpose

This baseline defines how PayMongo runtime status query support should be added safely. It is a design and implementation starting point for the next coding slice, not an implementation.

The status-query path must not bypass PayMongo callback verification, Central PMS payment finality, PaymentConfirmation ownership, or ExitAuthorization boundaries. PayMongo status query evidence is provider evidence only until Central PMS accepts it through the existing platform finality contract.

## Current State

#312 confirmed the pre-implementation runtime state:

- Payment Orchestrator has no PayMongo runtime status-query client method.
- Payment Orchestrator has no runtime status-query endpoint.
- Payment Orchestrator has no scheduled provider status poller.
- Payment Orchestrator has no reconciliation status-query handler.
- The PayMongo client currently creates checkout sessions with `POST /v1/checkout_sessions`.
- The PayMongo adapter currently verifies raw webhooks through `VerifyWebhookAsync`.
- Verified terminal webhook outcomes are reported from Payment Orchestrator to Central PMS.
- Current reconciliation diagnostics are read-only over persisted provider evidence such as `payments.provider_sessions`, `payments.provider_callbacks`, and `payments.provider_outcomes`.

The baseline DDL already contains `payments.provider_status_queries`, but the current runtime code does not write or read that table for PayMongo status polling.

#314 adds the first implementation baseline only:

- `ProviderStatusQueryCommand`
- `ProviderStatusQueryResult`
- `PayMongoClient.RetrieveCheckoutSessionStatusAsync`
- `PayMongoCheckoutAdapter.QueryProviderSessionStatusAsync`

These are model/client/adapter foundations only. #314 does not add an endpoint, poller, scheduler, Central PMS reporting path, or schema change.

## Target Architecture

Payment Orchestrator should own PayMongo runtime status query because it already owns provider-specific PayMongo interaction and webhook verification.

The intended flow:

1. Payment Orchestrator receives a controlled status-query command for a known provider session or provider reference.
2. Payment Orchestrator retrieves PayMongo status using the provider session/provider reference and PayMongo credentials from approved configuration.
3. The PayMongo adapter validates the provider response shape, reference, amount, and currency against the persisted provider session or payment attempt basis.
4. The adapter normalizes the PayMongo response into provider-neutral status evidence.
5. Payment Orchestrator reports only verified terminal provider-neutral outcomes to Central PMS.
6. Central PMS decides platform finality and PaymentConfirmation recording.
7. Reconciliation diagnostics may surface status-query evidence, but diagnostics do not mutate payment finality.

Non-terminal, unknown, malformed, timeout, network-failed, or mismatched status-query results must not be reported to Central PMS as finality.

## Ownership and Boundaries

| Component | Owns | Must not do |
| --- | --- | --- |
| WebPay | User-facing payment initiation and display of server-returned payment state. | Must not query PayMongo directly, declare payment finality, create PaymentConfirmation, issue ExitAuthorization, or bypass server-side method validation. |
| Payment Orchestrator | PayMongo API calls, PayMongo response verification, status normalization, provider evidence handling, and reporting verified provider-neutral outcomes to Central PMS. | Must not own platform PaymentAttempt finality, create platform PaymentConfirmation directly, issue ExitAuthorization, or treat unverified status evidence as finality. |
| PayMongo adapter | Provider-specific request/response mapping, response schema validation, status mapping, and mismatch detection. | Must not write Central PMS payment state, hide mismatches, log secrets, or accept unknown statuses as success. |
| Central PMS | Platform PaymentAttempt state, payment finality, PaymentConfirmation recording, provider outcome acceptance, and ExitAuthorization issuance through the explicit authorization path. | Must not accept pending/unknown/malformed/mismatched status-query results as finality or allow provider evidence to issue ExitAuthorization automatically. |
| Reconciliation diagnostics | Read-only comparison and workflow visibility over provider/core/gate evidence. | Must not mutate PaymentAttempt, PaymentConfirmation, provider outcome truth, ExitAuthorization, gate consumption, or vendor-paid state. |
| Gate Integration | Validation and consumption of issued ExitAuthorization. | Must not create PaymentConfirmation, finalize payment, query PayMongo, or issue ExitAuthorization. |

## Status Mapping Baseline

Exact PayMongo status names must be verified against PayMongo documentation and test fixtures before implementation. The table below is provisional and uses current canonical outcome concepts already present in code: `Succeeded`, `Failed`, `Expired`, `Cancelled`, and `PendingProvider`.

| PayMongo/source status | Normalized provider outcome | Terminal? | Report to Central PMS? | Retryable? | Creates PaymentConfirmation? | Issues ExitAuthorization? | Notes |
| --- | --- | --- | --- | --- | --- | --- | --- |
| `paid`, `succeeded`, successful checkout/payment status | `Succeeded` | Yes | Yes, if amount/currency/reference match | No | Only if Central PMS accepts and records finality | No | Report as verified provider-neutral outcome. |
| `failed`, `declined` | `Failed` | Yes | Only if Central PMS contract supports deterministic non-success outcome reports | No | No | No | Should not create successful payment finality. |
| `expired` | `Expired` | Yes | Only if Central PMS contract supports deterministic non-success outcome reports | No | No | No | Should close or mark failure according to Central PMS contract, not confirm payment. |
| `cancelled`, `canceled` | `Cancelled` | Yes | Only if Central PMS contract supports deterministic non-success outcome reports | No | No | No | Normalize spelling differences. |
| `pending`, `awaiting_payment`, `processing`, `active`, `unpaid` | `PendingProvider` | No | No | Yes | No | No | Keep as evidence/retry candidate only. |
| Unknown/unmapped status | `Unknown` or `PendingProvider` with failure code | No | No | Depends on provider HTTP result | No | No | Fail closed or route to reconciliation review. |
| Malformed response | Rejected evidence | No | No | No until corrected | No | No | Response must not be accepted as provider truth. |
| Timeout/network error | Query failure | No | No | Yes | No | No | Preserve retryable evidence; do not mutate finality. |
| Amount mismatch | Rejected evidence | No | No | No | No | No | Mismatch must become reconciliation review, not finality. |
| Currency mismatch | Rejected evidence | No | No | No | No | No | Mismatch must become reconciliation review, not finality. |
| Provider reference mismatch | Rejected evidence | No | No | No | No | No | Prevent replay across attempts/sessions. |

## Evidence and Persistence Baseline

The baseline DDL contains `payments.provider_status_queries` with these relevant fields:

- `provider_status_query_id`
- `payment_attempt_id`
- `provider_session_id`
- `payment_rail_id`
- `provider_transaction_ref`
- `query_status`
- `provider_result_status`
- `http_status_code`
- `request_hash`
- `response_hash`
- `response_storage_ref`
- `failure_reason_code`
- `requested_at`
- `completed_at`
- `correlation_id`
- `created_at`
- `created_by_service_identity_id`

The enum `payments.provider_status_query_status_enum` currently includes `REQUESTED`, `COMPLETED`, `FAILED`, `TIMEOUT`, and `INCONCLUSIVE`.

Recommended evidence fields for the runtime design:

- `provider_session_id`
- `provider_status_query_id`, if this DDL table is used
- provider reference / checkout session id / payment id
- source provider status
- normalized provider status
- amount
- currency
- payload hash
- correlation id
- queried timestamp
- verification status
- retryable flag
- error code/message
- response storage reference, only if the existing evidence policy supports raw payload retention

Rules:

- Do not store secrets.
- Do not log provider credentials.
- Do not log raw authorization headers.
- Raw payload storage must follow the current provider evidence policy.
- Prefer payload hash and controlled normalized fields when raw payload contains sensitive data.
- If the current `payments.provider_status_queries` table cannot support amount/currency/reference mismatch details cleanly, treat that as a design question for a separate schema slice.

## Trigger Model

| Trigger | MVP? | Notes |
| --- | --- | --- |
| Internal/manual status query for one known provider session | Yes | Controlled service-side trigger for support/reconciliation. Must be authenticated and audited. |
| Scheduled retry for ambiguous provider sessions | Later | Requires bounded retry, rate limits, idempotency, and operational controls first. |
| Payment Orchestrator background reconciliation poller | Later | Should not be broad or unbounded. Needs provider rate-limit policy and duplicate protection. |
| WebPay user status refresh/polling | Future | WebPay should poll ExitPass state, not PayMongo directly. |
| Support/operator manual status check | MVP candidate | Read/write impact must be explicit: query evidence only unless verified terminal report is accepted by Central PMS. |

Recommended MVP: internal/manual or controlled service-side status query only. Do not expose public WebPay direct provider query and do not add a broad scheduler until idempotency, rate limits, and evidence persistence are in place.

## Idempotency and Replay Rules

- Same provider reference plus same normalized outcome must be idempotent.
- Same provider reference with conflicting amount, currency, status, payment attempt, or provider session must be rejected or routed to reconciliation review.
- Repeated successful terminal status must not double-confirm.
- Repeated successful terminal status must not double-finalize.
- Repeated successful terminal status must not issue ExitAuthorization.
- Status-query-derived outcome and webhook-derived outcome must converge deterministically.
- If callback-derived finality has already been accepted, identical status-query evidence is idempotent evidence.
- If callback-derived finality and status-query evidence conflict, create or surface a reconciliation exception rather than silently mutating platform state.
- Provider references must not be replayable across different PaymentAttempts.
- Payload hash or provider event/reference identity should be used to detect duplicate/replayed evidence where available.

## Central PMS Reporting Contract

Payment Orchestrator should use the existing verified provider outcome internal endpoint if it remains the correct contract:

- `POST /v1/internal/payments/outcome`
- `X-Correlation-Id`
- `Idempotency-Key`
- existing service identity/internal auth as required by current Central PMS configuration

Rules:

- Report only verified terminal provider-neutral outcomes.
- Never report pending, unknown, timeout, malformed, amount-mismatched, currency-mismatched, or reference-mismatched results as finality.
- Central PMS decides finality and PaymentConfirmation creation.
- Payment Orchestrator does not create platform PaymentConfirmation.
- Payment Orchestrator does not issue ExitAuthorization.
- If Central PMS rejects a verified terminal report because it conflicts with existing evidence, Payment Orchestrator must preserve evidence for retry/reconciliation rather than declaring platform finality.

## Failure Handling

| Failure | Handling |
| --- | --- |
| PayMongo timeout | Mark query `TIMEOUT` or retryable failure evidence. Do not report finality. |
| PayMongo 5xx | Mark retryable failure evidence. Do not report finality. |
| PayMongo 4xx | Mark non-retryable or configuration/reference failure based on status code. Do not report finality. |
| Invalid provider reference | Reject deterministically and route to reconciliation/support review. |
| Missing provider session | Reject before PayMongo call unless a controlled reference-only support flow is explicitly approved. |
| Amount mismatch | Reject as mismatch evidence. Do not report finality. |
| Currency mismatch | Reject as mismatch evidence. Do not report finality. |
| Unknown status | Fail closed or mark inconclusive. Do not report finality. |
| Malformed JSON | Reject as malformed evidence. Do not report finality. |
| Duplicate/replay | Return existing status-query/outcome result or deterministic duplicate response. Do not duplicate finality/confirmation. |
| Central PMS report failure after verified terminal status | Keep provider evidence available for retry/reconciliation. Do not declare platform finality in Payment Orchestrator. |

## Security Requirements

- PayMongo API key/secret must come only from approved secret store or environment configuration.
- Do not commit PayMongo API keys, webhook secrets, or raw credentials.
- Do not log secrets.
- Do not include raw secret-bearing headers in diagnostics.
- Verify the PayMongo base URL, TLS scheme, and environment mode.
- Validate provider response schema before normalization.
- Require or generate correlation id according to the current Payment Orchestrator service convention.
- Service-to-service calls to Central PMS must use existing internal auth, service identity, mTLS, or RBAC convention.
- Do not expose a public endpoint that accepts arbitrary provider reference and returns payment finality.
- Mask provider references in logs where full values are not operationally required.

## Rate Limiting and Operational Safety

- Avoid aggressive polling.
- Respect PayMongo rate limits.
- Use bounded retry and backoff.
- Apply per-provider-session maximum query attempts.
- Stop polling after terminal status is accepted or terminal non-success is deterministically handled.
- Treat stale pending sessions as reconciliation review candidates.
- Audit operator/manual query triggers if implemented.
- Bound batch size, page size, scan windows, and concurrency before any scheduler/poller is introduced.

## Observability

Log, trace, or metric events should cover:

- status query attempted
- status query succeeded
- status query failed
- normalized status
- retryable versus non-retryable failure
- report-to-Central-PMS attempted
- report-to-Central-PMS succeeded
- report-to-Central-PMS failed
- correlation id
- masked provider reference

Never log:

- PayMongo API key
- webhook secret
- raw authorization header
- database password
- secret-bearing payload fields

## Test Plan for the Next Implementation Slice

Payment Orchestrator unit tests:

- successful terminal status maps to verified outcome
- failed, expired, and cancelled statuses map deterministically
- pending/processing does not report finality
- unknown/unmapped status fails closed
- timeout is retryable and does not report finality
- malformed response fails closed
- amount mismatch does not report finality
- currency mismatch does not report finality
- provider reference mismatch does not report finality
- duplicate status query does not double-report

Payment Orchestrator integration tests:

- verified terminal status reports once to Central PMS
- pending, timeout, malformed, and mismatch results do not call Central PMS
- Central PMS report failure leaves retryable evidence
- duplicate status query replay returns deterministic result

Central PMS contract tests:

- status-query-derived verified outcome follows the same finality rules as callback-derived verified outcome
- duplicate/replay remains deterministic
- provider reference cannot be replayed across attempts
- no automatic ExitAuthorization issuance
- non-success status-query-derived outcomes do not create successful PaymentConfirmation

## Implementation Sequencing

Recommended follow-up slices:

- #314 Add PayMongo status-query adapter/client tests and mapping model. Completed baseline names: `ProviderStatusQueryCommand`, `ProviderStatusQueryResult`, `RetrieveCheckoutSessionStatusAsync`, and `QueryProviderSessionStatusAsync`.
- #315 Add controlled Payment Orchestrator status-query handler.
- #316 Add status-query persistence/evidence support if schema already supports it; otherwise design a DB change separately.
- #317 Add Central PMS verified outcome contract tests for status-query source.
- #318 Add reconciliation diagnostics visibility for status-query evidence.

## Open Decisions

- Exact PayMongo status names to support for checkout sessions and related payment objects.
- Whether status query should retrieve checkout session, payment, payment intent, or a provider-specific combination.
- Whether status query result should persist raw payload or only hash plus normalized fields.
- Whether an internal manual status-query endpoint is needed in the MVP.
- Whether scheduler/poller support is MVP or post-MVP.
- Maximum retry count and backoff policy.
- Callback-versus-status-query precedence when both evidence sources exist.
- Whether `payments.provider_status_queries` fully supports this use case or needs a separate schema design.
- Whether successful terminal non-callback status evidence should be reported through the current `POST /v1/internal/payments/outcome` contract unchanged or with a source indicator.
- How reconciliation exceptions should display conflicts between callback and status-query evidence.

## Cross References

- #307 WebPay PayMongo correlation recovery
- #308 Payment finalization contract hardening
- #309 Payment confirmation contract hardening
- #310 Provider outcome duplicate/replay contract hardening
- #311 PayMongo callback verification contract hardening
- #312 PayMongo reconciliation diagnostics contract hardening
- `scripts/dev-data/webpay-paymongo-reconciliation-diagnostics.sql`
- `scripts/dev-data/persist-webpay-paymongo-reconciliation-run.sql`
- `src/Services/PaymentOrchestrator/src/ExitPass.PaymentOrchestrator.Infrastructure/Providers/PayMongo/PayMongoClient.cs`
- `src/Services/PaymentOrchestrator/src/ExitPass.PaymentOrchestrator.Infrastructure/Providers/PayMongo/PayMongoCheckoutAdapter.cs`
- `src/Services/PaymentOrchestrator/src/ExitPass.PaymentOrchestrator.Infrastructure/Infrastructure/CentralPmsPaymentOutcomeReporter.cs`
