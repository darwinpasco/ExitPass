# Central PMS to POS Server Same-Key/Different-Hash Conflict Validation

## Result

Passed. The same upstream finality reference was reused with a controlled semantic request fact change, and POS Server rejected the request as an idempotency conflict without creating a second fiscal document or allocating a new fiscal number.

This evidence is separate from the passing first-issuance baseline and same-key/same-hash replay evidence.

## Invocation

- Endpoint invoked: `POST http://localhost:5080/internal/controlled-uat/fiscal-issuance/run`
- Invocation count: `1`
- Request file: `conflict-request.json`
- Command evidence: `run-command.txt`
- Central PMS response: `run-response.json`
- POS Server POST log evidence: `posserver-post-log-lines.txt`

## Semantic Hashes

- Original semantic hash: `ea863d4f8dc2c11e061236bec63855a26e896e700b4de92e5666bf8ee78cd38d`
- Changed Central PMS semantic hash: `f2c0fe56ab8718e957f3dce31bd8f29b194ab0f80b6863e8865768a4e6b02e24`
- Controlled semantic difference: `fiscalDocumentStatusCodeId` changed from `10000000-0000-4000-8000-000000000107` to `10000000-0000-4000-8000-000000000112`
- Upstream finality reference remained unchanged: `CPS-POS-UAT:CPS-POS-UAT-20260703-DEV-ATC-001:newly_created:001`

## POS Server Evidence

- POS Server POST attempted: `yes`
- POS Server HTTP result: `409`
- POS Server fiscal document count before conflict run: `1`
- POS Server fiscal document count after conflict run: `1`
- POS Server idempotency record count before conflict run: `1`
- POS Server idempotency record count after conflict run: `1`
- Existing fiscal document id remained: `deac11e4-fc31-4c40-9a44-da690b9730ef`
- Existing fiscal document number remained: `SI-00000001-UAT`
- Fiscal sequence value before conflict run: `1`
- Fiscal sequence value after conflict run: `1`
- No new fiscal number was allocated.

## Central PMS Evidence

- Fiscal issuance reference id: `66f7225b-a45f-461c-bc85-1a091bd5ed4f`
- Fiscal issuance state: `FISCAL_ISSUANCE_CONFLICT`
- Fiscal number assignment state: `NOT_ASSIGNED`
- Latest exception reason: `FISCAL_DOCUMENT_IDEMPOTENCY_CONFLICT`
- Latest error code: `fiscal_document_idempotency_conflict`
- Latest error posture: `DO_NOT_RETRY_WITHOUT_REQUEST_CHANGE`
- POS Server fiscal document id recorded: none
- Fiscal document number recorded: none

## Forbidden Side Effects

All captured forbidden side-effect counts were zero:

- `core.exit_authorizations`
- `gates.gate_authorization_consumptions`
- `gates.gate_events`
- `gates.gate_heartbeats`
- `operations.manual_gate_logs`

## Evidence Files

- `context.txt`
- `conflict-request.json`
- `semantic-difference-note.txt`
- `pre-run-central-db-check.txt`
- `posserver-baseline-before-conflict.txt`
- `posserver-counts-sequence-before-conflict.txt`
- `posserver-counts-sequence-immediate-before-conflict.txt`
- `run-command.txt`
- `run-response.json`
- `run-evidence.json`
- `central-fiscal-reference-after-conflict.txt`
- `posserver-fiscal-documents-after-conflict.txt`
- `posserver-idempotency-records-after-conflict.txt`
- `posserver-counts-sequence-after-conflict.txt`
- `posserver-post-log-lines.txt`
- `centralpms-conflict-log-lines.txt`
- `forbidden-side-effect-counts-after-conflict.txt`
