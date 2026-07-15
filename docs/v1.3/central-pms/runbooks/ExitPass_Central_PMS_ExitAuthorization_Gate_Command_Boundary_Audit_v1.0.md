# ExitPass Central PMS ExitAuthorization Gate Command Boundary Audit v1.0

## Result

PASSED.

This audit found an active Central PMS path for ExitAuthorization issuance and gate-facing ExitAuthorization consumption, but no active source path that creates gate commands or calls HikCentral/physical gate APIs. Gate command lifecycle, gate-consumed processing inbox, retry policy, and HikCentral gate action audit objects remain app-local DB patch scope and are not present in the canonical `exitpassdb_v1.2` generated SQL.

## Executive Summary

- ExitAuthorization issuance is active, DB-backed, and now hard-gated by payment finality and fiscal readiness before `core.issue_exit_authorization(...)` is called.
- Issuance creates/returns an ExitAuthorization record and publishes best-effort `ExitAuthorizationIssued` and fiscal-gating diagnostic events. It does not create a gate command, consume an authorization, call HikCentral, or open a gate.
- Gate-facing consumption is active through `/v1/gate/authorizations/{exitAuthorizationId}/consume`. It validates service identity and gate-device/site/lane assignment, calls `core.consume_exit_authorization(...)`, updates the canonical consumption row with gate metadata, and publishes/persists consumption events.
- No active Central PMS source creates `gates.gate_commands`, processes `GateAuthorizationConsumed` into a gate command, writes `gates.hikcentral_gate_action_audits`, or calls HikCentral gate/barrier APIs.
- Before gate command or HikCentral behavior is implemented, the gate command DB objects need canonical alignment and a no-live-gate adapter proof.

## Source Areas Inspected

| Area | Files/paths inspected |
| --- | --- |
| v1.3 central docs | `docs/v1.3/central-pms`, including fiscal issuance and aligned-DB ExitAuthorization proof runbooks |
| v1.3 operator-console docs | `docs/v1.3/operator-console`, focused on no-gate/no-HikCentral safety assertions |
| v1.3 continuity docs | `docs/v1.3/continuity`, focused on manual release, degraded behavior, and gate/exit authority boundaries |
| Central PMS source | `src/Services/CentralPms/src`, especially payment handlers, API endpoints, gateways, eventing, security, and fiscal gating |
| Central PMS tests | Relevant issue/consume API, handler, and DB routine tests under `src/Services/CentralPms/tests` |
| App-local DB patches | `infra/db/patches`, retirement manifest, and gate command/HikCentral patch files |
| Canonical DB generated SQL | Read-only check of `D:\SourceCodes\exitpassdb_v1.2\build\generated\exitpass-full-object.generated.sql` for relevant object names |

## Current ExitAuthorization Issue Path

| Item | Current state | Classification |
| --- | --- | --- |
| `InternalPaymentAttemptExitAuthorizationEndpoints` | Maps `POST /v1/internal/payment-attempts/{paymentAttemptId}/issue-exit-authorization`; requires internal service mTLS, correlation header, and idempotency header. | Active runtime path |
| `IssueExitAuthorizationHandler` | Validates command, checks payment finality and fiscal readiness, blocks fail-closed when readiness blocks, then calls the issue gateway only when allowed. | Active runtime path |
| `IssueExitAuthorizationGateway` | Calls canonical typed routine `core.issue_exit_authorization(uuid, uuid, uuid, uuid, timestamptz)` after payment/payable-basis validation. | Active runtime path, canonical DB-backed |
| `ExitAuthorizationIssuedPayload` | Best-effort integration event payload after successful issuance. | Active event/audit surface |
| `ExitAuthorizationFiscalGatingShadowObservedPayload` | Diagnostic fiscal gating observation emitted during issue preflight. | Active diagnostic/audit surface |

When ExitAuthorization is issued today:

- It creates or reuses a DB-backed `core.exit_authorizations` row through the canonical typed routine.
- It returns token/status/timestamps to the internal caller.
- It publishes best-effort issue and fiscal-gating events.
- It does not create a gate command.
- It does not call HikCentral.
- It does not consume the authorization.
- It does not open a gate or barrier.

## Current Gate Command / Gate Consumption Objects

