# Passing Same-Key Same-Hash Replay Evidence

This folder preserves the passing same-key/same-hash idempotent replay validation for normal Central PMS to POS Server fiscal issuance. This evidence is separate from the passing first-issuance baseline.

## Result

- Replay `/run` executed exactly once.
- POS Server `POST /v1/fiscal-documents` returned HTTP `202`.
- POS Server fiscal document count remained `1` before and after replay.
- POS Server idempotency record count remained `1` before and after replay.
- Fiscal document id remained `deac11e4-fc31-4c40-9a44-da690b9730ef`.
- Fiscal document number remained `SI-00000001-UAT`.
- Fiscal sequence value remained `1` before and after replay.
- No new fiscal number was allocated.
- Central PMS recorded `FISCAL_ISSUANCE_REPLAYED`.
- Result classification was `IDEMPOTENT_REPLAY`.
- Central PMS fiscal reference id was `a078eec1-a027-422b-87c0-9ec2a8523682`.

## Side Effects

Forbidden side-effect counts were zero for:

- `core.exit_authorizations`
- `gates.gate_authorization_consumptions`
- `gates.gate_events`
- `gates.gate_heartbeats`
- `operations.manual_gate_logs`

No source code, production schema, tests, runtime features, retry behavior, ExitAuthorization behavior, or gate behavior are part of this evidence package.

## Evidence Files

- `Result-Summary.md`
- `run-response.json`
- `run-evidence.json`
- `central-fiscal-reference-after-replay.txt`
- `posserver-baseline-before-replay.txt`
- `posserver-counts-sequence-before-replay.txt`
- `posserver-counts-sequence-immediate-before-replay.txt`
- `posserver-counts-sequence-after-replay.txt`
- `posserver-fiscal-documents-after-replay.txt`
- `posserver-idempotency-records-after-replay.txt`
- `posserver-post-log-lines.txt`
- `forbidden-side-effect-counts-after-replay.txt`
