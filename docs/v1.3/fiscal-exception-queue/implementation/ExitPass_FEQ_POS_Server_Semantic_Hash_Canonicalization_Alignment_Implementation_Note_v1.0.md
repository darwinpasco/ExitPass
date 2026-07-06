# ExitPass FEQ POS Server Semantic Hash Canonicalization Alignment Implementation Note v1.0

## Scope

This slice aligns Central PMS semantic request hash canonicalization with the POS Server `sha256:v1` canonical JSON fixture. It is contract-readiness work only. It does not add retry execution, a retry worker, an executable scheduler/job, a retry endpoint, POS Server POST behavior, payment finality mutation, ExitAuthorization changes, gate behavior changes, fiscal number editing, or manual fiscal document creation.

## Alignment Approach

Central PMS now uses POS Server-compatible compact canonical JSON for semantic request hash source generation instead of the previous Central PMS newline fact-list source. The Central PMS hash source version is now `sha256:v1`, and the hash remains lowercase SHA-256 over the UTF-8 canonical source text.

The canonical JSON writer matches the POS Server fixture field names and ordering for:
- site and fiscal document type context;
- parking session, payment attempt, payment confirmation, and payment finality references;
- `payable_basis`;
- document lines;
- tenders;
- tax details;
- discount privilege details;
- totals;
- reference-context dictionaries.

Collections and dictionaries are ordered deterministically. Strings are trimmed, currency codes are normalized to uppercase, dates use `yyyy-MM-dd`, GUIDs use `D` format, and nulls are written as JSON null. POS Server `sha256:v1` includes `channel_terminal_id`, so Central PMS includes it as a semantic scope field. Response, retry, replay, and fiscal-number outcome fields remain excluded.

## Fixture Proof

Fixture path:

`src/Services/CentralPms/tests/ExitPass.CentralPms.UnitTests/FiscalIssuance/Fixtures/pos_server_semantic_hash_sha256_v1_representative_fixture.json`

Expected POS Server hash:

`6a490379e4275a57f0a0695ff9dbd1271c4480adaeeefb9b6bfbd11e4d1ed201`

Central PMS hash after alignment:

`6a490379e4275a57f0a0695ff9dbd1271c4480adaeeefb9b6bfbd11e4d1ed201`

Parity result:

`proven`

Central PMS canonical source text for the mapped representative create request now matches the POS Server fixture `canonical_source_text` exactly, and hashing it as UTF-8 SHA-256 produces the POS Server expected lowercase hex digest.

## Readiness Impact

The FEQ POS Server retry contract readiness path can now treat the fixture-backed semantic hash compatibility proof as ready when the parity proof is supplied. `RetryExecutionAvailable` remains `false`.

The synthetic mismatch path remains covered and continues to block with `pos_server_semantic_hash_mismatch` when the expected POS Server source or hash differs from the Central PMS canonical source or hash.

## Remaining Blockers Before Retry Execution

Retry execution remains outside this slice. Future execution still requires the controlled execution slice to keep all non-hash gates satisfied, including durable command/scheduler audit, latest `not_found` readback basis, confirmed same upstream finality/idempotency context, POS Server readiness gates, dual-control policy, audit persistence, and an explicit execution enablement decision.

Existing stored semantic hashes produced by the older Central PMS fact-list version are not backfilled by this slice. A future operational migration or re-evaluation policy should decide how to handle historical `central-pms-pos-server-fiscal-request-v1` records before any production retry execution is enabled.

## Validation

Validated with focused and full Central PMS unit test filters:

- `dotnet test src\Services\CentralPms\tests\ExitPass.CentralPms.UnitTests\ExitPass.CentralPms.UnitTests.csproj --filter "FiscalSemanticRequestHashCalculatorTests|FiscalSemanticRequestHashParityProofServiceTests|FiscalExceptionPosServerRetryContractReadinessServiceTests" --no-restore --logger "console;verbosity=minimal"`
- Full requested FEQ/FiscalIssuance filter to be recorded in the completion summary.
