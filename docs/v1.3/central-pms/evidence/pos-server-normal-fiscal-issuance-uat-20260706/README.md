# Central PMS to POS Server Normal Fiscal Issuance UAT Evidence

Date: 2026-07-06
Branch: test/central-pms-pos-server-normal-fiscal-issuance-uat
Commit: e0d560af74c86c90f23873d5205c1d22551ed694

## Scope

This evidence folder records the attempted minimal controlled UAT for the normal Central PMS to POS Server fiscal issuance path. This is not FEQ retry work.

No retry endpoint, retry worker, batch retry, scheduler/background job, Operator Console UI, dashboard work, fiscal-gated ExitAuthorization, fiscal number editing, or manual fiscal document creation was added or used.

## Runtime Checked

- Central PMS host-local URL checked: `http://localhost:5080`
- Central PMS Docker-mapped URL checked: `http://localhost:8080`
- POS Server URL checked: `http://localhost:5000`
- PostgreSQL container checked: `exitpass-postgres`

The controlled UAT endpoint was present on both Central PMS URLs:

- `POST /internal/controlled-uat/fiscal-issuance/preflight`
- `POST /internal/controlled-uat/fiscal-issuance/run`

## Preflight Result

The approved first-run request was saved as:

- `normal-fiscal-issuance-uat-request.json`

Preflight passed on:

- `http://localhost:5080/internal/controlled-uat/fiscal-issuance/preflight`
- `http://localhost:8080/internal/controlled-uat/fiscal-issuance/preflight`

Saved responses:

- `preflight-response.json`
- `preflight-response-8080.json`

The preflight response reported:

- `accepted = true`
- `status = preflight_passed`
- `readinessStatus = enabled_ready`
- `diagnosticInvoked = false`
- `posServerCallAttempted = false`
- `paymentFinalityChanged = false`
- `exitAuthorizationIssued = false`
- `gateBehaviorTriggered = false`
- `fiscalGatingEnforcementEnabled = false`

## Follow-up Attempt After Manual DB Confirmation

After manual confirmation that Central PMS on `http://localhost:5080` was restarted with `ConnectionStrings__MainDatabase` set to `centralpms_feq_retry_uat_local`, preflight was rerun and passed:

- `preflight-command-after-db-confirmation.txt`
- `preflight-response-after-db-confirmation.json`

The Docker Central PMS endpoint on `http://localhost:8080` was no longer reachable, matching the manual note that Docker Central PMS was stopped.

Before invoking the mutating `/run` endpoint, the disposable DB was checked directly. The database name was safe, but the database was empty:

- `db-schema-check-after-db-confirmation.txt`

The required Central PMS table `core.fiscal_issuance_references` was not present, and `information_schema.tables` returned no application tables.

## Follow-up Attempt After Schema Confirmation

After manual confirmation that `centralpms_feq_retry_uat_local` had been migrated, the disposable DB was checked again:

- `db-schema-confirmed-before-run.txt`

Confirmed:

- application table count: `91`
- required table exists: `core.fiscal_issuance_references`
- approved upstream finality reference had no pre-existing fiscal issuance reference row

Preflight was rerun and passed:

- `preflight-command-after-schema-confirmation.txt`
- `preflight-response-after-schema-confirmation.json`

The controlled `/run` endpoint was then invoked once:

- `run-command-after-schema-confirmation.txt`
- `run-error-after-schema-confirmation.json`
- `run-response-failed-after-schema-confirmation.json`

Run result:

- HTTP status: `409 Conflict`
- status: `fiscal_reference_prepare_failed`
- readinessStatus: `enabled_ready`
- diagnosticInvoked: `false`
- posServerCallAttempted: `false`
- paymentFinalityChanged: `false`
- exitAuthorizationIssued: `false`
- gateBehaviorTriggered: `false`
- fiscalGatingEnforcementEnabled: `false`

After the failed run, the disposable DB was checked:

- `db-fiscal-reference-after-run-failure.txt`
- `db-seed-check-after-run-failure.txt`

Confirmed:

- no fiscal issuance reference row was created for the approved upstream finality reference;
- required controlled UAT seed rows were missing:
  - `core.payment_confirmations`: `0` rows for `00000000-0000-4000-8000-000000000301`
  - `core.payment_attempts`: `0` rows for `00000000-0000-4000-8000-000000000302`
  - `core.parking_sessions`: `0` rows for `00000000-0000-4000-8000-000000000303`

Because `core.fiscal_issuance_references` has foreign keys to those payment/session tables, the run failed before POS Server invocation during fiscal reference preparation.

## Blocker

The mutating `/run` step was not executed.

Initial reason: the Central PMS runtime database could not be proven to be disposable before invoking the controlled fiscal issuance run.

Observed local PostgreSQL databases included both:

- `centralpms_feq_retry_uat_local`
- `exitpass_v12_dev`

Because `exitpass_v12_dev` is not a clearly disposable UAT database name and the active Central PMS process connection string could not be safely inspected without exposing environment/secret-bearing configuration, the first UAT attempt stopped before any mutating fiscal issuance call.

