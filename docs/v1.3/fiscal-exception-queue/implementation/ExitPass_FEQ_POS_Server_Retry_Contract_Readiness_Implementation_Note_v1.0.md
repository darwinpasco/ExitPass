# ExitPass FEQ POS Server Retry Contract Readiness Implementation Note v1.0

## Document Control

| Field | Value |
| --- | --- |
| Document | ExitPass FEQ POS Server Retry Contract Readiness Implementation Note |
| Version | v1.0 |
| Branch | feature/central-pms-feq-pos-server-retry-contract-readiness |
| Scope | Central PMS FEQ POS Server retry/readback contract readiness only |
| Status | implemented_for_review |

## Purpose

This slice verifies and exposes whether Central PMS FEQ can safely consider future retry execution from a POS Server contract perspective.

It does not execute retry, add a retry worker, enqueue an executable retry job, expose a retry endpoint, call POS Server POST from FEQ retry flow, change fiscal-gated ExitAuthorization behavior, edit fiscal numbers, create manual fiscal documents, or trigger gate behavior.

## POS Server Contract Assumptions Used

This slice uses the merged POS Server fiscal numbering/idempotency runtime foundation assumptions supplied for this branch:

- idempotency scope: `fiscal_document_creation:{site_pos_server_id:N}:{fiscal_document_type_code_id:N}`;
- idempotency key source: `payableBasis.upstreamFinalityRef`;
- POS Server semantic request hash posture: `sha256:v1`;
- same scope/key/hash replays deterministically;
- same scope/key with different hash conflicts deterministically;
- POS Server GET/readback can expose idempotency, hash, fiscal identity, fiscal sequence, and fiscal document numbering fields.

## Readiness Dimensions Implemented

Central PMS FEQ now exposes read-only POS Server retry contract readiness posture:

- overall POS Server retry contract readiness status;
- semantic hash compatibility status;
- idempotency mapping status;
- readback field compatibility status;
- fiscal numbering readiness visibility status;
- conflict/replay behavior expectation status;
- safe block reason;
- safe summary;
- retry execution availability, still false.

## Semantic Hash Compatibility Result

Central PMS only confirms compatibility when the persisted hash facts indicate a SHA-256-compatible algorithm and the POS Server source version `sha256:v1`.

The current Central PMS semantic request hash source version remains `central-pms-pos-server-fiscal-request-v1`. That proves Central PMS has a deterministic local hash, but it does not prove byte-for-byte compatibility with POS Server `sha256:v1`.

Therefore the readiness evaluator returns:

`pos_server_semantic_hash_compatibility_unconfirmed`

when current Central PMS hash metadata is present but cannot prove the POS Server hash source version.

No fake compatibility is inferred.

## Idempotency Mapping Result

Central PMS idempotency mapping is aligned with POS Server key-source expectations when:

- `UpstreamFinalityReference` is present;
- retry planning keeps the same upstream finality reference;
- no new upstream finality reference is requested to bypass a conflict.

Readiness blocks with `pos_server_idempotency_mapping_not_compatible` when the upstream finality reference is missing or changed.

## Readback Fields Consumed

Central PMS POS Server readback models and parser now expose optional safe fields:

- `idempotencyScope`;
- `idempotencyKey`;
- `idempotencyKeySource`;
- `semanticRequestHash`;
- `semanticRequestHashVersion`;
- `semanticRequestHashStatus`;
- `fiscalIdentityId`;
- `fiscalSequencePolicyId`;
- `fiscalSequenceValue`;
- `fiscalDocumentNumber`;
- `fiscalSeries`;
- `fiscalNumberPrefixText`;
- `fiscalNumberSuffixText`;
- `fiscalNumberAssignedAt`;
- `fiscalNumberAssignedByRef`.

FEQ readback classification now treats mismatched idempotency key source, idempotency key, semantic request hash, fiscal identity, fiscal sequence policy, fiscal sequence value, or fiscal document number as mismatch when both Central PMS and POS Server expose the relevant field.

Missing optional fields remain backward compatible for older/minimal read responses.

## Execution Safety

Execution preparation now blocks when POS Server retry contract readiness is not ready, even if the existing POS Server readiness gates are configured.

`RetryExecutionAvailable` remains false.

## Remaining Blockers Before Retry Execution

Before any future retry execution slice can be approved:

- Central PMS and POS Server must prove semantic hash byte/source compatibility for `sha256:v1`, or Central PMS must persist the POS Server-returned hash as the authoritative replay basis.
- POS Server readiness gates must remain explicitly confirmed.
- Production dual-control policy must be finalized.
- No retry execution worker or endpoint should be introduced until these blockers are closed.

## Validation Notes

Coverage was added for semantic hash compatibility, semantic hash unconfirmed/missing behavior, upstream finality/idempotency mapping, POS Server readback parsing for idempotency/hash/fiscal numbering fields, readback mismatch classification using idempotency/hash fields, execution-prep blocking on unconfirmed contract readiness, and no retry execution side effects.
