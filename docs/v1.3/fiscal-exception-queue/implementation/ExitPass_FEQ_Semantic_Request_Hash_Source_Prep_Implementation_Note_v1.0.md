# ExitPass FEQ Semantic Request Hash Source Prep Implementation Note v1.0

## Document Control

| Field | Value |
| --- | --- |
| Document | ExitPass FEQ Semantic Request Hash Source Prep Implementation Note |
| Version | v1.0 |
| Branch | feature/central-pms-feq-semantic-request-hash-source-prep |
| Scope | Central PMS fiscal issuance semantic request hash source preparation only |
| Status | implemented_for_review |

## Purpose

This slice defines, calculates, and persists the Central PMS semantic request hash source used by future FEQ retry safety checks. It does not execute retry, schedule retry, expose a retry endpoint, introduce a retry job, call POS Server POST outside the existing issuance flow, change fiscal-gated ExitAuthorization behavior, edit fiscal numbers, or create manual fiscal documents.

## Source Facts

The hash source is calculated from the Central PMS POS Server fiscal document create request produced by the existing mapper. It includes stable fiscal request facts that must remain unchanged across safe replay:

- Site POS Server context;
- fiscal document type and document status context;
- business day when available;
- Central PMS parking session, payment attempt, and payment confirmation references;
- upstream finality, payment finality, vendor acknowledgment, and payable-basis references;
- payable amount and currency;
- payable-basis discount references and reference context;
- fiscal document line facts;
- tender facts;
- tax details;
- discount privilege details;
- total facts;
- safe reference-context dictionaries.

The calculator excludes transport and volatile facts such as channel terminal id, timestamps, correlation ids, generated fiscal document ids, response data, retry count, and transport metadata.

## Canonicalization Rules

`FiscalSemanticRequestHashCalculator` produces a versioned canonical key/value source and hashes it with SHA-256.

Canonicalization rules:

- collections are ordered deterministically by stable fiscal attributes and context fingerprints;
- dictionary entries are ordered by normalized key and value using ordinal comparison;
- dates use `yyyy-MM-dd`;
- decimal and formattable values use invariant culture;
- null, empty, and whitespace strings normalize consistently;
- hash output is lowercase hexadecimal;
- raw payloads and secrets are not stored.

If required source facts are missing, the calculator returns `Incomplete` with no hash value. It does not invent hashes or infer them from partial payloads.

## Persistence Approach

This slice extends the existing `core.fiscal_issuance_references` persistence surface with narrow semantic hash metadata:

- semantic request hash status;
- hash value;
- hash algorithm;
- hash source version;
- source fact count;
- safe source summary;
- recorded timestamp.

The repository update records only semantic hash metadata for the known fiscal issuance reference. It does not mutate fiscal issuance state, fiscal evidence, payment finality, ExitAuthorization, gate state, or fiscal numbers.

## Flow Integration

The existing live POS Server issuance path now calculates and records the semantic request hash immediately after mapping the fiscal request and before marking the fiscal reference requested. If the hash source is incomplete or persistence fails, the flow returns a local-context-invalid result and does not proceed as if the hash is auditable.

FEQ detail maps persisted available hash metadata to `AvailableAndConfirmed`. Retry command preparation advances past the semantic hash gate only when the persisted status is available and the hash value, algorithm, and source version are present. The prepared envelope remains non-executable and `RetryExecutionAvailable` remains false.

## Explicit Non-Goals

This slice does not implement:

- retry execution;
- retry scheduler or retry job;
- retry endpoint;
- new POS Server POST behavior;
- POS Server repository/runtime changes;
- fiscal-gated ExitAuthorization enforcement;
- payment finality mutation;
- ExitAuthorization issuance;
- gate behavior;
- Operator Console UI;
- Management Dashboard projection;
- fiscal number editing;
- manual fiscal document creation.

## Validation Notes

Coverage was added for deterministic hashing, semantic changes producing different hashes, volatile fields not affecting the hash, incomplete source handling without fake hashes, semantic hash persistence, FEQ confirmed availability projection, and non-executable command preparation behavior when the hash is available.

## Follow-Up Slice

Recommended next branch:

`feature/central-pms-feq-controlled-retry-scheduler-prep`

Purpose:

Prepare a controlled retry scheduler/job model only after semantic hash source and audit prerequisites are durable, while keeping retry execution disabled until an explicit execution slice.