Updated reason after manual DB confirmation: `centralpms_feq_retry_uat_local` is safe/disposable, but it had not yet been migrated/restored with the Central PMS schema. The UAT remained stopped before `/run` because fiscal issuance reference preparation cannot safely execute without `core.fiscal_issuance_references`.

Final blocker after schema confirmation: the disposable DB schema exists, but the fixed controlled UAT payment/session seed rows are missing. The controlled `/run` failed safely with `fiscal_reference_prepare_failed` before POS Server invocation.

## Result

Normal fiscal issuance UAT did not complete.

Preflight passed, and `/run` was attempted once after DB/schema confirmation, but fiscal issuance did not reach POS Server. The controlled run failed safely before POS Server invocation due to missing disposable Central PMS seed data.

Environment blockers encountered:

1. verified disposable Central PMS DB identity was initially unavailable;
2. after manual DB confirmation, the disposable DB was verified empty/unmigrated;
3. after schema confirmation, required controlled UAT payment/session seed rows were missing.

## Forbidden Side Effects

Confirmed by stopping before POS Server invocation:

- No POS Server fiscal issuance POST was invoked by this UAT attempt.
- No Central PMS fiscal issuance reference/evidence success mutation was performed.
- No fiscal issuance reference row exists for the approved upstream finality reference after the failed run.
- No ExitAuthorization issuance occurred.
- No gate behavior occurred.
- No fiscal number editing occurred.
- No manual fiscal document creation occurred.
- No retry execution occurred.
- No batch operation occurred.

## Next Action

Seed the required disposable Central PMS UAT payment/session rows into `centralpms_feq_retry_uat_local`, restart or verify Central PMS remains pointed at that DB, rerun preflight, verify the seed rows exist, and then invoke the controlled `/run` endpoint again.

## Follow-up After Disposable Seed

The minimum disposable Central PMS seed rows were added with:

- `tmp/manual-smoke/central-pms-pos-server-normal-fiscal-issuance-uat/Seed-NormalFiscalIssuanceUatRows.sql`

Seed command and output:

- `seed-command.txt`
- `seed-apply-output.txt`

Confirmed seed rows:

- `core.parking_sessions`: `00000000-0000-4000-8000-000000000303`, `CPS-POS-UAT-PARKING-SESSION-001`
- `core.payment_attempts`: `00000000-0000-4000-8000-000000000302`, `CPS-POS-UAT-PAYMENT-ATTEMPT-001`
- `core.payment_confirmations`: `00000000-0000-4000-8000-000000000301`, upstream finality ref `CPS-POS-UAT:CPS-POS-UAT-20260703-DEV-ATC-001:newly_created:001`

Verification files:

- `db-seed-verify-after-seed.txt`
- `db-seed-counts-after-seed-run.txt`

After seeding, preflight passed again:

- `preflight-command-after-seed.txt`
- `preflight-response-after-seed.json`

The controlled `/run` endpoint was invoked once:

- `run-command-after-seed.txt`
- `run-response-after-seed.json`
- `run-evidence-after-seed.json`

Run result:

- HTTP status: `200`
- status: `service_failure_mapped`
- readinessStatus: `enabled_ready`
- diagnosticInvoked: `true`
- posServerCallAttempted: `true`
- centralPmsFiscalState: `FiscalIssuanceFailedService`
- errorCode: `persistence_write_failed`
- errorPosture: `RetryAfterServiceRecovery`
- fiscalDocumentId: null
- fiscalDocumentNumber: null
- fiscalNumberAssignmentState: `NotAssigned`
- semantic hash/version recorded on the fiscal reference: `ea863d4f8dc2c11e061236bec63855a26e896e700b4de92e5666bf8ee78cd38d` / `sha256:v1`

Central PMS persisted a failed-service fiscal reference:

- `db-fiscal-reference-after-seed-run.txt`

Key row:

- fiscal issuance reference id: `7b35ec5c-3314-4b02-99de-4006ce2b066a`
- state: `FISCAL_ISSUANCE_FAILED_SERVICE`
- latest exception reason: `PERSISTENCE_WRITE_FAILED`
- latest error code: `persistence_write_failed`
- POS Server fiscal document id: null
- fiscal document number: null

No POS Server GET/readback was performed because the controlled run did not return a `fiscalDocumentId`.

Forbidden side-effect evidence:

- `db-forbidden-side-effect-table-list-after-seed-run.txt`
- `db-forbidden-side-effect-counts-after-seed-run.txt`

Confirmed counts after the seeded `/run`:

- `core.exit_authorizations`: `0`
- `gates.gate_authorization_consumptions`: `0`
- `gates.gate_events`: `0`
- `gates.gate_heartbeats`: `0`
- `operations.manual_gate_logs`: `0`

## Seeded Run Result

Normal fiscal issuance UAT did not pass.

The disposable seed unblocked fiscal reference preparation and the run reached the POS Server invocation path, but the controlled UAT result mapped to `service_failure_mapped` / `persistence_write_failed`. No POS Server fiscal document id, fiscal document number, or readback target was produced. No forbidden side effects were detected.

Recommended next action: inspect Central PMS and POS Server service logs for the `persistence_write_failed` mapped during run correlation `00000000-0000-4000-8000-000000000101`, then rerun this UAT from a clean disposable DB only after the persistence-write issue is understood.
