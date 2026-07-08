# ExitPass Central PMS to POS Server Fiscal Issuance Controlled UAT Runbook v1.0

## Purpose And Scope

This runbook documents a controlled local/disposable UAT procedure for the normal Central PMS to POS Server fiscal issuance path and POS Server idempotency behavior.

It covers evidence generation for:

- First fiscal issuance.
- Same-key/same-hash idempotent replay.
- Same-key/different-hash idempotency conflict.

This runbook is not a production certification. It supports evidence-backed UAT validation only.

## Required Repositories

- `D:\SourceCodes\ExitPass`
- `D:\SourceCodes\ExitPass-PoSServer`

## Required Local Services

- PostgreSQL Docker container: `exitpass-postgres`
- Central PMS: `http://localhost:5080`
- POS Server: `http://localhost:5000`

## Required Databases

- Central PMS disposable UAT database: `centralpms_feq_retry_uat_local`
- POS Server disposable/local validation database: `posserver_api_smoke_validation_local`
- Central PMS schema source: `exitpass_v12_dev`

Do not use production databases or production identifiers.

## POS Server Aligned Seed Prerequisite

The POS Server database must contain aligned disposable controlled-code seed data for the Central PMS UAT identity.

Required POS Server identifiers:

- `site_pos_server_id`: `10000000-0000-4000-8000-000000000201`
- `fiscal_document_type_code_id`: `10000000-0000-4000-8000-000000000103`
- `fiscal_identity_id`: `10000000-0000-4000-8000-000000000701`
- `fiscal_sequence_policy_id`: `10000000-0000-4000-8000-000000000803`
- `fiscal_sequence_state_id`: `10000000-0000-4000-8000-000000000804`

Required active controlled code IDs:

- `10000000-0000-4000-8000-000000000107`
- `10000000-0000-4000-8000-000000000108`
- `10000000-0000-4000-8000-000000000109`
- `10000000-0000-4000-8000-000000000110`
- `10000000-0000-4000-8000-000000000111`
- `10000000-0000-4000-8000-000000000112`

## Central PMS Disposable DB Reset

Each fresh first-run or conflict scenario must recreate only:

`centralpms_feq_retry_uat_local`

Restore schema only from:

`exitpass_v12_dev`

Seed only the approved controlled UAT identity:

`CPS-POS-UAT:CPS-POS-UAT-20260703-DEV-ATC-001:newly_created:001`

Before a fresh first-run or conflict run, verify there is no `core.fiscal_issuance_references` row for that upstream finality reference.

## Runtime Startup

Start POS Server on port `5000` with:

```powershell
$env:ConnectionStrings__PosServer = "Host=localhost;Port=5433;Database=posserver_api_smoke_validation_local;Username=exitpass;Password=change_me"
```

Start Central PMS on port `5080` with:

```powershell
$env:ConnectionStrings__MainDatabase = "Host=localhost;Port=5433;Database=centralpms_feq_retry_uat_local;Username=exitpass;Password=change_me"
```

Use local-only UAT configuration. Do not enable production payment, exit, gate, batch retry, scheduler, or FEQ retry execution behavior for this validation.

## Preflight Checks

Before any scenario, verify:

- Docker container `exitpass-postgres` is running.
- `centralpms_feq_retry_uat_local` exists when a Central PMS run is needed.
- `posserver_api_smoke_validation_local` exists.
- Central PMS schema exists in `centralpms_feq_retry_uat_local`.
- Required Central PMS UAT seed rows exist.
- Required POS Server aligned seed rows exist.
- Central PMS is reachable at `http://localhost:5080`.
- POS Server is reachable at `http://localhost:5000`.
- No stale Central PMS `core.fiscal_issuance_references` row exists before a fresh first-run scenario.
- Existing POS Server fiscal document/idempotency baseline exists before replay or conflict scenarios.

Stop if the active database cannot be proven safe and disposable.

## Scenario 1: First Issuance

Expected pass criteria:

