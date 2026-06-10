# Operator Console Production Policy Candidate Dry Run Report v1

## Purpose

This report documents the offline dry run for the production statutory discount policy candidate worksheet.

The dry run proves that the worksheet and offline validator can identify valid-looking candidate structure, unsafe dry-run markers, missing required fields, evidence mismatches, proposed-only approval mistakes, sandbox/test markers, and duplicate policy codes before any database import, DB baseline reference-data work, or production activation.

## Sample File Location

Dry-run sample:

- `docs/operator-console/samples/OperatorConsole_Production_Policy_Candidate_Dry_Run_Sample_v1.csv`

Header-only candidate worksheet:

- `docs/operator-console/OperatorConsole_Production_Policy_Candidate_Worksheet_v1.csv`

## Non-Production Statement

The sample file is not production data. It contains fake, dummy, and dry-run-only values.

The sample file does not contain real production policy rows, real LGU production approvals, personal data, production IDs, private keys, raw evidence, or real ordinance copies as import approval.

Rows marked `DRY_RUN_ONLY` or `EXAMPLE_DO_NOT_IMPORT` must not be imported, seeded, inserted, or treated as production authority.

## Validator Commands

Local direct `.ps1` execution was blocked by PowerShell execution policy, so the actual offline validation used `powershell -NoProfile -ExecutionPolicy Bypass -File`. This did not change system policy and did not connect to the database.

Header-only worksheet validation:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\operator-console\Test-ProductionPolicyImportTemplate.ps1 -Path .\docs\operator-console\OperatorConsole_Production_Policy_Candidate_Worksheet_v1.csv
```

Dry-run sample validation:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\operator-console\Test-ProductionPolicyImportTemplate.ps1 -Path .\docs\operator-console\samples\OperatorConsole_Production_Policy_Candidate_Dry_Run_Sample_v1.csv
```

## Expected PASS/WARN/FAIL Behavior

The header-only candidate worksheet is expected to pass because it contains no policy rows.

The dry-run sample is expected to fail validation. The failures are intentional and prove that unsafe rows are caught before import or baseline alignment.

Expected behavior:

- `PASS` for header-only worksheet shape and no hard failures.
- `FAIL` for dry-run/example markers.
- `FAIL` for missing `source_reference`.
- `FAIL` for PWD row using `SENIOR_CITIZEN_ID`.
- `FAIL` for `APPROVE_FOR_IMPORT` with `verification_status=PROPOSED_ONLY`.
- `FAIL` for sandbox/test policy code markers.
- `FAIL` for duplicate `policy_code`.
- `WARN` for proposed-only rows not eligible for production auto-application.

## Sample Row Summary

| Row | Policy code | Scenario | Expected result |
| --- | --- | --- | --- |
| 2 | `DRYRUNONLY_SC_VALID_LOOKING` | Structurally valid-looking Senior Citizen row, marked dry-run only. | Reject as not importable due `DRY_RUN_ONLY` and `EXAMPLE_DO_NOT_IMPORT` markers. |
| 3 | `DUMMY_SC_MISSING_SOURCE` | Senior Citizen row missing `source_reference`. | Reject missing required source and dry-run markers. |
| 4 | `DUMMY_PWD_WRONG_EVIDENCE` | PWD row uses `SENIOR_CITIZEN_ID`. | Reject entitlement/evidence mismatch and dry-run markers. |
| 5 | `DUMMY_SC_PROPOSED_APPROVED` | Proposed-only row incorrectly marked `APPROVE_FOR_IMPORT`. | Reject proposed-only approval mismatch and warn not production eligible. |
| 6 | `TEST_OC_POLICY_SANDBOX_MARKER` | Sandbox/test marker policy code. | Reject sandbox/test marker and dry-run markers. |
| 7 | `DUMMY_DUPLICATE_POLICY` | First duplicate policy code row. | Reject dry-run markers. |
| 8 | `DUMMY_DUPLICATE_POLICY` | Duplicate policy code row. | Reject duplicate policy code and dry-run markers. |

## Expected Validation Findings

The validator should identify these review findings:

- dry-run/example rows are not importable production policy data
- source reference is mandatory
- PWD evidence must use `PWD_ID` when evidence is required
- proposed-only rows cannot be approved for import
- sandbox/test/dev/E2E policy markers cannot be used for production import
- duplicate policy codes must be rejected
- proposed-only rows are not eligible for production auto-application

