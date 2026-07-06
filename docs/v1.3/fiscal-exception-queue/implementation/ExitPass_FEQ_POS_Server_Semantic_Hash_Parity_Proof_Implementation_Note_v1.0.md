# ExitPass FEQ POS Server Semantic Hash Parity Proof Implementation Note v1.0

## Document Control

| Field | Value |
| --- | --- |
| Document | ExitPass FEQ POS Server Semantic Hash Parity Proof Implementation Note |
| Version | v1.0 |
| Branch | feature/central-pms-feq-pos-server-semantic-hash-parity-proof |
| Scope | Central PMS semantic request hash parity proof only |
| Status | implemented_for_review |

## Purpose

This slice checks whether Central PMS can prove byte/source parity between its FEQ semantic request hash and POS Server `sha256:v1`.

It does not execute retry, add a retry worker, enqueue an executable retry job, expose a retry endpoint, call POS Server POST from FEQ retry flow, mutate payment finality, change ExitAuthorization or gate behavior, edit fiscal numbers, create manual fiscal documents, or modify the POS Server repository.

## Parity Result

Parity is unconfirmed.

The ExitPass repo contains the POS Server API/integration contract statements that POS Server computes a deterministic server-side semantic request hash using `sha256:v1`, but it does not contain the POS Server canonicalization source code or an exact POS Server expected canonical source/hash fixture for the representative Central PMS fiscal document create request.

Because source-level byte parity cannot be proven from the available local artifacts, Central PMS readiness remains unconfirmed with:

`pos_server_hash_source_code_not_available_for_parity_proof`

No fake POS Server hash is inferred.

## Fixture Used

The Central PMS parity fixture uses the existing POS Server fiscal document request mapper test context and includes:

- Site POS Server id/ref;
- fiscal document type/status context;
- business day;
- parking session, payment attempt, and payment confirmation references;
- payable basis and `payableBasis.upstreamFinalityRef`;
- payable amount and currency;
- document lines;
- tenders;
- tax details;
- totals;
- discount reference and discount privilege details;
- safe reference-context dictionaries.

## Central PMS Hash Source

Central PMS canonical source version:

`central-pms-pos-server-fiscal-request-v1`

Central PMS canonicalization now exposes safe inspection output for tests:

- normalized fact list;
- canonical source text;
- SHA-256 hash value;
- hash algorithm;
- source version;
- fact count;
- safe source summary.

The representative fixture produces a stable lowercase 64-character SHA-256 hash from the Central PMS canonical source. Identical Central PMS facts produce the same hash, changed stable fiscal facts change the hash, and volatile transport fields such as `ChannelTerminalId` do not affect the hash.

## POS Server sha256:v1 Expectation Used

The local contracts state:

- POS Server semantic request hash posture is `sha256:v1`;
- POS Server computes the hash server-side over normalized fiscal request facts;
- replay requires same idempotency scope, same idempotency key, and same semantic request hash;
- conflict is same scope/key with different semantic request hash;
- idempotency key source is `payableBasis.upstreamFinalityRef`.

The exact POS Server canonical source text/hash fixture is not available in this repo, so parity cannot be proven.

## Readiness Behavior

FEQ POS Server retry contract readiness now requires a proven semantic hash parity result before semantic hash compatibility can be marked ready.

Readiness remains unconfirmed when no POS Server expected source/hash is available. Readiness blocks with `pos_server_semantic_hash_mismatch` when a supplied POS Server expected source/hash differs from the Central PMS canonical source/hash.

`RetryExecutionAvailable` remains false.

## Remaining Blockers Before Retry Execution

- Add or import an exact POS Server `sha256:v1` canonical source/hash fixture for the same representative request, or expose a shared canonicalization contract.
- Resolve any mismatch if the POS Server expected source/hash differs.
- Keep POS Server readiness gates, production dual-control, scheduler/execution guards, and audit prerequisites closed until parity is proven.

## Validation Notes

Unit coverage was added for:

- Central PMS canonical source inspection;
- stable hash for identical request facts;
- hash changes for line, tender, tax, totals, and upstream finality changes;
- volatile transport fields excluded from the hash;
- parity proven only when exact POS Server expected source/hash is supplied;
- unconfirmed parity when POS Server source/hash fixture is missing;
- mismatch reporting when POS Server expected hash/source differs;
- FEQ readiness remaining unconfirmed without a parity proof and blocked on mismatch;
- retry execution availability remaining false.
