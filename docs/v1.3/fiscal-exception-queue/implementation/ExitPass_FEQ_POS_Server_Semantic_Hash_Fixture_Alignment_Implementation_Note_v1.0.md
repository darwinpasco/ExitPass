# ExitPass FEQ POS Server Semantic Hash Fixture Alignment Implementation Note v1.0

## Document Control

| Field | Value |
| --- | --- |
| Document | ExitPass FEQ POS Server Semantic Hash Fixture Alignment Implementation Note |
| Version | v1.0 |
| Branch | feature/central-pms-feq-pos-server-semantic-hash-fixture-alignment |
| Scope | Central PMS test fixture alignment against POS Server `sha256:v1` semantic hash fixture |
| Status | implemented_for_review |

## Purpose

This slice consumes the POS Server `sha256:v1` representative semantic hash fixture and uses it to prove or deny Central PMS parity.

It does not execute retry, add a retry worker, enqueue an executable retry job, expose a retry endpoint, call POS Server POST, mutate payment finality, change ExitAuthorization or gate behavior, edit fiscal numbers, create manual fiscal documents, or modify the POS Server repository.

## Fixture Source and Path

Source fixture copied from the POS Server fiscal numbering/idempotency runtime fixture:

`D:\SourceCodes\ExitPass-PoSServer\docs\v1.3\pos-server\fiscal-numbering\fixtures\pos_server_semantic_hash_sha256_v1_representative_fixture.json`

Central PMS test fixture path:

`src/Services/CentralPms/tests/ExitPass.CentralPms.UnitTests/FiscalIssuance/Fixtures/pos_server_semantic_hash_sha256_v1_representative_fixture.json`

The fixture is used as safe test data only. It is not runtime configuration and contains no production identifiers, customer PII, secrets, raw payment provider payloads, or raw statutory evidence.

## POS Server Expected Hash

Fixture canonical source version:

`sha256:v1`

Expected POS Server SHA-256 hash:

`6a490379e4275a57f0a0695ff9dbd1271c4480adaeeefb9b6bfbd11e4d1ed201`

Central PMS verifies that hashing the fixture `canonical_source_text` as UTF-8 SHA-256 returns the expected lowercase hex digest.

## Central PMS Calculated Hash

The fixture representative facts were mapped into the Central PMS POS Server fiscal document create request model.

Central PMS semantic hash source version:

`central-pms-pos-server-fiscal-request-v1`

Central PMS calculated hash:

`1430a2fbd8c9c128d101658777ba427bb34564bb84460621af179a464b5d7ab8`

## Parity Result

Parity is mismatched.

The POS Server fixture canonical source/hash and the Central PMS canonical source/hash do not match exactly. Central PMS therefore does not mark semantic hash compatibility as proven.

Exact block reason:

`pos_server_semantic_hash_mismatch`

The mismatch is expected from the current Central PMS canonicalization posture because Central PMS still uses its newline fact-list source version while POS Server `sha256:v1` uses its canonical JSON source fixture.

## Readiness Impact

When the POS Server fixture parity proof is supplied to FEQ POS Server retry contract readiness, readiness no longer reports `pos_server_hash_source_code_not_available_for_parity_proof`.

It now reports:

`pos_server_semantic_hash_mismatch`

and keeps retry blocked. `RetryExecutionAvailable` remains false.

## Remaining Blockers Before Retry Execution

- Align Central PMS canonicalization to POS Server `sha256:v1`, or adopt the POS Server-returned semantic hash as the authoritative retry/readback comparison basis after initial issuance.
- Re-run fixture parity after alignment until Central PMS canonical source/hash exactly matches the POS Server fixture.
- Keep controlled retry execution disabled until semantic hash parity is proven and all execution, scheduler, POS Server readiness, dual-control, and audit gates remain satisfied.

## Validation Notes

Coverage was added for:

- POS Server fixture `canonical_source_text` hashing to the expected fixture hash;
- deterministic Central PMS mapping from fixture representative facts;
- actual Central PMS-to-POS fixture parity mismatch;
- synthetic proof path still only proving parity when source/hash exactly match;
- readiness changing from source-code unavailable to semantic hash mismatch when the actual fixture proof is supplied;
- retry execution availability remaining false.
