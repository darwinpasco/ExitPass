# Passing Normal Fiscal Issuance UAT Baseline

This folder preserves the passing normal Central PMS to POS Server fiscal issuance UAT baseline from July 8, 2026.

## Result

- Central PMS `/run` executed exactly once.
- POS Server `POST /v1/fiscal-documents` returned HTTP `202`.
- POS Server created fiscal document `deac11e4-fc31-4c40-9a44-da690b9730ef`.
- Fiscal document number `SI-00000001-UAT` was assigned.
- Central PMS recorded fiscal reference `bf5288c9-426c-4f22-9567-ac5efac03ec0` as `FISCAL_ISSUANCE_RECORDED`.
- POS Server idempotency record `f24bbd01-110d-4632-9b0c-99e29721c54f` was persisted and linked to the fiscal document.
- Semantic hash was `ea863d4f8dc2c11e061236bec63855a26e896e700b4de92e5666bf8ee78cd38d`.
- The passing result required aligned disposable POS Server controlled-code seed data.

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
- `central-fiscal-reference-after-run.txt`
- `posserver-fiscal-documents-after-run.txt`
- `posserver-idempotency-records-after-run.txt`
- `posserver-post-log-lines.txt`
- `pre-run-central-db-check.txt`
- `pre-run-posserver-seed-check.txt`
- `posserver-controlled-code-coverage-before-run.txt`
- `forbidden-side-effect-counts-after-run.txt`
