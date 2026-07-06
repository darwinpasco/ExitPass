# ExitPass FEQ Semantic Hash Backfill Readiness Policy Implementation Note v1.0

## Scope

This slice adds a read-only readiness policy for persisted semantic request hash metadata on historical fiscal issuance references. It handles records created under the old Central PMS source version after Central PMS aligned canonicalization to POS Server `sha256:v1`.

This slice does not execute retry, add a retry worker, enqueue executable jobs, expose retry endpoints, call POS Server POST, change POS Server code, mutate payment finality, change ExitAuthorization or gate behavior, edit fiscal numbers, or create manual fiscal documents.

## Policy Implemented

The policy is implemented by `FiscalExceptionSemanticHashReadinessPolicy`.

It classifies persisted semantic hash metadata as:

- `ReadyCurrent`
- `LegacyRecalculationRequired`
- `Missing`
- `Incomplete`
- `Incompatible`
- `Unavailable`

The current required source version is:

`sha256:v1`

A persisted hash is current-ready only when:

- semantic hash status is available/confirmed;
- hash algorithm is SHA-256 compatible;
- source version is `sha256:v1`;
- hash value is present;
- source fact count and safe source summary are present.

## Legacy Handling

Records with stored source version:

`central-pms-pos-server-fiscal-request-v1`

are never treated as retry-safe. They are classified as:

- readiness: `LegacyRecalculationRequired`
- block reason: `semantic_hash_legacy_version_requires_recalculation`

FEQ detail surfaces the stored source version, required source version, recalculation posture, safe summary, and block reason.

## Recalculation And Backfill

Automatic recalculation/backfill is deferred.

The current policy does not mutate historical fiscal issuance references and does not reconstruct hashes from partial facts. The recalculation posture is exposed as `Unknown` because this slice does not add a durable original-request reconstruction source or a migration workflow.

## Readiness Impact

The semantic hash readiness policy is integrated into:

- FEQ read-only detail/projection summary;
- retry eligibility evaluation;
- retry command preparation;
- retry scheduling preparation;
- retry execution preparation.

Legacy hashes and other non-current hash metadata block retry readiness before any future retry execution could be considered. `RetryExecutionAvailable` remains `false`.

## Remaining Blockers Before Retry Execution

Future retry execution remains blocked until a separate slice defines a safe operational approach for historical records, such as an audited re-evaluation/backfill process using complete original fiscal request facts. Execution still requires all previously modeled retry command, scheduler, execution, dual-control, POS Server readiness, audit, and readback gates.