- `/run` executed exactly once.
- POS Server `POST /v1/fiscal-documents` returned HTTP `202`.
- POS Server created a fiscal document.
- Fiscal document number was assigned.
- Central PMS state: `FISCAL_ISSUANCE_RECORDED`.
- Forbidden side-effect counts were zero.

Known passing baseline values:

- Fiscal document id: `deac11e4-fc31-4c40-9a44-da690b9730ef`
- Fiscal document number: `SI-00000001-UAT`
- Central PMS fiscal reference: `bf5288c9-426c-4f22-9567-ac5efac03ec0`

## Scenario 2: Same-Key/Same-Hash Replay

Expected pass criteria:

- `/run` executed exactly once.
- POS Server `POST /v1/fiscal-documents` returned HTTP `202`.
- POS Server fiscal document count remains `1 -> 1`.
- POS Server idempotency count remains `1 -> 1`.
- Fiscal sequence value remains `1 -> 1`.
- Fiscal document id remains unchanged.
- Fiscal document number remains unchanged.
- Central PMS state: `FISCAL_ISSUANCE_REPLAYED`.
- Result classification: `IDEMPOTENT_REPLAY`.
- Forbidden side-effect counts were zero.

## Scenario 3: Same-Key/Different-Hash Conflict

Expected pass criteria:

- `/run` executed exactly once.
- POS Server `POST /v1/fiscal-documents` returned HTTP `409`.
- Original and changed semantic hashes differ.
- POS Server fiscal document count remains `1 -> 1`.
- POS Server idempotency count remains `1 -> 1`.
- Fiscal sequence value remains `1 -> 1`.
- No new fiscal document is created.
- No new fiscal number is allocated.
- Central PMS state: `FISCAL_ISSUANCE_CONFLICT`.
- Error code: `fiscal_document_idempotency_conflict`.
- Posture: `DO_NOT_RETRY_WITHOUT_REQUEST_CHANGE`.
- Forbidden side-effect counts were zero.

## Forbidden Side-Effect Checks

Every scenario must confirm zero counts for:

- `core.exit_authorizations`
- `gates.gate_authorization_consumptions`
- `gates.gate_events`
- `gates.gate_heartbeats`
- `operations.manual_gate_logs`

## Evidence Folder Conventions

Existing evidence references:

- `docs/v1.3/central-pms/evidence/pos-server-normal-fiscal-issuance-clean-rerun-controlled-codes-20260708/`
- `docs/v1.3/central-pms/evidence/pos-server-normal-fiscal-issuance-same-key-same-hash-replay-20260708/`
- `docs/v1.3/central-pms/evidence/pos-server-normal-fiscal-issuance-same-key-different-hash-conflict-20260708/`

For future reruns, preserve evidence under a date-specific folder and include:

- branch and commit tested;
- Central PMS URL and database name;
- POS Server URL and database name;
- feature/config flags used;
- request file;
- command used;
- Central PMS response;
- POS Server POST log line;
- Central PMS fiscal reference row;
- POS Server fiscal document and idempotency rows;
- fiscal sequence value before and after;
- forbidden side-effect counts;
- pass/fail summary.

Do not store secrets, production data, customer PII, raw payment provider payloads, or statutory evidence payloads.

## Cleanup

After validation:

- Stop Central PMS on port `5080`.
- Stop POS Server on port `5000`.
- Remove local disposable runtime log files if they block `git pull` or `git switch`.
- Use targeted `Remove-Item` commands for known disposable files only.
- Do not use broad `git clean`.
- Delete temporary `tmp/manual-smoke` folders only after evidence has been preserved.

## Known Limitations

This runbook does not cover:

- Production certification.
- Production load/performance validation.
- Production security hardening.
- mTLS/service identity enforcement validation.
- Real live site data.
- Final BIR statutory receipt wording.
- PDF/HTML/QR generation.
- Annex E, X/Z, and reporting validation.
- Gate/ExitAuthorization validation.
- Refund/reversal validation.
- FEQ batch retry scheduler validation.

## Recommended Next Action

Keep the proof set closed. Use this runbook only when the proof set needs rerun evidence.

Move to deployment/UAT runbook hardening or the next product slice.

Do not keep adding idempotency scenarios unless a new risk appears.
