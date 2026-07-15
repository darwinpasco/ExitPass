# ExitPass Central PMS Gate Command DB Alignment Audit v1.0

## Result

PASSED.

This focused, doc-only audit found that canonical `exitpassdb_v1.2` already contains the active ExitAuthorization issue/consume foundation and gate consumption facts used by Central PMS, but does not yet contain the app-local gate command processing inbox, vendor-neutral gate command lifecycle, gate command retry/failure policy, or HikCentral gate action audit objects. Those four app-local patches remain structurally useful but should be promoted in small canonical DB slices before any gate command runtime work.

## Executive summary

- Canonical `exitpassdb_v1.2` contains `core.exit_authorizations`, typed `core.issue_exit_authorization(...)`, typed `core.consume_exit_authorization(...)`, `gates.gate_authorization_consumptions`, `gates.gate_devices`, and `gates.gate_events`.
- The canonical gates object-source layout currently contains consumption/device/event/heartbeat objects and related enums, but no `gates.gate_authorization_consumed_processing`, no `gates.gate_commands`, and no `gates.hikcentral_gate_action_audits`.
- `Validate-V13CentralPmsAlignment.sql` validates the typed issue/consume routines but does not validate any gate command, consumed-processing, retry policy, or HikCentral gate action audit objects.
- The four app-local gate patches are still classified `STILL_ACTIVE` in the app-local DB patch retirement manifest.
- The safest next step is canonical DB object-source alignment only, beginning with the vendor-neutral command lifecycle and consumed-processing inbox, then retry/failure policy, then HikCentral audit, then validation updates.

## Source areas inspected

| Area | Files/paths inspected |
| --- | --- |
| App-local gate patches | `infra/db/patches/ExitPass_GateAuthorizationConsumedProcessingInbox_v1.2.sql`; `infra/db/patches/ExitPass_GateCommandLifecycle_v1.2.sql`; `infra/db/patches/ExitPass_GateCommandRetryFailurePolicy_v1.2.sql`; `infra/db/patches/ExitPass_HikCentralGateActionAudit_v1.2.sql` |
| App-local patch posture | `infra/db/patches/ExitPass_AppLocal_Db_Patch_Retirement_Manifest_v1.0.md` |
| Gate boundary audit | `docs/v1.3/central-pms/runbooks/ExitPass_Central_PMS_ExitAuthorization_Gate_Command_Boundary_Audit_v1.0.md` |
| Canonical DB object source | `D:\SourceCodes\exitpassdb_v1.2\objects\schemas\gates`; focused core issue/consume object files under `objects\schemas\core` |
| Canonical generated SQL | `D:\SourceCodes\exitpassdb_v1.2\build\generated\exitpass-full-object.generated.sql` |
| Canonical validation | `D:\SourceCodes\exitpassdb_v1.2\scripts\validation\Validate-V13CentralPmsAlignment.sql` |
| Focused Central PMS source check | Gate consume handler/gateway/event publisher and focused consume tests only, to confirm current terminology and no active command/HikCentral source path |

## Canonical DB objects already present

| Object | Canonical status | Notes |
| --- | --- | --- |
| `core.exit_authorizations` | Present | Canonical table stores issued ExitAuthorization rows with payment attempt, payment confirmation, hashed token, status, issue/expiry timestamps, correlation, and service-identity audit columns. |
| `core.issue_exit_authorization(uuid, uuid, uuid, uuid, timestamptz)` | Present | Typed routine creates or reuses an authorization for a confirmed payment attempt and returns a deterministic replay result for existing authorizations. |
| `core.consume_exit_authorization(uuid, uuid, uuid, timestamptz)` | Present | Typed routine enforces single-use consumption, rejects missing/expired/not-issued/already-consumed authorizations, writes consumption facts, and returns consumed status/timestamp. |
| `gates.gate_authorization_consumptions` | Present | Canonical table records consume status, gate device/site/lane scope, command-requested/result summary fields, failure detail, correlation, and audit columns. |
| `gates.gate_devices` | Present | Canonical table supports active gate device identity, site/lane binding, device code, vendor ref, serial number, status, and audit columns. |
| `gates.gate_events` | Present | Canonical table stores gate event facts, including consumption, exit authorization, site/lane/device, event type/status, payload references, source event ref, occurrence/receipt timestamps, and correlation. |
| Gates enums | Present | Includes consumption status, command result status, device status/type, event status/type, and heartbeat status enums. |