| Item | Current state | Classification |
| --- | --- | --- |
| `GateExitAuthorizationConsumeEndpoints` | Maps `POST /v1/gate/authorizations/{exitAuthorizationId}/consume`; requires internal service mTLS, `X-Correlation-Id`, `X-Service-Identity-Id`, and `X-Gate-Device-Id`. | Active runtime path |
| `GateDeviceIdentityValidator` | Validates active `DEVICE` service identity, active gate device, same-site assignment, active assignment, and outbound/bidirectional lane posture. | Active runtime path |
| `ConsumeExitAuthorizationHandler` | Validates command, calls consume gateway, publishes `GateAuthorizationConsumed`; rejected duplicate consume publishes `DuplicateGateConsumeRejected`. | Active runtime path |
| `ConsumeExitAuthorizationGateway` | Calls `core.consume_exit_authorization(uuid, uuid, uuid, timestamptz)`; updates `gates.gate_authorization_consumptions` with gate device/site/lane metadata; reads handoff facts. | Active runtime path, canonical DB-backed |
| `DurableIntegrationEventPublisher` | Persists `GateAuthorizationConsumed` and duplicate-rejected outcomes into `gates.gate_events`. | Active audit/readback surface |
| `GateAuthorizationConsumedPayload` | Carries consumed authorization, consumption ID, parking/payment/tariff, gate device, lane, site, vendor system, status, timestamp, and correlation ID. | Active event payload |
| `gates.gate_authorization_consumed_processing` patch | Durable processing inbox for turning consumed events into gate actions. No active Central PMS source processor found. | App-local patch only / future-deferred |
| `gates.gate_commands` patch | Vendor-neutral gate command lifecycle. No active Central PMS source creates commands. | App-local patch only / future-deferred |
| gate command retry policy patch | Adds retry/failure policy columns and indexes for `gates.gate_commands`. No active command executor found. | App-local patch only / future-deferred |

What consumes an ExitAuthorization today:

- The active gate-facing API endpoint can consume it through `ConsumeExitAuthorizationHandler` and `ConsumeExitAuthorizationGateway`.
- The canonical typed routine `core.consume_exit_authorization(...)` is the state transition boundary.
- The consume path is one-time and fail-closed for missing, expired, not-issued, or already-consumed authorizations.
- The consume path creates/updates consumption/audit state, but it does not create `gates.gate_commands` and does not call a physical gate adapter.

## Current HikCentral / Gate Adapter Boundary

| Item | Current state | Classification |
| --- | --- | --- |
| HikCentral vendor session projection code | Exists for parking/session projection and normalization. It is not a gate-open path. | Active projection path, not gate command |
| `ExitPass_HikCentralGateActionAudit_v1.2.sql` | Defines safe request/response audit metadata for future HikCentral gate action calls. Excludes raw request/response bodies and secrets. | App-local patch only / future-deferred |
| HikCentral gate command adapter/service | No active Central PMS source object found. | Not implemented |
| Physical gate/barrier open call | No active Central PMS source call found. | Not implemented |

The current gate boundary stops at Central PMS authorization consumption and event/audit persistence. There is no active HikCentral gate adapter, no command executor, and no physical barrier operation in the inspected Central PMS source.

## Current DB Object and Patch Posture

| Object/patch | Current posture |
| --- | --- |
| `core.exit_authorizations` | Canonical DB-backed; used by issue/consume paths. |
| `core.issue_exit_authorization(...)` | Canonical DB-backed typed routine; app-local patch retired as canonical-superseded. |
| `core.consume_exit_authorization(...)` | Canonical DB-backed typed routine; app-local patch retired as canonical-superseded. |
| `gates.gate_authorization_consumptions` | Canonical DB-backed; used by consume routine/gateway and tests. |
| `gates.gate_devices` | Canonical DB-backed; used by gate identity validation. |
| `gates.gate_events` | Canonical DB-backed; used by durable event publisher for consume/rejected outcomes. |
| `ExitPass_GateAuthorizationConsumedProcessingInbox_v1.2.sql` | Classified `STILL_ACTIVE` in the app-local patch retirement manifest, but not proven canonical and no active processor found. |
| `ExitPass_GateCommandLifecycle_v1.2.sql` | Classified `STILL_ACTIVE`; canonical generated SQL check found `gates.gate_commands: 0`. |
| `ExitPass_GateCommandRetryFailurePolicy_v1.2.sql` | Classified `STILL_ACTIVE`; depends on app-local `gates.gate_commands`. |
| `ExitPass_HikCentralGateActionAudit_v1.2.sql` | Classified `STILL_ACTIVE`; canonical generated SQL check found `gates.hikcentral_gate_action_audits: 0`. |