## Actual Offline Validation Results

Header-only worksheet command result:

```text
PASS: Template is header-only and contains no policy rows.
PASS: No hard validation failures found.
SUMMARY: pass=2 warn=0 fail=0
```

Dry-run sample command result:

```text
SUMMARY: pass=0 warn=2 fail=17
```

The dry-run sample failed as expected. Key findings included:

- row 2 rejected because it is marked `DRY_RUN_ONLY` / `EXAMPLE_DO_NOT_IMPORT`
- row 3 rejected for missing `source_reference`
- row 4 rejected for PWD evidence mismatch
- row 5 rejected because `APPROVE_FOR_IMPORT` requires `verification_status=ACTIVE_APPROVED`
- row 6 rejected for sandbox/test marker usage
- row 8 rejected for duplicate `policy_code`
- rows with repeated active-approved entitlement/scope/effective-period were rejected as duplicate active policy scope candidates

## What This Dry Run Proves

- The candidate worksheet can be checked offline without a database connection.
- Candidate worksheet review columns can be accepted while import template columns remain validated.
- Bad rows are rejected before any DB import or DB baseline reference-data work.
- Dry-run and example rows are clearly separated from production policy authority.
- Validator output includes row numbers and a PASS/WARN/FAIL summary for review triage.

## What This Dry Run Does Not Prove

- It does not approve production policy rows.
- It does not verify real LGU ordinances or legal interpretation.
- It does not prove production readiness.
- It does not insert rows into `discounts.statutory_discount_policy_registry`.
- It does not modify the DB repo baseline.
- It does not exercise backend, frontend, payment, coupon, reconciliation, HikCentral, or gate behavior.

## Production No-Go Reminder

Production statutory discount auto-application remains NO-GO until approved Senior Citizen and PWD policy rows exist, are encoded through governed DB repo or import/admin workflow, pass readiness verification, and pass controlled application validation.

## Next Steps

1. Use this dry-run sample to confirm review triage expectations.
2. Prepare a real candidate worksheet only after Product, Legal/Compliance, Operations, QA, Backend/Architecture, Data/DB, and site/client reviewers agree on evidence requirements.
3. Validate any candidate worksheet offline before import or DB repo reference-data work.
4. Route reviewed candidates through maker/checker approval.
5. Keep production auto-application disabled until readiness checks pass with approved production rows.

## Implementation Status After #262

Backend dry-run validation now also exists as a Central PMS application service foundation. It parses import-template CSV text, accepts candidate worksheet review columns, validates rows, and returns row-level `PASS` / `WARN` / `FAIL` findings with aggregate dry-run counts.

No production import/write endpoint is active in #262. No policies are inserted, seeded, activated, or approved by the service.

Recommended next slice:

- #264 Production policy import/admin maker-checker design

## Implementation Status After #263

Dry-run validation is now exposed through the Operator Console admin API endpoint:

- `POST /v1/ops/operator-console/statutory-discounts/policies/import/dry-run`

The endpoint returns row-level findings and aggregate dry-run summary fields with `imported=false`, `importedRowCount=0`, and `dryRunOnly=true`. It does not import, seed, activate, approve, or write policy rows.

Production remains NO-GO until approved Senior Citizen and PWD rows exist and pass readiness verification.

## Implementation Status After #264

The Central PMS application layer now has a production policy import review queue and maker/checker workflow foundation.

The workflow can move reviewed dry-run candidates only to `APPROVED_FOR_DB_REPO_ALIGNMENT`; it cannot import, seed, activate, or approve production auto-application.

No import/activation endpoint or DB-backed review queue is active in #264. Persistent review queue storage requires a future DB repo baseline slice.

Production remains NO-GO until approved Senior Citizen and PWD rows exist and pass readiness verification.

## Boundary Confirmations

- No backend behavior changes.
- No frontend behavior changes.
- No database, DDL, migration, or seed mutations.
- No production policy seed data added.
- No WebPay changes.
- No payment/provider routing changes.
- No AUB changes.
- No coupon, reconciliation, HikCentral, or gate changes.
- No sensitive credentials, production IDs, private keys, raw evidence, or personal data added.