## App-local gate patches inspected

| Patch | Primary objects | Current retirement manifest posture |
| --- | --- | --- |
| `ExitPass_GateAuthorizationConsumedProcessingInbox_v1.2.sql` | `gates.gate_authorization_consumed_processing` plus processing key/event/consumption/status/correlation indexes | `STILL_ACTIVE` |
| `ExitPass_GateCommandLifecycle_v1.2.sql` | `gates.gate_commands` plus source-processing/event/consumption/status/correlation indexes | `STILL_ACTIVE` |
| `ExitPass_GateCommandRetryFailurePolicy_v1.2.sql` | Additive retry/failure columns, constraints, backfill, and retry/terminal indexes on `gates.gate_commands` | `STILL_ACTIVE` |
| `ExitPass_HikCentralGateActionAudit_v1.2.sql` | `gates.hikcentral_gate_action_audits` plus command/source/consumption/authorization/vendor/outcome/time indexes | `STILL_ACTIVE` |

## Object-level gap table

| Object / capability | Canonical DB | App-local patch | Classification | Recommendation |
| --- | --- | --- | --- | --- |
| `core.exit_authorizations` | Present | Retired app-local issue patch previously superseded | `DEPRECATED_OR_DUPLICATE` for app-local patch scope | No new work. Keep canonical ownership. |
| `core.issue_exit_authorization(...)` | Present | Retired app-local issue patch previously superseded | `DEPRECATED_OR_DUPLICATE` for app-local patch scope | No new work. Keep canonical ownership. |
| `core.consume_exit_authorization(...)` | Present | Retired app-local consume patch previously superseded | `DEPRECATED_OR_DUPLICATE` for app-local patch scope | No new work. Keep canonical ownership. |
| `gates.gate_authorization_consumptions` | Present | Current command summary columns already exist canonically | `PROMOTE_LATER` only for future command-result refinements | Do not replace with command table. Use as consume fact and summary surface. |
| `gates.gate_devices` | Present | Used by gate identity validation | `DEPRECATED_OR_DUPLICATE` for app-local patch scope | No new work. Keep canonical ownership. |
| `gates.gate_events` | Present | Used by durable event publisher for consume/rejected outcomes | `PROMOTE_LATER` only for future command event type/status expansion | Do not overload with command lifecycle state. |
| `gates.gate_authorization_consumed_processing` | Missing | Present in app-local processing inbox patch | `PROMOTE_NOW` | Promote as a durable idempotent handoff inbox before runtime processing. Add object-source files and validation. |
| `gates.gate_commands` | Missing | Present in app-local lifecycle patch | `PROMOTE_NOW` | Promote as vendor-neutral command lifecycle before any executor. Fold retry columns into initial canonical table if possible. |
| Gate command retry/failure columns and indexes | Missing because `gates.gate_commands` is missing | Present as additive patch | `PROMOTE_NOW` with command lifecycle | Promote with `gates.gate_commands`; avoid a separate canonical backfill if introduced before production rows exist. |
| `gates.hikcentral_gate_action_audits` | Missing | Present in app-local HikCentral audit patch | `PROMOTE_LATER` | Promote after vendor-neutral command table shape is stable; keep vendor-specific audit isolated and secret-free. |

## Patch-by-patch assessment

### `ExitPass_GateAuthorizationConsumedProcessingInbox_v1.2.sql`

The patch creates `gates.gate_authorization_consumed_processing` with a durable inbox shape for processing `GateAuthorizationConsumed` handoffs. It stores processing identity/key, event identity/type/ref, consumption/authorization/session/payment/tariff identifiers, gate device identity, lane/site/vendor scope, consumed timestamp, correlation, processing status/result, attempt counters, first/last/processed timestamps, failure metadata, and audit timestamps.

Structural assessment:

