# Passing Same-Key Same-Hash Replay UAT Evidence

## Scope

Controlled local replay validation for normal Central PMS to POS Server fiscal issuance. This evidence is separate from the passing first-issuance baseline.

- Branch: `test/central-pms-pos-server-normal-issuance-same-hash-replay`
- Central PMS DB: `centralpms_feq_retry_uat_local`
- POS Server DB: `posserver_api_smoke_validation_local`
- Central PMS URL: `http://localhost:5080`
- POS Server URL: `http://localhost:5000`
- Upstream finality reference: `CPS-POS-UAT:CPS-POS-UAT-20260703-DEV-ATC-001:newly_created:001`
- `/run` replay invocation count: `1`

## Pre-Run Baseline

- Existing POS Server fiscal document count for the UAT upstream finality reference: `1`
- Existing POS Server idempotency count for the UAT key/hash: `1`
- Existing fiscal document id: `deac11e4-fc31-4c40-9a44-da690b9730ef`
- Existing fiscal document number: `SI-00000001-UAT`
- Existing idempotency record id: `f24bbd01-110d-4632-9b0c-99e29721c54f`
- Existing semantic hash: `ea863d4f8dc2c11e061236bec63855a26e896e700b4de92e5666bf8ee78cd38d`
- Fiscal sequence value before replay: `1`
- Central PMS disposable DB was recreated, schema-restored, seeded, and had no fiscal issuance reference row before replay.

## Replay Result

- Central PMS `/run` executed exactly once.
- Central PMS attempted one POS Server POST to `/v1/fiscal-documents`.
- POS Server returned HTTP `202`.
- Central PMS status: `replay_recorded`.
- Result classification: `IDEMPOTENT_REPLAY`.
- Central PMS fiscal state: `FISCAL_ISSUANCE_REPLAYED`.
- Central PMS fiscal reference id: `a078eec1-a027-422b-87c0-9ec2a8523682`.
- No error code or error posture was returned.

## Post-Run State

- POS Server fiscal document count for the UAT upstream finality reference after replay: `1`
- POS Server idempotency count for the UAT key/hash after replay: `1`
- Fiscal document id after replay: `deac11e4-fc31-4c40-9a44-da690b9730ef`
- Fiscal document number after replay: `SI-00000001-UAT`
- Fiscal sequence value after replay: `1`
- No new fiscal document number was allocated.
- No duplicate fiscal document row was created.
- POS Server idempotency record remained linked to fiscal document `deac11e4-fc31-4c40-9a44-da690b9730ef`.

## Central PMS Evidence

- Central PMS fiscal reference id: `a078eec1-a027-422b-87c0-9ec2a8523682`
- `pos_server_fiscal_document_id`: `deac11e4-fc31-4c40-9a44-da690b9730ef`
- `fiscal_document_number`: `SI-00000001-UAT`
- `result_classification`: `IDEMPOTENT_REPLAY`
- `fiscal_issuance_state`: `FISCAL_ISSUANCE_REPLAYED`
- Semantic hash: `ea863d4f8dc2c11e061236bec63855a26e896e700b4de92e5666bf8ee78cd38d`
- Semantic hash version: `sha256:v1`

## Forbidden Side Effects

All captured forbidden side-effect counts were zero:

- `core.exit_authorizations`
- `gates.gate_authorization_consumptions`
- `gates.gate_events`
- `gates.gate_heartbeats`
- `operations.manual_gate_logs`

No payment finality mutation, ExitAuthorization, gate behavior, fiscal number editing by Central PMS, manual fiscal document creation, FEQ retry, batch, or scheduler behavior was observed.

## Evidence Files

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

## Recommended Next Action

Preserve this as the same-key/same-hash idempotent replay evidence. A separate validation can cover same-key/different-hash conflict using disposable data and isolated evidence.