Read-only canonical generated SQL check found:

- `core.issue_exit_authorization(...)`: present.
- `core.consume_exit_authorization(...)`: present.
- `gates.gate_authorization_consumptions`: present.
- `gates.gate_devices`: present.
- `gates.gate_events`: present.
- `gates.gate_commands`: not present.
- `gates.gate_authorization_consumed_processing`: not present.
- `gates.hikcentral_gate_action_audits`: not present.

## Current Audit / Reconciliation Posture

- Issue path publishes `ExitAuthorizationIssued` best-effort after successful issue.
- Issue path publishes fiscal-gating diagnostic observations.
- Consume path publishes `GateAuthorizationConsumed` after successful consume.
- Duplicate consume rejection publishes `DuplicateGateConsumeRejected`.
- `DurableIntegrationEventPublisher` persists gate consume and duplicate-denied facts into `gates.gate_events`.
- Reconciliation tests reference gate consumption tables as read-only facts and assert reconciliation paths do not mutate gate consumption rows.
- No active report/read model for gate command lifecycle or HikCentral action audit was found in this audit.

## Safety Boundaries Already Proven

- Fiscal-before-ExitAuthorization hard blocking prevents issue when payment finality or fiscal readiness is missing/unsafe.
- Discounted payment/fiscal runtime proofs asserted no gate open, no gate consumption, and no HikCentral call.
- Issue replay returns the same ExitAuthorization and does not duplicate rows.
- Consume replay fails closed after the authorization is consumed.
- Consume endpoint enforces gate service identity and active gate-device/site/lane assignment before DB consumption.
- Rejected consume attempts do not publish `GateAuthorizationConsumed`.

## Gaps and Risks

- Gate command objects are app-local patch-only and not canonical DB-backed.
- No active processor was found to consume `GateAuthorizationConsumed` events into a durable processing inbox.
- No active code creates `gates.gate_commands`.
- No active HikCentral gate adapter or safe fake/local adapter was found.
- No active runtime proof exists for command lifecycle, retry policy, or HikCentral action audit writes.
- Physical gate safety has not been proven because no gate command/adapter implementation exists yet.
- A future implementation must avoid silently treating ExitAuthorization issuance as physical gate release.

## Recommended Next High-Value Slices

1. Canonical DB alignment audit for gate command, gate-consumed processing inbox, retry policy, and HikCentral action audit patches.
2. Read-only gate boundary/status inventory API or diagnostic that reports ExitAuthorization, consumption, gate events, and absence/presence of command rows.
3. Gate command creation contract tests using only fake/local event input and app-local or canonicalized command tables.
4. Gate consumption idempotency contract hardening around `GateAuthorizationConsumed` event persistence and duplicate-denied audit.
5. HikCentral adapter boundary contract with a fake/local adapter and no-live-gate guard.
6. No-live-gate safety smoke proving fake adapter execution cannot reach production HikCentral or physical gate endpoints.
7. Controlled local gate command runtime proof after DB objects and fake adapter are canonicalized.

## Explicit Non-Goals

This audit did not:

- add gate command behavior;
- consume any authorization at runtime;
- call HikCentral;
- open a gate or barrier;
- modify `exitpassdb_v1.2`;
- modify POS Server;
- change ExitAuthorization behavior;
- change fiscal hard blocking;
- add UI;
- add tests or run broad test suites.

## Files Changed

- `docs/v1.3/central-pms/runbooks/ExitPass_Central_PMS_ExitAuthorization_Gate_Command_Boundary_Audit_v1.0.md`

## Validation

| Command | Result |
| --- | --- |
| `git diff --check` | PASSED. |
| `git status --short --branch --untracked-files=all` | PASSED/reviewed. Only this audit document is untracked/changed. |