- Schema target is valid: canonical DB already owns the `gates` schema.
- Table name still matches current source terminology: current Central PMS emits `GateAuthorizationConsumed` events and carries `GateAuthorizationConsumptionId`.
- Status model is string-check based: `PROCESSING`, `PROCESSED`, `FAILED`; acceptable for first promotion, though canonical object-source may prefer an enum if the DB repo standard requires it.
- Indexes are useful and focused: unique `(processing_key, event_type)`, optional `event_id`, consumption, status, and correlation.
- Dependencies are mostly implicit: the patch does not declare foreign keys to canonical `gates.gate_authorization_consumptions`, `core.exit_authorizations`, `core.parking_sessions`, `core.payment_attempts`, tariff snapshots, devices, lanes, sites, or vendor systems.
- Design decision before promotion: decide whether canonical should add foreign keys for core identifiers or intentionally keep this as an integration inbox tolerant of late/out-of-order event processing.

Classification: `PROMOTE_NOW`.

### `ExitPass_GateCommandLifecycle_v1.2.sql`

The patch creates `gates.gate_commands` as a vendor-neutral command lifecycle table. It stores command identity/type, source processing/event links, authorization/consumption/session/payment/tariff identifiers, gate device identity, lane/site/vendor scope, lifecycle status, attempt count, request/start/complete timestamps, failure metadata, correlation, and audit timestamps.

Structural assessment:

- Schema target is valid: canonical DB already owns the `gates` schema.
- Table name matches the current boundary-audit terminology, but no active Central PMS source creates rows yet.
- Status model is string-check based: `REQUESTED`, `IN_PROGRESS`, `SUCCEEDED`, `FAILED`, `RETRYABLE`, `TERMINAL_FAILURE`.
- Idempotency is modeled by unique `(source_processing_id, command_type)`, which is appropriate if the consumed-processing inbox is the command creation source.
- The `started_at` and `completed_at` checks encode a clear lifecycle invariant.
- Dependencies are implicit except for later HikCentral audit: no foreign keys are declared to the processing inbox, consumption row, authorization, session, payment attempt, tariff snapshot, gate device, lane, site, or vendor system.
- Design decision before promotion: decide whether command status should remain a constrained string or become a canonical enum; also decide whether to include retry/failure columns directly in the initial canonical command table.

Classification: `PROMOTE_NOW`.

### `ExitPass_GateCommandRetryFailurePolicy_v1.2.sql`

The patch is additive to `gates.gate_commands`. It adds `max_attempts`, `retry_policy_code`, `last_attempted_at`, `next_attempt_at`, `terminal_failure_at`, `last_failure_code`, and `last_failure_reason`; backfills existing rows; makes required policy fields non-null with defaults; adds max-attempt, attempt-policy, retryable-next-attempt, and terminal-failure timestamp constraints; and adds partial indexes for retryable and terminal-failure queues.

Structural assessment:

- The patch is valid only after `gates.gate_commands` exists.
- The policy fields are compatible with the command lifecycle statuses in the command patch.
- Because canonical DB has no command rows yet, promotion should fold these columns and constraints into the first canonical command table definition rather than preserve an app-local-style backfill migration as the primary design.
- `last_attempted_at NOT NULL` currently backfills from `started_at` or `requested_at`; for a fresh canonical table this requires a clear insert contract for `REQUESTED` commands.
- Design decision before promotion: define whether `max_attempts`/`retry_policy_code` belong on every command row or should reference a future policy table. For the next small DB slice, row-level columns are the safer minimal shape.

Classification: `PROMOTE_NOW` with `gates.gate_commands`.

### `ExitPass_HikCentralGateActionAudit_v1.2.sql`

The patch creates `gates.hikcentral_gate_action_audits` for secret-free HikCentral request/response metadata. It references `gates.gate_commands(command_id)` and stores command/source/authorization/consumption/session/payment/tariff identifiers, gate device identity, `door_index_code`, lane/site/vendor scope, vendor identity, operation, request method/path/hash/signed header list, request/vendor correlations, HTTP/vendor outcome fields, retryability/failure booleans, duration/timeout/unavailable/transport metadata, request/response timestamps, and created timestamp.

Structural assessment:

- The table cannot be promoted before `gates.gate_commands`.
- The `gate_command_id` foreign key is useful and should remain.
- The patch correctly excludes raw request/response bodies, credentials, signatures, and secret-bearing header values.
- Vendor constraint currently fixes `vendor_code = 'HikCentral'`, while the current Central PMS vendor provider code elsewhere is typically uppercase `HIKCENTRAL`; this naming should be settled before runtime use.
- `request_method = 'POST'` is appropriate for a first gate action audit if the adapter contract is POST-only, but this should be confirmed against the future fake/local adapter boundary before production use.
- Design decision before promotion: decide whether a vendor-specific audit table should live in `gates` or whether a provider-neutral gate adapter audit table plus vendor-specific columns would age better. For now, promote later after command shape is stable.

