# Passing Normal Fiscal Issuance UAT Baseline

## Scope

Controlled local UAT rerun for normal Central PMS to POS Server fiscal issuance. This is the passing normal issuance UAT baseline.

- Branch: `test/central-pms-pos-server-normal-issuance-clean-rerun`
- Central PMS DB: `centralpms_feq_retry_uat_local`
- POS Server DB: `posserver_api_smoke_validation_local`
- Central PMS URL: `http://localhost:5080`
- POS Server URL: `http://localhost:5000`
- Upstream finality reference: `CPS-POS-UAT:CPS-POS-UAT-20260703-DEV-ATC-001:newly_created:001`
- `/run` invocation count: `1`
- Passing result required aligned disposable POS Server controlled-code seed data.

## Pre-Run Guards

- Recreated only `centralpms_feq_retry_uat_local`.
- Restored schema-only from `exitpass_v12_dev`.
- Seeded only the original approved Central PMS UAT identity.
- Confirmed no pre-existing Central PMS fiscal reference for the UAT upstream finality reference.
- Confirmed Central PMS and POS Server were reachable.
- Confirmed POS Server site, fiscal identity, sequence policy, sequence state, and active controlled-code coverage for `...103` and `...107` through `...112`.

## Run Result

- Central PMS `/run` accepted the request and passed readiness validation.
- Central PMS attempted one POS Server POST to `/v1/fiscal-documents`.
- POS Server returned HTTP `202`.
- POS Server created fiscal document `deac11e4-fc31-4c40-9a44-da690b9730ef`.
- Fiscal document number `SI-00000001-UAT` was assigned.
- POS Server idempotency record `f24bbd01-110d-4632-9b0c-99e29721c54f` was persisted and linked to fiscal document `deac11e4-fc31-4c40-9a44-da690b9730ef`.
- Central PMS status: `newly_created_recorded`.
- Result classification: `NewlyCreated`.
- Central PMS fiscal reference `bf5288c9-426c-4f22-9567-ac5efac03ec0` was recorded as `FISCAL_ISSUANCE_RECORDED`.
- No error code or error posture was returned.

## Key IDs

- Central PMS fiscal issuance reference id: `bf5288c9-426c-4f22-9567-ac5efac03ec0`
- POS Server fiscal document id: `deac11e4-fc31-4c40-9a44-da690b9730ef`
- Fiscal document number: `SI-00000001-UAT`
- Fiscal identity id: `10000000-0000-4000-8000-000000000701`
- Fiscal sequence policy id: `10000000-0000-4000-8000-000000000803`
- Fiscal sequence value: `1`
- Idempotency record id: `f24bbd01-110d-4632-9b0c-99e29721c54f`
- Idempotency scope: `fiscal_document_creation:10000000000040008000000000000201:10000000000040008000000000000103`
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
- `central-fiscal-reference-after-run.txt`
- `posserver-fiscal-documents-after-run.txt`
- `posserver-idempotency-records-after-run.txt`
- `posserver-post-log-lines.txt`
- `pre-run-central-db-check.txt`
- `pre-run-posserver-seed-check.txt`
- `posserver-controlled-code-coverage-before-run.txt`
- `forbidden-side-effect-counts-after-run.txt`

## Recommended Next Action

Preserve this evidence as the passing normal Central PMS to POS Server fiscal issuance UAT baseline. A follow-up can validate same-key/same-hash replay using the same disposable identity, but do not mix that with this one-run newly-created evidence.
