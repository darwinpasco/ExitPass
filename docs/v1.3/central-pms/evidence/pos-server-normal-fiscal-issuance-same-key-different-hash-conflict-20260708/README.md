# Same-Key/Different-Hash Conflict Evidence

This folder preserves the passing normal Central PMS to POS Server same-key/different-hash idempotency conflict validation evidence from 2026-07-08.

This evidence is separate from the first-issuance baseline and same-key/same-hash replay evidence.

## Result

- Conflict `/run` executed exactly once.
- POS Server `POST /v1/fiscal-documents` returned HTTP `409`.
- Original semantic hash: `ea863d4f8dc2c11e061236bec63855a26e896e700b4de92e5666bf8ee78cd38d`
- Changed semantic hash: `f2c0fe56ab8718e957f3dce31bd8f29b194ab0f80b6863e8865768a4e6b02e24`
- POS Server fiscal document count remained `1 -> 1`.
- POS Server idempotency count remained `1 -> 1`.
- Fiscal sequence value remained `1 -> 1`.
- No new fiscal document was created.
- No new fiscal number was allocated.
- Central PMS recorded `FISCAL_ISSUANCE_CONFLICT`.
- Error code was `fiscal_document_idempotency_conflict`.
- Posture was `DO_NOT_RETRY_WITHOUT_REQUEST_CHANGE`.
- No POS Server fiscal document id or fiscal document number was recorded on the conflict reference.

## Forbidden Side Effects

Forbidden side-effect counts were zero for:

- `core.exit_authorizations`
- `gates.gate_authorization_consumptions`
- `gates.gate_events`
- `gates.gate_heartbeats`
- `operations.manual_gate_logs`

## Key Evidence

- `Result-Summary.md`
- `conflict-request.json`
- `run-command.txt`
- `run-response.json`
- `run-evidence.json`
- `central-fiscal-reference-after-conflict.txt`
- `posserver-fiscal-documents-after-conflict.txt`
- `posserver-idempotency-records-after-conflict.txt`
- `posserver-counts-sequence-before-conflict.txt`
- `posserver-counts-sequence-after-conflict.txt`
- `posserver-post-log-lines.txt`
- `forbidden-side-effect-counts-after-conflict.txt`