Classification: `PROMOTE_LATER`.

## Recommended canonical DB promotion sequence

1. Gate command DB object-source alignment: add `gates.gate_commands` to `exitpassdb_v1.2` object source with lifecycle, idempotency, retry/failure columns, constraints, indexes, comments, generated SQL refresh, and a focused validation check.
2. Gate consumed processing inbox alignment: add `gates.gate_authorization_consumed_processing` object source with its idempotency/status indexes and comments.
3. Retry/failure policy alignment: if not folded into step 1, add the retry columns, constraints, defaults, and queue indexes as the next additive DB slice.
4. HikCentral gate action audit alignment: add `gates.hikcentral_gate_action_audits` only after the command table is canonical and after vendor-code casing/adapter audit shape is confirmed.
5. Validation script updates: extend `Validate-V13CentralPmsAlignment.sql` or add a focused gate alignment validation to assert the new command/inbox/audit objects and critical indexes/constraints.
6. App repo read-only inventory/proof later: after canonical generated SQL includes the objects, add app-side read-only inventory and contract proofs without creating live gate behavior.

## Risks and design decisions

- Foreign-key posture: the app-local command and processing tables mostly store IDs without foreign keys. Canonical promotion should explicitly decide between strong relational enforcement and integration-inbox tolerance for event ordering.
- Status representation: app-local patches use `varchar` plus check constraints. Canonical gates objects already use enums for several gate states, so status enum promotion should be considered before object-source implementation.
- Retry policy shape: row-level `max_attempts` and `retry_policy_code` are simple and auditable, but a future policy table could be needed if multiple per-site/vendor retry policies become operational.
- Vendor naming: `HikCentral` in the audit patch should be reconciled with existing uppercase `HIKCENTRAL` provider terminology before runtime use.
- Command summary duplication: `gates.gate_authorization_consumptions` already has command summary columns. `gates.gate_commands` should be the lifecycle source of truth, while consumption rows remain consume facts and optional read summary.
- Physical gate safety: canonical DB objects must not imply that issuance or consumption opens a physical gate. Runtime remains blocked until fake/no-live-gate proofs exist.

## Explicit non-goals

- No gate command processor.
- No HikCentral adapter.
- No physical gate open.
- No consume-to-command runtime path.
- No retries/executor.
- No production endpoint or credential assumptions.
- No POS Server changes.
- No Operator Console UI changes.
- No app source changes.
- No canonical DB repo changes in this audit.
- No runtime proof.
- No branch-protection or governance docs.
- No broad test suite.

## Recommended next small slices

1. `exitpassdb_v1.2`: add canonical `gates.gate_commands` object-source files with retry/failure columns included in the initial table shape, generated SQL refresh, and focused validation.
2. `exitpassdb_v1.2`: add canonical `gates.gate_authorization_consumed_processing` object-source files and focused validation.
3. `exitpassdb_v1.2`: add or refine gate command retry/failure policy validation, especially retryable/terminal partial indexes and lifecycle constraints.
4. `exitpassdb_v1.2`: add `gates.hikcentral_gate_action_audits` after vendor naming and audit shape decisions are closed.
5. ExitPass app later: add read-only gate command/status inventory against canonical DB.
6. ExitPass app later: add gate command creation contract tests from a fake consumed event.
7. ExitPass app later: add gate command idempotency tests.
8. ExitPass app later: add fake/local HikCentral adapter boundary contract.
9. ExitPass app later: add no-live-gate safety smoke.
10. ExitPass app later: run a controlled local gate command runtime proof.

## Files changed

- `docs/v1.3/central-pms/db-alignment/ExitPass_Central_PMS_Gate_Command_DB_Alignment_Audit_v1.0.md`

## Validation

| Command | Result |
| --- | --- |
| `git diff --check` | PASSED. |
| `git status --short --branch --untracked-files=all` | PASSED/reviewed. App repo branch is `docs/gate-command-db-alignment-audit`; only this audit document is changed in the app repo. |
